using System.Collections.ObjectModel;

namespace TPC.App.Models;

public enum NavigationPageKind
{
    Dashboard,
    Game,
    RemoteDesktop,
    Connection,
    FileTransfer,
    Security,
    PluginMarket,
    Developer,
    Settings
}

public enum RemoteDesktopQuality
{
    Balanced,
    Performance,
    Quality,
    Lossless
}

public enum TunnelProtocol
{
    Tcp,
    Udp
}

public enum ProxyRuleType
{
    Tcp,
    Udp,
    Http,
    Https,
    Stcp,
    Sudp,
    Xtcp,
    TcpMux,
    PortRange
}

public enum ProxyRuleMode
{
    Auto,
    P2P,
    Gateway,
    Secret,
    SmartDirect
}

public sealed record NavigationItem(string Title, string Icon, NavigationPageKind Kind, object Page);

public sealed class ProxyProfileDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Web 服务";
    public string RoomCode { get; set; } = "";
    public string RoomPasswordHash { get; set; } = "";
    public string Role { get; set; } = "";
    public string TrustedPackageHash { get; set; } = "";
    public string TrustedPackage { get; set; } = "";
    public string DisplayStatus { get; set; } = "";
    public string ConnectionPath { get; set; } = "";
    public string LastError { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? LastConnectedAt { get; set; }
    public ProxyRuleType Type { get; set; } = ProxyRuleType.Tcp;
    public ProxyRuleMode Mode { get; set; } = ProxyRuleMode.Auto;
    public string LocalHost { get; set; } = "127.0.0.1";
    public ushort LocalPort { get; set; } = 8080;
    public string PeerHost { get; set; } = "127.0.0.1";
    public ushort RemotePort { get; set; } = 80;
    public ushort PublicPort { get; set; } = 8080;
    public string GatewayHost { get; set; } = "";
    public ushort GatewayControlPort { get; set; } = 7000;
    public string GatewayToken { get; set; } = "";
    public List<string> Domains { get; set; } = new();
    public string SecretMode { get; set; } = "TrustedDevicePackage";
    public bool PreferP2p { get; set; } = true;
    public bool AllowGatewayFallback { get; set; } = true;
    public string BandwidthLimit { get; set; } = "";
    public string HealthCheck { get; set; } = "tcp";
    public bool AutoStart { get; set; }
    public bool Compression { get; set; }
    public bool Encryption { get; set; } = true;
}

public sealed class AppPersonalizationSettings
{
    public string VisualPreset { get; set; } = "Fluent Pro";
    public string BackdropMaterial { get; set; } = "Acrylic 毛玻璃";
    public int GlassTransparency { get; set; } = 55;
    public string AccentColor { get; set; } = "#4F8CFF";
    public int CornerRadius { get; set; } = 8;
    public string AnimationLevel { get; set; } = "流畅";
    public bool CompactLayout { get; set; }
    public bool TrafficGlow { get; set; } = true;
    public string DefaultStartPage { get; set; } = "Connection";
    public bool CloseToTray { get; set; } = true;
    public string DefaultConnectionMode { get; set; } = "自动：P2P 优先，网关回退";
    public bool AdvancedMode { get; set; }
    public bool AdvancedOptionsCollapsed { get; set; }
    public bool TutorialCompleted { get; set; }
    public List<string> DhtBootstrapNodes { get; set; } = new();

    // 安全页已经从界面上拿掉了，但旧配置里可能还有这两个值。
    // 先保留并默认关闭，用户升级时配置文件就不会因为少字段出奇怪问题。
    public string StealthPolicy { get; set; } = "关闭";
    public string TrafficCamouflage { get; set; } = "关闭";

    // 新手创建房间时优先用公网 IPv4。
    // 自动检测看不到路由器外网地址时，手动填写的地址会派上用场。
    public bool PreferPublicIpv4Direct { get; set; } = true;
    public string ManualPublicIpv4 { get; set; } = "";
    public int OfflineMessageHours { get; set; } = 24;
    public int OfflineMessageCapacityMb { get; set; } = 128;
    public Dictionary<string, string> KeyboardShortcuts { get; set; } = new()
    {
        ["CreateRoom"] = "Ctrl+N",
        ["JoinRoom"] = "Ctrl+J",
        ["SendFile"] = "Ctrl+F",
        ["RemoteDesktop"] = "Ctrl+D",
        ["Help"] = "F1"
    };

    public static AppPersonalizationSettings CreateDefault() => new();

