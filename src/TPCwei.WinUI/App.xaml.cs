using Microsoft.UI.Xaml;

namespace TPC.WinUI;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new ShellWindow();
            _window.Activate();
        }
        catch (Exception ex)
        {
            File.WriteAllText(
                Path.Combine(AppContext.BaseDirectory, "TPC.WinUI.startup.log"),
                ex.ToString());
            throw;
        }
    }
}
