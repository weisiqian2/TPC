using TPC.App.Models;

namespace TPC.App.Services;

public sealed class FrpImportResult
{
    public List<ProxyProfileDefinition> Profiles { get; } = new();
    public List<string> UnsupportedItems { get; } = new();
}

public sealed class FrpImportService
{
    public FrpImportResult ImportText(string text)
    {
        var result = new FrpImportResult();
        var currentName = "";
        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Flush()
        {
            if (current.Count == 0 || string.IsNullOrWhiteSpace(currentName))
            {
                current.Clear();
                return;
            }

            result.Profiles.Add(ToProfile(currentName, current, result.UnsupportedItems));
            current.Clear();
        }

        foreach (var rawLine in (text ?? "").Split(["\r\n", "\n", "\r"], StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                Flush();
                currentName = line.Trim('[', ']').Trim('"');
                continue;
            }

            var equal = line.IndexOf('=');
            if (equal <= 0)
            {
                continue;
            }

            var key = line[..equal].Trim();
            var value = line[(equal + 1)..].Trim().Trim('"', '\'');
            current[key] = value;
        }

        Flush();
        return result;
    }

    public async Task<FrpImportResult> ImportFileAsync(string path, CancellationToken cancellationToken = default)
    {
        var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return ImportText(text);
    }

    private static ProxyProfileDefinition ToProfile(string name, IReadOnlyDictionary<string, string> values, List<string> unsupported)
    {
        var type = values.TryGetValue("type", out var typeValue) ? typeValue : "tcp";
        var profile = new ProxyProfileDefinition
        {
            Name = name,
            Type = ParseType(type),
            Mode = ProxyRuleMode.Gateway,
            LocalHost = values.TryGetValue("localIP", out var localIp) ? localIp : "127.0.0.1",
            PeerHost = "127.0.0.1",
            LocalPort = ParsePort(values, "localPort", 80),
            RemotePort = ParsePort(values, "localPort", 80),
            PublicPort = ParsePort(values, "remotePort", ParsePort(values, "localPort", 80)),
            PreferP2p = false,
            AllowGatewayFallback = true,
            HealthCheck = type.Equals("http", StringComparison.OrdinalIgnoreCase) ? "http" : "tcp"
        };

        if (values.TryGetValue("customDomains", out var domains))
        {
            profile.Domains = domains
                .Split([','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        foreach (var key in values.Keys)
        {
            if (!KnownKeys.Contains(key))
            {
                unsupported.Add($"{name}.{key}");
            }
        }

        return profile;
    }

    private static readonly HashSet<string> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "type",
        "localIP",
        "localPort",
        "remotePort",
        "customDomains"
    };

    private static ushort ParsePort(IReadOnlyDictionary<string, string> values, string key, ushort fallback)
    {
        return values.TryGetValue(key, out var text) && ushort.TryParse(text, out var port) && port > 0
            ? port
            : fallback;
    }

    private static ProxyRuleType ParseType(string type) => type.ToLowerInvariant() switch
    {
        "udp" => ProxyRuleType.Udp,
        "http" => ProxyRuleType.Http,
        "https" => ProxyRuleType.Https,
        "stcp" => ProxyRuleType.Stcp,
        "sudp" => ProxyRuleType.Sudp,
        "xtcp" => ProxyRuleType.Xtcp,
        "tcpmux" => ProxyRuleType.TcpMux,
        _ => ProxyRuleType.Tcp
    };
}