    public void Normalize()
    {
        VisualPreset = string.IsNullOrWhiteSpace(VisualPreset) ? "Fluent Pro" : VisualPreset;
        BackdropMaterial = string.IsNullOrWhiteSpace(BackdropMaterial) ? "Acrylic 毛玻璃" : BackdropMaterial;
        GlassTransparency = Math.Clamp(GlassTransparency, 35, 85);
        AccentColor = string.IsNullOrWhiteSpace(AccentColor) ? "#4F8CFF" : AccentColor;
        CornerRadius = Math.Clamp(CornerRadius, 6, 12);
        AnimationLevel = string.IsNullOrWhiteSpace(AnimationLevel) ? "流畅" : AnimationLevel;
        DefaultStartPage = string.IsNullOrWhiteSpace(DefaultStartPage) ? "Connection" : DefaultStartPage;
        if (DefaultStartPage is not ("Connection" or "Dashboard" or "FileTransfer" or "RemoteDesktop" or "Game" or "Settings"))
        {
            DefaultStartPage = "Connection";
        }
        DefaultConnectionMode = string.IsNullOrWhiteSpace(DefaultConnectionMode) ? "自动：P2P 优先，网关回退" : DefaultConnectionMode;
        if (DefaultConnectionMode.Contains("替代 FRP", StringComparison.Ordinal))
        {
            DefaultConnectionMode = "自建网关：跨网兜底";
        }
        StealthPolicy = string.IsNullOrWhiteSpace(StealthPolicy) ? "关闭" : StealthPolicy;
        TrafficCamouflage = string.IsNullOrWhiteSpace(TrafficCamouflage) ? "关闭" : TrafficCamouflage;
        ManualPublicIpv4 = string.IsNullOrWhiteSpace(ManualPublicIpv4) ? "" : ManualPublicIpv4.Trim();
        OfflineMessageHours = Math.Clamp(OfflineMessageHours, 1, 168);
        OfflineMessageCapacityMb = Math.Clamp(OfflineMessageCapacityMb, 16, 4096);
        KeyboardShortcuts ??= new();
        if (!KeyboardShortcuts.ContainsKey("CreateRoom")) KeyboardShortcuts["CreateRoom"] = "Ctrl+N";
        if (!KeyboardShortcuts.ContainsKey("JoinRoom")) KeyboardShortcuts["JoinRoom"] = "Ctrl+J";
        if (!KeyboardShortcuts.ContainsKey("SendFile")) KeyboardShortcuts["SendFile"] = "Ctrl+F";
        if (!KeyboardShortcuts.ContainsKey("RemoteDesktop")) KeyboardShortcuts["RemoteDesktop"] = "Ctrl+D";
        if (!KeyboardShortcuts.ContainsKey("Help")) KeyboardShortcuts["Help"] = "F1";
    }
}

public enum PlatformModuleStatus
{
    Planned,
    SkeletonReady,
    Running,
    Degraded,
    Blocked
}

public sealed class PlatformModuleDefinition
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public PlatformModuleStatus Status { get; set; } = PlatformModuleStatus.SkeletonReady;
    public int HealthScore { get; set; } = 90;
}

public sealed class RouteCandidateDefinition
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public int RttMs { get; set; }
    public double LossRate { get; set; }
    public string Status { get; set; } = "";
}

public sealed class AuditEventDefinition
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string Level { get; set; } = "info";
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";
}

public sealed class PluginManifestDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Version { get; set; } = "0.1.0";
    public string Entry { get; set; } = "";
    public List<string> Permissions { get; set; } = new();
    public bool Sandbox { get; set; } = true;
}

public sealed class GatewayDeployOptions
{
    public string GatewayHost { get; set; } = "YOUR_VPS_IP";
    public ushort ControlPort { get; set; } = 7000;
    public ushort AdminPort { get; set; } = 7400;
    public string Token { get; set; } = "please-change-this-token";
}

public sealed class PeerMember
{
    public string Name { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public int LatencyMs { get; set; }
    public string Status { get; set; } = "";
}

public sealed class TrafficSample
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public double UpBytesPerSecond { get; set; }
    public double DownBytesPerSecond { get; set; }
}

public sealed class FileTransferItem
{
    public string FileName { get; set; } = "";
    public string SizeText { get; set; } = "";
    public double Progress { get; set; }
    public string SpeedText { get; set; } = "";
    public string Status { get; set; } = "";
    public ulong Handle { get; set; }
}

public sealed class TunnelRule
{
    public ulong Handle { get; set; }
    public string Name { get; set; } = "Service";
    public TunnelProtocol Protocol { get; set; } = TunnelProtocol.Tcp;
    public ushort LocalPort { get; set; } = 7000;
    public string PeerHost { get; set; } = "127.0.0.1";
    public ushort PeerPort { get; set; } = 80;
    public bool Running { get; set; }
    public ulong BytesUp { get; set; }
    public ulong BytesDown { get; set; }
    public uint ActiveConnections { get; set; }
    public ObservableCollection<TrafficSample> Samples { get; } = new();
}
