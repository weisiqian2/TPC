using System.Collections.ObjectModel;
using TPC.App.Interop;
using TPC.App.Models;

namespace TPC.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly GamePageViewModel _gamePage;
    private readonly RemoteDesktopPageViewModel _remoteDesktopPage;
    private readonly ConnectionPageViewModel _connectionPage;
    private readonly FileTransferPageViewModel _fileTransferPage;
    private readonly SecurityPageViewModel _securityPage;
    private readonly SettingsPageViewModel _settingsPage;
    private NavigationItem? _selectedNavigationItem;
    private ulong _nodeHandle;
    private string _nodeStatus = "核心未启动";

    public MainWindowViewModel()
    {
        _gamePage = new GamePageViewModel();
        _remoteDesktopPage = new RemoteDesktopPageViewModel();
        _connectionPage = new ConnectionPageViewModel(this);
        _fileTransferPage = new FileTransferPageViewModel(this);
        _securityPage = new SecurityPageViewModel(_gamePage);
        _settingsPage = new SettingsPageViewModel();

        NavigationItems =
        [
            new("游戏联机", "🎮", NavigationPageKind.Game, _gamePage),
            new("远程桌面", "▣", NavigationPageKind.RemoteDesktop, _remoteDesktopPage),
            new("连接", "⇄", NavigationPageKind.Connection, _connectionPage),
            new("文件传输", "⇧", NavigationPageKind.FileTransfer, _fileTransferPage),
            new("安全", "◇", NavigationPageKind.Security, _securityPage),
            new("设置", "⚙", NavigationPageKind.Settings, _settingsPage)
        ];

        _selectedNavigationItem = NavigationItems[2];
        StartNodeCommand = new AsyncRelayCommand(StartNodeAsync);
        StopNodeCommand = new AsyncRelayCommand(StopNodeAsync, () => IsNodeRunning);
        NativeInterop.TunnelEvent += OnTunnelEvent;
    }

    public ObservableCollection<NavigationItem> NavigationItems { get; }

    public NavigationItem? SelectedNavigationItem
    {
        get => _selectedNavigationItem;
        set
        {
            if (SetProperty(ref _selectedNavigationItem, value))
            {
                RaisePropertyChanged(nameof(CurrentPage));
            }
        }
    }

    public object? CurrentPage => SelectedNavigationItem?.Page;

    public ulong NodeHandle
    {
        get => _nodeHandle;
        private set
        {
            if (SetProperty(ref _nodeHandle, value))
            {
                RaisePropertyChanged(nameof(IsNodeRunning));
            }
        }
    }

    public bool IsNodeRunning => NodeHandle != 0;

    public string NodeStatus
    {
        get => _nodeStatus;
        set => SetProperty(ref _nodeStatus, value);
    }

    public AsyncRelayCommand StartNodeCommand { get; }
    public AsyncRelayCommand StopNodeCommand { get; }

    public async Task EnsureNodeStartedAsync()
    {
        if (!IsNodeRunning)
        {
            await StartNodeAsync().ConfigureAwait(true);
        }
    }

    private async Task StartNodeAsync()
    {
        await NativeInterop.InitializeAsync().ConfigureAwait(true);
        NodeHandle = await NativeInterop.StartNodeAsync(port: 0, lanDiscovery: true, upnp: true).ConfigureAwait(true);
        var metrics = await NativeInterop.GetNodeMetricsAsync(NodeHandle).ConfigureAwait(true);
        NodeStatus = $"节点运行中：UDP/TCP {metrics.UdpPort}";
    }

    private async Task StopNodeAsync()
    {
        if (NodeHandle == 0)
        {
            return;
        }

        await NativeInterop.StopNodeAsync(NodeHandle).ConfigureAwait(true);
        NodeHandle = 0;
        NodeStatus = "核心已停止";
    }

    private void OnTunnelEvent(object? sender, TunnelEventArgs e)
    {
        if (e.Metrics is { } metrics)
        {
            _connectionPage.UpdateTunnelMetrics(e.Tunnel, metrics);
        }
    }
}
