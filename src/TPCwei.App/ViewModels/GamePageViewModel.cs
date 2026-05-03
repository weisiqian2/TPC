using System.Collections.ObjectModel;
using TPC.App.Models;

namespace TPC.App.ViewModels;

public sealed class GamePageViewModel : ObservableObject
{
    private string _roomCode = "TPCWEI-LOCAL";
    private string _publicKey = "";
    private string _privateKey = "";
    private string _status = "等待创建或加入链接";

    public GamePageViewModel()
    {
        CreateLinkCommand = new RelayCommand(() =>
        {
            RoomCode = $"ROOM-{DateTimeOffset.Now:HHmmss}";
            Status = "链接已创建，等待成员加入";
            if (Members.Count == 0)
            {
                Members.Add(new PeerMember { Name = "本机", PublicKey = PublicKey, LatencyMs = 0, Status = "房主" });
            }
        });

        JoinLinkCommand = new RelayCommand(() =>
        {
            Status = string.IsNullOrWhiteSpace(RoomCode) ? "请输入房间码" : "正在尝试加入链接";
        });
    }

    public string RoomCode
    {
        get => _roomCode;
        set => SetProperty(ref _roomCode, value);
    }

    public string PublicKey
    {
        get => _publicKey;
        set => SetProperty(ref _publicKey, value);
    }

    public string PrivateKey
    {
        get => _privateKey;
        set => SetProperty(ref _privateKey, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public ObservableCollection<PeerMember> Members { get; } = new()
    {
        new() { Name = "本机", PublicKey = "尚未生成", LatencyMs = 0, Status = "在线" }
    };

    public RelayCommand CreateLinkCommand { get; }
    public RelayCommand JoinLinkCommand { get; }
}
