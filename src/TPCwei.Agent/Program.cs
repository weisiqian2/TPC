using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using TPC.App.Interop;
using TPC.App.Models;
using TPC.App.Services;

namespace TPC.Agent;

internal static class Program
{
    private static readonly AgentRuntime Runtime = new();

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Contains("--service", StringComparer.OrdinalIgnoreCase))
        {
            ServiceBase.Run(new AgentWindowsService());
            return 0;
        }

        if (args.Contains("--install-service", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine("请以管理员身份运行：sc create TPCAgent binPath= \"" + Environment.ProcessPath + " --service\" start= auto");
            return 0;
        }
        if (args.Contains("--uninstall-service", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine("请以管理员身份运行：sc stop TPCAgent & sc delete TPCAgent");
            return 0;
        }

        await RunAgentAsync(CancellationToken.None).ConfigureAwait(false);
        return 0;
    }

    internal static async Task RunAgentAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("TPC Agent 已启动，Named Pipe: " + AgentClient.PipeName);
        await Runtime.AutoStartAsync().ConfigureAwait(false);
        await RunPipeServerAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task RunPipeServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(
                AgentClient.PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                break;
            }

            try
            {
                await HandleClientAsync(pipe).ConfigureAwait(false);
            }
            finally
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task HandleClientAsync(Stream stream)
    {
        try
        {
            AgentLog("Pipe client connected.");
            var line = await ReadLineAsync(stream).ConfigureAwait(false);
            AgentLog("Pipe request: " + (line ?? "<null>"));
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var method = root.TryGetProperty("method", out var methodElement) ? methodElement.GetString() ?? "" : "";
            var parameters = root.TryGetProperty("params", out var paramsElement) ? paramsElement : default;
            var response = await Runtime.HandleAsync(method, parameters).ConfigureAwait(false);
            var responseBytes = Encoding.UTF8.GetBytes(response + "\n");
            await stream.WriteAsync(responseBytes).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
            AgentLog("Pipe response sent.");
        }
        catch (Exception ex)
        {
            AgentLog("Pipe error: " + ex);
            var response = JsonSerializer.Serialize(new { ok = false, error = ex.Message });
            var bytes = Encoding.UTF8.GetBytes(response + "\n");
            await stream.WriteAsync(bytes).ConfigureAwait(false);
        }
    }

    private static async Task<string?> ReadLineAsync(Stream stream)
    {
        var buffer = new List<byte>(512);
        var one = new byte[1];
        while (buffer.Count < 1024 * 1024)
        {
            var read = await stream.ReadAsync(one, 0, 1).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }
            if (one[0] == (byte)'\n')
            {
                break;
            }
            if (one[0] != (byte)'\r')
            {
                buffer.Add(one[0]);
            }
        }
        return buffer.Count == 0 ? null : Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void AgentLog(string message)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TPCwei");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "agent.log"), $"{DateTimeOffset.Now:HH:mm:ss} {message}{Environment.NewLine}", Encoding.UTF8);
        }
        catch
        {
        }
    }
}

internal sealed class AgentRuntime
{
    private const int LanDiscoveryPort = 54558;
    private const string LanDiscoveryMagic = "TPCWEI-LAN1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly ProfileStore _profileStore = new();
    private readonly ConcurrentDictionary<string, ulong> _runningProfiles = new(StringComparer.Ordinal);
    private readonly List<string> _diagnostics = new();
    private ulong _nodeHandle;
    private Process? _meshdProcess;
    private Process? _gatewayProcess;
    private bool _meshRunning;
    private readonly string _meshNodeId = "agent-" + Guid.NewGuid().ToString("N");
    private readonly List<string> _meshBootstraps = new();
    private readonly List<OfflineMeshMessage> _meshMessages = new();
    private readonly ConcurrentDictionary<string, LanDiscoveryRecord> _lanPeers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, LanDiscoveryAdvertisement> _lanAdvertisements = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConnectRuntimeStatus> _connectStatuses = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _mcLanSessions = new(StringComparer.Ordinal);
    private CancellationTokenSource? _lanDiscoveryCts;
    private Task? _lanDiscoveryTask;

    public AgentRuntime()
    {
        NativeInterop.ProxyEvent += (_, e) => AddDiagnostic($"代理：{e.Message}");
        NativeInterop.GatewayEvent += (_, e) => AddDiagnostic($"网关：{e.Message}");
        AppDomain.CurrentDomain.ProcessExit += (_, _) => StopOwnedChildProcesses();
        Console.CancelKeyPress += (_, _) => StopOwnedChildProcesses();
    }

    public async Task AutoStartAsync()
    {
        var profiles = await _profileStore.LoadAsync().ConfigureAwait(false);
        foreach (var profile in profiles.Where(x => x.AutoStart))
        {
            try
            {
                await StartProfileAsync(profile).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AddDiagnostic($"自动启动 {profile.Name} 失败：{ex.Message}");
            }
        }
    }

    public async Task<string> HandleAsync(string method, JsonElement parameters)
    {
        try
        {
            object result = method switch
            {
                "profile.list" => await _profileStore.LoadAsync().ConfigureAwait(false),
                "profile.save" => await SaveProfileAsync(parameters).ConfigureAwait(false),
                "profile.delete" => await DeleteProfileAsync(parameters).ConfigureAwait(false),
                "profile.start" => await StartProfileFromRequestAsync(parameters).ConfigureAwait(false),
                "profile.stop" => await StopProfileAsync(parameters).ConfigureAwait(false),
                "connect.plan" => await CreateConnectPlanAsync(parameters).ConfigureAwait(false),
                "connect.race" => await StartConnectAsync(parameters).ConfigureAwait(false),
                "connect.start" => await StartConnectAsync(parameters).ConfigureAwait(false),
                "connect.status" => ConnectStatus(parameters),
                "lan.discovery.start" => await StartLanDiscoveryAsync(parameters).ConfigureAwait(false),
                "lan.discovery.stop" => StopLanDiscovery(parameters),
                "lan.peers.list" => ListLanPeers(parameters),
                "lan.peer.find" => FindLanPeer(parameters),
                "mc.lan.start" => StartMinecraftLanAsync(parameters),
                "mc.lan.stop" => StopMinecraftLan(parameters),
                "network.publicIpv4.detect" => DetectPublicIpv4(parameters),
                "metrics.snapshot" => await MetricsSnapshotAsync().ConfigureAwait(false),
                "diagnostics.export" => _diagnostics.ToArray(),
                "gateway.deployPlan" => CreateDeployPlan(parameters),
                var m when m.StartsWith("gateway.", StringComparison.OrdinalIgnoreCase) => await HandleGatewayModuleAsync(m, parameters).ConfigureAwait(false),
                var m when m.StartsWith("mesh.", StringComparison.OrdinalIgnoreCase) => await HandlePlatformModuleAsync(m, parameters).ConfigureAwait(false),
                var m when m.StartsWith("identity.", StringComparison.OrdinalIgnoreCase) => await HandlePlatformModuleAsync(m, parameters).ConfigureAwait(false),
                var m when m.StartsWith("route.", StringComparison.OrdinalIgnoreCase) => await HandlePlatformModuleAsync(m, parameters).ConfigureAwait(false),
                var m when m.StartsWith("audit.", StringComparison.OrdinalIgnoreCase) => await HandlePlatformModuleAsync(m, parameters).ConfigureAwait(false),
                var m when m.StartsWith("game.", StringComparison.OrdinalIgnoreCase) => await HandlePlatformModuleAsync(m, parameters).ConfigureAwait(false),
                var m when m.StartsWith("remote.", StringComparison.OrdinalIgnoreCase) => await HandlePlatformModuleAsync(m, parameters).ConfigureAwait(false),
                var m when m.StartsWith("fileSync.", StringComparison.OrdinalIgnoreCase) => await HandlePlatformModuleAsync(m, parameters).ConfigureAwait(false),
                var m when m.StartsWith("dht.", StringComparison.OrdinalIgnoreCase) => await HandlePlatformModuleAsync(m, parameters).ConfigureAwait(false),
                var m when m.StartsWith("security.", StringComparison.OrdinalIgnoreCase) => await HandleSecurityModuleAsync(m, parameters).ConfigureAwait(false),
                var m when m.StartsWith("developer.", StringComparison.OrdinalIgnoreCase) => HandleDeveloperModule(m, parameters),
                var m when m.StartsWith("ai.", StringComparison.OrdinalIgnoreCase) => await HandlePlatformModuleAsync(m, parameters).ConfigureAwait(false),
                _ => throw new InvalidOperationException("未知 Agent 方法：" + method)
            };

            return JsonSerializer.Serialize(new { ok = true, result }, JsonOptions);
        }
        catch (Exception ex)
        {
            AddDiagnostic($"{method} 失败：{ex.Message}");
            return JsonSerializer.Serialize(new { ok = false, error = ex.Message }, JsonOptions);
        }
    }

