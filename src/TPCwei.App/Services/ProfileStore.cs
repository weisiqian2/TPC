using System.Text.Json;
using TPC.App.Models;

namespace TPC.App.Services;

public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string BaseDirectory { get; }
    public string ProfilesPath => Path.Combine(BaseDirectory, "profiles.json");

    public ProfileStore(string? baseDirectory = null)
    {
        BaseDirectory = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TPCwei");
        Directory.CreateDirectory(BaseDirectory);
    }

    public async Task<IReadOnlyList<ProxyProfileDefinition>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ProfilesPath))
        {
            return [];
        }

        await using var stream = File.OpenRead(ProfilesPath);
        var profiles = await JsonSerializer.DeserializeAsync<List<ProxyProfileDefinition>>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return profiles ?? [];
    }

    public async Task SaveAllAsync(IEnumerable<ProxyProfileDefinition> profiles, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(BaseDirectory);
        var tempPath = ProfilesPath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, profiles.ToList(), JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(ProfilesPath))
        {
            File.Replace(tempPath, ProfilesPath, null);
        }
        else
        {
            File.Move(tempPath, ProfilesPath);
        }
    }

    public async Task SaveAsync(ProxyProfileDefinition profile, CancellationToken cancellationToken = default)
    {
        var profiles = (await LoadAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var index = profiles.FindIndex(x => string.Equals(x.Id, profile.Id, StringComparison.Ordinal));
        if (index >= 0)
        {
            profiles[index] = profile;
        }
        else
        {
            profiles.Add(profile);
        }

        await SaveAllAsync(profiles, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var profiles = (await LoadAsync(cancellationToken).ConfigureAwait(false))
            .Where(x => !string.Equals(x.Id, id, StringComparison.Ordinal))
            .ToList();
        await SaveAllAsync(profiles, cancellationToken).ConfigureAwait(false);
    }

    public static string ToNativeJson(ProxyProfileDefinition profile)
    {
        var payload = new
        {
            profile.Name,
            type = ToWireType(profile.Type),
            mode = ToWireMode(profile.Mode),
            localHost = profile.LocalHost,
            localPort = profile.LocalPort,
            peerHost = profile.PeerHost,
            remotePort = profile.RemotePort,
            publicPort = profile.PublicPort,
            gatewayHost = profile.GatewayHost,
            gatewayControlPort = profile.GatewayControlPort,
            roomCode = profile.RoomCode,
            roomPasswordHash = profile.RoomPasswordHash,
            connectionPath = profile.ConnectionPath,
            domains = profile.Domains,
            secretMode = profile.SecretMode,
            preferP2p = profile.PreferP2p,
            allowGatewayFallback = profile.AllowGatewayFallback,
            bandwidthLimit = profile.BandwidthLimit,
            healthCheck = profile.HealthCheck,
            autoStart = profile.AutoStart,
            compression = profile.Compression,
            encryption = profile.Encryption
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string ToWireType(ProxyRuleType type) => type switch
    {
        ProxyRuleType.Udp => "udp",
        ProxyRuleType.Http => "http",
        ProxyRuleType.Https => "https",
        ProxyRuleType.Stcp => "stcp",
        ProxyRuleType.Sudp => "sudp",
        ProxyRuleType.Xtcp => "xtcp",
        ProxyRuleType.TcpMux => "tcpmux",
        ProxyRuleType.PortRange => "port-range",
        _ => "tcp"
    };

    private static string ToWireMode(ProxyRuleMode mode) => mode switch
    {
        ProxyRuleMode.P2P => "p2p",
        ProxyRuleMode.Gateway => "gateway",
        ProxyRuleMode.Secret => "secret",
        ProxyRuleMode.SmartDirect => "smart-direct",
        _ => "auto"
    };
}
