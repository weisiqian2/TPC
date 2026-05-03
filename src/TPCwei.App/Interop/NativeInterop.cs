using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using TPC.App.Models;

namespace TPC.App.Interop;

internal static class NativeInterop
{
    private const string LibraryName = "p2p_core";
    private static readonly NatCallback NativeNatCallback = HandleNatCallback;
    private static readonly TransferCallback NativeTransferCallback = HandleTransferCallback;
    private static readonly TunnelCallback NativeTunnelCallback = HandleTunnelCallback;
    private static readonly GatewayCallback NativeGatewayCallback = HandleGatewayCallback;
    private static readonly ProxyCallback NativeProxyCallback = HandleProxyCallback;
    private static bool _callbacksRegistered;

    static NativeInterop()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeInterop).Assembly, ResolveNativeLibrary);
    }

    public static event EventHandler<NatEventArgs>? NatEvent;
    public static event EventHandler<TransferEventArgs>? TransferEvent;
    public static event EventHandler<TunnelEventArgs>? TunnelEvent;
    public static event EventHandler<GatewayEventArgs>? GatewayEvent;
    public static event EventHandler<ProxyEventArgs>? ProxyEvent;

    public static async Task InitializeAsync()
    {
        await Task.Run(() => ThrowIfError(NativeMethods.p2p_initialize())).ConfigureAwait(false);
        RegisterCallbacks();
    }

    public static Task ShutdownAsync()
    {
        return Task.Run(() => ThrowIfError(NativeMethods.p2p_shutdown()));
    }

    public static Task<ulong> StartNodeAsync(string bindAddress = "0.0.0.0", ushort port = 0, bool lanDiscovery = true, bool upnp = true)
    {
        return Task.Run(() =>
        {
            var config = new NodeConfig
            {
                BindAddress = bindAddress,
                LocalPort = port,
                WorkerThreads = 2,
                EnableLanDiscovery = lanDiscovery ? (byte)1 : (byte)0,
                EnableUpnp = upnp ? (byte)1 : (byte)0
            };
            ThrowIfError(NativeMethods.p2p_node_start(in config, out var node));
            return node;
        });
    }

    public static Task StopNodeAsync(ulong node)
    {
        return Task.Run(() => ThrowIfError(NativeMethods.p2p_node_stop(node)));
    }

    public static Task<NodeMetrics> GetNodeMetricsAsync(ulong node)
    {
        return Task.Run(() =>
        {
            ThrowIfError(NativeMethods.p2p_node_get_metrics(node, out var metrics));
            return metrics;
        });
    }

    public static async Task<bool> PunchUdpAsync(ulong node, string peerHost, ushort peerPort, uint attempts = 12, uint intervalMs = 100, TimeSpan? timeout = null)
    {
        RegisterCallbacks();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? _, NatEventArgs args)
        {
            if (args.Address == peerHost && args.Port == peerPort &&
                (args.EventType is global::TPC.App.Interop.NatEvent.UdpPunchSuccess or global::TPC.App.Interop.NatEvent.UdpPunchFailed))
            {
                completion.TrySetResult(args.EventType == global::TPC.App.Interop.NatEvent.UdpPunchSuccess);
            }
        }

        NatEvent += Handler;
        try
        {
            ThrowIfError(NativeMethods.p2p_nat_udp_punch(node, peerHost, peerPort, attempts, intervalMs));
            var winner = await Task.WhenAny(completion.Task, Task.Delay(timeout ?? TimeSpan.FromSeconds(5))).ConfigureAwait(false);
            return winner == completion.Task && await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            NatEvent -= Handler;
        }
    }

    public static async Task<bool> PunchTcpAsync(ulong node, string peerHost, ushort peerPort, uint timeoutMs = 3000)
    {
        RegisterCallbacks();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? _, NatEventArgs args)
        {
            if (args.Address == peerHost && args.Port == peerPort &&
                (args.EventType is global::TPC.App.Interop.NatEvent.TcpPunchSuccess or global::TPC.App.Interop.NatEvent.TcpPunchFailed))
            {
                completion.TrySetResult(args.EventType == global::TPC.App.Interop.NatEvent.TcpPunchSuccess);
            }
        }

        NatEvent += Handler;
        try
        {
            ThrowIfError(NativeMethods.p2p_nat_tcp_punch(node, peerHost, peerPort, timeoutMs));
            var winner = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromMilliseconds(timeoutMs + 1500))).ConfigureAwait(false);
            return winner == completion.Task && await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            NatEvent -= Handler;
        }
    }

    public static Task<bool> CreateUpnpMappingAsync(ushort localPort, ushort externalPort, string protocol, uint leaseSeconds = 3600)
    {
        return Task.Run(() => NativeMethods.p2p_nat_upnp_add_mapping(localPort, externalPort, protocol, leaseSeconds) == 0);
    }

    public static Task<(string Address, ushort Port)> GetExternalEndpointAsync()
    {
        return Task.Run(() =>
        {
            // UPnP 成功后，native 层会记住路由器分配出来的外网地址。
            // 如果没拿到，就返回空值，让界面继续提示用户手动端口转发。
            var address = new StringBuilder(128);
            ushort port = 0;
            var error = NativeMethods.p2p_nat_get_external_endpoint(address, (uint)address.Capacity, out port);
            return error == 0 ? (address.ToString(), port) : ("", (ushort)0);
        });
    }

    public static Task<ulong> SendFileAsync(ulong node, string localPath, string peerHost, ushort peerPort, string? remotePath = null, bool resume = true)
    {
        return Task.Run(() =>
        {
            var options = new FileTransferOptions
            {
                LocalPath = localPath,
                RemotePath = remotePath,
                PeerHost = peerHost,
                PeerPort = peerPort,
                ResumeEnabled = resume ? (byte)1 : (byte)0,
                ParallelPaths = 1,
                ChunkSize = 64 * 1024
            };
            ThrowIfError(NativeMethods.p2p_file_send(node, in options, out var transfer));
            return transfer;
        });
    }

    public static Task<ulong> ReceiveFileAsync(ulong node, string targetPath, ushort listenPort, bool resume = true)
    {
        return Task.Run(() =>
        {
            var options = new FileTransferOptions
            {
                LocalPath = targetPath,
                PeerPort = listenPort,
                ResumeEnabled = resume ? (byte)1 : (byte)0,
                ParallelPaths = 1,
                ChunkSize = 64 * 1024
            };
            ThrowIfError(NativeMethods.p2p_file_receive(node, in options, out var transfer));
            return transfer;
        });
    }

    public static Task CancelTransferAsync(ulong transfer)
    {
        return Task.Run(() => ThrowIfError(NativeMethods.p2p_file_cancel(transfer)));
    }

    public static Task<TransferProgress> GetTransferProgressAsync(ulong transfer)
    {
        return Task.Run(() =>
        {
            ThrowIfError(NativeMethods.p2p_file_get_progress(transfer, out var progress));
            return progress;
        });
    }

    public static Task<ulong> CreateStreamAsync(ulong node, string peerHost, ushort peerPort, bool reliable = true)
    {
        return Task.Run(() =>
        {
            var options = new StreamOptions
            {
                PeerHost = peerHost,
                PeerPort = peerPort,
                Reliable = reliable ? (byte)1 : (byte)0,
                MaxFrameSize = 64 * 1024
            };
            ThrowIfError(NativeMethods.p2p_stream_create(node, in options, out var stream));
            return stream;
        });
    }

    public static Task<ulong> StartTunnelAsync(ulong node, string localBindAddress, ushort localPort, string peerHost, ushort peerPort, TunnelProtocol protocol, bool allowLanClients)
    {
        return Task.Run(() =>
        {
            var options = new TunnelOptions
            {
                LocalBindAddress = localBindAddress,
                LocalPort = localPort,
                PeerHost = peerHost,
                PeerPort = peerPort,
                Protocol = protocol == TunnelProtocol.Udp ? NativeTunnelProtocol.Udp : NativeTunnelProtocol.Tcp,
                AggressiveReconnect = 1,
                AllowLanClients = allowLanClients ? (byte)1 : (byte)0
            };
            ThrowIfError(NativeMethods.p2p_tunnel_start(node, in options, out var tunnel));
            return tunnel;
        });
    }

    public static Task StopTunnelAsync(ulong tunnel)
    {
        return Task.Run(() => ThrowIfError(NativeMethods.p2p_tunnel_stop(tunnel)));
    }

    public static Task<TunnelMetrics> GetTunnelMetricsAsync(ulong tunnel)
    {
        return Task.Run(() =>
        {
            ThrowIfError(NativeMethods.p2p_tunnel_get_metrics(tunnel, out var metrics));
            return metrics;
        });
    }

    public static Task<ulong> ConnectGatewayAsync(string gatewayHost, ushort controlPort, string token, string roomCode, string publicCode, bool autoReconnect = true)
    {
        RegisterCallbacks();
        return Task.Run(() =>
        {
            var config = new GatewayConfig
            {
                GatewayHost = gatewayHost,
                ControlPort = controlPort,
                Token = token,
                RoomCode = roomCode,
                PublicCode = publicCode,
                AutoReconnect = autoReconnect ? (byte)1 : (byte)0
            };
            ThrowIfError(NativeMethods.p2p_gateway_connect(in config, out var gateway));
            return gateway;
        });
    }

    public static Task DisconnectGatewayAsync(ulong gateway)
    {
        return Task.Run(() => ThrowIfError(NativeMethods.p2p_gateway_disconnect(gateway)));
    }

    public static Task<ulong> StartGatewayTunnelAsync(ulong gateway, string name, string localHost, ushort localPort, ushort publicPort, TunnelProtocol protocol)
    {
        return Task.Run(() =>
        {
            var options = new GatewayTunnelOptions
            {
                Name = name,
                LocalHost = localHost,
                LocalPort = localPort,
                PublicPort = publicPort,
                Protocol = protocol == TunnelProtocol.Udp ? NativeTunnelProtocol.Udp : NativeTunnelProtocol.Tcp,
                AllowUdpOverGateway = protocol == TunnelProtocol.Udp ? (byte)1 : (byte)0
            };
            ThrowIfError(NativeMethods.p2p_gateway_start_tunnel(gateway, in options, out var tunnel));
            return tunnel;
        });
    }

    public static Task StopGatewayTunnelAsync(ulong tunnel)
    {
        return Task.Run(() => ThrowIfError(NativeMethods.p2p_gateway_stop_tunnel(tunnel)));
    }

    public static Task<GatewayMetrics> GetGatewayMetricsAsync(ulong gateway, ulong tunnel = 0)
    {
        return Task.Run(() =>
        {
            ThrowIfError(NativeMethods.p2p_gateway_get_metrics(gateway, tunnel, out var metrics));
            return metrics;
        });
    }

    public static Task<string> ValidateProxyProfileAsync(string profileJson)
    {
        return Task.Run(() =>
        {
            var message = new StringBuilder(1024);
            var errorCode = NativeMethods.p2p_proxy_validate_json(profileJson, message, (uint)message.Capacity);
            if (errorCode != 0)
            {
                throw new NativeCallException(errorCode, message.Length > 0 ? message.ToString() : "Proxy Profile 校验失败。");
            }
            return message.ToString();
        });
    }

    public static Task<ulong> StartProxyProfileAsync(ulong node, string profileJson)
    {
        RegisterCallbacks();
        return Task.Run(() =>
        {
            ThrowIfError(NativeMethods.p2p_proxy_start_json(node, profileJson, out var proxy));
            return proxy;
        });
    }

    public static Task StopProxyAsync(ulong proxy)
    {
        return Task.Run(() => ThrowIfError(NativeMethods.p2p_proxy_stop(proxy)));
    }

    public static Task<ProxyMetrics> GetProxyMetricsAsync(ulong proxy)
    {
        return Task.Run(() =>
        {
            ThrowIfError(NativeMethods.p2p_proxy_get_metrics(proxy, out var metrics));
            return metrics;
        });
    }

    public static Task<ulong> StartMeshAsync(string configJson)
    {
        return Task.Run(() =>
        {
            ThrowIfError(NativeMethods.p2p_mesh_start_json(configJson, out var handle));
            return handle;
        });
    }

    public static Task<ulong> StartDhtAsync(string configJson)
    {
        return Task.Run(() =>
        {
            ThrowIfError(NativeMethods.p2p_dht_start_json(configJson, out var handle));
            return handle;
        });
    }

    public static Task<ulong> StartGameVlanAsync(string configJson)
    {
        return Task.Run(() =>
        {
            ThrowIfError(NativeMethods.p2p_game_vlan_start_json(configJson, out var handle));
            return handle;
        });
    }

    public static Task<ulong> StartRemoteSessionAsync(string configJson)
    {
        return Task.Run(() =>
        {
            ThrowIfError(NativeMethods.p2p_remote_session_start_json(configJson, out var handle));
            return handle;
        });
    }

    public static Task<ulong> StartFileSyncAsync(string configJson)
    {
        return Task.Run(() =>
        {
            ThrowIfError(NativeMethods.p2p_file_sync_start_json(configJson, out var handle));
            return handle;
        });
    }

    public static Task<string> GetRouteCandidatesJsonAsync(string requestJson)
    {
        return Task.Run(() => CallJsonBufferApi(requestJson, NativeMethods.p2p_route_get_candidates));
    }

    public static Task<string> AuthorizeIdentityJsonAsync(string policyJson)
    {
        return Task.Run(() => CallJsonBufferApi(policyJson, NativeMethods.p2p_identity_authorize_json));
    }

    public static Task<string> QueryAuditJsonAsync(string queryJson)
    {
        return Task.Run(() => CallJsonBufferApi(queryJson, NativeMethods.p2p_audit_query_json));
    }

    public static Task<string> LoadPluginJsonAsync(string manifestJson)
    {
        return Task.Run(() => CallJsonBufferApi(manifestJson, NativeMethods.p2p_plugin_load_json));
    }

    public static Task<string> DiagnoseAiJsonAsync(string diagnosticsJson)
    {
        return Task.Run(() => CallJsonBufferApi(diagnosticsJson, NativeMethods.p2p_ai_diagnose_json));
    }

    private delegate int JsonBufferApi(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string inputJson,
        StringBuilder responseJsonBuffer,
        uint responseJsonBufferLength);

    private static string CallJsonBufferApi(string inputJson, JsonBufferApi api)
    {
        var buffer = new StringBuilder(8192);
        ThrowIfError(api(inputJson, buffer, (uint)buffer.Capacity));
        return buffer.ToString();
    }

    public static Task<SecurityPairingResult> GeneratePairingCodesAsync(string deviceCode)
    {
        return Task.Run(() =>
        {
            ThrowIfError(NativeMethods.p2p_security_generate_pairing_codes(deviceCode, out var result));
            return result;
        });
    }

    public static Task<SecurityPublicResult> PrivateToPublicAsync(string privateCode)
    {
        return Task.Run(() =>
        {
            var publicCode = new StringBuilder(129);
            var publicHash = new StringBuilder(65);
            ThrowIfError(NativeMethods.p2p_security_private_to_public(privateCode, publicCode, (uint)publicCode.Capacity, publicHash, (uint)publicHash.Capacity));
            return new SecurityPublicResult(publicCode.ToString(), publicHash.ToString());
        });
    }

    public static Task<SecurityPublicResult> PrivateGroupToPublicAsync(IEnumerable<string> privateCodes)
    {
        return Task.Run(() =>
        {
            var normalized = string.Join(
                "\n",
                privateCodes
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x)));
            var publicCode = new StringBuilder(129);
            var publicHash = new StringBuilder(65);
            ThrowIfError(NativeMethods.p2p_security_private_group_to_public(normalized, publicCode, (uint)publicCode.Capacity, publicHash, (uint)publicHash.Capacity));
            return new SecurityPublicResult(publicCode.ToString(), publicHash.ToString());
        });
    }

    public static Task<bool> ValidatePairingAsync(string privateCode, string publicCode)
    {
        return Task.Run(() =>
        {
            ThrowIfError(NativeMethods.p2p_security_validate_pairing(privateCode, publicCode, out var valid));
            return valid != 0;
        });
    }

    private static void RegisterCallbacks()
    {
        if (_callbacksRegistered)
        {
            return;
        }

        ThrowIfError(NativeMethods.p2p_nat_set_callback(NativeNatCallback, IntPtr.Zero));
        ThrowIfError(NativeMethods.p2p_file_set_callback(NativeTransferCallback, IntPtr.Zero));
        ThrowIfError(NativeMethods.p2p_tunnel_set_callback(NativeTunnelCallback, IntPtr.Zero));
        ThrowIfError(NativeMethods.p2p_gateway_set_callback(NativeGatewayCallback, IntPtr.Zero));
        ThrowIfError(NativeMethods.p2p_proxy_set_callback(NativeProxyCallback, IntPtr.Zero));
        _callbacksRegistered = true;
    }

    private static IntPtr ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal))
        {
            return IntPtr.Zero;
        }

        var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "p2p_core.dll"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "libp2p_core.dylib"
                : "libp2p_core.so";

        var rid = RuntimeInformation.RuntimeIdentifier;
        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, fileName),
            Path.Combine(baseDirectory, "runtimes", rid, "native", fileName),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "..", "build", "bin", "Debug", fileName)),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "..", "build", "bin", fileName))
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
            {
                return handle;
            }
        }

        return NativeLibrary.TryLoad(fileName, assembly, searchPath, out var fallback) ? fallback : IntPtr.Zero;
    }

    private static void ThrowIfError(int errorCode)
    {
        if (errorCode == 0)
        {
            return;
        }

        var buffer = new StringBuilder(1024);
        _ = NativeMethods.p2p_get_last_error(buffer, (uint)buffer.Capacity);
        var message = buffer.Length > 0 ? buffer.ToString() : $"Native call failed with error code {errorCode}.";
        throw new NativeCallException(errorCode, message);
    }

    private static void HandleNatCallback(NatEvent eventType, int errorCode, string? address, ushort port, IntPtr userData)
    {
        NatEvent?.Invoke(null, new NatEventArgs(eventType, errorCode, address ?? "", port));
    }

    private static void HandleTransferCallback(ulong transfer, NativeTransferEvent eventType, IntPtr progress, IntPtr userData)
    {
        TransferProgress? managedProgress = progress == IntPtr.Zero ? null : Marshal.PtrToStructure<TransferProgress>(progress);
        TransferEvent?.Invoke(null, new TransferEventArgs(transfer, eventType, managedProgress));
    }

    private static void HandleTunnelCallback(ulong tunnel, NativeTunnelEvent eventType, IntPtr metrics, IntPtr userData)
    {
        TunnelMetrics? managedMetrics = metrics == IntPtr.Zero ? null : Marshal.PtrToStructure<TunnelMetrics>(metrics);
        TunnelEvent?.Invoke(null, new TunnelEventArgs(tunnel, eventType, managedMetrics));
    }

    private static void HandleGatewayCallback(ulong gateway, ulong tunnel, NativeGatewayEvent eventType, int errorCode, string? message, IntPtr metrics, IntPtr userData)
    {
        GatewayMetrics? managedMetrics = metrics == IntPtr.Zero ? null : Marshal.PtrToStructure<GatewayMetrics>(metrics);
        GatewayEvent?.Invoke(null, new GatewayEventArgs(gateway, tunnel, eventType, errorCode, message ?? "", managedMetrics));
    }

    private static void HandleProxyCallback(ulong proxy, NativeProxyEvent eventType, int errorCode, string? message, IntPtr metrics, IntPtr userData)
    {
        ProxyMetrics? managedMetrics = metrics == IntPtr.Zero ? null : Marshal.PtrToStructure<ProxyMetrics>(metrics);
        ProxyEvent?.Invoke(null, new ProxyEventArgs(proxy, eventType, errorCode, message ?? "", managedMetrics));
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NatCallback(NatEvent eventType, int errorCode, [MarshalAs(UnmanagedType.LPUTF8Str)] string? address, ushort port, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void TransferCallback(ulong transfer, NativeTransferEvent eventType, IntPtr progress, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void TunnelCallback(ulong tunnel, NativeTunnelEvent eventType, IntPtr metrics, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GatewayCallback(ulong gateway, ulong tunnel, NativeGatewayEvent eventType, int errorCode, [MarshalAs(UnmanagedType.LPUTF8Str)] string? message, IntPtr metrics, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ProxyCallback(ulong proxy, NativeProxyEvent eventType, int errorCode, [MarshalAs(UnmanagedType.LPUTF8Str)] string? message, IntPtr metrics, IntPtr userData);

    private static class NativeMethods
    {
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_initialize();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_shutdown();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_node_start(in NodeConfig config, out ulong node);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_node_stop(ulong node);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_node_get_metrics(ulong node, out NodeMetrics metrics);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_get_last_error(StringBuilder buffer, uint bufferLength);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_nat_set_callback(NatCallback callback, IntPtr userData);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_nat_udp_punch(ulong node, [MarshalAs(UnmanagedType.LPUTF8Str)] string peerHost, ushort peerPort, uint attempts, uint intervalMs);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_nat_tcp_punch(ulong node, [MarshalAs(UnmanagedType.LPUTF8Str)] string peerHost, ushort peerPort, uint timeoutMs);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_nat_upnp_add_mapping(ushort localPort, ushort externalPort, [MarshalAs(UnmanagedType.LPUTF8Str)] string protocol, uint leaseSeconds);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_nat_get_external_endpoint(StringBuilder addressBuffer, uint addressBufferLength, out ushort port);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_file_set_callback(TransferCallback callback, IntPtr userData);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_file_send(ulong node, in FileTransferOptions options, out ulong transfer);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_file_receive(ulong node, in FileTransferOptions options, out ulong transfer);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_file_get_progress(ulong transfer, out TransferProgress progress);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_file_cancel(ulong transfer);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_stream_create(ulong node, in StreamOptions options, out ulong stream);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_tunnel_set_callback(TunnelCallback callback, IntPtr userData);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_tunnel_start(ulong node, in TunnelOptions options, out ulong tunnel);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_tunnel_stop(ulong tunnel);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_tunnel_get_metrics(ulong tunnel, out TunnelMetrics metrics);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_gateway_set_callback(GatewayCallback callback, IntPtr userData);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_gateway_connect(in GatewayConfig config, out ulong gateway);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_gateway_disconnect(ulong gateway);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_gateway_start_tunnel(ulong gateway, in GatewayTunnelOptions options, out ulong tunnel);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_gateway_stop_tunnel(ulong tunnel);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_gateway_get_metrics(ulong gateway, ulong tunnel, out GatewayMetrics metrics);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_proxy_set_callback(ProxyCallback callback, IntPtr userData);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_proxy_validate_json([MarshalAs(UnmanagedType.LPUTF8Str)] string profileJson, StringBuilder messageBuffer, uint messageBufferLength);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_proxy_start_json(ulong node, [MarshalAs(UnmanagedType.LPUTF8Str)] string profileJson, out ulong proxy);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_proxy_stop(ulong proxy);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_proxy_get_metrics(ulong proxy, out ProxyMetrics metrics);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_mesh_start_json([MarshalAs(UnmanagedType.LPUTF8Str)] string configJson, out ulong mesh);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_route_get_candidates([MarshalAs(UnmanagedType.LPUTF8Str)] string requestJson, StringBuilder responseJsonBuffer, uint responseJsonBufferLength);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_identity_authorize_json([MarshalAs(UnmanagedType.LPUTF8Str)] string policyJson, StringBuilder responseJsonBuffer, uint responseJsonBufferLength);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_audit_query_json([MarshalAs(UnmanagedType.LPUTF8Str)] string queryJson, StringBuilder responseJsonBuffer, uint responseJsonBufferLength);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_dht_start_json([MarshalAs(UnmanagedType.LPUTF8Str)] string configJson, out ulong dht);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_game_vlan_start_json([MarshalAs(UnmanagedType.LPUTF8Str)] string configJson, out ulong session);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_remote_session_start_json([MarshalAs(UnmanagedType.LPUTF8Str)] string configJson, out ulong session);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_file_sync_start_json([MarshalAs(UnmanagedType.LPUTF8Str)] string configJson, out ulong task);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_plugin_load_json([MarshalAs(UnmanagedType.LPUTF8Str)] string manifestJson, StringBuilder responseJsonBuffer, uint responseJsonBufferLength);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_ai_diagnose_json([MarshalAs(UnmanagedType.LPUTF8Str)] string diagnosticsJson, StringBuilder responseJsonBuffer, uint responseJsonBufferLength);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_security_generate_pairing_codes([MarshalAs(UnmanagedType.LPUTF8Str)] string deviceCode, out SecurityPairingResult result);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_security_private_to_public([MarshalAs(UnmanagedType.LPUTF8Str)] string privateCode, StringBuilder publicCodeBuffer, uint publicCodeBufferLength, StringBuilder publicHashHexBuffer, uint publicHashBufferLength);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_security_private_group_to_public([MarshalAs(UnmanagedType.LPUTF8Str)] string privateCodesText, StringBuilder publicCodeBuffer, uint publicCodeBufferLength, StringBuilder publicHashHexBuffer, uint publicHashBufferLength);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int p2p_security_validate_pairing([MarshalAs(UnmanagedType.LPUTF8Str)] string privateCode, [MarshalAs(UnmanagedType.LPUTF8Str)] string publicCode, out byte isValid);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NodeConfig
    {
        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public string? BindAddress;
        public ushort LocalPort;
        public uint WorkerThreads;
        public byte EnableLanDiscovery;
        public byte EnableUpnp;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NodeMetrics
    {
        public ulong BytesSent;
        public ulong BytesReceived;
        public uint ActiveStreams;
        public uint ActiveTransfers;
        public ushort UdpPort;
        public ushort TcpPort;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FileTransferOptions
    {
        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public string? LocalPath;
        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public string? RemotePath;
        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public string? PeerHost;
        public ushort PeerPort;
        public byte ResumeEnabled;
        public byte ParallelPaths;
        public uint ChunkSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct StreamOptions
    {
        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public string? PeerHost;
        public ushort PeerPort;
        public byte Reliable;
        public uint MaxFrameSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TunnelOptions
    {
        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public string? LocalBindAddress;
        public ushort LocalPort;
        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public string? PeerHost;
        public ushort PeerPort;
        public NativeTunnelProtocol Protocol;
        public byte AggressiveReconnect;
        public byte AllowLanClients;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GatewayConfig
    {
        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public string? GatewayHost;
        public ushort ControlPort;
        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public string? Token;
        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public string? RoomCode;
        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public string? PublicCode;
        public byte AutoReconnect;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GatewayTunnelOptions
    {
        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public string? Name;
        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public string? LocalHost;
        public ushort LocalPort;
        public ushort PublicPort;
        public NativeTunnelProtocol Protocol;
        public byte AllowUdpOverGateway;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TransferProgress
    {
        public ulong TotalBytes;
        public ulong TransferredBytes;
        public ulong BytesPerSecond;
        public float Progress;
        public NativeTransferStatus Status;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TunnelMetrics
    {
        public ulong BytesUp;
        public ulong BytesDown;
        public uint ActiveConnections;
        public ushort LocalPort;
        public ushort PeerPort;
        public NativeTunnelProtocol Protocol;
        public byte Running;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GatewayMetrics
    {
        public ulong BytesUp;
        public ulong BytesDown;
        public uint ActiveConnections;
        public uint ReconnectCount;
        public uint ErrorCount;
        public byte Running;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProxyMetrics
    {
        public ulong BytesUp;
        public ulong BytesDown;
        public uint ActiveConnections;
        public uint ErrorCount;
        public uint ReconnectCount;
        public uint HealthScore;
        public ushort LocalPort;
        public ushort RemotePort;
        public ushort PublicPort;
        public NativeProxyType Type;
        public NativeProxyMode Mode;
        public byte Running;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct SecurityPairingResult
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 65)]
        public string PrivateCode;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 129)]
        public string PublicCode;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 65)]
        public string PrivateHashHex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 65)]
        public string PublicHashHex;
    }
}

internal sealed record SecurityPublicResult(string PublicCode, string PublicHashHex);

internal sealed class NativeCallException : Exception
{
    public NativeCallException(int errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public int ErrorCode { get; }
}

internal sealed record NatEventArgs(NatEvent EventType, int ErrorCode, string Address, ushort Port);

internal sealed record TransferEventArgs(ulong Transfer, NativeTransferEvent EventType, NativeInterop.TransferProgress? Progress);

internal sealed record TunnelEventArgs(ulong Tunnel, NativeTunnelEvent EventType, NativeInterop.TunnelMetrics? Metrics);

internal sealed record GatewayEventArgs(ulong Gateway, ulong Tunnel, NativeGatewayEvent EventType, int ErrorCode, string Message, NativeInterop.GatewayMetrics? Metrics);

internal sealed record ProxyEventArgs(ulong Proxy, NativeProxyEvent EventType, int ErrorCode, string Message, NativeInterop.ProxyMetrics? Metrics);

internal enum NatEvent
{
    StateChanged = 0,
    UdpPunchSuccess = 1,
    UdpPunchFailed = 2,
    TcpPunchSuccess = 3,
    TcpPunchFailed = 4,
    UpnpMappingSuccess = 5,
    UpnpMappingFailed = 6,
    ExternalEndpoint = 7
}

internal enum NativeTransferStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Cancelled = 3,
    Failed = 4
}

internal enum NativeTransferEvent
{
    Started = 0,
    Progress = 1,
    Completed = 2,
    Cancelled = 3,
    Failed = 4
}

internal enum NativeTunnelProtocol
{
    Tcp = 0,
    Udp = 1
}

internal enum NativeTunnelEvent
{
    Started = 0,
    Stopped = 1,
    ConnectionOpened = 2,
    ConnectionClosed = 3,
    Traffic = 4,
    Error = 5
}

internal enum NativeGatewayEvent
{
    Connected = 0,
    Disconnected = 1,
    TunnelRegistered = 2,
    TunnelStopped = 3,
    ConnectionOpened = 4,
    ConnectionClosed = 5,
    Traffic = 6,
    Error = 7,
    Diagnostic = 8
}

internal enum NativeProxyType
{
    Tcp = 0,
    Udp = 1,
    Http = 2,
    Https = 3,
    Stcp = 4,
    Sudp = 5,
    Xtcp = 6,
    TcpMux = 7,
    PortRange = 8
}

internal enum NativeProxyMode
{
    Auto = 0,
    P2P = 1,
    Gateway = 2,
    Secret = 3,
    SmartDirect = 4
}

internal enum NativeProxyEvent
{
    Started = 0,
    Stopped = 1,
    Traffic = 2,
    HealthChanged = 3,
    Diagnostic = 4,
    Error = 5
}