    private async Task<ProxyProfileDefinition> SaveProfileAsync(JsonElement parameters)
    {
        var profile = parameters.Deserialize<ProxyProfileDefinition>(JsonOptions)
            ?? throw new InvalidOperationException("缺少 Profile 参数");
        await _profileStore.SaveAsync(profile).ConfigureAwait(false);
        AddDiagnostic($"已保存规则：{profile.Name}");
        return profile;
    }

    private async Task<object> DeleteProfileAsync(JsonElement parameters)
    {
        var id = parameters.GetProperty("id").GetString() ?? "";
        await _profileStore.DeleteAsync(id).ConfigureAwait(false);
        AddDiagnostic($"已删除规则：{id}");
        return new { id };
    }

    private async Task<object> StartProfileFromRequestAsync(JsonElement parameters)
    {
        ProxyProfileDefinition profile;
        if (parameters.TryGetProperty("profile", out var profileElement))
        {
            profile = profileElement.Deserialize<ProxyProfileDefinition>(JsonOptions)
                ?? throw new InvalidOperationException("Profile 解析失败");
        }
        else
        {
            var id = parameters.GetProperty("id").GetString() ?? "";
            profile = (await _profileStore.LoadAsync().ConfigureAwait(false))
                .FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal))
                ?? throw new InvalidOperationException("找不到 Profile：" + id);
        }

        ValidateProfileCanStart(profile);
        var handle = await StartProfileAsync(profile).ConfigureAwait(false);
        return new
        {
            profile.Id,
            profile.Name,
            handle,
            status = "listening",
            path = IsLoopbackOrWildcardHost(profile.PeerHost) ? "local" : "direct",
            listeningPort = profile.LocalPort,
            peer = $"{profile.PeerHost}:{profile.RemotePort}",
            message = "本地监听已启动；收到朋友访问后才代表真正连通。"
        };
    }

    private static void ValidateProfileCanStart(ProxyProfileDefinition profile)
    {
        if (profile.LocalPort == 0)
        {
            throw new InvalidOperationException("本地端口无效，请重新创建连接。");
        }

        if (!string.IsNullOrWhiteSpace(profile.RoomCode) && IsLoopbackOrWildcardHost(profile.PeerHost))
        {
            throw new InvalidOperationException("这条朋友连接还没有有效对端地址。请使用完整连接信息，或启动自建节点后重试。");
        }

        if (profile.RemotePort == 0)
        {
            throw new InvalidOperationException("远端端口无效，请重新创建连接。");
        }
    }

    private static bool IsLoopbackOrWildcardHost(string host)
    {
        var value = host.Trim();
        if (value.Length == 0)
        {
            return true;
        }

        if (string.Equals(value, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "::1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "0.0.0.0", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(value, out var address)
            && (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any));
    }

    private async Task<ulong> StartProfileAsync(ProxyProfileDefinition profile)
    {
        await EnsureNodeAsync().ConfigureAwait(false);
        var json = ProfileStore.ToNativeJson(profile);
        _ = await NativeInterop.ValidateProxyProfileAsync(json).ConfigureAwait(false);
        var handle = await NativeInterop.StartProxyProfileAsync(_nodeHandle, json).ConfigureAwait(false);
        _runningProfiles[profile.Id] = handle;
        AddDiagnostic($"已启动规则：{profile.Name}，句柄 {handle}");
        return handle;
    }

    private async Task<object> StopProfileAsync(JsonElement parameters)
    {
        var id = parameters.GetProperty("id").GetString() ?? "";
        if (_runningProfiles.TryRemove(id, out var handle))
        {
            await NativeInterop.StopProxyAsync(handle).ConfigureAwait(false);
            AddDiagnostic($"已停止规则：{id}");
        }
        return new { id };
    }

    private object DetectPublicIpv4(JsonElement parameters)
    {
        var manual = GetString(parameters, "manualPublicIpv4").Trim();
        var port = GetInt(parameters, "port", 0);

        // 这里只看自己电脑上的地址，不偷偷去问网上的查 IP 服务。
        // 用户说要无服务器化，所以这里宁愿少猜一点，也不要暗中依赖别人家的服务。
        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(x => x.OperationalStatus == OperationalStatus.Up
                && x.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .SelectMany(nic => nic.GetIPProperties().UnicastAddresses
                .Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(x => new
                {
                    address = x.Address.ToString(),
                    adapter = nic.Name,
                    scope = Ipv4Scope(x.Address),
                    publicRoutable = IsPublicRoutableIpv4(x.Address)
                }))
            .ToArray();

        var manualValid = IPAddress.TryParse(manual, out var manualAddress)
            && manualAddress.AddressFamily == AddressFamily.InterNetwork
            && IsPublicRoutableIpv4(manualAddress);
        var direct = addresses.FirstOrDefault(x => x.publicRoutable)?.address ?? "";
        var privateAddresses = addresses.Where(x => x.scope == "private").Select(x => x.address).Distinct().ToArray();

        // 有些家里宽带的公网 IP 在路由器上，不在电脑上。
        // 这种情况不是软件坏了，只是需要路由器帮忙把端口放进来，所以单独给 UI 一个更好懂的提示。
        var mode = manualValid
            ? "manual-public-ipv4"
            : !string.IsNullOrWhiteSpace(direct)
                ? "direct-public-ipv4"
                : privateAddresses.Length > 0
                    ? "router-public-ipv4-unknown"
                    : "no-ipv4";
        var selected = manualValid ? manualAddress!.ToString() : direct;
        var message = mode switch
        {
            "manual-public-ipv4" => $"已使用手动公网 IPv4：{selected}。",
            "direct-public-ipv4" => $"本机网卡检测到公网 IPv4：{selected}。",
            "router-public-ipv4-unknown" => "本机只检测到内网 IPv4。若路由器/光猫有公网 IPv4，请开启 UPnP 或手动端口转发。",
            _ => "没有检测到可用于公网直连的 IPv4。"
        };

        return new
        {
            mode,
            selectedPublicIpv4 = selected,
            manualPublicIpv4 = manual,
            manualValid,
            port,
            addresses,
            privateIpv4 = privateAddresses,
            publicIpv4DirectReady = !string.IsNullOrWhiteSpace(selected) && port > 0,
            portAdvice = port > 0
                ? $"请确认 Windows 防火墙允许 TPCwei，并在路由器上放行/转发端口 {port}。"
                : "请选择 Minecraft Java 25565 或基岩版 UDP 19132 等有效端口。",
            message
        };
    }

    private async Task<object> CreateConnectPlanAsync(JsonElement parameters)
    {
        var profile = await ResolveProfileAsync(parameters).ConfigureAwait(false);
        var discoveryKey = DiscoveryKeyForProfile(profile);
        var ipv6 = GetLocalAddresses(AddressFamily.InterNetworkV6).Any();
        var ipv4 = GetLocalAddresses(AddressFamily.InterNetwork).ToArray();
        var publicIpv4 = ipv4.FirstOrDefault(x => IPAddress.TryParse(x, out var address) && IsPublicRoutableIpv4(address)) ?? "";
        var gatewayReady = !string.IsNullOrWhiteSpace(profile.GatewayHost)
            && !IsLoopbackOrWildcardHost(profile.GatewayHost)
            && profile.GatewayControlPort > 0;

        // 这里是在告诉界面“接下来准备怎么连”。
        // 不确定能连的路就别装作成功，用户最讨厌点了按钮却不知道到底发生了什么。
        var candidates = new List<object>
        {
            new { id = "lan", name = "同 Wi-Fi 局域网发现", available = !string.IsNullOrWhiteSpace(discoveryKey), priority = 1 },
            new { id = "ipv6", name = "IPv6 直连", available = ipv6, priority = 2 },
            new { id = "public-ipv4", name = "公网 IPv4 直连", available = HasUsablePeerEndpoint(profile) || !string.IsNullOrWhiteSpace(publicIpv4), status = HasUsablePeerEndpoint(profile) ? "可用" : !string.IsNullOrWhiteSpace(publicIpv4) ? "本机公网地址可用" : ipv4.Length > 0 ? "需要端口转发" : "不可用", priority = 3 },
            new { id = "upnp", name = "UPnP/PCP/NAT-PMP 自动开洞", available = true, priority = 4 },
            new { id = "udp-punch", name = "UDP 打洞", available = true, priority = 5 },
            new { id = "tcp-punch", name = "TCP 打洞", available = true, priority = 6 },
            new { id = "quic-websocket", name = "QUIC/WebSocket 直连尝试", available = File.Exists(Path.Combine(AppContext.BaseDirectory, "tpcwei_meshd.exe")), priority = 7 },
            new { id = "self-node", name = "自建节点兜底", available = File.Exists(Path.Combine(AppContext.BaseDirectory, "tpcwei_gateway.exe")) || gatewayReady, priority = 8 }
        };

        return new
        {
            profile.Id,
            profile.Name,
            profile.RoomCode,
            discoveryKeyReady = !string.IsNullOrWhiteSpace(discoveryKey),
            localIpv4 = ipv4,
            publicIpv4,
            hasIpv6 = ipv6,
            gatewayReady,
            candidates,
            note = "优先直连；没有直连路径时启动或提示自建节点。不会使用官方服务器。"
        };
    }

    private async Task<object> StartConnectAsync(JsonElement parameters)
    {
        var profile = await ResolveProfileAsync(parameters).ConfigureAwait(false);
        var discoveryKey = DiscoveryKeyForProfile(profile);
        if (string.IsNullOrWhiteSpace(discoveryKey))
        {
            discoveryKey = StableHash(FirstNonEmpty(profile.RoomCode, profile.Id));
        }

        var role = FirstNonEmpty(GetString(parameters, "role"), profile.Role, "设备");
        var isGuest = role.Contains("房客", StringComparison.Ordinal) || profile.Role.Contains("房客", StringComparison.Ordinal);
        var minecraft = GetBool(parameters, "minecraft", true);
        var protocol = GetString(parameters, "protocol", profile.Type is ProxyRuleType.Udp or ProxyRuleType.Sudp ? "UDP" : "TCP");
        var advertPort = profile.PublicPort > 0 ? profile.PublicPort : profile.LocalPort;

        await StartLanDiscoveryAsync(JsonSerializer.SerializeToElement(new
        {
            profileId = profile.Id,
            profile.RoomCode,
            discoveryKey,
            role = isGuest ? "房客" : "房主",
            protocol,
            port = advertPort,
            deviceName = Environment.MachineName
        }, JsonOptions)).ConfigureAwait(false);

        if (minecraft)
        {
            _ = StartMinecraftLanAsync(JsonSerializer.SerializeToElement(new
            {
                profileId = profile.Id,
                profile.RoomCode,
                discoveryKey,
                role = isGuest ? "guest" : "host",
                localPort = profile.LocalPort == 0 ? 25565 : profile.LocalPort,
                advertisePort = advertPort,
                motd = string.IsNullOrWhiteSpace(profile.Name) ? "TPCwei Remote LAN" : profile.Name
            }, JsonOptions));
        }

        if (!isGuest)
        {
            if (HasUsablePeerEndpoint(profile))
            {
                // 房主这边只是把地址准备好了，还不能说已经连上。
                // 朋友真正输入房间码并启动后，才算进入下一步。
                var publicStatus = new ConnectRuntimeStatus(
                    profile.Id,
                    "waiting-public-ipv4",
                    "等待朋友（公网 IPv4 直连）",
                    "公网 IPv4 直连",
                    false,
                    profile.PeerHost,
                    profile.PublicPort > 0 ? profile.PublicPort : advertPort,
                    $"房主已公布公网直连地址 {profile.PeerHost}:{(profile.PublicPort > 0 ? profile.PublicPort : advertPort)}。请确认 Windows 防火墙和路由器端口已放行。",
                    "",
                    DateTimeOffset.Now);
                _connectStatuses[profile.Id] = publicStatus;
                return publicStatus;
            }

            var node = await TryStartSelfNodeAsync(profile).ConfigureAwait(false);
            var status = new ConnectRuntimeStatus(profile.Id, "waiting", "等待朋友", "局域网发现", false, "", advertPort, "房主已开始广播房间。朋友输入房间码和密码后会自动发现。", node.Message, DateTimeOffset.Now);
            _connectStatuses[profile.Id] = status;
            return status;
        }

        for (var attempt = 0; attempt < 12; attempt++)
        {
            // 朋友端先找一下附近有没有房主。
            // 如果两台电脑在同一个 Wi-Fi，这条路最简单，用户基本不用管任何参数。
            var peer = FindBestLanPeer(discoveryKey, profile.RoomCode, "房主");
            if (peer is not null)
            {
                profile.PeerHost = peer.Host;
                profile.RemotePort = peer.Port;
                await _profileStore.SaveAsync(profile).ConfigureAwait(false);
                ValidateProfileCanStart(profile);
                var handle = await StartProfileAsync(profile).ConfigureAwait(false);
                var status = new ConnectRuntimeStatus(profile.Id, "connected", "已连接", "局域网直连", true, peer.Host, peer.Port, $"已发现房主 {peer.DeviceName}，本地端口 {profile.LocalPort} 已可用。", "", DateTimeOffset.Now, handle);
                _connectStatuses[profile.Id] = status;
                return status;
            }

            await Task.Delay(600).ConfigureAwait(false);
        }

        if (HasUsablePeerEndpoint(profile))
        {
            // 附近没找到，再试房间信息里的公网地址。
            // 这里失败时，别甩一堆术语给用户，重点提示防火墙和路由器端口。
            var handle = await StartProfileAsync(profile).ConfigureAwait(false);
            var status = new ConnectRuntimeStatus(profile.Id, "connected", "已连接", "公网/手动直连", true, profile.PeerHost, profile.RemotePort, $"已连接到 {profile.PeerHost}:{profile.RemotePort}。", "", DateTimeOffset.Now, handle);
            _connectStatuses[profile.Id] = status;
            return status;
        }

        var selfNode = await TryStartSelfNodeAsync(profile).ConfigureAwait(false);
        var failed = new ConnectRuntimeStatus(
            profile.Id,
            "need-node",
            "需要自建节点",
            "自建节点",
            false,
            "",
            0,
            "没有发现同 Wi-Fi 房主，公网直连也没有成功。可能是 Windows 防火墙或路由器端口未开放。",
            $"{selfNode.Message} 下一步：检查防火墙 / 检查路由器端口转发 / 启动自建节点。",
            DateTimeOffset.Now);
        _connectStatuses[profile.Id] = failed;
        return failed;
    }

    private object ConnectStatus(JsonElement parameters)
    {
        var id = GetString(parameters, "id");
        if (!string.IsNullOrWhiteSpace(id) && _connectStatuses.TryGetValue(id, out var status))
        {
            return status;
        }

        return _connectStatuses.Values.OrderByDescending(x => x.UpdatedAt).ToArray();
    }

    private async Task<ProxyProfileDefinition> ResolveProfileAsync(JsonElement parameters)
    {
        if (parameters.TryGetProperty("profile", out var profileElement))
        {
            return profileElement.Deserialize<ProxyProfileDefinition>(JsonOptions)
                ?? throw new InvalidOperationException("Profile 解析失败");
        }

        var id = GetString(parameters, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException("缺少连接 ID。");
        }

        return (await _profileStore.LoadAsync().ConfigureAwait(false))
            .FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("找不到 Profile：" + id);
    }

    private LanDiscoveryRecord? FindBestLanPeer(string discoveryKey, string roomCode, string desiredRole)
    {
        PruneLanPeers();
        return _lanPeers.Values
            .Where(x => string.Equals(x.DiscoveryKey, discoveryKey, StringComparison.Ordinal))
            .Where(x => string.IsNullOrWhiteSpace(roomCode) || string.IsNullOrWhiteSpace(x.RoomCode) || string.Equals(x.RoomCode, roomCode, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(desiredRole) || x.Role.Contains(desiredRole, StringComparison.Ordinal))
            .OrderByDescending(x => x.LastSeen)
            .FirstOrDefault();
    }

    private async Task<(bool Running, string Message)> TryStartSelfNodeAsync(ProxyProfileDefinition profile)
    {
        var exePath = Path.Combine(AppContext.BaseDirectory, "tpcwei_gateway.exe");
        if (!File.Exists(exePath))
        {
            return (false, "发布目录中没有自建节点组件 tpcwei_gateway.exe。");
        }

        try
        {
            var token = string.IsNullOrWhiteSpace(profile.GatewayToken) ? StableHash(profile.RoomCode + profile.RoomPasswordHash)[..16] : profile.GatewayToken;
            var result = await HandleGatewayModuleAsync("gateway.start", JsonSerializer.SerializeToElement(new
            {
                bind = "0.0.0.0",
                controlPort = profile.GatewayControlPort == 0 ? 7000 : profile.GatewayControlPort,
                adminPort = 7400,
                token
            }, JsonOptions)).ConfigureAwait(false);
            return (true, "本机自建节点已尝试启动。若这台电脑没有公网或端口映射，朋友仍需要换一台可达电脑作为节点。");
        }
        catch (Exception ex)
        {
            return (false, "自建节点启动失败：" + ex.Message);
        }
    }

    private static bool HasUsablePeerEndpoint(ProxyProfileDefinition profile)
    {
        return profile.RemotePort > 0
            && !string.IsNullOrWhiteSpace(profile.PeerHost)
            && !IsLoopbackOrWildcardHost(profile.PeerHost);
    }

    private static string DiscoveryKeyForProfile(ProxyProfileDefinition profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.RoomPasswordHash))
        {
            return profile.RoomPasswordHash;
        }

        if (!string.IsNullOrWhiteSpace(profile.TrustedPackageHash))
        {
            return profile.TrustedPackageHash;
        }

        return !string.IsNullOrWhiteSpace(profile.RoomCode) ? StableHash(profile.RoomCode) : "";
    }

    private Task<object> StartLanDiscoveryAsync(JsonElement parameters)
    {
        var profileId = GetString(parameters, "profileId");
        if (string.IsNullOrWhiteSpace(profileId))
        {
            profileId = Guid.NewGuid().ToString("N");
        }

        var discoveryKey = FirstNonEmpty(
            GetString(parameters, "discoveryKey"),
            GetString(parameters, "publicHash"),
            GetString(parameters, "roomCode"));
        if (string.IsNullOrWhiteSpace(discoveryKey))
        {
            throw new InvalidOperationException("局域网发现需要房间码或公钥指纹。");
        }

        var port = GetInt(parameters, "port", GetInt(parameters, "localPort", 0));
        if (port <= 0 || port > ushort.MaxValue)
        {
            throw new InvalidOperationException("局域网发现需要有效端口。Minecraft Java 默认 25565，基岩版默认 19132。");
        }

        var advert = new LanDiscoveryAdvertisement(
            profileId,
            discoveryKey.Trim(),
            GetString(parameters, "roomCode").Trim(),
            FirstNonEmpty(GetString(parameters, "role"), "设备"),
            FirstNonEmpty(GetString(parameters, "protocol"), "TCP"),
            (ushort)port,
            FirstNonEmpty(GetString(parameters, "deviceName"), Environment.MachineName),
            DateTimeOffset.Now);
        _lanAdvertisements[profileId] = advert;
        EnsureLanDiscoveryLoop();
        AddDiagnostic($"局域网发现已开启：{advert.Role} {advert.RoomCode} {advert.Protocol}/{advert.Port}");
        return Task.FromResult<object>(new
        {
            running = true,
            discoveryPort = LanDiscoveryPort,
            advert.ProfileId,
            advert.RoomCode,
            advert.Role,
            advert.Protocol,
            advert.Port,
            message = "同一 Wi-Fi/局域网内会自动发现同房间设备；不会使用官方服务器。"
        });
    }

    private object StopLanDiscovery(JsonElement parameters)
    {
        var profileId = GetString(parameters, "profileId");
        if (string.IsNullOrWhiteSpace(profileId))
        {
            _lanAdvertisements.Clear();
        }
        else
        {
            _lanAdvertisements.TryRemove(profileId, out _);
        }

        if (_lanAdvertisements.IsEmpty)
        {
            _lanDiscoveryCts?.Cancel();
        }

        AddDiagnostic(string.IsNullOrWhiteSpace(profileId) ? "局域网发现已停止。" : $"局域网发现已停止：{profileId}");
        return new { running = !_lanAdvertisements.IsEmpty, profileId };
    }

    private object ListLanPeers(JsonElement parameters)
    {
        PruneLanPeers();
        var discoveryKey = FirstNonEmpty(
            GetString(parameters, "discoveryKey"),
            GetString(parameters, "publicHash"),
            GetString(parameters, "roomCode"));
        var roomCode = GetString(parameters, "roomCode");
        var peers = _lanPeers.Values
            .Where(x => string.IsNullOrWhiteSpace(discoveryKey) || string.Equals(x.DiscoveryKey, discoveryKey, StringComparison.Ordinal))
            .Where(x => string.IsNullOrWhiteSpace(roomCode) || string.IsNullOrWhiteSpace(x.RoomCode) || string.Equals(x.RoomCode, roomCode, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.LastSeen)
            .Select(x => new
            {
                x.ProfileId,
                x.RoomCode,
                x.Role,
                x.Protocol,
                host = x.Host,
                port = x.Port,
                x.DeviceName,
                lastSeen = x.LastSeen
            })
            .ToArray();
        return peers;
    }

    private object FindLanPeer(JsonElement parameters)
    {
        PruneLanPeers();
        var discoveryKey = FirstNonEmpty(
            GetString(parameters, "discoveryKey"),
            GetString(parameters, "publicHash"),
            GetString(parameters, "roomCode"));
        var roomCode = GetString(parameters, "roomCode");
        var desiredRole = GetString(parameters, "desiredRole");
        var peer = _lanPeers.Values
            .Where(x => string.IsNullOrWhiteSpace(discoveryKey) || string.Equals(x.DiscoveryKey, discoveryKey, StringComparison.Ordinal))
            .Where(x => string.IsNullOrWhiteSpace(roomCode) || string.IsNullOrWhiteSpace(x.RoomCode) || string.Equals(x.RoomCode, roomCode, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(desiredRole) || x.Role.Contains(desiredRole, StringComparison.Ordinal))
            .OrderByDescending(x => x.LastSeen)
            .FirstOrDefault();
        if (peer is null)
        {
            return new
            {
                found = false,
                message = "同一局域网内暂时没有发现朋友。请确认双方都打开 TPCwei，并启动同一条连接。"
            };
        }

        return new
        {
            found = true,
            peer.ProfileId,
            peer.RoomCode,
            peer.Role,
            peer.Protocol,
            host = peer.Host,
            port = peer.Port,
            peer.DeviceName,
            lastSeen = peer.LastSeen,
            message = $"已发现 {peer.DeviceName}：{peer.Host}:{peer.Port}"
        };
    }

    private void EnsureLanDiscoveryLoop()
    {
        if (_lanDiscoveryTask is { IsCompleted: false })
        {
            return;
        }

        _lanDiscoveryCts?.Dispose();
        _lanDiscoveryCts = new CancellationTokenSource();
        _lanDiscoveryTask = Task.Run(() => RunLanDiscoveryLoopAsync(_lanDiscoveryCts.Token));
    }

    private async Task RunLanDiscoveryLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, LanDiscoveryPort));
            udp.EnableBroadcast = true;

            var receiveTask = ReceiveLanDiscoveryAsync(udp, cancellationToken);
            var sendTask = BroadcastLanDiscoveryAsync(udp, cancellationToken);
            await Task.WhenAll(receiveTask, sendTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AddDiagnostic($"局域网发现失败：{ex.Message}");
        }
    }

    private async Task ReceiveLanDiscoveryAsync(UdpClient udp, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await udp.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            try
            {
                var payload = JsonSerializer.Deserialize<LanDiscoveryPayload>(result.Buffer, JsonOptions);
                if (payload is null
                    || !string.Equals(payload.Magic, LanDiscoveryMagic, StringComparison.Ordinal)
                    || string.Equals(payload.InstanceId, _meshNodeId, StringComparison.Ordinal))
                {
                    continue;
                }

                var discoveryKey = FirstNonEmpty(payload.DiscoveryKey, payload.RoomCode);
                if (string.IsNullOrWhiteSpace(discoveryKey) || payload.Port == 0)
                {
                    continue;
                }

                var host = result.RemoteEndPoint.Address.ToString();
                var key = $"{discoveryKey}|{payload.InstanceId}|{payload.ProfileId}|{host}|{payload.Port}";
                _lanPeers[key] = new LanDiscoveryRecord(
                    payload.ProfileId,
                    discoveryKey,
                    payload.RoomCode,
                    payload.Role,
                    payload.Protocol,
                    host,
                    payload.Port,
                    payload.DeviceName,
                    DateTimeOffset.Now);
            }
            catch
            {
                // Ignore packets from other software on the same UDP port.
            }
        }
    }

    private async Task BroadcastLanDiscoveryAsync(UdpClient udp, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_lanAdvertisements.IsEmpty)
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var endpoints = GetLanBroadcastEndpoints().ToArray();
            foreach (var advert in _lanAdvertisements.Values)
            {
                var payload = new LanDiscoveryPayload(
                    LanDiscoveryMagic,
                    _meshNodeId,
                    advert.ProfileId,
                    advert.DiscoveryKey,
                    advert.RoomCode,
                    advert.Role,
                    advert.Protocol,
                    advert.Port,
                    advert.DeviceName,
                    DateTimeOffset.Now.ToUnixTimeMilliseconds());
                var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
                foreach (var endpoint in endpoints)
                {
                    try
                    {
                        await udp.SendAsync(bytes, endpoint, cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }
            }

            PruneLanPeers();
            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        }
    }

    private static IEnumerable<IPEndPoint> GetLanBroadcastEndpoints()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask is null)
                {
                    continue;
                }

                var broadcast = GetBroadcastAddress(unicast.Address, unicast.IPv4Mask);
                if (seen.Add(broadcast.ToString()))
                {
                    yield return new IPEndPoint(broadcast, LanDiscoveryPort);
                }
            }
        }

        if (seen.Add(IPAddress.Broadcast.ToString()))
        {
            yield return new IPEndPoint(IPAddress.Broadcast, LanDiscoveryPort);
        }
    }

    private static IPAddress GetBroadcastAddress(IPAddress address, IPAddress mask)
    {
        var ipBytes = address.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();
        var broadcast = new byte[ipBytes.Length];
        for (var i = 0; i < ipBytes.Length; i++)
        {
            broadcast[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
        }
        return new IPAddress(broadcast);
    }

    private void PruneLanPeers()
    {
        var cutoff = DateTimeOffset.Now.AddSeconds(-20);
        foreach (var pair in _lanPeers.ToArray())
        {
            if (pair.Value.LastSeen < cutoff)
            {
                _lanPeers.TryRemove(pair.Key, out _);
            }
        }
    }

    private static string GetString(JsonElement parameters, string name, string fallback = "")
    {
        return parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    private static int GetInt(JsonElement parameters, string name, int fallback = 0)
    {
        return parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty(name, out var value)
            && value.TryGetInt32(out var number)
            ? number
            : fallback;
    }

    private static bool GetBool(JsonElement parameters, string name, bool fallback = false)
    {
        return parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => fallback
            }
            : fallback;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
    }

    private static string StableHash(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()))).ToLowerInvariant();
    }

    private static IEnumerable<string> GetLocalAddresses(AddressFamily family)
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            foreach (var address in nic.GetIPProperties().UnicastAddresses.Select(x => x.Address))
            {
                if (address.AddressFamily == family && !IPAddress.IsLoopback(address))
                {
                    yield return address.ToString();
                }
            }
        }
    }

    private static string Ipv4Scope(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return "loopback";
        }

        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
        {
            return "unknown";
        }

        if (bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            || (bytes[0] == 192 && bytes[1] == 168))
        {
            return "private";
        }

        if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
        {
            return "cgnat";
        }

        if (bytes[0] == 169 && bytes[1] == 254)
        {
            return "link-local";
        }

        if (bytes[0] == 0 || bytes[0] >= 224 || bytes[0] == 127)
        {
            return "reserved";
        }

        return "public";
    }

    private static bool IsPublicRoutableIpv4(IPAddress address)
    {
        return address.AddressFamily == AddressFamily.InterNetwork
            && Ipv4Scope(address) == "public";
    }

    private object StartMinecraftLanAsync(JsonElement parameters)
    {
        var profileId = FirstNonEmpty(GetString(parameters, "profileId"), Guid.NewGuid().ToString("N"));
        var role = GetString(parameters, "role", "guest");
        var localPort = GetInt(parameters, "localPort", 25565);
        var advertisePort = GetInt(parameters, "advertisePort", localPort);
        var motd = FirstNonEmpty(GetString(parameters, "motd"), "TPCwei Remote LAN");
        var roomCode = GetString(parameters, "roomCode");

        StopMinecraftLan(new { profileId });
        var cts = new CancellationTokenSource();
        _mcLanSessions[profileId] = cts;
        if (role.Contains("host", StringComparison.OrdinalIgnoreCase) || role.Contains("房主", StringComparison.Ordinal))
        {
            _ = Task.Run(() => CaptureMinecraftJavaLanAsync(profileId, cts.Token));
        }
        else
        {
            _ = Task.Run(() => ReplayMinecraftJavaLanAsync(motd, (ushort)Math.Clamp(localPort, 1, ushort.MaxValue), cts.Token));
        }

        AddDiagnostic(role.Contains("host", StringComparison.OrdinalIgnoreCase)
            ? $"Minecraft Java LAN 捕获已启动：{roomCode}"
            : $"Minecraft Java LAN 本地广播已启动：127.0.0.1:{localPort}");
        return new
        {
            running = true,
            profileId,
            role,
            javaLanMulticast = "224.0.2.60:4445",
            localPort,
            advertisePort,
            bedrockPort = 19132,
            message = role.Contains("host", StringComparison.OrdinalIgnoreCase)
                ? "正在监听 Minecraft Java 的“打开局域网世界”广播；识别到端口后会自动更新房间。"
                : "正在本机重放 Minecraft Java LAN 广播；多人游戏列表应出现 TPCwei 房间，失败时可手动添加 127.0.0.1。"
        };
    }

    private object StopMinecraftLan(JsonElement parameters)
    {
        return StopMinecraftLan(new { profileId = GetString(parameters, "profileId") });
    }

    private object StopMinecraftLan(object payload)
    {
        var profileId = payload.GetType().GetProperty("profileId")?.GetValue(payload)?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(profileId))
        {
            foreach (var cts in _mcLanSessions.Values)
            {
                cts.Cancel();
            }
            _mcLanSessions.Clear();
            return new { running = false };
        }

        if (_mcLanSessions.TryRemove(profileId, out var token))
        {
            token.Cancel();
        }
        return new { running = _mcLanSessions.Count > 0, profileId };
    }

    private async Task CaptureMinecraftJavaLanAsync(string profileId, CancellationToken cancellationToken)
    {
        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, 4445));
            udp.JoinMulticastGroup(IPAddress.Parse("224.0.2.60"));
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await udp.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                var text = Encoding.UTF8.GetString(result.Buffer);
                var port = TryParseMinecraftLanPort(text);
                if (port == 0)
                {
                    continue;
                }

                if (_lanAdvertisements.TryGetValue(profileId, out var advert))
                {
                    _lanAdvertisements[profileId] = advert with { Port = port, Protocol = "TCP" };
                    AddDiagnostic($"已识别 Minecraft Java 局域网世界端口：{port}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AddDiagnostic($"Minecraft Java LAN 捕获失败：{ex.Message}");
        }
    }

    private static async Task ReplayMinecraftJavaLanAsync(string motd, ushort localPort, CancellationToken cancellationToken)
    {
        var endpoint = new IPEndPoint(IPAddress.Parse("224.0.2.60"), 4445);
        using var udp = new UdpClient(AddressFamily.InterNetwork) { MulticastLoopback = true };
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        var payload = Encoding.UTF8.GetBytes($"[MOTD]{motd}[/MOTD][AD]{localPort}[/AD]");
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await udp.SendAsync(payload, endpoint, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }
            await Task.Delay(1500, cancellationToken).ConfigureAwait(false);
        }
    }

    private static ushort TryParseMinecraftLanPort(string text)
    {
        var start = text.IndexOf("[AD]", StringComparison.OrdinalIgnoreCase);
        var end = text.IndexOf("[/AD]", StringComparison.OrdinalIgnoreCase);
        if (start < 0 || end <= start)
        {
            return 0;
        }

        var value = text[(start + 4)..end].Trim();
        return ushort.TryParse(value, out var port) ? port : (ushort)0;
    }

    private async Task<object> MetricsSnapshotAsync()
    {
        var items = new List<object>();
        foreach (var pair in _runningProfiles)
        {
            try
            {
                var metrics = await NativeInterop.GetProxyMetricsAsync(pair.Value).ConfigureAwait(false);
                items.Add(new
                {
                    id = pair.Key,
                    handle = pair.Value,
                    metrics.BytesUp,
                    metrics.BytesDown,
                    metrics.ActiveConnections,
                    metrics.ErrorCount,
                    metrics.ReconnectCount,
                    metrics.HealthScore,
                    metrics.Running
                });
            }
            catch (Exception ex)
            {
                items.Add(new { id = pair.Key, handle = pair.Value, error = ex.Message });
            }
        }
        return items;
    }

    private static GatewayDeployPlan CreateDeployPlan(JsonElement parameters)
    {
        var options = parameters.Deserialize<GatewayDeployOptions>(JsonOptions) ?? new GatewayDeployOptions();
        return new GatewayDeployService().CreatePlan(options);
    }

    private async Task<object> HandleGatewayModuleAsync(string method, JsonElement parameters)
    {
        AddDiagnostic($"网关模块调用：{method}");
        if (method.Equals("gateway.start", StringComparison.OrdinalIgnoreCase))
        {
            var exePath = Path.Combine(AppContext.BaseDirectory, "tpcwei_gateway.exe");
            if (!File.Exists(exePath))
            {
                throw new FileNotFoundException("发布目录中没有 tpcwei_gateway.exe", exePath);
            }

            var bind = parameters.TryGetProperty("bind", out var bindElement) ? bindElement.GetString() ?? "127.0.0.1" : "127.0.0.1";
            var controlPort = parameters.TryGetProperty("controlPort", out var controlElement) ? controlElement.GetInt32() : 7000;
            var adminPort = parameters.TryGetProperty("adminPort", out var adminElement) ? adminElement.GetInt32() : 7400;
            var token = parameters.TryGetProperty("token", out var tokenElement) ? tokenElement.GetString() ?? "change-me" : "change-me";
            if (_gatewayProcess is null || _gatewayProcess.HasExited)
            {
                _gatewayProcess = Process.Start(new ProcessStartInfo(exePath)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = AppContext.BaseDirectory,
                    ArgumentList =
                    {
                        "--bind", bind,
                        "--control-port", controlPort.ToString(),
                        "--admin-port", adminPort.ToString(),
                        "--token", token
                    }
                });
                await Task.Delay(400).ConfigureAwait(false);
            }

            return new { running = _gatewayProcess is { HasExited: false }, pid = _gatewayProcess?.Id, bind, controlPort, adminPort };
        }

        if (method.Equals("gateway.stop", StringComparison.OrdinalIgnoreCase))
        {
            if (_gatewayProcess is { HasExited: false })
            {
                _gatewayProcess.Kill(entireProcessTree: true);
                await _gatewayProcess.WaitForExitAsync().ConfigureAwait(false);
            }
            return new { running = false };
        }

        if (method.Equals("gateway.health", StringComparison.OrdinalIgnoreCase))
        {
            return await QueryGatewayAdminAsync("http://127.0.0.1:7400/health").ConfigureAwait(false);
        }
        if (method.Equals("gateway.status", StringComparison.OrdinalIgnoreCase))
        {
            return await QueryGatewayAdminAsync("http://127.0.0.1:7400/api/status").ConfigureAwait(false);
        }
        if (method.Equals("gateway.metrics", StringComparison.OrdinalIgnoreCase))
        {
            return await QueryGatewayAdminAsync("http://127.0.0.1:7400/metrics").ConfigureAwait(false);
        }

        return new { method, status = "unsupported", message = "支持 gateway.start/stop/health/status/metrics/deployPlan。" };
    }

    private static async Task<object> QueryGatewayAdminAsync(string url)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var text = await client.GetStringAsync(url).ConfigureAwait(false);
        if (text.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            return JsonDocument.Parse(text).RootElement.Clone();
        }
        return new { url, text };
    }

    private async Task<object> HandlePlatformModuleAsync(string method, JsonElement parameters)
    {
        var json = parameters.ValueKind == JsonValueKind.Undefined ? "{}" : parameters.GetRawText();
        AddDiagnostic($"平台模块调用：{method}");

        if (method.StartsWith("mesh.", StringComparison.OrdinalIgnoreCase))
        {
            var sidecar = await TryCallMeshdAsync(method, json).ConfigureAwait(false);
            if (sidecar is not null)
            {
                return sidecar;
            }

            return await HandleMeshFallbackAsync(method, parameters).ConfigureAwait(false);
        }

        await NativeInterop.InitializeAsync().ConfigureAwait(false);

        if (method.Equals("mesh.start", StringComparison.OrdinalIgnoreCase))
        {
            var handle = await NativeInterop.StartMeshAsync(json).ConfigureAwait(false);
            return new { module = "mesh", status = "running", handle, message = "全域组网规则引擎已启动，正在使用本地路径候选和网关回退策略。" };
        }
        if (method.Equals("dht.start", StringComparison.OrdinalIgnoreCase))
        {
            var handle = await NativeInterop.StartDhtAsync(json).ConfigureAwait(false);
            return new { module = "dht", status = "running", handle, message = "授权节点发现已启动；未配置授权节点时会保持本机离线模式。" };
        }
        if (method.Equals("game.start", StringComparison.OrdinalIgnoreCase))
        {
            var handle = await NativeInterop.StartGameVlanAsync(json).ConfigureAwait(false);
            return new { module = "game", status = "running", handle, message = "游戏 TCP/UDP 会话已启动，广播/组播按当前 Profile 策略转发。" };
        }
        if (method.Equals("remote.start", StringComparison.OrdinalIgnoreCase))
        {
            var handle = await NativeInterop.StartRemoteSessionAsync(json).ConfigureAwait(false);
            return new { module = "remote", status = "running", handle, message = "远程桌面 RDP 映射会话已启动。" };
        }
        if (method.Equals("fileSync.start", StringComparison.OrdinalIgnoreCase))
        {
            var handle = await NativeInterop.StartFileSyncAsync(json).ConfigureAwait(false);
            return new { module = "fileSync", status = "running", handle, message = "文件同步任务已进入队列，进度可通过任务列表查看。" };
        }
        if (method.Equals("route.candidates", StringComparison.OrdinalIgnoreCase))
        {
            return JsonDocument.Parse(await NativeInterop.GetRouteCandidatesJsonAsync(json).ConfigureAwait(false)).RootElement.Clone();
        }
        if (method.Equals("identity.authorize", StringComparison.OrdinalIgnoreCase))
        {
            return JsonDocument.Parse(await NativeInterop.AuthorizeIdentityJsonAsync(json).ConfigureAwait(false)).RootElement.Clone();
        }
        if (method.Equals("audit.query", StringComparison.OrdinalIgnoreCase))
        {
            return JsonDocument.Parse(await NativeInterop.QueryAuditJsonAsync(json).ConfigureAwait(false)).RootElement.Clone();
        }
        if (method.Equals("ai.diagnose", StringComparison.OrdinalIgnoreCase))
        {
            return JsonDocument.Parse(await NativeInterop.DiagnoseAiJsonAsync(json).ConfigureAwait(false)).RootElement.Clone();
        }

        return new { method, status = "unsupported", message = "当前版本不支持该 Agent 方法，请使用连接、网关、文件、自测或诊断导出等已启用功能。" };
    }

    private async Task<object> HandleMeshFallbackAsync(string method, JsonElement parameters)
    {
        await Task.Yield();
        AddDiagnostic($"meshd 不可用，mesh 方法回退到 Agent 本地自愈状态：{method}");

        if (method.Equals("mesh.start", StringComparison.OrdinalIgnoreCase))
        {
            _meshRunning = true;
            if (parameters.TryGetProperty("bootstraps", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
            {
                _meshBootstraps.Clear();
                _meshBootstraps.AddRange(nodes.EnumerateArray()
                    .Select(x => x.GetString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase));
            }

            return new
            {
                nodeId = _meshNodeId,
                running = true,
                mode = "agent-local-fallback",
                bootstraps = _meshBootstraps.ToArray(),
                transports = new[] { "tcp", "quic", "websocket" },
                dht = "等待 tpcwei_meshd/libp2p 可执行文件，当前使用本地规则引擎",
                relayPolicy = "authorized-or-same-group",
                maxRelayHops = 3
            };
        }

        if (method.Equals("mesh.stop", StringComparison.OrdinalIgnoreCase))
        {
            _meshRunning = false;
            return new { running = false, mode = "agent-local-fallback" };
        }

        if (method.Equals("mesh.status", StringComparison.OrdinalIgnoreCase))
        {
            PruneMeshMessages();
            return new
            {
                nodeId = _meshNodeId,
                running = _meshRunning,
                mode = "agent-local-fallback",
                peerCount = 0,
                bootstraps = _meshBootstraps.ToArray(),
                cachedMessages = _meshMessages.Count,
                transportPlan = new[] { "tcp", "quic", "websocket" },
                note = "发布目录提供 tpcwei_meshd.exe 后会自动切换到 Rust libp2p sidecar。"
            };
        }

        if (method.Equals("mesh.bootstrap.set", StringComparison.OrdinalIgnoreCase))
        {
            _meshBootstraps.Clear();
            if (parameters.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
            {
                _meshBootstraps.AddRange(nodes.EnumerateArray()
                    .Select(x => x.GetString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase));
            }
            return new { bootstraps = _meshBootstraps.ToArray(), mode = "agent-local-fallback" };
        }

        if (method.Equals("mesh.peers.list", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<object>();
        }

        if (method.Equals("mesh.route.find", StringComparison.OrdinalIgnoreCase))
        {
            var target = parameters.TryGetProperty("targetPeer", out var targetElement)
                ? targetElement.GetString() ?? "auto"
                : "auto";
            return new
            {
                targetPeer = target,
                direct = false,
                relayCandidates = Array.Empty<object>(),
                maxRelayHops = 3,
                policy = "prefer-lowest-rtt-authorized-path",
                note = _meshBootstraps.Count == 0
                    ? "未配置 Bootstrap 节点，当前只做本机诊断。"
                    : "已保存 Bootstrap 节点；等待 meshd/libp2p 可执行文件接管真实 DHT。"
            };
        }

        if (method.Equals("mesh.message.send", StringComparison.OrdinalIgnoreCase))
        {
            var targetPeer = parameters.TryGetProperty("targetPeer", out var targetElement)
                ? targetElement.GetString() ?? ""
                : "";
            var ciphertext = parameters.TryGetProperty("ciphertext", out var ciphertextElement)
                ? ciphertextElement.GetString() ?? ""
                : "";
            if (string.IsNullOrWhiteSpace(targetPeer) || string.IsNullOrWhiteSpace(ciphertext))
            {
                throw new InvalidOperationException("mesh.message.send 需要 targetPeer 和 ciphertext；Agent 只缓存端到端加密后的消息。");
            }

            var ttlHours = parameters.TryGetProperty("ttlHours", out var ttlElement) && ttlElement.TryGetInt32(out var ttl)
                ? Math.Clamp(ttl, 1, 168)
                : 24;
            var message = new OfflineMeshMessage(Guid.NewGuid().ToString("N"), targetPeer, ciphertext, DateTimeOffset.Now.AddHours(ttlHours));
            _meshMessages.Add(message);
            PruneMeshMessages();
            return message;
        }

        if (method.Equals("mesh.message.sync", StringComparison.OrdinalIgnoreCase))
        {
            PruneMeshMessages();
            var peer = parameters.TryGetProperty("peer", out var peerElement) ? peerElement.GetString() ?? "" : "";
            return _meshMessages
                .Where(x => string.IsNullOrWhiteSpace(peer) || string.Equals(x.TargetPeer, peer, StringComparison.Ordinal))
                .ToArray();
        }

        return new { method, status = "unsupported", mode = "agent-local-fallback" };
    }

    private void PruneMeshMessages()
    {
        var now = DateTimeOffset.Now;
        _meshMessages.RemoveAll(x => x.ExpiresAt <= now);
        if (_meshMessages.Count > 1024)
        {
            _meshMessages.RemoveRange(0, _meshMessages.Count - 1024);
        }
    }

    private async Task<object> HandleSecurityModuleAsync(string method, JsonElement parameters)
    {
        var state = await LoadSecurityStateAsync().ConfigureAwait(false);
        AddDiagnostic($"安全/隐身模块调用：{method}");

        if (method.Equals("security.stealth.apply", StringComparison.OrdinalIgnoreCase))
        {
            state.Policy = parameters.TryGetProperty("policy", out var policyElement) ? policyElement.GetString() ?? "关闭" : "关闭";
            state.Camouflage = parameters.TryGetProperty("camouflage", out var camouflageElement) ? camouflageElement.GetString() ?? "关闭" : "关闭";
            state.PortKnocking = parameters.TryGetProperty("portKnocking", out var knockElement) && knockElement.ValueKind == JsonValueKind.True;
            state.MaxFailures = parameters.TryGetProperty("maxFailures", out var failuresElement) && failuresElement.TryGetInt32(out var failures) ? Math.Clamp(failures, 1, 20) : 5;
            state.BlockMinutes = parameters.TryGetProperty("blockMinutes", out var blockElement) && blockElement.TryGetInt32(out var block) ? Math.Clamp(block, 1, 1440) : 30;
            if (parameters.TryGetProperty("blacklistedIps", out var ipsElement) && ipsElement.ValueKind == JsonValueKind.Array)
            {
                state.BlacklistedIps = ipsElement.EnumerateArray()
                    .Select(x => x.GetString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            await SaveSecurityStateAsync(state).ConfigureAwait(false);
            return new
            {
                applied = true,
                state.Policy,
                state.Camouflage,
                state.PortKnocking,
                state.MaxFailures,
                state.BlockMinutes,
                blacklistedIps = state.BlacklistedIps,
                note = "隐身策略已保存。本轮为应用层最佳努力：最小化固定监听、敲门策略、失败计数和黑名单；不包含内核驱动。"
            };
        }

        if (method.Equals("security.blacklist.list", StringComparison.OrdinalIgnoreCase))
        {
            return new { blacklistedIps = state.BlacklistedIps };
        }

        if (method.Equals("security.blacklist.add", StringComparison.OrdinalIgnoreCase))
        {
            var ip = parameters.TryGetProperty("ip", out var ipElement) ? ipElement.GetString() ?? "" : "";
            if (!string.IsNullOrWhiteSpace(ip) && !state.BlacklistedIps.Contains(ip, StringComparer.OrdinalIgnoreCase))
            {
                state.BlacklistedIps.Add(ip.Trim());
                await SaveSecurityStateAsync(state).ConfigureAwait(false);
            }
            return new { blacklistedIps = state.BlacklistedIps };
        }

        if (method.Equals("security.blacklist.remove", StringComparison.OrdinalIgnoreCase))
        {
            var ip = parameters.TryGetProperty("ip", out var ipElement) ? ipElement.GetString() ?? "" : "";
            state.BlacklistedIps.RemoveAll(x => string.Equals(x, ip, StringComparison.OrdinalIgnoreCase));
            await SaveSecurityStateAsync(state).ConfigureAwait(false);
            return new { blacklistedIps = state.BlacklistedIps };
        }

        if (method.Equals("security.blacklist.clear", StringComparison.OrdinalIgnoreCase))
        {
            state.BlacklistedIps.Clear();
            await SaveSecurityStateAsync(state).ConfigureAwait(false);
            return new { blacklistedIps = state.BlacklistedIps };
        }

        return new { method, status = "unsupported", message = "支持 security.stealth.apply 和 security.blacklist.list/add/remove/clear。" };
    }

    private static async Task<SecurityRuntimeState> LoadSecurityStateAsync()
    {
        var path = SecurityStatePath();
        if (!File.Exists(path))
        {
            return new SecurityRuntimeState();
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, Encoding.UTF8).ConfigureAwait(false);
            return JsonSerializer.Deserialize<SecurityRuntimeState>(json, JsonOptions) ?? new SecurityRuntimeState();
        }
        catch
        {
            return new SecurityRuntimeState();
        }
    }

    private static async Task SaveSecurityStateAsync(SecurityRuntimeState state)
    {
        var path = SecurityStatePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(state, JsonOptions), Encoding.UTF8).ConfigureAwait(false);
    }

    private static string SecurityStatePath()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TPCwei", "security-state.json");
    }

    private object HandleDeveloperModule(string method, JsonElement parameters)
    {
        AddDiagnostic($"开发者模式调用：{method}");
        var timestamp = DateTimeOffset.Now;
        if (method.Equals("developer.capture", StringComparison.OrdinalIgnoreCase))
        {
            var npcapAvailable = File.Exists(Path.Combine(Environment.SystemDirectory, "Npcap", "Packet.dll"))
                || Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Npcap"));
            return new
            {
                method,
                scope = "本机",
                timestamp,
                npcapAvailable,
                enabled = npcapAvailable,
                runningProfiles = _runningProfiles.Keys.ToArray(),
                note = npcapAvailable
                    ? "Npcap 可用，可抓取本机 TPCwei 相关流量并导出。"
                    : "未检测到 Npcap，已禁用真实抓包；仍可导出 Agent 内部运行态。"
            };
        }

        if (method.Equals("developer.apiTest", StringComparison.OrdinalIgnoreCase))
        {
            var target = parameters.TryGetProperty("target", out var targetElement)
                ? targetElement.GetString() ?? "localhost"
                : "localhost";
            var allowed = target is "localhost" or "127.0.0.1" or "::1";
            return new
            {
                method,
                scope = "本机",
                timestamp,
                ok = allowed,
                allowed,
                roundTrip = allowed ? "Named Pipe JSON-RPC 正常" : "已拒绝外部目标；开发者测试仅限本机/自有靶场。",
                echo = parameters.ValueKind == JsonValueKind.Undefined ? "{}" : parameters.GetRawText()
            };
        }

        if (method.Equals("developer.profile", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                method,
                scope = "本机",
                timestamp,
                runningCount = _runningProfiles.Count,
                runningProfiles = _runningProfiles.Select(x => new { id = x.Key, handle = x.Value }).ToArray(),
                suggestion = _runningProfiles.Count == 0 ? "当前没有后台运行规则，可先在连接页保存并后台启动。" : "后台规则正在运行，可继续观察 metrics.snapshot。"
            };
        }

        if (method.Equals("developer.export", StringComparison.OrdinalIgnoreCase))
        {
            var exportDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TPCwei", "exports");
            Directory.CreateDirectory(exportDir);
            var exportPath = Path.Combine(exportDir, $"diagnostics-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json");
            File.WriteAllText(exportPath, JsonSerializer.Serialize(new
            {
                timestamp,
                diagnostics = _diagnostics.Select(Redact).ToArray(),
                runningProfiles = _runningProfiles.Select(x => new { id = x.Key, handle = x.Value }).ToArray()
            }, JsonOptions), Encoding.UTF8);
            return new
            {
                method,
                scope = "本机",
                timestamp,
                format = "json",
                path = exportPath,
                diagnostics = _diagnostics.Select(Redact).ToArray()
            };
        }

        if (method.Equals("developer.lab", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                method,
                scope = "localhost/127.0.0.1",
                timestamp,
                allowed = true,
                note = "本机靶场限制已启用；不会对外部目标执行扫描或攻击性动作。"
            };
        }

        return new
        {
            method,
            scope = "本机/自有靶场",
            allowed = true,
            warning = "开发者演练被限制在本机或自有靶场，不会对第三方目标执行攻击性操作。",
            exports = new[] { "json" },
            tools = new[] { "抓包分析", "API 测试", "性能分析", "日志导出", "本机靶场演练" }
        };
    }

    private static string Redact(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var text = value;
        foreach (var marker in new[] { "token=", "令牌", "private", "私钥" })
        {
            var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                text = text[..index] + marker + "***";
            }
        }
        return text;
    }

    private async Task<object?> TryCallMeshdAsync(string method, string parametersJson)
    {
        var exePath = Path.Combine(AppContext.BaseDirectory, "tpcwei_meshd.exe");
        if (!File.Exists(exePath))
        {
            AddDiagnostic("meshd 未随发布包提供，mesh.* 暂时回退到内置本地规则引擎。");
            return null;
        }

        try
        {
            if (_meshdProcess is null || _meshdProcess.HasExited)
            {
                _meshdProcess = Process.Start(new ProcessStartInfo(exePath)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = AppContext.BaseDirectory
                });
                await Task.Delay(250).ConfigureAwait(false);
            }

            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await client.ConnectAsync("127.0.0.1", 8765, cts.Token).ConfigureAwait(false);
            await using var stream = client.GetStream();
            var request = JsonSerializer.Serialize(new
            {
                id = Guid.NewGuid().ToString("N"),
                method,
                @params = JsonDocument.Parse(parametersJson).RootElement
            });
            var bytes = Encoding.UTF8.GetBytes(request + "\n");
            await stream.WriteAsync(bytes, cts.Token).ConfigureAwait(false);
            await stream.FlushAsync(cts.Token).ConfigureAwait(false);

            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            var line = await reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            var root = JsonDocument.Parse(line).RootElement.Clone();
            if (root.TryGetProperty("ok", out var okElement) && okElement.ValueKind == JsonValueKind.True)
            {
                return root.TryGetProperty("result", out var resultElement) ? resultElement.Clone() : root;
            }
            if (root.TryGetProperty("error", out var errorElement))
            {
                throw new InvalidOperationException(errorElement.GetString() ?? "meshd 返回失败");
            }
            return root;
        }
        catch (Exception ex)
        {
            AddDiagnostic($"meshd 调用失败，已回退内置规则引擎：{ex.Message}");
            return null;
        }
    }

    private async Task EnsureNodeAsync()
    {
        if (_nodeHandle != 0)
        {
            return;
        }

        await NativeInterop.InitializeAsync().ConfigureAwait(false);
        _nodeHandle = await NativeInterop.StartNodeAsync(port: 0, lanDiscovery: true, upnp: true).ConfigureAwait(false);
        AddDiagnostic("核心节点已启动。");
    }

    private void AddDiagnostic(string message)
    {
        lock (_diagnostics)
        {
            _diagnostics.Add($"{DateTimeOffset.Now:HH:mm:ss} {message}");
            while (_diagnostics.Count > 200)
            {
                _diagnostics.RemoveAt(0);
            }
        }
        Console.WriteLine(message);
    }

    private void StopOwnedChildProcesses()
    {
        foreach (var process in new[] { _meshdProcess, _gatewayProcess })
        {
            try
            {
                if (process is { HasExited: false })
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        }
    }
}

internal sealed class AgentWindowsService : ServiceBase
{
    private CancellationTokenSource? _cts;
    private Task? _runTask;

    public AgentWindowsService()
    {
        ServiceName = "TPCAgent";
        CanStop = true;
        CanShutdown = true;
    }

    protected override void OnStart(string[] args)
    {
        _cts = new CancellationTokenSource();
        _runTask = Task.Run(() => Program.RunAgentAsync(_cts.Token));
    }

    protected override void OnStop()
    {
        _cts?.Cancel();
        try
        {
            _runTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _runTask = null;
        }
    }

    protected override void OnShutdown()
    {
        OnStop();
        base.OnShutdown();
    }
}

internal sealed record OfflineMeshMessage(string Id, string TargetPeer, string Ciphertext, DateTimeOffset ExpiresAt);

internal sealed record LanDiscoveryAdvertisement(
    string ProfileId,
    string DiscoveryKey,
    string RoomCode,
    string Role,
    string Protocol,
    ushort Port,
    string DeviceName,
    DateTimeOffset CreatedAt);

internal sealed record LanDiscoveryPayload(
    string Magic,
    string InstanceId,
    string ProfileId,
    string DiscoveryKey,
    string RoomCode,
    string Role,
    string Protocol,
    ushort Port,
    string DeviceName,
    long Timestamp);

internal sealed record LanDiscoveryRecord(
    string ProfileId,
    string DiscoveryKey,
    string RoomCode,
    string Role,
    string Protocol,
    string Host,
    ushort Port,
    string DeviceName,
    DateTimeOffset LastSeen);

internal sealed record ConnectRuntimeStatus(
    string ProfileId,
    string Status,
    string DisplayStatus,
    string Path,
    bool Connected,
    string Host,
    ushort Port,
    string Message,
    string Detail,
    DateTimeOffset UpdatedAt,
    ulong Handle = 0);

internal sealed class SecurityRuntimeState
{
    public string Policy { get; set; } = "关闭";
    public string Camouflage { get; set; } = "关闭";
    public bool PortKnocking { get; set; }
    public int MaxFailures { get; set; } = 5;
    public int BlockMinutes { get; set; } = 30;
    public List<string> BlacklistedIps { get; set; } = new();
}
