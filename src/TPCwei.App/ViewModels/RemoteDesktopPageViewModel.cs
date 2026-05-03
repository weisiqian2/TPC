using System.Collections.ObjectModel;
using TPC.App.Models;

namespace TPC.App.ViewModels;

public sealed class RemoteDesktopPageViewModel : ObservableObject
{
    private bool _isConnected;
    private bool _isFullScreen;
    private RemoteDesktopQuality _selectedQuality = RemoteDesktopQuality.Balanced;
    private string _status = "未连接";

    public RemoteDesktopPageViewModel()
    {
        QualityOptions = new ObservableCollection<RemoteDesktopQuality>
        {
            RemoteDesktopQuality.Performance,
            RemoteDesktopQuality.Balanced,
            RemoteDesktopQuality.Quality,
            RemoteDesktopQuality.Lossless
        };

        ToggleConnectionCommand = new RelayCommand(() =>
        {
            IsConnected = !IsConnected;
            Status = IsConnected ? "远程桌面通道已准备" : "未连接";
        });

        ToggleFullScreenCommand = new RelayCommand(() => IsFullScreen = !IsFullScreen);
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (SetProperty(ref _isConnected, value))
            {
                RaisePropertyChanged(nameof(ConnectionButtonText));
            }
        }
    }

    public bool IsFullScreen
    {
        get => _isFullScreen;
        set => SetProperty(ref _isFullScreen, value);
    }

    public RemoteDesktopQuality SelectedQuality
    {
        get => _selectedQuality;
        set => SetProperty(ref _selectedQuality, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string ConnectionButtonText => IsConnected ? "断开" : "连接";
    public ObservableCollection<RemoteDesktopQuality> QualityOptions { get; }
    public RelayCommand ToggleConnectionCommand { get; }
    public RelayCommand ToggleFullScreenCommand { get; }
}
