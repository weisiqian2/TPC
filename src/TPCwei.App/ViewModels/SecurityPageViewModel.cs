using TPC.App.Interop;

namespace TPC.App.ViewModels;

public sealed class SecurityPageViewModel : ObservableObject
{
    private readonly GamePageViewModel _gamePage;
    private string _deviceCode = Environment.MachineName;
    private string _privateCode = "";
    private string _publicCode = "";
    private string _privateHash = "";
    private string _publicHash = "";
    private string _status = "输入设备码后生成配对码。";

    public SecurityPageViewModel(GamePageViewModel gamePage)
    {
        _gamePage = gamePage;
        GenerateCommand = new AsyncRelayCommand(GenerateAsync);
        ValidateCommand = new AsyncRelayCommand(ValidateAsync, () => !string.IsNullOrWhiteSpace(PrivateCode) && !string.IsNullOrWhiteSpace(PublicCode));
    }

    public string DeviceCode
    {
        get => _deviceCode;
        set => SetProperty(ref _deviceCode, value);
    }

    public string PrivateCode
    {
        get => _privateCode;
        set => SetProperty(ref _privateCode, value);
    }

    public string PublicCode
    {
        get => _publicCode;
        set => SetProperty(ref _publicCode, value);
    }

    public string PrivateHash
    {
        get => _privateHash;
        set => SetProperty(ref _privateHash, value);
    }

    public string PublicHash
    {
        get => _publicHash;
        set => SetProperty(ref _publicHash, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public AsyncRelayCommand GenerateCommand { get; }
    public AsyncRelayCommand ValidateCommand { get; }

    private async Task GenerateAsync()
    {
        await NativeInterop.InitializeAsync().ConfigureAwait(true);
        var result = await NativeInterop.GeneratePairingCodesAsync(DeviceCode).ConfigureAwait(true);
        PrivateCode = result.PrivateCode;
        PublicCode = result.PublicCode;
        PrivateHash = result.PrivateHashHex;
        PublicHash = result.PublicHashHex;
        _gamePage.PrivateKey = PrivateCode;
        _gamePage.PublicKey = PublicCode;
        Status = "配对码已生成并同步到游戏联机页面";
    }

    private async Task ValidateAsync()
    {
        var valid = await NativeInterop.ValidatePairingAsync(PrivateCode, PublicCode).ConfigureAwait(true);
        Status = valid ? "公私码匹配成功" : "公私码不匹配";
    }
}
