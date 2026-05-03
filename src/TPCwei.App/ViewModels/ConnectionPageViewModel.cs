using System.Collections.ObjectModel;
using TPC.App.Interop;
using TPC.App.Models;

namespace TPC.App.ViewModels;

public sealed class ConnectionPageViewModel : ObservableObject
{
    private readonly MainWindowViewModel _mainWindow;
    private string _newTunnelName = "Web 服务";
    private TunnelProtocol _newTunnelProtocol = TunnelProtocol.Tcp;
    private ushort _newLocalPort = 8080;
    private string _newPeerHost = "127.0.0.1";
    private ushort _newPeerPort = 80;
    private bool _allowLanClients;
    private string _status = "可创建 TCP/UDP 端口映射，用自己的节点完成跨网连接。";

    public ConnectionPageViewModel(MainWindowViewModel mainWindow)
    {
        _mainWindow = mainWindow;
        StartTunnelCommand = new AsyncRelayCommand(StartTunnelAsync);
        StopSelectedTunnelCommand = new AsyncRelayCommand(StopSelectedTunnelAsync, () => SelectedTunnel is { Running: true });
        ProtocolOptions = new ObservableCollection<TunnelProtocol> { TunnelProtocol.Tcp, TunnelProtocol.Udp };
    }

    public ObservableCollection<TunnelRule> Tunnels { get; } = new();
    public ObservableCollection<TunnelProtocol> ProtocolOptions { get; }

    private TunnelRule? _selectedTunnel;
    public TunnelRule? SelectedTunnel
    {
        get => _selectedTunnel;
        set => SetProperty(ref _selectedTunnel, value);
    }

    public string NewTunnelName
    {
        get => _newTunnelName;
        set => SetProperty(ref _newTunnelName, value);
    }

    public TunnelProtocol NewTunnelProtocol
    {
        get => _newTunnelProtocol;
        set => SetProperty(ref _newTunnelProtocol, value);
    }

    public ushort NewLocalPort
    {
        get => _newLocalPort;
        set => SetProperty(ref _newLocalPort, value);
    }

    public string NewPeerHost
    {
        get => _newPeerHost;
        set => SetProperty(ref _newPeerHost, value);
    }

    public ushort NewPeerPort
    {
        get => _newPeerPort;
        set => SetProperty(ref _newPeerPort, value);
    }

    public bool AllowLanClients
    {
        get => _allowLanClients;
        set => SetProperty(ref _allowLanClients, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public AsyncRelayCommand StartTunnelCommand { get; }
    public AsyncRelayCommand StopSelectedTunnelCommand { get; }

    internal void UpdateTunnelMetrics(ulong handle, NativeInterop.TunnelMetrics metrics)
    {
        var tunnel = Tunnels.FirstOrDefault(x => x.Handle == handle);
        if (tunnel is null)
        {
            return;
        }

        tunnel.BytesUp = metrics.BytesUp;
        tunnel.BytesDown = metrics.BytesDown;
        tunnel.ActiveConnections = metrics.ActiveConnections;
        tunnel.Running = metrics.Running != 0;
        tunnel.Samples.Add(new TrafficSample
        {
            UpBytesPerSecond = metrics.BytesUp,
            DownBytesPerSecond = metrics.BytesDown
        });

        while (tunnel.Samples.Count > 60)
        {
            tunnel.Samples.RemoveAt(0);
        }

        RaisePropertyChanged(nameof(Tunnels));
    }

    private async Task StartTunnelAsync()
    {
        await _mainWindow.EnsureNodeStartedAsync().ConfigureAwait(true);
        var handle = await NativeInterop.StartTunnelAsync(
            _mainWindow.NodeHandle,
            AllowLanClients ? "0.0.0.0" : "127.0.0.1",
            NewLocalPort,
            NewPeerHost,
            NewPeerPort,
            NewTunnelProtocol,
            AllowLanClients).ConfigureAwait(true);

        var rule = new TunnelRule
        {
            Handle = handle,
            Name = NewTunnelName,
            Protocol = NewTunnelProtocol,
            LocalPort = NewLocalPort,
            PeerHost = NewPeerHost,
            PeerPort = NewPeerPort,
            Running = true
        };
        Tunnels.Add(rule);
        SelectedTunnel = rule;
        Status = $"映射已启动：127.0.0.1:{NewLocalPort} -> {NewPeerHost}:{NewPeerPort}";
    }

    private async Task StopSelectedTunnelAsync()
    {
        if (SelectedTunnel is null)
        {
            return;
        }

        await NativeInterop.StopTunnelAsync(SelectedTunnel.Handle).ConfigureAwait(true);
        SelectedTunnel.Running = false;
        Status = $"映射已停止：{SelectedTunnel.Name}";
    }
}
