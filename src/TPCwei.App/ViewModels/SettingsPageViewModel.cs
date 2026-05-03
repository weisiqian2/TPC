namespace TPC.App.ViewModels;

public sealed class SettingsPageViewModel : ObservableObject
{
    private bool _enableUpnp = true;
    private bool _enableLanDiscovery = true;
    private bool _startMinimized;
    private string _language = "简体中文";

    public bool EnableUpnp
    {
        get => _enableUpnp;
        set => SetProperty(ref _enableUpnp, value);
    }

    public bool EnableLanDiscovery
    {
        get => _enableLanDiscovery;
        set => SetProperty(ref _enableLanDiscovery, value);
    }

    public bool StartMinimized
    {
        get => _startMinimized;
        set => SetProperty(ref _startMinimized, value);
    }

    public string Language
    {
        get => _language;
        set => SetProperty(ref _language, value);
    }
}
