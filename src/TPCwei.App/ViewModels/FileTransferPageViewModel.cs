using System.Collections.ObjectModel;
using TPC.App.Interop;
using TPC.App.Models;

namespace TPC.App.ViewModels;

public sealed class FileTransferPageViewModel : ObservableObject
{
    private readonly MainWindowViewModel _mainWindow;
    private string _selectedPath = "";
    private string _peerHost = "127.0.0.1";
    private ushort _peerPort = 9090;
    private string _status = "支持拖拽添加文件，默认启用断点续传。";

    public FileTransferPageViewModel(MainWindowViewModel mainWindow)
    {
        _mainWindow = mainWindow;
        SendFileCommand = new AsyncRelayCommand(SendFileAsync, () => !string.IsNullOrWhiteSpace(SelectedPath));
        StartReceiveCommand = new AsyncRelayCommand(StartReceiveAsync);
        NativeInterop.TransferEvent += OnTransferEvent;
    }

    public ObservableCollection<FileTransferItem> Transfers { get; } = new();

    public string SelectedPath
    {
        get => _selectedPath;
        set => SetProperty(ref _selectedPath, value);
    }

    public string PeerHost
    {
        get => _peerHost;
        set => SetProperty(ref _peerHost, value);
    }

    public ushort PeerPort
    {
        get => _peerPort;
        set => SetProperty(ref _peerPort, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public AsyncRelayCommand SendFileCommand { get; }
    public AsyncRelayCommand StartReceiveCommand { get; }

    public void AddDroppedFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            SelectedPath = path;
            Transfers.Add(new FileTransferItem
            {
                FileName = Path.GetFileName(path),
                SizeText = File.Exists(path) ? FormatBytes((ulong)new FileInfo(path).Length) : "未知",
                Progress = 0,
                SpeedText = "-",
                Status = "等待发送"
            });
        }
    }

    private async Task SendFileAsync()
    {
        await _mainWindow.EnsureNodeStartedAsync().ConfigureAwait(true);
        var handle = await NativeInterop.SendFileAsync(_mainWindow.NodeHandle, SelectedPath, PeerHost, PeerPort).ConfigureAwait(true);
        Transfers.Add(new FileTransferItem
        {
            Handle = handle,
            FileName = Path.GetFileName(SelectedPath),
            SizeText = File.Exists(SelectedPath) ? FormatBytes((ulong)new FileInfo(SelectedPath).Length) : "未知",
            Progress = 0,
            SpeedText = "-",
            Status = "发送中"
        });
        Status = "文件发送任务已创建";
    }

    private async Task StartReceiveAsync()
    {
        await _mainWindow.EnsureNodeStartedAsync().ConfigureAwait(true);
        var target = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var handle = await NativeInterop.ReceiveFileAsync(_mainWindow.NodeHandle, target, PeerPort).ConfigureAwait(true);
        Transfers.Add(new FileTransferItem
        {
            Handle = handle,
            FileName = "等待对端文件",
            SizeText = "-",
            Progress = 0,
            SpeedText = "-",
            Status = "监听接收"
        });
        Status = $"正在监听端口 {PeerPort}";
    }

    private void OnTransferEvent(object? sender, TransferEventArgs e)
    {
        if (e.Progress is not { } progress)
        {
            return;
        }

        var item = Transfers.FirstOrDefault(x => x.Handle == e.Transfer);
        if (item is null)
        {
            return;
        }

        item.Progress = Math.Clamp(progress.Progress * 100.0, 0, 100);
        item.SpeedText = FormatBytes(progress.BytesPerSecond) + "/s";
        item.SizeText = progress.TotalBytes == 0 ? item.SizeText : FormatBytes(progress.TotalBytes);
        item.Status = e.EventType switch
        {
            NativeTransferEvent.Started => "传输开始",
            NativeTransferEvent.Progress => "传输中",
            NativeTransferEvent.Completed => "已完成",
            NativeTransferEvent.Cancelled => "已取消",
            NativeTransferEvent.Failed => "失败",
            _ => item.Status
        };
        RaisePropertyChanged(nameof(Transfers));
    }

    private static string FormatBytes(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
