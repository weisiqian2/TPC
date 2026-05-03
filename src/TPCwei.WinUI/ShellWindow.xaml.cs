using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using TPC.App.Interop;
using TPC.App.Models;
using TPC.App.Services;
using WinRT.Interop;

namespace TPC.WinUI;

public sealed partial class ShellWindow : Window
{
    private readonly ObservableCollection<TunnelViewItem> _tunnels = new();
    private readonly ObservableCollection<MemberViewItem> _members = new();
    private readonly ObservableCollection<TransferViewItem> _transfers = new();
    private readonly ObservableCollection<string> _diagnostics = new();
    private readonly ObservableCollection<string> _privateCodeItems = new();
    private readonly ObservableCollection<ProxyProfileDefinition> _createdProfiles = new();
    private readonly ProfileStore _profileStore = new();
    private readonly PersonalizationStore _personalizationStore = new();
    private readonly AgentClient _agentClient = new();
    private readonly GatewayDeployService _gatewayDeployService = new();
    private static AppPersonalizationSettings _activePersonalization = AppPersonalizationSettings.CreateDefault();

    private Grid _titleBarDragRegion = null!;
    private Grid _titleBar = null!;
    private NavigationView _rootNavigation = null!;
    private TextBlock _nodeStatusText = null!;
    private TextBlock _tunnelStatusText = null!;
    private TextBlock _remoteStatusText = null!;
    private TextBox _tunnelNameBox = null!;
    private TextBox _peerHostBox = null!;
    private TextBox _simplePeerHostBox = null!;
    private TextBox _roomCodeBox = null!;
    private TextBox _roomPasswordBox = null!;
    private TextBox _publicCodeBox = null!;
    private TextBox _privateCodeBox = null!;
    private TextBox _groupPrivateCodesBox = null!;
    private TextBox _filePathBox = null!;
    private TextBox _filePeerHostBox = null!;
    private TextBox _inviteCodeBox = null!;
    private TextBox _joinPublicCodeBox = null!;
    private TextBox _joinPrivateCodeBox = null!;
    private TextBox _gatewayHostBox = null!;
    private TextBox _gatewayTokenBox = null!;
    private ComboBox _quickScenarioCombo = null!;
    private ComboBox _connectionModeCombo = null!;
    private ComboBox _gameProtocolCombo = null!;
    private ComboBox _tunnelProtocolCombo = null!;
    private ComboBox _qualityCombo = null!;
    private ComboBox _visualPresetCombo = null!;
    private ComboBox _backdropMaterialCombo = null!;
    private ComboBox _accentColorCombo = null!;
    private ComboBox _cornerRadiusCombo = null!;
    private ComboBox _animationLevelCombo = null!;
    private ComboBox _layoutDensityCombo = null!;
    private ComboBox _defaultStartPageCombo = null!;
    private ComboBox _defaultConnectionModeCombo = null!;
    private TextBox _dhtBootstrapBox = null!;
    private TextBox _manualPublicIpv4Box = null!;
    private TextBox _shortcutCreateBox = null!;
    private TextBox _shortcutJoinBox = null!;
    private TextBox _shortcutFileBox = null!;
    private TextBox _shortcutDesktopBox = null!;
    private TextBox _shortcutHelpBox = null!;
    private NumberBox _localPortBox = null!;
    private NumberBox _peerPortBox = null!;
    private NumberBox _publicPortBox = null!;
    private NumberBox _gatewayPortBox = null!;
    private NumberBox _filePeerPortBox = null!;
    private NumberBox _offlineMessageHoursBox = null!;
    private NumberBox _offlineMessageCapacityBox = null!;
    private CheckBox _allowLanClientsBox = null!;
    private Border _advancedTunnelCard = null!;
    private Border _gameSetupCard = null!;
    private ListView _tunnelList = null!;
    private ListView _membersList = null!;
    private ListView _transferList = null!;
    private ListView _diagnosticsList = null!;
    private TrafficChart _trafficChart = null!;
    private Button _remoteConnectButton = null!;
    private TextBlock _linkSummaryText = null!;
    private TextBlock _healthStatusText = null!;
    private TextBlock _connectionListTotalText = null!;
    private TextBlock _connectionListRunningText = null!;
    private TextBlock _connectionListHealthText = null!;
    private TextBlock _connectionListTrafficText = null!;
    private TextBlock _connectionListNetworkText = null!;
    private TextBlock _meshStatusText = null!;
    private TextBlock _publicIpv4StatusText = null!;
    private Grid _connectionListSection = null!;
    private StackPanel _connectionListRowsHost = null!;
    private StackPanel _connectedDevicesRowsHost = null!;
    private Grid _connectionCreateSection = null!;
    private Grid _connectionJoinSection = null!;
    private Grid _connectionManageSection = null!;
    private Grid _connectionCreateAdvancedHost = null!;
    private FrameworkElement _connectionCreateAdvancedHintCard = null!;
    private Grid _connectionManageAdvancedHost = null!;
    private Button _advancedCollapseButton = null!;
    private bool _advancedOptionsCollapsed;
    private Button _connectionCreateTab = null!;
    private Button _connectionJoinTab = null!;
    private Button _connectionManageTab = null!;
    private ToggleSwitch _preferP2pSwitch = null!;
    private ToggleSwitch _allowGatewayFallbackSwitch = null!;
    private ToggleSwitch _enableUpnpSwitch = null!;
    private ToggleSwitch _enableTcpPunchSwitch = null!;
    private ToggleSwitch _enableUdpPunchSwitch = null!;
    private ToggleSwitch _dualChannelSwitch = null!;
    private ToggleSwitch _encryptionSwitch = null!;
    private ToggleSwitch _compressionSwitch = null!;
    private ToggleSwitch _trafficGlowSwitch = null!;
    private ToggleSwitch _closeToTraySwitch = null!;
    private ToggleSwitch _advancedModeSwitch = null!;
    private ToggleSwitch _preferPublicIpv4Switch = null!;
    private Slider _glassTransparencySlider = null!;
    private ComboBox _healthCheckCombo = null!;
    private FrameworkElement _connectionCreateQuickActions = null!;
    private FrameworkElement _connectionManageAdvancedActions = null!;
    private FrameworkElement _connectionManageAdvancedBody = null!;
    private FrameworkElement _networkAdvancedActions = null!;
    private FrameworkElement _connectionPage = null!;
    private FrameworkElement _dashboardPage = null!;
    private FrameworkElement _gamePage = null!;
    private FrameworkElement _remoteDesktopPage = null!;
    private FrameworkElement _fileTransferPage = null!;
    private FrameworkElement _developerPage = null!;
    private FrameworkElement _settingsPage = null!;
    private Border _helpPanel = null!;
    private TextBox _helpSearchBox = null!;
    private Win32TrayIcon? _trayIcon;
    private AppWindow? _appWindow;
    private IntPtr _hwnd;
    private bool _exitRequested;
    private bool _suppressNavigationSelection;
    private bool _isRootTransitioning;
    private bool _isConnectionSectionTransitioning;
    private bool _friendConnectionBusy;
    private string _currentRootPageTag = "Connection";
    private string _currentConnectionSection = "List";

    private ulong _nodeHandle;
    private ulong _gatewayHandle;
    private bool _remoteConnected;
    private bool _loadingPersonalizationUi;
    private AppPersonalizationSettings _personalization = AppPersonalizationSettings.CreateDefault();
    private string _currentRoomCode = "";
    private string _currentPrivateCode = "";
    private string _currentPublicCode = "";
    private string _currentPublicHash = "";
    private string _currentInviteCode = "";
    private string _deviceName = Environment.MachineName;

    public ShellWindow()
    {
        InitializeComponent();
        _personalization = _personalizationStore.Load();
        _advancedOptionsCollapsed = _personalization.AdvancedOptionsCollapsed;
        _activePersonalization = _personalization;
        RootGrid.Background = Brush(RootOverlayColor());
        BuildInterface();
        ConfigureWindow();
        ApplyPersonalization(_personalization, save: false, navigateToDefaultPage: true);
        InitializeUiData();
        _ = _personalizationStore.SaveAsync(_personalization);
        if (!string.IsNullOrWhiteSpace(_personalizationStore.LastLoadError))
        {
            AppendDiagnostic($"个性化配置读取失败，已回退默认值：{_personalizationStore.LastLoadError}");
        }
        NativeInterop.TunnelEvent += OnTunnelEvent;
        NativeInterop.TransferEvent += OnTransferEvent;
        NativeInterop.GatewayEvent += OnGatewayEvent;
        NativeInterop.ProxyEvent += OnProxyEvent;
        RootGrid.Loaded += RootGrid_Loaded;
        Closed += (_, _) => _trayIcon?.Dispose();
    }

    private void ConfigureWindow()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(_titleBarDragRegion);
        TryEnableBackdrop();

        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow = appWindow;
        appWindow.Title = "TPC";
        var iconPath = Path.Combine(AppContext.BaseDirectory, "favicon.ico");
        if (File.Exists(iconPath))
        {
            appWindow.SetIcon(iconPath);
        }
        appWindow.Resize(new Windows.Graphics.SizeInt32(1240, 800));
        appWindow.Closing += AppWindow_Closing;

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 980;
            presenter.PreferredMinimumHeight = 640;
        }

        ConfigureTray();
    }

    private void TryEnableBackdrop()
    {
        if (string.Equals(_personalization.BackdropMaterial, "纯色高性能", StringComparison.Ordinal))
        {
            SystemBackdrop = null;
            return;
        }

        try
        {
            SystemBackdrop = string.Equals(_personalization.BackdropMaterial, "Mica 云母", StringComparison.Ordinal)
                ? new MicaBackdrop()
                : new DesktopAcrylicBackdrop();
        }
        catch
        {
            try
            {
                SystemBackdrop = string.Equals(_personalization.BackdropMaterial, "Mica 云母", StringComparison.Ordinal)
                    ? new DesktopAcrylicBackdrop()
                    : new MicaBackdrop();
            }
            catch
            {
                SystemBackdrop = null;
            }
        }
    }

    private void ConfigureTray()
    {
        _trayIcon = new Win32TrayIcon(
            () => DispatcherQueue.TryEnqueue(ShowMainWindow),
            () => DispatcherQueue.TryEnqueue(() => AppendDiagnostic("已收到暂停全部请求；正在运行的规则可在列表中逐个停止。")),
            () => DispatcherQueue.TryEnqueue(() => AppendDiagnostic("已收到恢复全部请求；自动启动规则由后台 Agent 接管。")),
            () => DispatcherQueue.TryEnqueue(() =>
            {
                _exitRequested = true;
                Close();
            }));
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Loaded -= RootGrid_Loaded;
        if (_personalization.TutorialCompleted)
        {
            return;
        }

        await ShowFirstRunTutorialAsync();
    }

    private async Task ShowFirstRunTutorialAsync()
    {
        var tutorial = new Grid { RowSpacing = 14, MaxWidth = 640 };
        for (var i = 0; i < 5; i++)
        {
            tutorial.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        tutorial.Children.Add(new TextBlock
        {
            Text = "TPC 默认是新手模式：少按钮、少参数、先完成连接。下面 4 步可以直接跳过，跳过后使用推荐配置。",
            Foreground = Brush("#E8FFFFFF"),
            TextWrapping = TextWrapping.Wrap
        });

        var form = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var purpose = Combo("步骤 1：选择用途", "综合", "游戏联机", "远程桌面", "文件传输");
        var deviceName = TextBox("步骤 3：设备名称", Environment.MachineName);
        var autoStart = new ToggleSwitch { Header = "步骤 4：开机自启后台 Agent", IsOn = false };
        var advanced = new ToggleSwitch { Header = "进入后开启高级模式", IsOn = false };
        AddToGrid(form, purpose, 0, 0);
        AddToGrid(form, deviceName, 0, 1);
        AddToGrid(form, autoStart, 1, 0);
        AddToGrid(form, advanced, 1, 1);
        Grid.SetRow(form, 1);
        tutorial.Children.Add(form);

        var detect = new Grid { ColumnSpacing = 10 };
        detect.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        detect.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var progress = new ProgressBar { IsIndeterminate = true, Height = 4, VerticalAlignment = VerticalAlignment.Center };
        var detectText = new TextBlock { Text = "步骤 2：正在使用推荐网络策略：直连优先，失败后尝试备用方式。", Foreground = Brush("#B8FFFFFF"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        detect.Children.Add(detectText);
        AddToGrid(detect, progress, 0, 1);
        Grid.SetRow(detect, 2);
        tutorial.Children.Add(detect);

        var miniFlow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        miniFlow.Children.Add(AdvancedHintChip("创建房间", "生成设备包"));
        miniFlow.Children.Add(AdvancedHintChip("加入房间", "粘贴即可"));
        miniFlow.Children.Add(AdvancedHintChip("快速连接", "自动选择"));
        miniFlow.Children.Add(AdvancedHintChip("高级模式", "设置中开启"));
        Grid.SetRow(miniFlow, 3);
        tutorial.Children.Add(miniFlow);

        var note = new TextBlock
        {
            Text = "需要手动填写 P2P、网关、打洞、FRP 导入、DHT 等参数时，到“设置 → 高级模式”打开。",
            Foreground = Brush("#9FFFFFFF"),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(note, 4);
        tutorial.Children.Add(note);

        var dialog = new ContentDialog
        {
            Title = "第一次使用教程",
            Content = tutorial,
            PrimaryButtonText = "开始使用",
            SecondaryButtonText = "跳过并打开设置",
            CloseButtonText = "跳过",
            XamlRoot = RootGrid.XamlRoot,
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        _personalization.AdvancedMode = advanced.IsOn;
        _personalization.TutorialCompleted = true;
        await _personalizationStore.SaveAsync(_personalization);
        if (!string.IsNullOrWhiteSpace(deviceName.Text))
        {
            _deviceName = deviceName.Text.Trim();
        }
        AppendDiagnostic($"首次向导完成：用途={purpose.SelectedItem}，设备={deviceName.Text}，开机自启={(autoStart.IsOn ? "已请求" : "未开启")}。");
        if (result == ContentDialogResult.Secondary)
        {
            NavigateTo("Settings");
        }
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_exitRequested)
        {
            return;
        }

        if (!_personalization.CloseToTray)
        {
            _exitRequested = true;
            return;
        }

        args.Cancel = true;
        ShowWindow(_hwnd, 0);
        AppendDiagnostic("窗口已最小化到托盘，正在运行的隧道不会停止。");
    }

    private void ShowMainWindow()
    {
        ShowWindow(_hwnd, 5);
        Activate();
    }

    private void BuildInterface()
    {
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        RootGrid.Children.Add(BuildTitleBar());

        _rootNavigation = new NavigationView
        {
            PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            IsSettingsVisible = false,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            OpenPaneLength = 232,
            CompactPaneLength = 56
        };
        _rootNavigation.SelectionChanged += RootNavigation_SelectionChanged;
        Grid.SetRow(_rootNavigation, 1);

        AddNavItem("首页", "Connection", "\uE8AB", true);
        AddNavItem("设备", "Dashboard", "\uF0E2");
        AddNavItem("文件", "FileTransfer", "\uE898");
        AddNavItem("桌面", "RemoteDesktop", "\uE7F4");
        AddNavItem("游戏", "Game", "\uE7FC");
        AddNavItem("设置", "Settings", "\uE713");
        EnsureAdvancedNavigationItems();

        var content = new Grid { Padding = new Thickness(24) };
        _connectionPage = ScrollPage(BuildConnectionPage(), true);
        _dashboardPage = ScrollPage(BuildDashboardPage(), false);
        _gamePage = ScrollPage(BuildGamePage(), false);
        _remoteDesktopPage = ScrollPage(BuildRemoteDesktopPage(), false);
        _fileTransferPage = ScrollPage(BuildFileTransferPage(), false);
        _developerPage = ScrollPage(BuildDeveloperPage(), false);
        _settingsPage = ScrollPage(BuildSettingsPage(), false);

        content.Children.Add(_connectionPage);
        content.Children.Add(_dashboardPage);
        content.Children.Add(_gamePage);
        content.Children.Add(_remoteDesktopPage);
        content.Children.Add(_fileTransferPage);
        content.Children.Add(_developerPage);
        content.Children.Add(_settingsPage);
        _rootNavigation.Content = content;
        RootGrid.Children.Add(_rootNavigation);
        _helpPanel = BuildHelpPanel();
        Grid.SetRow(_helpPanel, 1);
        RootGrid.Children.Add(_helpPanel);
        RegisterKeyboardShortcuts();
    }

    private Grid BuildTitleBar()
    {
        var titleBar = new Grid
        {
            Background = Brush(TitleBarColor()),
            ColumnSpacing = 12
        };
        _titleBar = titleBar;
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _titleBarDragRegion = new Grid
        {
            Padding = new Thickness(18, 0, 150, 0),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            ColumnSpacing = 12
        };
        _titleBarDragRegion.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        _titleBarDragRegion.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(8),
            Background = Brush("#2F6FED"),
            Child = new TextBlock { Text = "T", Foreground = Brush("White"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }
        });
        brand.Children.Add(new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "TPC", FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("White") },
                new TextBlock { Text = "Remote LAN", FontSize = 11, Foreground = Brush("#88FFFFFF") }
            }
        });

        _nodeStatusText = new TextBlock { Text = "核心未启动", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#B8FFFFFF") };
        Grid.SetColumn(_nodeStatusText, 1);
        _titleBarDragRegion.Children.Add(brand);
        _titleBarDragRegion.Children.Add(_nodeStatusText);
        titleBar.Children.Add(_titleBarDragRegion);

        var help = new Button { Content = "?", Width = 38, Margin = new Thickness(0, 8, 8, 8) };
        help.Click += (_, _) => ToggleHelpPanel();
        Grid.SetColumn(help, 1);
        titleBar.Children.Add(help);

        var start = new Button { Content = "一键启动", Margin = new Thickness(0, 8, 8, 8) };
        start.Click += StartCore_Click;
        Grid.SetColumn(start, 1);
        // Keep the title bar as a clean drag surface; core controls live in the page content.

        var stop = new Button { Content = "停止", Margin = new Thickness(0, 8, 16, 8) };
        stop.Click += StopCore_Click;
        Grid.SetColumn(stop, 2);
        // Keep the system caption buttons unobstructed.

        return titleBar;
    }

    private void AddNavItem(string title, string tag, string glyph, bool selected = false)
    {
        var item = new NavigationViewItem
        {
            Content = title,
            Tag = tag,
            Icon = new FontIcon { Glyph = glyph },
            IsSelected = selected
        };
        _rootNavigation.MenuItems.Add(item);
        if (selected)
        {
            _rootNavigation.SelectedItem = item;
        }
    }

    private void EnsureAdvancedNavigationItems()
    {
        if (_rootNavigation is null)
        {
            return;
        }

        var existingAdvanced = _rootNavigation.MenuItems
            .OfType<NavigationViewItem>()
            .Where(x => x.Tag?.ToString() is "Developer")
            .ToList();

        if (!_personalization.AdvancedMode)
        {
            if ((_rootNavigation.SelectedItem as NavigationViewItem)?.Tag?.ToString() is "Developer")
            {
                NavigateTo("Settings");
            }

            foreach (var item in existingAdvanced)
            {
                item.Visibility = Visibility.Collapsed;
                item.IsEnabled = false;
            }
            return;
        }

        foreach (var item in existingAdvanced)
        {
            item.Visibility = Visibility.Visible;
            item.IsEnabled = true;
        }

        if (!existingAdvanced.Any(x => string.Equals(x.Tag?.ToString(), "Developer", StringComparison.Ordinal)))
        {
            AddNavItem("开发者", "Developer", "\uE7BE");
        }
    }

    private Border BuildHelpPanel()
    {
        var root = new StackPanel { Spacing = 12 };
        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = "帮助中心", FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("White") });
        var close = new Button { Content = "关闭" };
        close.Click += (_, _) => ToggleHelpPanel(false);
        AddToGrid(header, close, 0, 1);
        root.Children.Add(header);

        _helpSearchBox = TextBox("搜索帮助", "");
        _helpSearchBox.PlaceholderText = "输入：创建、加入、桌面、文件、网关";
        root.Children.Add(_helpSearchBox);

        root.Children.Add(new TextBlock
        {
            Text = "当前页面会优先显示相关帮助。按 F1 可随时打开或关闭。",
            Foreground = Brush("#B8FFFFFF"),
            TextWrapping = TextWrapping.Wrap
        });
        root.Children.Add(HelpStep("1", "创建房间", "首页点“创建房间”，软件会生成房间码和房间密码，直接发给朋友。"));
        root.Children.Add(HelpStep("2", "加入房间", "输入朋友发来的房间码和房间密码。一个人可以加入多条连接。"));
        root.Children.Add(HelpStep("3", "启动连接", "优先尝试同 Wi-Fi、IPv6、公网直连和自动开洞，失败时提示自建节点。"));
        root.Children.Add(HelpStep("4", "高级模式", "设置里打开高级模式后，才显示 DHT、网关、抓包等复杂入口。"));

        var panel = new Border
        {
            Tag = "GlassCard",
            Width = 380,
            Margin = new Thickness(0, 12, 18, 18),
            Padding = new Thickness(18),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brush(GlassStrongColor()),
            BorderBrush = Brush(GlassBorderColor()),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(_activePersonalization.CornerRadius),
            Visibility = Visibility.Collapsed,
            Child = new ScrollViewer { Content = root }
        };
        Canvas.SetZIndex(panel, 20);
        return panel;
    }

    private static Border HelpStep(string number, string title, string detail)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(15),
            Background = Brush(AccentPanelColor()),
            Child = new TextBlock { Text = number, Foreground = Brush("White"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }
        });
        var text = new StackPanel { Spacing = 4 };
        text.Children.Add(new TextBlock { Text = title, Foreground = Brush("White"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = detail, Foreground = Brush("#B8FFFFFF"), TextWrapping = TextWrapping.Wrap });
        AddToGrid(grid, text, 0, 1);
        return new Border
        {
            Tag = "GlassRow",
            Padding = new Thickness(10),
            Background = Brush(GlassPanelColor()),
            BorderBrush = Brush(GlassBorderColor()),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Math.Max(6, _activePersonalization.CornerRadius - 1)),
            Child = grid
        };
    }

    private void ToggleHelpPanel(bool? visible = null)
    {
        if (_helpPanel is null)
        {
            return;
        }

        var show = visible ?? _helpPanel.Visibility != Visibility.Visible;
        _helpPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            _helpSearchBox?.Focus(FocusState.Programmatic);
        }
    }

    private void RegisterKeyboardShortcuts()
    {
        RootGrid.KeyboardAccelerators.Clear();
        AddShortcut(VirtualKey.N, VirtualKeyModifiers.Control, OpenCreateOrCreateDirectly);
        AddShortcut(VirtualKey.J, VirtualKeyModifiers.Control, () => ShowConnectionSection("Join"));
        AddShortcut(VirtualKey.F, VirtualKeyModifiers.Control, () => NavigateTo("FileTransfer"));
        AddShortcut(VirtualKey.D, VirtualKeyModifiers.Control, () => NavigateTo("RemoteDesktop"));
        AddShortcut(VirtualKey.F1, VirtualKeyModifiers.None, () => ToggleHelpPanel());
    }

    private void AddShortcut(VirtualKey key, VirtualKeyModifiers modifiers, Action action)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += (_, args) =>
        {
            action();
            args.Handled = true;
        };
        RootGrid.KeyboardAccelerators.Add(accelerator);
    }

    private Grid BuildConnectionPage()
    {
        var page = PageBase("首页", "新手只需要“一键启动内核 / 创建房间 / 加入房间”；高级参数在设置中开启后再显示。", out _tunnelStatusText);
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _connectionListSection = BuildConnectionListSection();
        _connectionCreateSection = BuildConnectionCreateSection();
        _connectionJoinSection = BuildConnectionJoinSection();
        _connectionManageSection = BuildConnectionManageSection();
        _advancedTunnelCard = BuildAdvancedGameplayCard();
        ApplySelectedScenario();

        var sectionHost = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        sectionHost.Children.Add(_connectionListSection);
        sectionHost.Children.Add(_connectionCreateSection);
        sectionHost.Children.Add(_connectionJoinSection);
        sectionHost.Children.Add(_connectionManageSection);
        Grid.SetRow(sectionHost, 1);
        page.Children.Add(sectionHost);

        RefreshConnectionListRows();
        ShowConnectionSection("List");
        return page;
    }

    private Button ConnectionEntryButton(string title, string subtitle, string section)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("White") });
        stack.Children.Add(new TextBlock { Text = subtitle, Foreground = Brush("#B8FFFFFF"), TextWrapping = TextWrapping.Wrap });
        var button = new Button
        {
            Tag = section,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(18),
            Transitions = RepositionTransitions(),
            Content = stack
        };
        button.Click += (_, _) =>
        {
            if (string.Equals(section, "Create", StringComparison.Ordinal))
            {
                OpenCreateOrCreateDirectly();
                return;
            }

            ShowConnectionSection(section);
        };
        return button;
    }

    private void OpenCreateOrCreateDirectly()
    {
        if (_personalization.AdvancedMode)
        {
            ShowConnectionSection("Create");
            return;
        }

        _ = CreateFriendConnectionFromDefaultsAsync();
    }

    private Grid BuildConnectionListSection()
    {
        var section = new Grid { RowSpacing = 0, Transitions = EntranceTransitions() };
        section.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var card = Card;
        card.Transitions = RepositionTransitions();
        var root = new Grid { RowSpacing = 16 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var head = new Grid { ColumnSpacing = 18 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var copy = new StackPanel { Spacing = 8 };
        copy.Children.Add(new TextBlock { Text = "我的连接", FontSize = 26, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("White") });
        copy.Children.Add(new TextBlock { Text = "新手只需要创建房间或加入房间。连接成功前不会显示假成功；跨网络失败时会直接引导启动自建节点。", Foreground = Brush("#B8FFFFFF"), TextWrapping = TextWrapping.Wrap });
        _linkSummaryText = new TextBlock { Text = "当前链接：未创建  凭据：房间码 + 房间密码  模式：自动", Foreground = Brush("#D8FFFFFF"), TextWrapping = TextWrapping.Wrap };
        _healthStatusText = new TextBlock { Text = "状态：等待创建或加入朋友连接。", Foreground = Brush("#88FFFFFF"), TextWrapping = TextWrapping.Wrap };
        copy.Children.Add(_linkSummaryText);
        copy.Children.Add(_healthStatusText);
        head.Children.Add(copy);

        var entryStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var startKernel = new Button { Content = "一键启动内核", Width = 150, Padding = new Thickness(12, 10, 12, 10) };
        startKernel.Click += StartCore_Click;
        _connectionCreateTab = ConnectionEntryButton("创建房间", "生成房间码和密码", "Create");
        _connectionJoinTab = ConnectionEntryButton("加入房间", "输入房间码和密码", "Join");
        _connectionManageTab = ConnectionEntryButton("连接管理", "高级诊断", "Manage");
        _connectionCreateTab.Width = 196;
        _connectionJoinTab.Width = 196;
        _connectionManageTab.Width = 0;
        _connectionManageTab.Visibility = Visibility.Collapsed;
        entryStack.Children.Add(startKernel);
        entryStack.Children.Add(_connectionCreateTab);
        entryStack.Children.Add(_connectionJoinTab);
        Grid.SetColumn(entryStack, 1);
        head.Children.Add(entryStack);
        root.Children.Add(head);

        var stats = new Grid { ColumnSpacing = 10 };
        for (var i = 0; i < 5; i++)
        {
            stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        _connectionListTotalText = new TextBlock();
        _connectionListRunningText = new TextBlock();
        _connectionListHealthText = new TextBlock();
        _connectionListTrafficText = new TextBlock();
        _connectionListNetworkText = new TextBlock();
        AddToGrid(stats, StatCard(_connectionListTotalText, "链接总数"), 0, 0);
        AddToGrid(stats, StatCard(_connectionListRunningText, "运行中映射"), 0, 1);
        AddToGrid(stats, StatCard(_connectionListHealthText, "最佳健康度"), 0, 2);
        AddToGrid(stats, StatCard(_connectionListTrafficText, "今日总流量"), 0, 3);
        AddToGrid(stats, StatCard(_connectionListNetworkText, "网络状态"), 0, 4);
        Grid.SetRow(stats, 1);
        root.Children.Add(stats);

        _connectionListRowsHost = new StackPanel { Spacing = 6 };
        Grid.SetRow(_connectionListRowsHost, 2);
        root.Children.Add(_connectionListRowsHost);

        var hint = new TextBlock
        {
            Text = "提示：同一 Wi-Fi 会自动发现；异地网络会优先直连，失败时请启动自建节点或在高级详情复制完整连接信息。",
            Foreground = Brush("#88FFFFFF"),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(hint, 3);
        root.Children.Add(hint);

        card.Child = root;
        section.Children.Add(card);
        return section;
    }

    private static Border StatCard(TextBlock valueBlock, string label)
    {
        valueBlock.FontSize = 24;
        valueBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        valueBlock.Foreground = Brush("White");
        var stack = new StackPanel { Spacing = 5 };
        stack.Children.Add(valueBlock);
        stack.Children.Add(new TextBlock { Text = label, Foreground = Brush("#88FFFFFF") });
        return new Border
        {
            Tag = "GlassStat",
            Padding = new Thickness(12),
            Background = Brush(GlassWeakColor()),
            BorderBrush = Brush(GlassBorderColor()),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Transitions = RepositionTransitions(),
            Child = stack
        };
    }

    private Border BuildConnectionFunctionHeader(string title, string activeSection)
    {
        var card = Card;
        card.Padding = new Thickness(14);
        card.Transitions = RepositionTransitions();
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        var back = new Button { Content = "返回链接列表" };
        back.Click += (_, _) => ShowConnectionSection("List");
        left.Children.Add(back);
        left.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("White"), VerticalAlignment = VerticalAlignment.Center });
        grid.Children.Add(left);

        var jumps = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        jumps.Children.Add(ConnectionJumpButton("创建房间", "Create", activeSection));
        jumps.Children.Add(ConnectionJumpButton("加入房间", "Join", activeSection));
        Grid.SetColumn(jumps, 1);
        grid.Children.Add(jumps);
        card.Child = grid;
        return card;
    }

    private Button ConnectionJumpButton(string text, string section, string activeSection)
    {
        var selected = string.Equals(section, activeSection, StringComparison.Ordinal);
        var button = new Button
        {
            Content = text,
            Tag = section,
            Padding = new Thickness(12, 7, 12, 7),
            Background = selected ? Brush(AccentPanelColor()) : Brush(GlassWeakColor()),
            Opacity = selected ? 1.0 : 0.78,
            Transitions = RepositionTransitions()
        };
        button.Click += (_, _) => ShowConnectionSection(section);
        return button;
    }

    private void RefreshConnectionListRows()
    {
        if (_connectionListRowsHost is null)
        {
            return;
        }

        _connectionListRowsHost.Children.Clear();
        _connectionListRowsHost.Children.Add(BuildConnectionListRow("连接", "路径", "协议", "端口", "状态", "健康度", null, header: true));
        if (_createdProfiles.Count == 0 && _tunnels.Count == 0 && !HasCreatedCurrentLink())
        {
            AddEmptyConnectionListState();
        }

        foreach (var profile in _createdProfiles)
        {
            AddProfileConnectionRow(profile);
        }

        foreach (var item in _tunnels)
        {
            _connectionListRowsHost.Children.Add(BuildConnectionListRow(
                item.Name,
                item.Running ? "本地监听" : "已停止",
                item.ProtocolText,
                item.RouteText,
                item.StatusText,
                item.Running ? "96" : "-",
                item));
        }

        if (_createdProfiles.Count == 0 && _tunnels.Count == 0 && HasCreatedCurrentLink())
        {
            AddCurrentCreatedConnectionRow();
        }

        UpdateConnectionListStats();
        RefreshConnectedDevicesList();
    }

    private bool HasCreatedCurrentLink()
    {
        return !string.IsNullOrWhiteSpace(_currentRoomCode);
    }

    private void AddCurrentCreatedConnectionRow()
    {
        var mode = _connectionModeCombo?.SelectedItem?.ToString() ?? "自动";
        var protocol = _gameProtocolCombo?.SelectedItem?.ToString() ?? _tunnelProtocolCombo?.SelectedItem?.ToString() ?? "TCP+UDP";
        protocol = protocol.Replace("自动：", "", StringComparison.Ordinal).Trim();
        var route = $"{NumberBoxValueText(_localPortBox)} → {NumberBoxValueText(_publicPortBox)}";
        _connectionListRowsHost.Children.Add(BuildConnectionListRow(
            _currentRoomCode,
            mode,
            protocol,
            route,
            "等待朋友加入",
            "-",
            null,
            "Manage"));
    }

    private void AddProfileConnectionRow(ProxyProfileDefinition profile)
    {
        var status = string.IsNullOrWhiteSpace(profile.DisplayStatus)
            ? (string.IsNullOrWhiteSpace(profile.Role) ? "已创建" : profile.Role)
            : profile.DisplayStatus;
        var name = string.IsNullOrWhiteSpace(profile.RoomCode)
            ? profile.Name
            : profile.Name.Contains(profile.RoomCode, StringComparison.Ordinal)
                ? profile.Name
                : $"{profile.Name}  ·  {profile.RoomCode}";
        _connectionListRowsHost.Children.Add(BuildConnectionListRow(
            name,
            ProfileModeText(profile),
            ProfileProtocolText(profile),
            ProfileRouteText(profile),
            status,
            "-",
            null,
            "Manage",
            profile: profile));
    }

    private void AddEmptyConnectionListState()
    {
        var empty = new StackPanel
        {
            Spacing = 6,
            Padding = new Thickness(4, 2, 4, 2),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        empty.Children.Add(new TextBlock
        {
            Text = "还没有朋友连接",
            Foreground = Brush("#F4FFFFFF"),
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        empty.Children.Add(new TextBlock
        {
            Text = "点击右上角“创建房间”或“加入房间”，这里只显示真实创建或加入的连接。",
            Foreground = Brush("#9FFFFFFF"),
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        _connectionListRowsHost.Children.Add(new Border
        {
            Tag = "GlassRow",
            Padding = new Thickness(18, 24, 18, 24),
            Background = Brush(GlassPanelColor()),
            BorderBrush = Brush(GlassBorderColor()),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Transitions = RepositionTransitions(),
            Child = empty
        });
        if (_connectionListRowsHost.Children.LastOrDefault() is FrameworkElement createdEmpty)
        {
            EnsureInteractiveElement(createdEmpty);
        }
    }

    private Border BuildConnectionListRow(string name, string mode, string protocol, string route, string status, string health, TunnelViewItem? item, string primarySection = "Manage", bool header = false, ProxyProfileDefinition? profile = null)
    {
        var grid = new Grid { ColumnSpacing = 12, MinHeight = header ? 34 : 44 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.72, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.95, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.86, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.7, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        AddConnectionCell(grid, name, 0, header);
        AddConnectionCell(grid, mode, 1, header);
        AddConnectionCell(grid, protocol, 2, header);
        AddConnectionCell(grid, route, 3, header);
        AddConnectionCell(grid, status, 4, header);
        AddConnectionCell(grid, health, 5, header);

        if (header)
        {
            AddConnectionCell(grid, "操作", 6, true);
        }
        else
        {
            var menuButton = new Button
            {
                Content = "⋯",
                Width = 34,
                Height = 30,
                Padding = new Thickness(0),
                FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            menuButton.Flyout = BuildConnectionRowMenu(name, primarySection, item, profile);
            EnsureInteractiveElement(menuButton);
            AddToGrid(grid, menuButton, 0, 6);
        }

        var row = new Border
        {
            Tag = header ? "GlassHeaderRow" : "GlassRow",
            Padding = new Thickness(12, 6, 12, 6),
            Background = Brush(header ? GlassWeakColor() : GlassPanelColor()),
            BorderBrush = Brush(GlassBorderColor()),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Transitions = RepositionTransitions(),
            Child = grid
        };
        if (!header)
        {
            EnsureInteractiveElement(row);
        }
        return row;
    }

    private static void AddConnectionCell(Grid grid, string text, int column, bool header)
    {
        AddToGrid(grid, new TextBlock
        {
            Text = text,
            Foreground = header ? Brush("#88FFFFFF") : Brush("#D8FFFFFF"),
            FontWeight = header ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        }, 0, column);
    }

    private MenuFlyout BuildConnectionRowMenu(string name, string primarySection, TunnelViewItem? item, ProxyProfileDefinition? profile)
    {
        var menu = new MenuFlyout();
        if (profile is not null)
        {
            AddMenuItem(menu, "启动", async () => await StartFriendConnectionAsync(profile));
        }
        AddMenuItem(menu, primarySection == "Join" ? "加入房间" : "编辑规则", () => EditConnectionListItem(name, item, profile));
        AddMenuItem(menu, "详情", () => ManageConnectionListItem(name, item, profile));
        AddMenuItem(menu, "复制给朋友", () => CopyConnectionPackageFromList(profile));
        AddMenuItem(menu, item is null ? "停止" : "停止映射", async () => await StopConnectionListItemAsync(name, item, profile));
        AddMenuItem(menu, "查看诊断", () =>
        {
            AppendDiagnostic($"已打开{name}的诊断时间线。");
            ShowConnectionSection("Manage");
        });
        menu.Items.Add(new MenuFlyoutSeparator());
        AddMenuItem(menu, "删除连接", async () => await DeleteConnectionListItemAsync(name, item, profile));
        return menu;
    }

    private async Task StartProfileFromListAsync(ProxyProfileDefinition profile)
    {
        await StartFriendConnectionAsync(profile);
    }

    private async Task StartFriendConnectionAsync(ProxyProfileDefinition profile)
    {
        if (_friendConnectionBusy)
        {
            AppendDiagnostic("正在连接中，请等当前操作完成。");
            return;
        }

        _friendConnectionBusy = true;
        try
        {
            ApplyProfileToForm(profile);
            HydrateGatewayFieldsFromForm(profile);
            await UpdateProfileStatusAsync(profile, "连接中");
            var save = await CallAgentWithAutoStartAsync("profile.save", profile);
            if (!save.Ok)
            {
                throw new InvalidOperationException(save.Error ?? "后台保存连接失败");
            }

            var response = await CallAgentWithAutoStartAsync("connect.start", new
            {
                id = profile.Id,
                role = profile.Role,
                minecraft = true,
                protocol = ProfileProtocolText(profile)
            });
            if (!response.Ok)
            {
                await FailFriendConnectionAsync(
                    profile,
                    "连接调度失败",
                    response.Error ?? "后台 Agent 没有返回路径结果。",
                    "请重试；如果仍失败，点击“一键启动内核”后再启动这条连接。");
                return;
            }

            var result = response.Result;
            var status = ReadJsonString(result, "displayStatus", ReadJsonString(result, "status", "已启动"));
            var path = ReadJsonString(result, "path", "路径竞速");
            var message = ReadJsonString(result, "message", "连接调度已完成。");
            var detail = ReadJsonString(result, "detail", "");
            var host = ReadJsonString(result, "host", "");
            var port = ReadJsonUShort(result, "port", profile.RemotePort);
            var connected = ReadJsonBool(result, "connected", status.Contains("连接", StringComparison.Ordinal));

            profile.DisplayStatus = status;
            profile.ConnectionPath = path;
            profile.LastError = connected ? "" : detail;
            if (!string.IsNullOrWhiteSpace(host) && !IsLoopbackOrWildcardHost(host))
            {
                profile.PeerHost = host;
                profile.RemotePort = port;
            }
            if (connected)
            {
                profile.LastConnectedAt = DateTimeOffset.Now;
            }
            await RememberCreatedConnectionAsync(profile);
            RefreshConnectionListRows();
            _healthStatusText.Text = $"状态：{status}。路径：{path}。{message}";
            AppendDiagnostic($"{profile.Name}：{status} / {path}。{message} {detail}");
            ShowConnectionSection("List");
        }
        catch (Exception ex)
        {
            await FailFriendConnectionAsync(
                profile,
                "连接失败",
                ex.Message,
                "请检查本地端口是否被占用、后台 Agent 是否能启动、防火墙是否允许 TPC。");
        }
        finally
        {
            _friendConnectionBusy = false;
        }
    }

    private async Task StartFriendGatewayConnectionAsync(ProxyProfileDefinition profile)
    {
        await UpdateProfileStatusAsync(profile, "网关连接中");
        _gatewayHostBox.Text = profile.GatewayHost;
        _gatewayPortBox.Value = profile.GatewayControlPort <= 0 ? 7000 : profile.GatewayControlPort;
        if (!string.IsNullOrWhiteSpace(profile.GatewayToken))
        {
            _gatewayTokenBox.Text = profile.GatewayToken;
        }

        try
        {
            await EnsureGatewayConnectedAsync();
            if (_gatewayHandle == 0)
            {
                throw new InvalidOperationException("网关没有返回有效连接句柄。");
            }

            var protocol = profile.Type is ProxyRuleType.Udp or ProxyRuleType.Sudp ? TunnelProtocol.Udp : TunnelProtocol.Tcp;
            var gatewayTunnel = await NativeInterop.StartGatewayTunnelAsync(
                _gatewayHandle,
                string.IsNullOrWhiteSpace(profile.Name) ? "TPC 朋友连接" : profile.Name,
                "127.0.0.1",
                profile.LocalPort,
                profile.PublicPort == 0 ? profile.LocalPort : profile.PublicPort,
                protocol);

            profile.DisplayStatus = "网关已接管";
            profile.LastConnectedAt = DateTimeOffset.Now;
            await RememberCreatedConnectionAsync(profile);
            RefreshConnectionListRows();
            _healthStatusText.Text = $"状态：网关已接管。朋友可通过 {profile.GatewayHost}:{profile.PublicPort} 访问。";
            AppendDiagnostic($"网关已接管：公网 {profile.GatewayHost}:{profile.PublicPort} -> 本机 127.0.0.1:{profile.LocalPort}，句柄 {gatewayTunnel}。");
            ShowConnectionSection("List");
        }
        catch (Exception ex)
        {
            await FailFriendConnectionAsync(
                profile,
                "网关连接失败",
                ex.Message,
                "请检查网关地址、控制端口、令牌、防火墙和 tpcwei_gateway 是否正在运行。");
        }
    }

    private async Task<AgentRpcResponse> CallAgentWithAutoStartAsync(string method, object? parameters = null)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                EnsureAgentProcess();
                if (attempt > 0)
                {
                    await Task.Delay(500);
                }

                return await _agentClient.CallAsync(method, parameters);
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or SocketException or InvalidOperationException)
            {
                lastError = ex;
                await Task.Delay(600);
            }
        }

        return new AgentRpcResponse(false, null, $"后台 Agent 无法连接：{lastError?.Message ?? "未知错误"}");
    }

    private static string ReadJsonString(JsonElement? element, string name, string fallback = "")
    {
        return element is { ValueKind: JsonValueKind.Object } value
            && value.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? fallback
            : fallback;
    }

    private static bool ReadJsonBool(JsonElement? element, string name, bool fallback = false)
    {
        return element is { ValueKind: JsonValueKind.Object } value
            && value.TryGetProperty(name, out var property)
            ? property.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => fallback
            }
            : fallback;
    }

    private static ushort ReadJsonUShort(JsonElement? element, string name, ushort fallback = 0)
    {
        return element is { ValueKind: JsonValueKind.Object } value
            && value.TryGetProperty(name, out var property)
            && property.TryGetUInt16(out var number)
            ? number
            : fallback;
    }

    private void HydrateGatewayFieldsFromForm(ProxyProfileDefinition profile)
    {
        if (string.IsNullOrWhiteSpace(profile.GatewayHost) && _gatewayHostBox is not null)
        {
            profile.GatewayHost = _gatewayHostBox.Text.Trim();
        }

        if (profile.GatewayControlPort == 0 && _gatewayPortBox is not null)
        {
            profile.GatewayControlPort = ToPort(_gatewayPortBox.Value, 7000);
        }

        if (string.IsNullOrWhiteSpace(profile.GatewayToken) && _gatewayTokenBox is not null)
        {
            profile.GatewayToken = _gatewayTokenBox.Text;
        }
    }

    private static bool HasUsablePeerEndpoint(ProxyProfileDefinition profile)
    {
        return profile.RemotePort > 0
            && !string.IsNullOrWhiteSpace(profile.PeerHost)
            && !IsLoopbackOrWildcardHost(profile.PeerHost);
    }

    private static bool HasUsableInternetGateway(ProxyProfileDefinition profile)
    {
        return HasUsableGatewayAddress(profile)
            && profile.GatewayControlPort > 0
            && !string.IsNullOrWhiteSpace(profile.GatewayToken);
    }

    private static bool HasUsableGatewayAddress(ProxyProfileDefinition profile)
    {
        return profile.PublicPort > 0
            && !string.IsNullOrWhiteSpace(profile.GatewayHost)
            && !IsLoopbackOrWildcardHost(profile.GatewayHost);
    }

    private static bool IsGuestProfile(ProxyProfileDefinition profile)
    {
        return profile.Role.Contains("房客", StringComparison.Ordinal)
            || profile.DisplayStatus.Contains("已加入", StringComparison.Ordinal);
    }

    private static bool IsHostProfile(ProxyProfileDefinition profile)
    {
        return profile.Role.Contains("房主", StringComparison.Ordinal)
            || profile.DisplayStatus.Contains("等待朋友", StringComparison.Ordinal);
    }

    private async Task<bool> TryResolveLanPeerAsync(ProxyProfileDefinition profile)
    {
        var discoveryKey = DiscoveryKeyForProfile(profile);
        if (string.IsNullOrWhiteSpace(discoveryKey))
        {
            AppendDiagnostic("局域网发现未启动：这条连接缺少房间码或房间密码。");
            return false;
        }

        var role = IsGuestProfile(profile) ? "房客" : "房主";
        var start = await StartLanAdvertisementOnlyAsync(profile, discoveryKey, role);
        if (!start.Ok)
        {
            AppendDiagnostic($"局域网发现启动失败：{start.Error ?? "后台没有返回原因"}");
            return false;
        }

        if (!IsGuestProfile(profile))
        {
            var hasPublicIpv4 = IsPublicIpv4(profile.PeerHost);
            profile.DisplayStatus = hasPublicIpv4 ? "等待朋友（公网 IPv4 直连）" : "等待朋友（局域网发现已开启）";
            profile.ConnectionPath = hasPublicIpv4 ? "公网 IPv4 直连" : "局域网发现";
            await RememberCreatedConnectionAsync(profile);
            RefreshConnectionListRows();
            AppendDiagnostic(hasPublicIpv4
                ? $"公网 IPv4 直连已写入房间：{profile.PeerHost}:{profile.PublicPort}。朋友异地可优先尝试该地址。"
                : "局域网发现已开启：朋友在同一 Wi-Fi 输入房间码和房间密码后会自动找到这台电脑。");
            return false;
        }

        await UpdateProfileStatusAsync(profile, "正在查找同一局域网的房主");
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var find = await CallAgentWithAutoStartAsync("lan.peer.find", new
            {
                roomCode = profile.RoomCode,
                discoveryKey,
                desiredRole = "房主"
            });
            if (find.Ok
                && find.Result is { } result
                && result.ValueKind == JsonValueKind.Object
                && result.TryGetProperty("found", out var foundElement)
                && foundElement.ValueKind == JsonValueKind.True)
            {
                var host = result.TryGetProperty("host", out var hostElement) ? hostElement.GetString() ?? "" : "";
                var port = result.TryGetProperty("port", out var portElement) && portElement.TryGetUInt16(out var foundPort)
                    ? foundPort
                    : profile.RemotePort;
                if (!string.IsNullOrWhiteSpace(host) && !IsLoopbackOrWildcardHost(host) && port > 0)
                {
                    profile.PeerHost = host;
                    profile.RemotePort = port;
                    profile.DisplayStatus = "已发现朋友";
                    await RememberCreatedConnectionAsync(profile);
                    RefreshConnectionListRows();
                    AppendDiagnostic($"已在局域网发现房主：{host}:{port}。Minecraft 客户端可以连接本机 127.0.0.1:{profile.LocalPort}。");
                    return true;
                }
            }

            await Task.Delay(700);
        }

        AppendDiagnostic("同一 Wi-Fi 内没有发现房主。异地网络保持无官方服务器时，无法保证自动连通；需要双方在同一局域网、公网/UPnP 可达，或配置自建网关。");
        return false;
    }

    private static string DiscoveryKeyForProfile(ProxyProfileDefinition profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.RoomPasswordHash))
        {
            return profile.RoomPasswordHash;
        }

        if (!string.IsNullOrWhiteSpace(profile.TrustedPackage)
            && TryReadRoomSharePackage(profile.TrustedPackage, out var roomCode, out var roomPassword, out _))
        {
            return RoomDiscoveryHash(roomCode, roomPassword);
        }

        if (!string.IsNullOrWhiteSpace(profile.TrustedPackage)
            && TryReadPublicPrivateFromPackage(profile.TrustedPackage, out var publicCode, out var privateCode))
        {
            return StableHash($"{publicCode.Trim()}|{privateCode.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(profile.TrustedPackageHash))
        {
            return profile.TrustedPackageHash;
        }

        return !string.IsNullOrWhiteSpace(profile.RoomCode) ? StableHash(profile.RoomCode) : "";
    }

    private Task<AgentRpcResponse> StartLanAdvertisementOnlyAsync(ProxyProfileDefinition profile, string discoveryKey, string role)
    {
        var advertisePort = profile.PublicPort > 0 ? profile.PublicPort : profile.LocalPort;
        return CallAgentWithAutoStartAsync("lan.discovery.start", new
        {
            profileId = profile.Id,
            roomCode = profile.RoomCode,
            discoveryKey,
            role,
            protocol = ProfileProtocolText(profile),
            port = advertisePort,
            deviceName = Environment.MachineName
        });
    }

    private static bool IsLoopbackOrWildcardHost(string host)
    {
        var value = host.Trim();
        if (value.Length == 0)
        {
            return true;
        }

        if (string.Equals(value, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "::1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "0.0.0.0", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(value, out var address)
            && (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any));
    }

    private async Task UpdateProfileStatusAsync(ProxyProfileDefinition profile, string status)
    {
        profile.DisplayStatus = status;
        await RememberCreatedConnectionAsync(profile);
        RefreshConnectionListRows();
    }

    private async Task FailFriendConnectionAsync(ProxyProfileDefinition profile, string title, string reason, string action)
    {
        profile.DisplayStatus = title.Contains("网关", StringComparison.Ordinal) ? "网关连接失败" : "连接失败";
        await RememberCreatedConnectionAsync(profile);
        RefreshConnectionListRows();
        _healthStatusText.Text = $"状态：{title}。{action}";
        AppendDiagnostic($"{title}：{reason} 建议：{action}");
        ShowConnectionSection("List");
    }

    private static void AddMenuItem(MenuFlyout menu, string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private void EditConnectionListItem(string name, TunnelViewItem? item, ProxyProfileDefinition? profile)
    {
        if (profile is not null)
        {
            ApplyProfileToForm(profile);
        }
        else if (item is not null)
        {
            _tunnelNameBox.Text = item.Name;
            _localPortBox.Value = item.LocalPort;
            _peerHostBox.Text = item.PeerHost;
            _simplePeerHostBox.Text = item.PeerHost;
            _peerPortBox.Value = item.PeerPort;
            _tunnelProtocolCombo.SelectedIndex = item.Protocol == TunnelProtocol.Udp ? 1 : 0;
        }
        else
        {
            ApplyConnectionListPreset(name);
        }

        AppendDiagnostic($"已载入{name}到创建链接页，可以继续编辑高级玩法参数。");
        ShowConnectionSection("Create");
    }

    private void ApplyConnectionListPreset(string name)
    {
        if (name.Contains("游戏", StringComparison.Ordinal))
        {
            _quickScenarioCombo.SelectedIndex = 4;
        }
        else if (name.Contains("远程桌面", StringComparison.Ordinal))
        {
            _quickScenarioCombo.SelectedIndex = 3;
        }
        else if (name.Contains("SSH", StringComparison.Ordinal))
        {
            _quickScenarioCombo.SelectedIndex = 2;
        }
        else if (name.Contains("文件", StringComparison.Ordinal))
        {
            _quickScenarioCombo.SelectedIndex = 5;
        }
        else if (name.Contains("私密", StringComparison.Ordinal))
        {
            _quickScenarioCombo.SelectedIndex = 6;
        }
        else if (name.Contains("端口范围", StringComparison.Ordinal))
        {
            _quickScenarioCombo.SelectedIndex = 8;
        }
        else
        {
            _quickScenarioCombo.SelectedIndex = 0;
        }

        ApplySelectedScenario();
    }

    private void ManageConnectionListItem(string name, TunnelViewItem? item, ProxyProfileDefinition? profile)
    {
        if (profile is not null)
        {
            ApplyProfileToForm(profile);
            _tunnelStatusText.Text = $"已选中连接：{profile.Name}";
        }

        if (item is not null)
        {
            _tunnelList.SelectedItem = item;
            _trafficChart.SetSamples(item.Samples);
        }

        AppendDiagnostic($"正在管理链接：{name}。");
        ShowConnectionSection("Manage");
    }

    private void CopyConnectionPackageFromList(ProxyProfileDefinition? profile)
    {
        var shareText = profile is not null ? ShareTextForProfile(profile) : _currentInviteCode;
        if (string.IsNullOrWhiteSpace(shareText))
        {
            CopyInvite_Click(this, new RoutedEventArgs());
            return;
        }

        CopyShareTextToClipboard(shareText);
        AppendDiagnostic(shareText.Contains("房间码", StringComparison.Ordinal)
            ? "房间码和房间密码已复制。只发给你信任的朋友。"
            : "连接分享信息已复制。只发给你信任的朋友。");
    }

    private static string ShareTextForProfile(ProxyProfileDefinition profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.TrustedPackage)
            && TryFormatRoomShareText(profile.TrustedPackage, out var roomText))
        {
            return roomText;
        }

        if (!string.IsNullOrWhiteSpace(profile.TrustedPackage)
            && TryFormatKeyPairShareText(profile.TrustedPackage, out var keyText))
        {
            return keyText;
        }

        return !string.IsNullOrWhiteSpace(profile.TrustedPackage) ? profile.TrustedPackage : profile.RoomCode;
    }

    private static void CopyShareTextToClipboard(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private static bool TryFormatKeyPairShareText(string packageText, out string shareText)
    {
        shareText = "";
        if (TryFormatRoomShareText(packageText, out shareText))
        {
            return true;
        }

        if (!TryReadPublicPrivateFromPackage(packageText, out var publicCode, out var privateCode))
        {
            return false;
        }

        shareText = $"公钥：{publicCode}{Environment.NewLine}私钥：{privateCode}";
        return true;
    }

    private static bool TryFormatRoomShareText(string packageText, out string shareText)
    {
        shareText = "";
        if (!TryReadRoomSharePackage(packageText, out var roomCode, out var roomPassword, out _))
        {
            return false;
        }

        shareText = $"房间码：{roomCode}{Environment.NewLine}房间密码：{roomPassword}";
        return true;
    }

    private static bool TryReadRoomSharePackage(string packageText, out string roomCode, out string roomPassword, out string publicCode)
    {
        roomCode = "";
        roomPassword = "";
        publicCode = "";
        var parts = packageText.Trim().Split('|');
        if (parts.Length < 4 || parts[0] != "TPCWEIROOM1")
        {
            return false;
        }

        try
        {
            roomCode = parts[1].Trim();
            roomPassword = DecodePackageValue(parts[2]);
            publicCode = DecodePackageValue(parts[3]);
            return !string.IsNullOrWhiteSpace(roomCode);
        }
        catch
        {
            roomCode = "";
            roomPassword = "";
            publicCode = "";
            return false;
        }
    }

    private static bool TryReadPublicPrivateFromPackage(string packageText, out string publicCode, out string privateCode)
    {
        publicCode = "";
        privateCode = "";
        var parts = packageText.Trim().Split('|');
        if (parts.Length < 4 || parts[0] is not ("TPCWEIKEY1" or "TPCWEIKEY2" or "TPCWEIKEYPAIR"))
        {
            return false;
        }

        try
        {
            publicCode = DecodePackageValue(parts[2]).Trim();
            privateCode = DecodePackageValue(parts[3]).Trim();
            return !string.IsNullOrWhiteSpace(publicCode) && !string.IsNullOrWhiteSpace(privateCode);
        }
        catch
        {
            publicCode = "";
            privateCode = "";
            return false;
        }
    }

    private async Task StopConnectionListItemAsync(string name, TunnelViewItem? item, ProxyProfileDefinition? profile)
    {
        if (item is null)
        {
            if (profile is not null)
            {
                await TryStopAgentProfileAsync(profile.Id);
                profile.DisplayStatus = "已停止";
                await RememberCreatedConnectionAsync(profile);
                AppendDiagnostic($"连接已停止：{profile.Name}");
                return;
            }

            AppendDiagnostic($"{name} 还没有启动映射，进入管理连接启动后才能停止。");
            return;
        }

        _tunnelList.SelectedItem = item;
        _ = StopSelectedTunnelAsync();
    }

    private async Task DeleteConnectionListItemAsync(string name, TunnelViewItem? item, ProxyProfileDefinition? profile)
    {
        var detail = profile is not null
            ? $"将从本机配置中删除“{profile.Name}”。这个操作会更新 %AppData%\\TPCwei\\profiles.json。"
            : item is not null
                ? $"将停止并从当前列表中移除映射“{item.Name}”。"
                : $"将清除当前未保存链接“{name}”。";

        var confirmed = await ConfirmDangerousActionAsync("删除连接", detail + "\n\n此操作只删除本机连接记录，不会删除项目文件。");
        if (!confirmed)
        {
            AppendDiagnostic($"已取消删除连接：{name}");
            return;
        }

        try
        {
            if (item is not null)
            {
                if (item.Running)
                {
                    try
                    {
                        await NativeInterop.StopTunnelAsync(item.Handle);
                    }
                    catch (Exception ex)
                    {
                        AppendDiagnostic($"停止映射失败，仍会移除本机列表记录：{ex.Message}");
                    }
                }

                _tunnels.Remove(item);
            }

            if (profile is not null)
            {
                _createdProfiles.Remove(profile);
                await _profileStore.DeleteAsync(profile.Id);
                await TryStopAgentProfileAsync(profile.Id);
                if (string.Equals(profile.RoomCode, _currentRoomCode, StringComparison.Ordinal)
                    || string.Equals(profile.TrustedPackage, _currentInviteCode, StringComparison.Ordinal))
                {
                    _currentRoomCode = "";
                    _currentInviteCode = "";
                    _currentPublicCode = "";
                    _currentPublicHash = "";
                    _roomCodeBox.Text = "";
                    _publicCodeBox.Text = "";
                    _inviteCodeBox.Text = "";
                }
            }
            else if (item is null && HasCreatedCurrentLink())
            {
                _currentRoomCode = "";
                _currentInviteCode = "";
                _currentPublicCode = "";
                _currentPublicHash = "";
                _roomCodeBox.Text = "";
                _publicCodeBox.Text = "";
                _inviteCodeBox.Text = "";
            }

            RefreshConnectionListRows();
            RefreshLinkSummary();
            AppendDiagnostic($"连接已删除：{name}");
        }
        catch (Exception ex)
        {
            AppendDiagnostic($"删除连接失败：{ex.Message}");
        }
    }

    private async Task TryStopAgentProfileAsync(string profileId)
    {
        try
        {
            var stop = await _agentClient.CallAsync("profile.stop", new { id = profileId });
            if (!stop.Ok && !string.IsNullOrWhiteSpace(stop.Error))
            {
                AppendDiagnostic($"后台 Agent 停止规则提示：{stop.Error}");
            }
        }
        catch
        {
            // Agent may not be running; deleting the local profile still succeeds.
        }
    }

    private async Task<bool> ConfirmDangerousActionAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 520
            },
            PrimaryButtonText = "确认删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    private void UpdateConnectionListStats()
    {
        if (_connectionListTotalText is null)
        {
            return;
        }

        var total = _createdProfiles.Count + _tunnels.Count;
        if (total == 0 && HasCreatedCurrentLink())
        {
            total = 1;
        }
        var running = _tunnels.Count(x => x.Running)
            + _createdProfiles.Count(x => x.DisplayStatus is "连接中" or "本地监听中" or "网关连接中" or "网关已接管" or "已连接");
        var trafficBytes = 0UL;
        foreach (var tunnel in _tunnels)
        {
            trafficBytes = unchecked(trafficBytes + tunnel.BytesUp + tunnel.BytesDown);
        }
        _connectionListTotalText.Text = total.ToString();
        _connectionListRunningText.Text = running.ToString();
        _connectionListHealthText.Text = total == 0 || running == 0 ? "-" : _createdProfiles.Any(x => x.DisplayStatus.Contains("失败", StringComparison.Ordinal)) ? "需处理" : "96";
        _connectionListTrafficText.Text = FormatBytes(trafficBytes);
        if (_connectionListNetworkText is not null)
        {
            _connectionListNetworkText.Text = _gatewayHandle != 0
                ? "网关在线"
                : _nodeHandle != 0
                    ? "直连准备"
                    : "未启动";
        }
    }

    private static string NumberBoxValueText(NumberBox? numberBox)
    {
        if (numberBox is null || double.IsNaN(numberBox.Value) || numberBox.Value <= 0)
        {
            return "自动端口";
        }

        return ((int)Math.Round(numberBox.Value)).ToString();
    }

    private static string ProfileModeText(ProxyProfileDefinition profile)
    {
        var status = profile.DisplayStatus ?? "";
        if (status.Contains("网关已接管", StringComparison.Ordinal))
        {
            return "自建网关";
        }

        if (status.Contains("网关连接", StringComparison.Ordinal))
        {
            return "网关连接中";
        }

        if (status.Contains("本地监听", StringComparison.Ordinal) || status.Contains("运行中", StringComparison.Ordinal))
        {
            return "本地监听";
        }

        if (status.Contains("连接中", StringComparison.Ordinal))
        {
            return "正在选择";
        }

        if (status.Contains("失败", StringComparison.Ordinal) || status.Contains("需要完整", StringComparison.Ordinal))
        {
            return "未连接";
        }

        if (status.Contains("等待", StringComparison.Ordinal) || status.Contains("已加入", StringComparison.Ordinal))
        {
            return "等待连接";
        }

        return profile.Mode switch
        {
            ProxyRuleMode.P2P => "极速直连",
            ProxyRuleMode.Gateway => "自建网关",
            ProxyRuleMode.Secret => "私密访问",
            ProxyRuleMode.SmartDirect => "智能直连",
            _ => "自动选择"
        };
    }

    private static string ProfileProtocolText(ProxyProfileDefinition profile)
    {
        return profile.Type switch
        {
            ProxyRuleType.Udp => "UDP",
            ProxyRuleType.Http => "HTTP",
            ProxyRuleType.Https => "HTTPS/SNI",
            ProxyRuleType.Stcp => "STCP",
            ProxyRuleType.Sudp => "SUDP",
            ProxyRuleType.Xtcp => "XTCP",
            ProxyRuleType.TcpMux => "TCPMUX",
            ProxyRuleType.PortRange => "端口范围",
            _ => "TCP"
        };
    }

    private static string ProfileRouteText(ProxyProfileDefinition profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.ConnectionPath))
        {
            return profile.ConnectionPath;
        }

        return profile.Type == ProxyRuleType.PortRange
            ? $"{profile.PublicPort}"
            : $"{profile.LocalPort} → {profile.PublicPort}";
    }

    private Grid BuildConnectionCreateSection()
    {
        var section = new Grid { Visibility = Visibility.Collapsed, RowSpacing = 18, Transitions = EntranceTransitions() };
        section.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        section.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        section.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        section.Children.Add(BuildConnectionFunctionHeader("创建房间", "Create"));

        var basicCard = Card;
        var basic = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        basic.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
        basic.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        basic.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        basic.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        basic.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        basic.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        basic.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        basic.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        basic.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _quickScenarioCombo = new ComboBox { Header = "用途", SelectedIndex = 4, Items = { "网页服务", "HTTPS/SNI 服务", "SSH 管理", "Windows 远程桌面", "Minecraft Java 版", "文件传输", "私密访问", "智能直连", "端口范围", "自定义端口" } };
        _quickScenarioCombo.SelectionChanged += (_, _) => ApplySelectedScenario();
        _gameProtocolCombo = new ComboBox { Header = "协议玩法", SelectedIndex = 1, Items = { "自动：TCP + UDP", "TCP", "UDP" } };
        _gameProtocolCombo.SelectionChanged += (_, _) =>
        {
            if (_tunnelProtocolCombo is not null)
            {
                ApplyGameProtocolToConnection();
            }
        };
        _localPortBox = NumberBox("本地端口", 8080);
        _publicPortBox = NumberBox("公网端口", 8080);
        _gatewayHostBox = TextBox("网关地址", "");
        _roomPasswordBox = TextBox("房间密码（可空，建议 6 位以上）", "");
        _roomPasswordBox.PlaceholderText = "朋友加入时输入这个密码；留空也能用但安全性较低";
        var createLink = new Button { Content = "创建房间", VerticalAlignment = VerticalAlignment.Bottom };
        createLink.Click += CreateRoom_Click;

        AddToGrid(basic, _quickScenarioCombo, 0, 0);
        AddToGrid(basic, _gameProtocolCombo, 0, 1);
        AddToGrid(basic, _localPortBox, 0, 2);
        AddToGrid(basic, _publicPortBox, 0, 3);
        AddToGrid(basic, _gatewayHostBox, 0, 4);
        AddToGrid(basic, createLink, 0, 5);
        AddToGrid(basic, _roomPasswordBox, 1, 0, 1, 2);

        var quickActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _connectionCreateQuickActions = quickActions;
        var generatePublicQuick = new Button { Content = "生成设备公钥（高级）" };
        generatePublicQuick.Click += GeneratePublic_Click;
        quickActions.Children.Add(generatePublicQuick);
        quickActions.Children.Add(PresetButton("网页 80", "Web"));
        quickActions.Children.Add(PresetButton("SSH 22", "SSH"));
        quickActions.Children.Add(PresetButton("远程桌面 3389", "RDP"));
        quickActions.Children.Add(PresetButton("MC Bedrock UDP", "GameUdp"));
        quickActions.Children.Add(new TextBlock { Text = "普通用户选场景和端口即可；高级玩法区可以完整手动调参。", Foreground = Brush("#B8FFFFFF"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        AddToGrid(basic, quickActions, 2, 0, 1, 6);
        basicCard.Child = basic;
        Grid.SetRow(basicCard, 1);
        section.Children.Add(basicCard);

        var advancedArea = new Grid();
        _connectionCreateAdvancedHintCard = BuildAdvancedModeHintCard();
        _connectionCreateAdvancedHost = new Grid();
        advancedArea.Children.Add(_connectionCreateAdvancedHintCard);
        advancedArea.Children.Add(_connectionCreateAdvancedHost);
        Grid.SetRow(advancedArea, 2);
        section.Children.Add(advancedArea);
        return section;
    }

    private Border BuildAdvancedModeHintCard()
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock
        {
            Text = "已使用推荐设置",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Brush("White")
        });
        stack.Children.Add(new TextBlock
        {
            Text = "高级参数已收起。平时不用管这些；需要改网关、打洞、端口或导入旧规则时，点下面按钮就能打开。",
            Foreground = Brush("#B8FFFFFF"),
            TextWrapping = TextWrapping.Wrap
        });
        var openAdvanced = new Button { Content = "打开高级模式", HorizontalAlignment = HorizontalAlignment.Left };
        openAdvanced.Click += (_, _) => SetAdvancedModeEnabled(true, navigateSettings: false);
        stack.Children.Add(openAdvanced);

        var card = Card;
        card.Padding = new Thickness(16);
        card.Child = stack;
        return card;
    }

    private static Border AdvancedHintChip(string title, string value)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock { Text = title, Foreground = Brush("#C8FFFFFF"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        stack.Children.Add(new TextBlock { Text = value, Foreground = Brush("#39D58A") });
        return new Border
        {
            Tag = "GlassStat",
            Padding = new Thickness(12),
            Background = Brush(GlassWeakColor()),
            BorderBrush = Brush(GlassBorderColor()),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Math.Max(6, _activePersonalization.CornerRadius - 1)),
            Child = stack
        };
    }

    private Grid BuildConnectionJoinSection()
    {
        var section = new Grid { Visibility = Visibility.Collapsed, RowSpacing = 18, Transitions = EntranceTransitions() };
        section.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        section.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        section.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        section.Children.Add(BuildConnectionFunctionHeader("加入房间", "Join"));

        var packageCard = Card;
        var packageStack = new StackPanel { Spacing = 12 };
        packageStack.Children.Add(new TextBlock { Text = "输入房间码和房间密码", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("White") });
        packageStack.Children.Add(new TextBlock
        {
            Text = "朋友把房间码和房间密码发给你后，分别粘贴到下面两个框，点“加入”即可。旧版完整设备包仍可粘贴到可选框。",
            Foreground = Brush("#B8FFFFFF"),
            TextWrapping = TextWrapping.Wrap
        });
        var keyGrid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        keyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        keyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _joinPublicCodeBox = TextBox("房间码", "");
        _joinPrivateCodeBox = TextBox("房间密码", "");
        _joinPublicCodeBox.PlaceholderText = "例如 ROOM-20260502-120000";
        _joinPrivateCodeBox.PlaceholderText = "朋友设置的房间密码；可为空";
        AddToGrid(keyGrid, _joinPublicCodeBox, 0, 0);
        AddToGrid(keyGrid, _joinPrivateCodeBox, 0, 1);
        packageStack.Children.Add(keyGrid);
        _inviteCodeBox = new TextBox
        {
            Header = "完整连接信息（高级可选）",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 72,
            PlaceholderText = "如果朋友发的是完整 TPCWEIROOM1|... 或旧版 TPCWEIKEY2|...，粘贴到这里"
        };
        packageStack.Children.Add(_inviteCodeBox);
        var packageActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var pasteInvite = new Button { Content = "粘贴" };
        pasteInvite.Click += PasteInvite_Click;
        var join = new Button { Content = "加入" };
        join.Click += JoinRoom_Click;
        packageActions.Children.Add(pasteInvite);
        packageActions.Children.Add(join);
        packageStack.Children.Add(packageActions);
        packageCard.Child = packageStack;
        Grid.SetRow(packageCard, 1);
        section.Children.Add(packageCard);

        // 这些框不再显示给新手看，但先留在后台。
        // 旧版连接信息和内部生成公钥/私钥还会用到它们，删太狠反而容易把加入流程弄坏。
        _roomCodeBox = TextBox("房间", "TPCWEI-LOCAL");
        _publicCodeBox = TextBox("公钥", "");
        _privateCodeBox = TextBox("私钥（单组 64 位）", "");
        _groupPrivateCodesBox = new TextBox
        {
            Header = "群组私钥（每行一组，2 组以上会合成一个群组公钥）",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 108,
            PlaceholderText = "每行粘贴一组 64 位私钥"
        };
        return section;
    }

    private Grid BuildConnectionManageSection()
    {
        var section = new Grid { Visibility = Visibility.Collapsed, RowSpacing = 18, Transitions = EntranceTransitions() };
        section.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        section.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        section.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        section.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        section.Children.Add(BuildConnectionFunctionHeader("快速连接 / 管理", "Manage"));

        var actionsCard = Card;
        var actions = new StackPanel { Spacing = 12 };
        actions.Children.Add(new TextBlock { Text = "管理连接", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("White") });
        var actionLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var smartStart = new Button { Content = "一键连接" };
        smartStart.Click += StartSmartConnection_Click;
        var stop = new Button { Content = "停止选中" };
        stop.Click += StopTunnel_Click;
        var selfTest = new Button { Content = "一键自测" };
        selfTest.Click += SelfTest_Click;
        foreach (var button in new[] { smartStart, stop, selfTest })
        {
            actionLine.Children.Add(button);
        }
        actions.Children.Add(actionLine);

        var advancedLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _connectionManageAdvancedActions = advancedLine;
        var connectGateway = new Button { Content = "连接网关" };
        connectGateway.Click += ConnectGateway_Click;
        var startAgent = new Button { Content = "后台启动" };
        startAgent.Click += StartAgentProfile_Click;
        var saveProfile = new Button { Content = "保存规则" };
        saveProfile.Click += SaveProfile_Click;
        var importFrp = new Button { Content = "导入 FRP" };
        importFrp.Click += ImportFrp_Click;
        var deployGateway = new Button { Content = "部署命令" };
        deployGateway.Click += CopyGatewayDeploy_Click;
        var startCore = new Button { Content = "启动核心" };
        startCore.Click += StartCore_Click;
        var stopCore = new Button { Content = "停止核心" };
        stopCore.Click += StopCore_Click;
        foreach (var button in new[] { connectGateway, startAgent, saveProfile, importFrp, deployGateway, startCore, stopCore })
        {
            advancedLine.Children.Add(button);
        }
        actions.Children.Add(advancedLine);
        actionsCard.Child = actions;
        Grid.SetRow(actionsCard, 1);
        section.Children.Add(actionsCard);

        _connectionManageAdvancedHost = new Grid();
        Grid.SetRow(_connectionManageAdvancedHost, 2);
        section.Children.Add(_connectionManageAdvancedHost);

        var body = new Grid { ColumnSpacing = 18 };
        _connectionManageAdvancedBody = body;
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _tunnelList = new ListView { ItemsSource = _tunnels, MinHeight = 260 };
        _tunnelList.SelectionChanged += TunnelList_SelectionChanged;
        body.Children.Add(CardWithTitle("映射列表", _tunnelList));

        _trafficChart = new TrafficChart();
        var traffic = new Grid { RowSpacing = 12 };
        traffic.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        traffic.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        traffic.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        traffic.RowDefinitions.Add(new RowDefinition { Height = new GridLength(180) });
        traffic.Children.Add(new TextBlock { Text = "网络流量", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("White") });
        Grid.SetRow(_trafficChart, 1);
        traffic.Children.Add(_trafficChart);
        var hint = new TextBlock { Text = "红线表示下行，绿线表示上行。选中映射后显示实时趋势。", Foreground = Brush("#88FFFFFF") };
        Grid.SetRow(hint, 2);
        traffic.Children.Add(hint);
        _diagnosticsList = new ListView { ItemsSource = _diagnostics };
        Grid.SetRow(_diagnosticsList, 3);
        traffic.Children.Add(CardWithTitle("连接诊断", _diagnosticsList));
        var trafficCard = Card;
        trafficCard.Child = traffic;
        Grid.SetColumn(trafficCard, 1);
        body.Children.Add(trafficCard);
        Grid.SetRow(body, 3);
        section.Children.Add(body);
        return section;
    }

    private Border BuildAdvancedGameplayCard()
    {
        var card = Card;
        var form = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 8; i++)
        {
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = "高级玩法", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("White"), VerticalAlignment = VerticalAlignment.Center });
        _advancedCollapseButton = new Button { Content = "收起高级选项" };
        _advancedCollapseButton.Click += (_, _) => ToggleAdvancedOptionsCollapsed();
        AddToGrid(header, _advancedCollapseButton, 0, 1);
        AddToGrid(form, header, 0, 0, 1, 4);
        _tunnelNameBox = TextBox("映射名称", "Web 服务");
        _connectionModeCombo = new ComboBox { Header = "连接模式", SelectedIndex = 0, Items = { "自动：P2P 优先，网关回退", "极速直连：完全无服务器", "自建网关：跨网兜底", "私密访问：不暴露公网端口", "智能直连：XTCP 竞速" } };
        _tunnelProtocolCombo = new ComboBox { Header = "底层隧道协议", SelectedIndex = 0, Items = { "TCP", "UDP" } };
        _simplePeerHostBox = TextBox("对端地址 / P2P 地址", "127.0.0.1");
        _peerHostBox = TextBox("远端地址", "127.0.0.1");
        _peerPortBox = NumberBox("远端端口", 80);
        _gatewayPortBox = NumberBox("网关控制端口", 7000);
        _gatewayTokenBox = TextBox("网关令牌", "change-me");
        _healthCheckCombo = new ComboBox { Header = "健康检查", SelectedIndex = 0, Items = { "tcp", "http", "none" } };
        _allowLanClientsBox = new CheckBox { Content = "允许局域网设备访问本地监听端口", Foreground = Brush("White") };
        _preferP2pSwitch = new ToggleSwitch { Header = "优先 P2P", IsOn = true };
        _allowGatewayFallbackSwitch = new ToggleSwitch { Header = "允许网关回退", IsOn = true };
        _enableUpnpSwitch = new ToggleSwitch { Header = "启用 UPnP", IsOn = true };
        _enableTcpPunchSwitch = new ToggleSwitch { Header = "启用 TCP 打洞", IsOn = true };
        _enableUdpPunchSwitch = new ToggleSwitch { Header = "启用 UDP 打洞", IsOn = true };
        _dualChannelSwitch = new ToggleSwitch { Header = "TCP+UDP 双通道", IsOn = false };
        _encryptionSwitch = new ToggleSwitch { Header = "数据加密", IsOn = true };
        _compressionSwitch = new ToggleSwitch { Header = "压缩传输", IsOn = false };

        AddToGrid(form, _tunnelNameBox, 1, 0);
        AddToGrid(form, _connectionModeCombo, 1, 1);
        AddToGrid(form, _tunnelProtocolCombo, 1, 2);
        AddToGrid(form, _healthCheckCombo, 1, 3);
        AddToGrid(form, _simplePeerHostBox, 2, 0);
        AddToGrid(form, _peerHostBox, 2, 1);
        AddToGrid(form, _peerPortBox, 2, 2);
        AddToGrid(form, _gatewayPortBox, 2, 3);
        AddToGrid(form, _gatewayTokenBox, 3, 0, 1, 2);
        AddToGrid(form, _allowLanClientsBox, 3, 2, 1, 2);
        AddToGrid(form, _preferP2pSwitch, 4, 0);
        AddToGrid(form, _allowGatewayFallbackSwitch, 4, 1);
        AddToGrid(form, _enableUpnpSwitch, 4, 2);
        AddToGrid(form, _dualChannelSwitch, 4, 3);
        AddToGrid(form, _enableTcpPunchSwitch, 5, 0);
        AddToGrid(form, _enableUdpPunchSwitch, 5, 1);
        AddToGrid(form, _encryptionSwitch, 5, 2);
        AddToGrid(form, _compressionSwitch, 5, 3);
        var directStart = new Button { Content = "按高级参数启动映射" };
        directStart.Click += StartTunnel_Click;
        var stop = new Button { Content = "停止选中映射" };
        stop.Click += StopTunnel_Click;
        var hints = new TextBlock { Text = "这里的参数会影响一键连接、保存规则、后台 Agent 和 FRP 导入后的规则。", Foreground = Brush("#B8FFFFFF"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { directStart, stop, hints } };
        AddToGrid(form, actions, 6, 0, 1, 4);

        card.Child = form;
        return card;
    }

    private void ToggleAdvancedOptionsCollapsed()
    {
        _advancedOptionsCollapsed = !_advancedOptionsCollapsed;
        _personalization.AdvancedOptionsCollapsed = _advancedOptionsCollapsed;
        ApplyAdvancedModeVisibility();
        _ = _personalizationStore.SaveAsync(_personalization);
    }

    private void ApplyAdvancedOptionsCollapsedState()
    {
        if (_advancedTunnelCard?.Child is not Grid form)
        {
            return;
        }

        var fieldsVisibility = _advancedOptionsCollapsed ? Visibility.Collapsed : Visibility.Visible;
        foreach (var child in form.Children.OfType<FrameworkElement>())
        {
            if (Grid.GetRow(child) > 0)
            {
                child.Visibility = fieldsVisibility;
            }
        }

        if (_advancedCollapseButton is not null)
        {
            _advancedCollapseButton.Content = _advancedOptionsCollapsed ? "展开高级选项" : "收起高级选项";
        }
    }

    private void ShowConnectionSection(string section)
    {
        if (_connectionListSection is null || _connectionCreateSection is null || _connectionJoinSection is null || _connectionManageSection is null)
        {
            return;
        }

        var targetTag = NormalizeConnectionSection(section);
        var targetSection = ConnectionSectionForTag(targetTag)!;
        var previousTag = _currentConnectionSection;
        var previousSection = ConnectionSectionForTag(previousTag);

        if (_advancedTunnelCard is not null)
        {
            var target = string.Equals(targetTag, "Manage", StringComparison.Ordinal)
                ? _connectionManageAdvancedHost
                : _connectionCreateAdvancedHost;
            if (target is not null && !ReferenceEquals(_advancedTunnelCard.Parent, target))
            {
                if (_advancedTunnelCard.Parent is Panel parent)
                {
                    parent.Children.Remove(_advancedTunnelCard);
                }
                target.Children.Add(_advancedTunnelCard);
            }
        }

        if (string.Equals(targetTag, "List", StringComparison.Ordinal))
        {
            RefreshConnectionListRows();
        }

        if (string.Equals(previousTag, targetTag, StringComparison.Ordinal))
        {
            SetConnectionSectionVisibility(targetTag);
            return;
        }

        if (_isConnectionSectionTransitioning)
        {
            return;
        }

        if (AnimationDurationMilliseconds() <= 0)
        {
            SetConnectionSectionVisibility(targetTag);
            return;
        }

        _isConnectionSectionTransitioning = true;
        SetConnectionSectionInteractivity(false);
        CollapseConnectionSectionsExcept(previousSection, targetSection);
        targetSection.Transitions = EntranceTransitions();

        var forward = ConnectionSectionIndex(targetTag) >= ConnectionSectionIndex(previousTag);
        TransitionElements(previousSection, targetSection, horizontal: false, forward: forward, () =>
        {
            _currentConnectionSection = targetTag;
            SetConnectionSectionVisibility(targetTag);
            SetConnectionSectionInteractivity(true);
            _isConnectionSectionTransitioning = false;
        });
    }

    private void SetConnectionSectionVisibility(string section)
    {
        var targetTag = NormalizeConnectionSection(section);
        _connectionListSection.Visibility = string.Equals(targetTag, "List", StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
        _connectionCreateSection.Visibility = string.Equals(targetTag, "Create", StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
        _connectionJoinSection.Visibility = string.Equals(targetTag, "Join", StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
        _connectionManageSection.Visibility = string.Equals(targetTag, "Manage", StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
        ResetAnimatedElement(_connectionListSection);
        ResetAnimatedElement(_connectionCreateSection);
        ResetAnimatedElement(_connectionJoinSection);
        ResetAnimatedElement(_connectionManageSection);
        _currentConnectionSection = targetTag;
        RefreshConnectionTabsForCurrentSection();
    }

    private void CollapseConnectionSectionsExcept(FrameworkElement? outgoing, FrameworkElement incoming)
    {
        foreach (var item in new[] { _connectionListSection, _connectionCreateSection, _connectionJoinSection, _connectionManageSection })
        {
            if (item is null || ReferenceEquals(item, outgoing) || ReferenceEquals(item, incoming))
            {
                continue;
            }

            item.Visibility = Visibility.Collapsed;
            ResetAnimatedElement(item);
        }
    }

    private void SetConnectionSectionInteractivity(bool enabled)
    {
        _connectionListSection.IsHitTestVisible = enabled;
        _connectionCreateSection.IsHitTestVisible = enabled;
        _connectionJoinSection.IsHitTestVisible = enabled;
        _connectionManageSection.IsHitTestVisible = enabled;
        if (_connectionCreateTab is not null) _connectionCreateTab.IsEnabled = enabled;
        if (_connectionJoinTab is not null) _connectionJoinTab.IsEnabled = enabled;
        if (_connectionManageTab is not null) _connectionManageTab.IsEnabled = enabled;
    }

    private FrameworkElement? ConnectionSectionForTag(string section)
    {
        return NormalizeConnectionSection(section) switch
        {
            "List" => _connectionListSection,
            "Create" => _connectionCreateSection,
            "Join" => _connectionJoinSection,
            "Manage" => _connectionManageSection,
            _ => _connectionListSection
        };
    }

    private static string NormalizeConnectionSection(string section)
    {
        return section switch
        {
            "Create" => "Create",
            "Join" => "Join",
            "Manage" => "Manage",
            _ => "List"
        };
    }

    private static int ConnectionSectionIndex(string section)
    {
        return NormalizeConnectionSection(section) switch
        {
            "Create" => 1,
            "Join" => 2,
            "Manage" => 3,
            _ => 0
        };
    }

    private static void SetConnectionTabState(Button button, bool selected)
    {
        if (button is null)
        {
            return;
        }

        button.Opacity = selected ? 1.0 : 0.74;
        button.Background = selected ? Brush(AccentPanelColor()) : Brush(GlassWeakColor());
    }

    private Grid BuildDashboardPage()
    {
        var page = PageBase("设备", "查看已发现设备、连接路径、健康状态和本机诊断建议。", out _);
        for (var i = 0; i < 3; i++)
        {
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        var statusGrid = new Grid { ColumnSpacing = 12 };
        for (var i = 0; i < 4; i++)
        {
            statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        AddToGrid(statusGrid, PlatformStatusCard("健康分", "96", "自动路径竞速已就绪"), 0, 0);
        AddToGrid(statusGrid, PlatformStatusCard("路径候选", "5", "LAN / UPnP / 打洞 / 网关 / 多跳"), 0, 1);
        AddToGrid(statusGrid, PlatformStatusCard("零信任", "启用", "设备、应用、端口级授权"), 0, 2);
        AddToGrid(statusGrid, PlatformStatusCard("AI 诊断", "离线", "本地规则引擎"), 0, 3);
        var statusCard = CardWithTitle("实时总览", statusGrid);
        Grid.SetRow(statusCard, 1);
        page.Children.Add(statusCard);

        _connectedDevicesRowsHost = new StackPanel { Spacing = 8 };
        RefreshConnectedDevicesList();
        var devicesCard = CardWithTitle("已连接设备列表", _connectedDevicesRowsHost);
        devicesCard.Margin = new Thickness(0, 18, 0, 0);
        Grid.SetRow(devicesCard, 2);
        page.Children.Add(devicesCard);

        return page;
    }

    private void RefreshConnectedDevicesList()
    {
        if (_connectedDevicesRowsHost is null)
        {
            return;
        }

        _connectedDevicesRowsHost.Children.Clear();
        _connectedDevicesRowsHost.Children.Add(BuildConnectedDeviceRow("设备 / 房间", "状态", "连接方式", "地址 / 端口", "最后连接", header: true));

        var added = false;
        foreach (var profile in _createdProfiles)
        {
            added = true;
            var name = string.IsNullOrWhiteSpace(profile.RoomCode) ? profile.Name : $"{profile.Name} · {profile.RoomCode}";
            var endpoint = string.IsNullOrWhiteSpace(profile.PeerHost)
                ? ProfileRouteText(profile)
                : $"{profile.PeerHost}:{profile.RemotePort}";
            var lastSeen = profile.LastConnectedAt is null ? "还未连接" : profile.LastConnectedAt.Value.ToLocalTime().ToString("MM-dd HH:mm");
            _connectedDevicesRowsHost.Children.Add(BuildConnectedDeviceRow(
                name,
                string.IsNullOrWhiteSpace(profile.DisplayStatus) ? "等待连接" : profile.DisplayStatus,
                ProfileModeText(profile),
                endpoint,
                lastSeen));
        }

        foreach (var tunnel in _tunnels)
        {
            added = true;
            _connectedDevicesRowsHost.Children.Add(BuildConnectedDeviceRow(
                tunnel.Name,
                tunnel.StatusText,
                tunnel.Running ? "本机监听" : "已停止",
                tunnel.RouteText,
                tunnel.Running ? "正在运行" : "已停止"));
        }

        if (added)
        {
            return;
        }

        // 这里不要再放抽象拓扑图，新手只需要知道现在有没有设备连上。
        var empty = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        empty.Children.Add(new TextBlock
        {
            Text = "还没有已连接设备",
            Foreground = Brush("#F4FFFFFF"),
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        empty.Children.Add(new TextBlock
        {
            Text = "创建房间或加入朋友房间后，设备会出现在这里。",
            Foreground = Brush("#9FFFFFFF"),
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        _connectedDevicesRowsHost.Children.Add(new Border
        {
            Tag = "GlassRow",
            Padding = new Thickness(18, 24, 18, 24),
            Background = Brush(GlassPanelColor()),
            BorderBrush = Brush(GlassBorderColor()),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Transitions = RepositionTransitions(),
            Child = empty
        });
    }

    private Border BuildConnectedDeviceRow(string name, string status, string mode, string endpoint, string lastSeen, bool header = false)
    {
        var grid = new Grid { ColumnSpacing = 12, MinHeight = header ? 34 : 46 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star) });

        AddConnectedDeviceCell(grid, name, 0, header, status);
        AddConnectedDeviceCell(grid, status, 1, header, status);
        AddConnectedDeviceCell(grid, mode, 2, header, status);
        AddConnectedDeviceCell(grid, endpoint, 3, header, status);
        AddConnectedDeviceCell(grid, lastSeen, 4, header, status);

        var row = new Border
        {
            Tag = header ? "GlassHeaderRow" : "GlassRow",
            Padding = new Thickness(12, 6, 12, 6),
            Background = Brush(header ? GlassWeakColor() : GlassPanelColor()),
            BorderBrush = Brush(GlassBorderColor()),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Transitions = RepositionTransitions(),
            Child = grid
        };
        if (!header)
        {
            EnsureInteractiveElement(row);
        }
        return row;
    }

    private static void AddConnectedDeviceCell(Grid grid, string text, int column, bool header, string status)
    {
        var cell = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        if (!header && column == 0)
        {
            cell.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = Brush(DeviceStatusColor(status)),
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        cell.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = header ? Brush("#88FFFFFF") : Brush("#D8FFFFFF"),
            FontWeight = header ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });
        AddToGrid(grid, cell, 0, column);
    }

    private static string DeviceStatusColor(string status)
    {
        if (status.Contains("已连接", StringComparison.Ordinal) || status.Contains("运行", StringComparison.Ordinal) || status.Contains("网关已接管", StringComparison.Ordinal))
        {
            return "#39D58A";
        }
        if (status.Contains("连接中", StringComparison.Ordinal) || status.Contains("等待", StringComparison.Ordinal) || status.Contains("已加入", StringComparison.Ordinal))
        {
            return "#FBBF24";
        }
        if (status.Contains("失败", StringComparison.Ordinal) || status.Contains("错误", StringComparison.Ordinal))
        {
            return "#F87171";
        }
        return "#94A3B8";
    }

    private static Border PlatformStatusCard(string label, string value, string detail)
    {
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(new TextBlock { Text = value, FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("White") });
        stack.Children.Add(new TextBlock { Text = label, Foreground = Brush("#B8FFFFFF") });
        stack.Children.Add(new TextBlock { Text = detail, Foreground = Brush("#88FFFFFF"), TextWrapping = TextWrapping.Wrap });
        return new Border
        {
            Tag = "GlassStat",
            Padding = new Thickness(14),
            Background = Brush(GlassWeakColor()),
            BorderBrush = Brush(GlassBorderColor()),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Math.Max(6, _activePersonalization.CornerRadius - 1)),
            Child = stack
        };
    }

    private static Border PlatformModuleCard(string title, string detail)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock { Text = title, Foreground = Brush("White"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        stack.Children.Add(new TextBlock { Text = detail, Foreground = Brush("#B8FFFFFF"), TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock { Text = "状态：可在对应页面操作", Foreground = Brush("#39D58A") });
        return new Border
        {
            Tag = "GlassRow",
            Padding = new Thickness(12),
            Background = Brush(GlassPanelColor()),
            BorderBrush = Brush(GlassBorderColor()),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Math.Max(6, _activePersonalization.CornerRadius - 1)),
            Child = stack
        };
    }

    private Button AgentActionButton(string text, string method, object payload)
    {
        var button = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Click += async (_, _) => await CallAgentAndLogAsync(method, payload);
        return button;
    }

    private async Task StartMeshFromSettingsAsync()
    {
        var payload = new
        {
            bootstraps = ParseLines(_dhtBootstrapBox?.Text),
            offlineMessageHours = ToInt(_offlineMessageHoursBox?.Value, 24),
            offlineMessageCapacityMb = ToInt(_offlineMessageCapacityBox?.Value, 128),
            stealthPolicy = "关闭",
            trafficCamouflage = "关闭",
            relay = new { maxHops = 3, policy = "authorized-or-same-group" }
        };
        await CallAgentAndLogAsync("mesh.start", payload);
        if (_meshStatusText is not null)
        {
            _meshStatusText.Text = "自愈网络：已请求启动。可点击“查看节点状态”确认 meshd/libp2p 或本地规则引擎状态。";
        }
    }

    private async Task CallAgentAndLogAsync(string method, object payload)
    {
        try
        {
            EnsureAgentProcess();
            var response = await _agentClient.CallAsync(method, payload);
            AppendDiagnostic(response.Ok
                ? $"Agent {method} 成功：{response.Result?.GetRawText()}"
                : $"Agent {method} 失败：{response.Error}");
        }
        catch (Exception ex)
        {
            AppendDiagnostic($"Agent {method} 不可用：{ex.Message}");
        }
    }

    private async Task RunWithButtonStateAsync(Button button, string busyText, Func<Task> action)
    {
        var originalContent = button.Content;
        button.IsEnabled = false;
        button.Content = busyText;
        try
        {
            await action();
        }
        finally
        {
            button.Content = originalContent;
            button.IsEnabled = true;
        }
    }

    private static void EnsureInteractiveElement(FrameworkElement element)
    {
        const string key = "__tpcwei_interactive";
        if (element.Resources.ContainsKey(key))
        {
            return;
        }

        element.Resources[key] = true;
        PrepareAnimatedElement(element);
        element.PointerEntered += (_, _) => AnimateInteractiveElement(element, 1.018, 0.98);
        element.PointerExited += (_, _) => AnimateInteractiveElement(element, 1.0, 1.0);
        element.PointerCanceled += (_, _) => AnimateInteractiveElement(element, 1.0, 1.0);
        element.PointerPressed += (_, _) => AnimateInteractiveElement(element, 0.985, 0.94);
        element.PointerReleased += (_, _) => AnimateInteractiveElement(element, 1.018, 0.98);
    }

    private static void AnimateInteractiveElement(FrameworkElement element, double scale, double opacity)
    {
        if (string.Equals(_activePersonalization.AnimationLevel, "关闭", StringComparison.Ordinal))
        {
            if (element.RenderTransform is CompositeTransform directTransform)
            {
                directTransform.ScaleX = scale;
                directTransform.ScaleY = scale;
            }
            element.Opacity = opacity;
            return;
        }

        var duration = Math.Max(90, AnimationDurationMilliseconds() / 2);
        var transform = PrepareAnimatedElement(element);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var storyboard = new Storyboard();
        AddDoubleAnimation(storyboard, transform, "ScaleX", transform.ScaleX == 0 ? 1 : transform.ScaleX, scale, duration, easing, dependent: true);
        AddDoubleAnimation(storyboard, transform, "ScaleY", transform.ScaleY == 0 ? 1 : transform.ScaleY, scale, duration, easing, dependent: true);
        AddDoubleAnimation(storyboard, element, "Opacity", element.Opacity, opacity, duration, easing);
        storyboard.Begin();
    }

    private Grid BuildGamePage()
    {
        var page = PageBase("远程局域网（Minecraft）", "MC Java 默认 TCP 25565；房主创建房间，朋友输入房间码和房间密码加入。同一 Wi-Fi 会自动发现，异地无官方服务器时会明确提示需要自建节点。", out _);
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var roleGrid = new Grid
        {
            ColumnSpacing = 50,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 34, 0, 0)
        };
        roleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(274) });
        roleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(274) });

        var host = GameRoleCard("\uE7F4", "创建 MC 局域网", "房主运行 Minecraft Java 服务器，然后复制房间码和房间密码给朋友");
        host.Tapped += (_, _) =>
        {
            NavigateTo("Connection");
            ShowConnectionSection("Create");
            _quickScenarioCombo.SelectedIndex = 4;
            _gameProtocolCombo.SelectedIndex = 1;
            _dualChannelSwitch.IsOn = true;
            ApplySelectedScenario();
            AppendDiagnostic("已进入 Minecraft 房主模式：Java 版默认 TCP 25565，点击“创建房间”后把房间码和房间密码发给朋友。");
        };
        var guest = GameRoleCard("\uE718", "加入 MC 局域网", "输入房主发来的房间码和房间密码，本机会生成可连接的本地地址");
        guest.Tapped += (_, _) =>
        {
            NavigateTo("Connection");
            ShowConnectionSection("Join");
            AppendDiagnostic("已进入 Minecraft 房客模式：输入房主发来的房间码和房间密码，然后点击“加入”。");
        };
        roleGrid.Children.Add(host);
        AddToGrid(roleGrid, guest, 0, 1);
        Grid.SetRow(roleGrid, 1);
        page.Children.Add(roleGrid);

        var advanced = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 10,
            Margin = new Thickness(0, 18, 0, 0)
        };
        _gameSetupCard = new Border { Child = advanced };
        var startTcp = new Button { Content = "高级：启动 TCP 通道" };
        startTcp.Click += (_, _) =>
        {
            NavigateTo("Connection");
            ShowConnectionSection("Manage");
            _quickScenarioCombo.SelectedIndex = 4;
            _gameProtocolCombo.SelectedIndex = 1;
            ApplySelectedScenario();
            StartSmartConnection_Click(startTcp, new RoutedEventArgs());
        };
        var startUdp = new Button { Content = "高级：启动 UDP 通道" };
        startUdp.Click += (_, _) =>
        {
            NavigateTo("Connection");
            ShowConnectionSection("Manage");
            _quickScenarioCombo.SelectedIndex = 4;
            _gameProtocolCombo.SelectedIndex = 2;
            ApplySelectedScenario();
            StartSmartConnection_Click(startUdp, new RoutedEventArgs());
        };
        var latency = new Button { Content = "一键自测" };
        latency.Click += async (_, _) =>
        {
            try { await RunSelfTestAsync(); }
            catch (Exception ex) { AppendDiagnostic($"游戏联机自测失败：{ex.Message}"); }
        };
        advanced.Children.Add(startTcp);
        advanced.Children.Add(startUdp);
        advanced.Children.Add(latency);
        Grid.SetRow(_gameSetupCard, 2);
        page.Children.Add(_gameSetupCard);

        _membersList = new ListView { ItemsSource = _members, Margin = new Thickness(0, 18, 0, 0) };
        var members = CardWithTitle("当前 Minecraft 房间成员", _membersList);
        Grid.SetRow(members, 3);
        page.Children.Add(members);
        return page;
    }

    private Border GameRoleCard(string glyph, string title, string description)
    {
        var stack = new StackPanel
        {
            Spacing = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        stack.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 42,
            Foreground = Brush("#B8FFFFFF"),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Brush("White"),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = description,
            Foreground = Brush("#C8FFFFFF"),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        var card = new Border
        {
            Tag = "GlassRow",
            Width = 274,
            Height = 186,
            Padding = new Thickness(18),
            Background = Brush(GlassPanelColor()),
            BorderBrush = Brush(GlassBorderColor()),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Math.Max(8, _activePersonalization.CornerRadius)),
            Child = stack
        };
        EnsureInteractiveElement(card);
        return card;
    }

    private Grid BuildRemoteDesktopPage()
    {
        var page = PageBase("远程桌面", "使用 RDP 映射完成可用闭环：创建 3389 映射、复制连接地址并启动 Windows 远程桌面。", out _remoteStatusText);
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        _qualityCombo = new ComboBox { Width = 140, SelectedIndex = 1, Items = { "性能优先", "平衡", "画质优先", "无损" } };
        _remoteConnectButton = new Button { Content = "创建 RDP 映射并连接" };
        _remoteConnectButton.Click += RemoteConnect_Click;
        toolbar.Children.Add(_qualityCombo);
        toolbar.Children.Add(_remoteConnectButton);
        var copyRdp = new Button { Content = "复制 mstsc 地址" };
        copyRdp.Click += CopyRemoteDesktopAddress_Click;
        toolbar.Children.Add(copyRdp);
        var experimental = new Button { Content = "实验远桌通道" };
        experimental.Click += StartExperimentalRemote_Click;
        toolbar.Children.Add(experimental);
        var manage = new Button { Content = "返回连接管理" };
        manage.Click += (_, _) =>
        {
            NavigateTo("Connection");
            ShowConnectionSection("Manage");
        };
        toolbar.Children.Add(manage);
        page.Children.Add(toolbar);

        var screen = new Border
        {
            Margin = new Thickness(0, 70, 0, 0),
            Background = Brush("#FF050607"),
            CornerRadius = new CornerRadius(8),
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "RDP 映射模式", FontSize = 30, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("White"), HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = "默认将本机 3389 映射为可访问地址；公网网关可用时优先使用网关入口。", Foreground = Brush("#B8FFFFFF"), HorizontalAlignment = HorizontalAlignment.Center }
                }
            }
        };
        Grid.SetRow(screen, 1);
        page.Children.Add(screen);
        return page;
    }

    private Grid BuildFileTransferPage()
    {
        var page = PageBase("文件传输", "发送、接收监听、拖拽选择、任务队列和失败诊断都会直接写入当前列表。", out _);
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var form = new Grid { ColumnSpacing = 10 };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _filePathBox = new TextBox { PlaceholderText = "文件路径" };
        _filePeerHostBox = new TextBox { PlaceholderText = "对端地址", Text = "127.0.0.1" };
        _filePeerPortBox = NumberBox(null, 9090);
        var send = new Button { Content = "发送文件" };
        send.Click += SendFile_Click;
        var receive = new Button { Content = "接收监听" };
        receive.Click += ReceiveFile_Click;
        AddToGrid(form, _filePathBox, 0, 0);
        AddToGrid(form, _filePeerHostBox, 0, 1);
        AddToGrid(form, _filePeerPortBox, 0, 2);
        AddToGrid(form, send, 0, 3);
        AddToGrid(form, receive, 0, 4);
        var formCard = Card;
        formCard.Child = form;
        formCard.Margin = new Thickness(0, 18, 0, 0);
        formCard.AllowDrop = true;
        formCard.DragOver += FileTransfer_DragOver;
        formCard.Drop += FileTransfer_Drop;
        Grid.SetRow(formCard, 1);
        page.Children.Add(formCard);

        _transferList = new ListView { ItemsSource = _transfers };
        var listCard = CardWithTitle("传输列表", _transferList);
        Grid.SetRow(listCard, 2);
        page.Children.Add(listCard);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消选中任务" };
        cancel.Click += CancelSelectedTransfer_Click;
        var retry = new Button { Content = "重试选中任务" };
        retry.Click += RetrySelectedTransfer_Click;
        actions.Children.Add(cancel);
        actions.Children.Add(retry);
        var actionCard = Card;
        actionCard.Padding = new Thickness(12);
        actionCard.Child = actions;
        Grid.SetRow(actionCard, 3);
        page.Children.Add(actionCard);
        return page;
    }

    private Grid BuildDeveloperPage()
    {
        var page = PageBase("开发者", "抓包、API 测试、性能分析和本机靶场演练；所有危险操作限制在自有环境。", out _);
        for (var i = 0; i < 4; i++)
        {
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        var warning = new StackPanel { Spacing = 10 };
        warning.Children.Add(new TextBlock { Text = "使用边界：只允许本机或自有靶场，不对第三方目标执行命令注入、扫描或攻击性操作。", Foreground = Brush("#FCA5A5"), TextWrapping = TextWrapping.Wrap });
        warning.Children.Add(new TextBlock { Text = "所有演练动作都会写入审计日志，可导出 JSON；pcapng 导出作为独立导出器补齐。", Foreground = Brush("#B8FFFFFF"), TextWrapping = TextWrapping.Wrap });
        var warningCard = CardWithTitle("本机/自有靶场限制", warning);
        Grid.SetRow(warningCard, 1);
        page.Children.Add(warningCard);

        var tools = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        for (var i = 0; i < 3; i++)
        {
            tools.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        for (var i = 0; i < 3; i++)
        {
            tools.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        AddToGrid(tools, DeveloperToolCard("智能抓包分析", "可视化网络流量、协议事件和异常连接。", "developer.capture"), 0, 0);
        AddToGrid(tools, DeveloperToolCard("API 测试工具", "模拟 P2P、网关和 Agent JSON-RPC 请求。", "developer.apiTest"), 0, 1);
        AddToGrid(tools, DeveloperToolCard("性能分析器", "定位 RTT、抖动、吞吐和重连瓶颈。", "developer.profile"), 0, 2);
        AddToGrid(tools, DeveloperToolCard("日志导出", "导出审计、诊断和 Wireshark 兼容数据。", "developer.export"), 1, 0);
        AddToGrid(tools, DeveloperToolCard("配置生成器", "AI 规则引擎生成 Profile 和网关部署建议。", "ai.diagnose"), 1, 1);
        AddToGrid(tools, DeveloperToolCard("本机靶场演练", "仅启动本机测试服务，不触达外部目标。", "developer.lab"), 1, 2);
        AddToGrid(tools, DeveloperToolCard("DHT 自愈状态", "查看 meshd、Bootstrap、缓存消息和传输计划。", "mesh.status"), 2, 0);
        AddToGrid(tools, DeveloperToolCard("授权节点列表", "列出已发现或本地记录的授权中继节点。", "mesh.peers.list"), 2, 1);
        AddToGrid(tools, DeveloperToolCard("离线消息同步", "只同步端到端加密后的轻量消息，不承载大文件。", "mesh.message.sync"), 2, 2);
        var toolsCard = CardWithTitle("开发者工具箱", tools);
        toolsCard.Margin = new Thickness(0, 18, 0, 0);
        Grid.SetRow(toolsCard, 2);
        page.Children.Add(toolsCard);
        return page;
    }

    private Border DeveloperToolCard(string name, string detail, string method)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock { Text = name, Foreground = Brush("White"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        stack.Children.Add(new TextBlock { Text = detail, Foreground = Brush("#B8FFFFFF"), TextWrapping = TextWrapping.Wrap });
        var run = new Button { Content = "运行本机工具" };
        run.Click += async (_, _) => await CallAgentAndLogAsync(method, new { scope = "local-lab", safe = true });
        stack.Children.Add(run);
        var card = Card;
        card.Child = stack;
        return card;
    }

    private Grid BuildSettingsPage()
    {
        var page = PageBase("设置", "通用、网络、外观、快捷键和关于集中在这里；高级模式打开后显示更多入口。", out _);
        for (var i = 0; i < 7; i++)
        {
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        _loadingPersonalizationUi = true;
        var startupCard = BuildStartupSettingsCard();
        Grid.SetRow(startupCard, 1);
        page.Children.Add(startupCard);

        var networkCard = BuildNetworkDefaultsSettingsCard();
        Grid.SetRow(networkCard, 2);
        page.Children.Add(networkCard);

        var personalizationCard = BuildPersonalizationSettingsCard();
        Grid.SetRow(personalizationCard, 3);
        page.Children.Add(personalizationCard);

        var shortcutCard = BuildShortcutSettingsCard();
        Grid.SetRow(shortcutCard, 4);
        page.Children.Add(shortcutCard);

        var gatewayCard = BuildGatewaySettingsCard();
        Grid.SetRow(gatewayCard, 5);
        page.Children.Add(gatewayCard);

        var dangerCard = BuildDangerSettingsCard();
        Grid.SetRow(dangerCard, 6);
        page.Children.Add(dangerCard);
        _loadingPersonalizationUi = false;
        return page;
    }

    private Border BuildPersonalizationSettingsCard()
    {
        _visualPresetCombo = Combo("视觉预设", "Fluent Pro", "极简玻璃", "专注暗色", "高对比");
        _backdropMaterialCombo = Combo("背景材质", "Acrylic 毛玻璃", "Mica 云母", "纯色高性能");
        _accentColorCombo = Combo("强调色", "蓝 #4F8CFF", "青 #2DD4BF", "绿 #39D58A", "紫 #8B5CF6", "橙 #F59E0B", "粉 #EC4899");
        _cornerRadiusCombo = Combo("圆角大小", "小 6", "中 8", "大 12");
        _animationLevelCombo = Combo("动画强度", "关闭", "轻量", "流畅", "极致");
        _layoutDensityCombo = Combo("布局密度", "舒适", "紧凑");
        _trafficGlowSwitch = new ToggleSwitch { Header = "流量图发光", IsOn = _personalization.TrafficGlow };
        _glassTransparencySlider = new Slider
        {
            Header = $"玻璃透明度：{_personalization.GlassTransparency}%",
            Minimum = 35,
            Maximum = 85,
            Value = _personalization.GlassTransparency,
            StepFrequency = 1
        };

        SetComboSelection(_visualPresetCombo, _personalization.VisualPreset);
        SetComboSelection(_backdropMaterialCombo, _personalization.BackdropMaterial);
        SetComboSelection(_accentColorCombo, AccentLabelFromHex(_personalization.AccentColor));
        SetComboSelection(_cornerRadiusCombo, _personalization.CornerRadius switch { <= 6 => "小 6", >= 12 => "大 12", _ => "中 8" });
        SetComboSelection(_animationLevelCombo, _personalization.AnimationLevel);
        SetComboSelection(_layoutDensityCombo, _personalization.CompactLayout ? "紧凑" : "舒适");

        foreach (var combo in new[] { _visualPresetCombo, _backdropMaterialCombo, _accentColorCombo, _cornerRadiusCombo, _animationLevelCombo, _layoutDensityCombo })
        {
            combo.SelectionChanged += (_, _) => SavePersonalizationFromUi();
        }
        _trafficGlowSwitch.Toggled += (_, _) => SavePersonalizationFromUi();
        _glassTransparencySlider.ValueChanged += (_, _) =>
        {
            if (_glassTransparencySlider is not null)
            {
                _glassTransparencySlider.Header = $"玻璃透明度：{Math.Round(_glassTransparencySlider.Value)}%";
            }
            SavePersonalizationFromUi();
        };

        var grid = SettingsGrid(4);
        AddToGrid(grid, _visualPresetCombo, 0, 0);
        AddToGrid(grid, _backdropMaterialCombo, 0, 1);
        AddToGrid(grid, _accentColorCombo, 0, 2);
        AddToGrid(grid, _cornerRadiusCombo, 0, 3);
        AddToGrid(grid, _animationLevelCombo, 1, 0);
        AddToGrid(grid, _layoutDensityCombo, 1, 1);
        AddToGrid(grid, _trafficGlowSwitch, 1, 2);
        AddToGrid(grid, _glassTransparencySlider, 2, 0, 1, 4);
        return SettingsCard("外观", "控制毛玻璃、强调色、圆角、动效和布局密度，修改后立即预览并保存。", grid);
    }

    private Border BuildNetworkDefaultsSettingsCard()
    {
        _defaultConnectionModeCombo = Combo("默认连接模式", "自动：P2P 优先，网关回退", "极速直连：完全无服务器", "自建网关：跨网兜底", "私密访问：不暴露公网端口", "智能直连：XTCP 竞速");
        _advancedModeSwitch = new ToggleSwitch { Header = "高级模式", IsOn = _personalization.AdvancedMode };
        _preferPublicIpv4Switch = new ToggleSwitch { Header = "优先公网 IPv4 直连", IsOn = _personalization.PreferPublicIpv4Direct };
        _manualPublicIpv4Box = TextBox("手动公网 IPv4（可空）", _personalization.ManualPublicIpv4);
        _dhtBootstrapBox = TextBox("DHT Bootstrap 节点（每行一个，自建/朋友节点）", string.Join(Environment.NewLine, _personalization.DhtBootstrapNodes));
        _offlineMessageHoursBox = NumberBox("离线消息保留小时", _personalization.OfflineMessageHours);
        _offlineMessageCapacityBox = NumberBox("离线消息容量 MB", _personalization.OfflineMessageCapacityMb);
        _publicIpv4StatusText = new TextBlock
        {
            Text = "公网 IPv4：尚未检测。点击“自动检测公网 IPv4”后会给出直连或端口转发建议。",
            Foreground = Brush("#B8FFFFFF"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        _meshStatusText = new TextBlock
        {
            Text = "自愈网络：未启动。配置自己的 Bootstrap 节点后可加入 DHT；未配置时保持本机离线规则引擎。",
            Foreground = Brush("#B8FFFFFF"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        SetComboSelection(_defaultConnectionModeCombo, _personalization.DefaultConnectionMode);
        _defaultConnectionModeCombo.SelectionChanged += (_, _) => SavePersonalizationFromUi();
        _preferPublicIpv4Switch.Toggled += (_, _) => SavePersonalizationFromUi();
        _manualPublicIpv4Box.TextChanged += (_, _) => SavePersonalizationFromUi();
        _dhtBootstrapBox.TextChanged += (_, _) => SavePersonalizationFromUi();
        _offlineMessageHoursBox.ValueChanged += (_, _) => SavePersonalizationFromUi();
        _offlineMessageCapacityBox.ValueChanged += (_, _) => SavePersonalizationFromUi();
        _advancedModeSwitch.Toggled += (_, _) => SavePersonalizationFromUi();

        var advancedModeButton = new Button
        {
            Content = _personalization.AdvancedMode ? "关闭高级模式" : "打开高级模式",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _advancedModeSwitch.Toggled += (_, _) =>
        {
            advancedModeButton.Content = _advancedModeSwitch.IsOn ? "关闭高级模式" : "打开高级模式";
        };
        advancedModeButton.Click += (_, _) =>
        {
            var enabled = !_personalization.AdvancedMode;
            SetAdvancedModeEnabled(enabled, navigateSettings: false);
            advancedModeButton.Content = enabled ? "关闭高级模式" : "打开高级模式";
        };
        var advancedModeBox = new StackPanel { Spacing = 8, Children = { _advancedModeSwitch, advancedModeButton } };

        var detectPublicIpv4 = new Button { Content = "自动检测公网 IPv4" };
        detectPublicIpv4.Click += async (_, _) => await DetectPublicIpv4FromSettingsAsync();
        var portAdvice = new Button { Content = "测试/提示端口" };
        portAdvice.Click += async (_, _) => await DetectPublicIpv4FromSettingsAsync();
        var startMesh = new Button { Content = "启动自愈网络" };
        startMesh.Click += async (_, _) => await StartMeshFromSettingsAsync();
        var meshStatus = new Button { Content = "查看节点状态" };
        meshStatus.Click += async (_, _) => await CallAgentAndLogAsync("mesh.status", new { });
        var setBootstrap = new Button { Content = "应用 Bootstrap" };
        setBootstrap.Click += async (_, _) => await CallAgentAndLogAsync("mesh.bootstrap.set", new { nodes = ParseLines(_dhtBootstrapBox.Text) });
        var syncMessages = new Button { Content = "同步离线消息" };
        syncMessages.Click += async (_, _) => await CallAgentAndLogAsync("mesh.message.sync", new { peer = "" });
        _networkAdvancedActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { startMesh, meshStatus, setBootstrap, syncMessages }
        };

        var grid = SettingsGrid(2);
        AddToGrid(grid, _defaultConnectionModeCombo, 0, 0);
        AddToGrid(grid, advancedModeBox, 0, 1);
        AddToGrid(grid, _preferPublicIpv4Switch, 1, 0);
        AddToGrid(grid, _manualPublicIpv4Box, 1, 1);
        AddToGrid(grid, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { detectPublicIpv4, portAdvice } }, 2, 0);
        AddToGrid(grid, new ToggleSwitch { Header = "启用 UPnP 自动端口映射", IsOn = true }, 2, 1);
        AddToGrid(grid, _publicIpv4StatusText, 3, 0, 1, 2);
        AddToGrid(grid, _dhtBootstrapBox, 4, 0, 1, 2);
        AddToGrid(grid, _offlineMessageHoursBox, 5, 0);
        AddToGrid(grid, _offlineMessageCapacityBox, 5, 1);
        AddToGrid(grid, _networkAdvancedActions, 6, 0, 1, 2);
        AddToGrid(grid, _meshStatusText, 7, 0, 1, 2);
        return SettingsCard("网络", "优先公网 IPv4 直连；没有公网入口时再提示端口转发或自建节点。高级模式用于 DHT、自建节点、网关、打洞和调试参数。", grid);
    }

    private Border BuildShortcutSettingsCard()
    {
        _shortcutCreateBox = TextBox("创建房间", ShortcutValue("CreateRoom", "Ctrl+N"));
        _shortcutJoinBox = TextBox("加入房间", ShortcutValue("JoinRoom", "Ctrl+J"));
        _shortcutFileBox = TextBox("发送文件", ShortcutValue("SendFile", "Ctrl+F"));
        _shortcutDesktopBox = TextBox("远程桌面", ShortcutValue("RemoteDesktop", "Ctrl+D"));
        _shortcutHelpBox = TextBox("帮助中心", ShortcutValue("Help", "F1"));
        foreach (var box in new[] { _shortcutCreateBox, _shortcutJoinBox, _shortcutFileBox, _shortcutDesktopBox, _shortcutHelpBox })
        {
            box.TextChanged += (_, _) => SavePersonalizationFromUi();
        }

        var grid = SettingsGrid(3);
        AddToGrid(grid, _shortcutCreateBox, 0, 0);
        AddToGrid(grid, _shortcutJoinBox, 0, 1);
        AddToGrid(grid, _shortcutFileBox, 0, 2);
        AddToGrid(grid, _shortcutDesktopBox, 1, 0);
        AddToGrid(grid, _shortcutHelpBox, 1, 1);
        AddToGrid(grid, new TextBlock
        {
            Text = "当前版本会保存快捷键文本并检测冲突；运行时默认快捷键立即可用。自定义按键解析会在下一次启动时继续按默认映射保护可用性。",
            Foreground = Brush("#B8FFFFFF"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        }, 1, 2);
        return SettingsCard("快捷键", "常用操作快捷键和冲突提示。", grid);
    }

    private string ShortcutValue(string key, string fallback)
    {
        return _personalization.KeyboardShortcuts.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }

    private Border BuildStartupSettingsCard()
    {
        _defaultStartPageCombo = Combo("默认启动页", "首页", "设备", "文件", "桌面", "游戏", "设置");
        _closeToTraySwitch = new ToggleSwitch { Header = "关闭窗口时最小化到托盘", IsOn = _personalization.CloseToTray };
        SetComboSelection(_defaultStartPageCombo, StartPageLabel(_personalization.DefaultStartPage));
        _defaultStartPageCombo.SelectionChanged += (_, _) => SavePersonalizationFromUi();
        _closeToTraySwitch.Toggled += (_, _) => SavePersonalizationFromUi();

        var grid = SettingsGrid(2);
        AddToGrid(grid, _defaultStartPageCombo, 0, 0);
        AddToGrid(grid, _closeToTraySwitch, 0, 1);
        AddToGrid(grid, new ToggleSwitch { Header = "启动后最小化", IsOn = false }, 1, 0);
        AddToGrid(grid, TextBox("界面语言", "简体中文"), 1, 1);
        return SettingsCard("启动与托盘", "启动页、关闭行为和语言偏好，适合长期后台运行。", grid);
    }

    private Border BuildGatewaySettingsCard()
    {
        var startGateway = new Button { Content = "本机启动网关" };
        startGateway.Click += async (_, _) => await CallAgentAndLogAsync("gateway.start", new
        {
            bind = "127.0.0.1",
            controlPort = 7000,
            adminPort = 7400,
            token = FirstNonEmpty(_gatewayTokenBox?.Text, "change-me")
        });
        var statusGateway = new Button { Content = "读取网关状态" };
        statusGateway.Click += async (_, _) => await CallAgentAndLogAsync("gateway.status", new { });
        var metricsGateway = new Button { Content = "读取网关指标" };
        metricsGateway.Click += async (_, _) => await CallAgentAndLogAsync("gateway.metrics", new { });
        var stack = new StackPanel
        {
            Spacing = SettingSpacing(),
            Children =
            {
                TextBox("网关部署命令", "tpcwei_gateway.exe --bind 0.0.0.0 --control-port 7000 --token <请改成强令牌>"),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { startGateway, statusGateway, metricsGateway } }
            }
        };
        return SettingsCard("网关部署", "复制到自己的 VPS 或公网机器运行，用于彻底覆盖 FRP 常用场景。", stack);
    }

    private Border BuildDangerSettingsCard()
    {
        var exitButton = new Button { Content = "彻底退出" };
        exitButton.Click += (_, _) =>
        {
            _exitRequested = true;
            Close();
        };
        var helpButton = new Button { Content = "打开帮助中心" };
        helpButton.Click += (_, _) => ToggleHelpPanel(true);
        var stack = new StackPanel
        {
            Spacing = SettingSpacing(),
            Children =
            {
                new TextBlock { Text = "TPC 坚持免费、自建网络优先。退出会关闭控制台窗口；由 Agent 接管的规则不受影响，未后台启动的本地映射会随进程结束。", Foreground = Brush("#B8FFFFFF"), TextWrapping = TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { helpButton, exitButton } }
            }
        };
        return SettingsCard("关于", "版本、帮助和退出。", stack);
    }

    private Border SettingsCard(string title, string subtitle, UIElement body)
    {
        var stack = new StackPanel { Spacing = SettingSpacing() };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("White") });
        stack.Children.Add(new TextBlock { Text = subtitle, Foreground = Brush("#B8FFFFFF"), TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(body);
        var card = Card;
        card.Margin = new Thickness(0, 18, 0, 0);
        card.Child = stack;
        return card;
    }

    private static Grid SettingsGrid(int columns)
    {
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = SettingSpacing() };
        for (var i = 0; i < columns; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        for (var i = 0; i < 8; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        return grid;
    }

    private static ComboBox Combo(string header, params string[] items)
    {
        var combo = new ComboBox { Header = header, MinWidth = 180 };
        foreach (var item in items)
        {
            combo.Items.Add(item);
        }
        combo.SelectedIndex = 0;
        return combo;
    }

    private static void SetComboSelection(ComboBox combo, string value)
    {
        for (var i = 0; i < combo.Items.Count; i++)
        {
            if (string.Equals(combo.Items[i]?.ToString(), value, StringComparison.Ordinal))
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private async void SavePersonalizationFromUi()
    {
        if (_loadingPersonalizationUi || _visualPresetCombo is null)
        {
            return;
        }

        var previousPreset = _personalization.VisualPreset;
        var selectedPreset = _visualPresetCombo.SelectedItem?.ToString() ?? "Fluent Pro";
        if (!string.Equals(previousPreset, selectedPreset, StringComparison.Ordinal))
        {
            ApplyVisualPresetToControls(selectedPreset);
        }

        var settings = ReadPersonalizationFromUi();
        ApplyPersonalization(settings, save: false, navigateToDefaultPage: false);
        try
        {
            await _personalizationStore.SaveAsync(settings);
            AppendDiagnostic("个性化设置已保存并即时应用。");
        }
        catch (Exception ex)
        {
            AppendDiagnostic($"保存个性化设置失败：{ex.Message}");
        }
    }

    private AppPersonalizationSettings ReadPersonalizationFromUi()
    {
        var settings = new AppPersonalizationSettings
        {
            VisualPreset = _visualPresetCombo.SelectedItem?.ToString() ?? "Fluent Pro",
            BackdropMaterial = _backdropMaterialCombo.SelectedItem?.ToString() ?? "Acrylic 毛玻璃",
            GlassTransparency = (int)Math.Round(_glassTransparencySlider.Value),
            AccentColor = AccentHexFromLabel(_accentColorCombo.SelectedItem?.ToString()),
            CornerRadius = _cornerRadiusCombo.SelectedItem?.ToString() switch
            {
                "小 6" => 6,
                "大 12" => 12,
                _ => 8
            },
            AnimationLevel = _animationLevelCombo.SelectedItem?.ToString() ?? "流畅",
            CompactLayout = string.Equals(_layoutDensityCombo.SelectedItem?.ToString(), "紧凑", StringComparison.Ordinal),
            TrafficGlow = _trafficGlowSwitch.IsOn,
            DefaultStartPage = StartPageTag(_defaultStartPageCombo.SelectedItem?.ToString()),
            CloseToTray = _closeToTraySwitch.IsOn,
            DefaultConnectionMode = _defaultConnectionModeCombo.SelectedItem?.ToString() ?? "自动：P2P 优先，网关回退",
            AdvancedMode = _advancedModeSwitch?.IsOn == true,
            AdvancedOptionsCollapsed = _advancedOptionsCollapsed,
            TutorialCompleted = _personalization.TutorialCompleted,
            DhtBootstrapNodes = ParseLines(_dhtBootstrapBox?.Text),
            StealthPolicy = "关闭",
            TrafficCamouflage = "关闭",
            PreferPublicIpv4Direct = _preferPublicIpv4Switch?.IsOn != false,
            ManualPublicIpv4 = _manualPublicIpv4Box?.Text.Trim() ?? "",
            OfflineMessageHours = (int)Math.Round(_offlineMessageHoursBox?.Value ?? 24),
            OfflineMessageCapacityMb = (int)Math.Round(_offlineMessageCapacityBox?.Value ?? 128),
            KeyboardShortcuts = ReadShortcutSettings()
        };
        settings.Normalize();
        return settings;
    }

    private Dictionary<string, string> ReadShortcutSettings()
    {
        var values = new Dictionary<string, string>
        {
            ["CreateRoom"] = FirstNonEmpty(_shortcutCreateBox?.Text, "Ctrl+N"),
            ["JoinRoom"] = FirstNonEmpty(_shortcutJoinBox?.Text, "Ctrl+J"),
            ["SendFile"] = FirstNonEmpty(_shortcutFileBox?.Text, "Ctrl+F"),
            ["RemoteDesktop"] = FirstNonEmpty(_shortcutDesktopBox?.Text, "Ctrl+D"),
            ["Help"] = FirstNonEmpty(_shortcutHelpBox?.Text, "F1")
        };
        var duplicates = values
            .GroupBy(x => x.Value.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Key.Length > 0 && x.Count() > 1)
            .Select(x => x.Key)
            .ToList();
        if (duplicates.Count > 0)
        {
            AppendDiagnostic($"快捷键存在冲突：{string.Join("、", duplicates)}。默认快捷键仍会保护可用。");
        }
        return values;
    }

    private async Task DetectPublicIpv4FromSettingsAsync()
    {
        if (_publicIpv4StatusText is null)
        {
            return;
        }

        _publicIpv4StatusText.Text = "公网 IPv4：正在检测本机网卡...";
        var port = ToPort(_publicPortBox?.Value ?? 0, ToPort(_localPortBox?.Value ?? 0, 25565));
        var response = await CallAgentWithAutoStartAsync("network.publicIpv4.detect", new
        {
            manualPublicIpv4 = _manualPublicIpv4Box?.Text.Trim() ?? "",
            port
        });
        if (!response.Ok || response.Result is null)
        {
            _publicIpv4StatusText.Text = $"公网 IPv4：检测失败。{response.Error ?? "后台没有返回结果"}";
            return;
        }

        var result = response.Result.Value;
        var mode = ReadJsonString(result, "mode", "unknown");
        var selected = ReadJsonString(result, "selectedPublicIpv4", "");
        var message = ReadJsonString(result, "message", "");
        var portAdvice = ReadJsonString(result, "portAdvice", "");
        if (!string.IsNullOrWhiteSpace(selected) && _manualPublicIpv4Box is not null && string.IsNullOrWhiteSpace(_manualPublicIpv4Box.Text))
        {
            _manualPublicIpv4Box.Text = selected;
        }

        _publicIpv4StatusText.Text = mode switch
        {
            "manual-public-ipv4" or "direct-public-ipv4" => $"公网 IPv4：可用于直连 {selected}:{port}。{portAdvice}",
            "router-public-ipv4-unknown" => $"公网 IPv4：本机在路由器后面。{message} {portAdvice}",
            _ => $"公网 IPv4：不可用。{message}"
        };
        AppendDiagnostic(_publicIpv4StatusText.Text);
    }

    private static List<string> ParseLines(string? text)
    {
        return (text ?? "")
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ApplyVisualPresetToControls(string preset)
    {
        _loadingPersonalizationUi = true;
        switch (preset)
        {
            case "极简玻璃":
                SetComboSelection(_backdropMaterialCombo, "Acrylic 毛玻璃");
                SetComboSelection(_cornerRadiusCombo, "大 12");
                SetComboSelection(_animationLevelCombo, "流畅");
                _glassTransparencySlider.Value = 72;
                break;
            case "专注暗色":
                SetComboSelection(_backdropMaterialCombo, "Mica 云母");
                SetComboSelection(_cornerRadiusCombo, "中 8");
                SetComboSelection(_animationLevelCombo, "轻量");
                _glassTransparencySlider.Value = 45;
                break;
            case "高对比":
                SetComboSelection(_backdropMaterialCombo, "纯色高性能");
                SetComboSelection(_cornerRadiusCombo, "小 6");
                SetComboSelection(_animationLevelCombo, "关闭");
                _glassTransparencySlider.Value = 35;
                break;
            default:
                SetComboSelection(_backdropMaterialCombo, "Acrylic 毛玻璃");
                SetComboSelection(_cornerRadiusCombo, "中 8");
                SetComboSelection(_animationLevelCombo, "流畅");
                _glassTransparencySlider.Value = 55;
                break;
        }
        _glassTransparencySlider.Header = $"玻璃透明度：{Math.Round(_glassTransparencySlider.Value)}%";
        _loadingPersonalizationUi = false;
    }

    private void ApplyPersonalization(AppPersonalizationSettings settings, bool save, bool navigateToDefaultPage)
    {
        settings.Normalize();
        _personalization = settings;
        _activePersonalization = settings;
        RootGrid.Background = Brush(RootOverlayColor());
        if (_titleBar is not null)
        {
            _titleBar.Background = Brush(TitleBarColor());
        }
        TryEnableBackdrop();
        ApplyDefaultConnectionMode();
        ApplyAdvancedModeVisibility();
        RegisterKeyboardShortcuts();
        RefreshPersonalizedVisuals(RootGrid);
        RefreshConnectionTabsForCurrentSection();
        if (_trafficChart is not null)
        {
            _trafficChart.GlowEnabled = settings.TrafficGlow;
        }
        if (navigateToDefaultPage)
        {
            NavigateTo(settings.DefaultStartPage);
        }
        if (save)
        {
            _ = _personalizationStore.SaveAsync(settings);
        }
    }

    private void ApplyAdvancedModeVisibility()
    {
        var visibility = _personalization.AdvancedMode ? Visibility.Visible : Visibility.Collapsed;
        if (_connectionCreateQuickActions is not null)
        {
            _connectionCreateQuickActions.Visibility = visibility;
        }
        if (_connectionCreateAdvancedHost is not null)
        {
            _connectionCreateAdvancedHost.Visibility = visibility;
        }
        if (_connectionCreateAdvancedHintCard is not null)
        {
            _connectionCreateAdvancedHintCard.Visibility = _personalization.AdvancedMode ? Visibility.Collapsed : Visibility.Visible;
        }
        if (_connectionManageAdvancedActions is not null)
        {
            _connectionManageAdvancedActions.Visibility = visibility;
        }
        if (_connectionManageAdvancedHost is not null)
        {
            _connectionManageAdvancedHost.Visibility = visibility;
        }
        ApplyAdvancedOptionsCollapsedState();
        if (_gameSetupCard is not null)
        {
            _gameSetupCard.Visibility = visibility;
        }
        if (_advancedModeSwitch is not null && _advancedModeSwitch.IsOn != _personalization.AdvancedMode)
        {
            _advancedModeSwitch.IsOn = _personalization.AdvancedMode;
        }
        EnsureAdvancedNavigationItems();
        foreach (var advancedSetting in new FrameworkElement?[]
        {
            _dhtBootstrapBox,
            _offlineMessageHoursBox,
            _offlineMessageCapacityBox,
            _networkAdvancedActions,
            _meshStatusText
        })
        {
            if (advancedSetting is not null)
            {
                advancedSetting.Visibility = visibility;
            }
        }
    }

    private void SetAdvancedModeEnabled(bool enabled, bool navigateSettings)
    {
        _personalization.AdvancedMode = enabled;
        if (_advancedModeSwitch is not null)
        {
            _advancedModeSwitch.IsOn = enabled;
        }
        ApplyPersonalization(_personalization, save: true, navigateToDefaultPage: false);
        AppendDiagnostic(enabled ? "高级模式已开启，连接页高级选项已显示。" : "高级模式已关闭，连接页恢复新手模式。");
        if (navigateSettings)
        {
            NavigateTo("Settings");
        }
    }

    private void RefreshPersonalizedVisuals(DependencyObject root)
    {
        if (root is Border border)
        {
            switch (border.Tag as string)
            {
                case "GlassPanel":
                    border.Background = Brush(GlassPanelColor());
                    border.BorderBrush = Brush(GlassBorderColor());
                    border.CornerRadius = new CornerRadius(_activePersonalization.CornerRadius);
                    border.Transitions = RepositionTransitions();
                    EnsureInteractiveElement(border);
                    break;
                case "GlassStat":
                    border.Background = Brush(GlassWeakColor());
                    border.BorderBrush = Brush(GlassBorderColor());
                    border.CornerRadius = new CornerRadius(Math.Max(6, _activePersonalization.CornerRadius - 1));
                    border.Transitions = RepositionTransitions();
                    EnsureInteractiveElement(border);
                    break;
                case "GlassHeaderRow":
                    border.Background = Brush(GlassWeakColor());
                    border.BorderBrush = Brush(GlassBorderColor());
                    border.CornerRadius = new CornerRadius(Math.Max(6, _activePersonalization.CornerRadius - 1));
                    border.Transitions = RepositionTransitions();
                    break;
                case "GlassRow":
                    border.Background = Brush(GlassPanelColor());
                    border.BorderBrush = Brush(GlassBorderColor());
                    border.CornerRadius = new CornerRadius(Math.Max(6, _activePersonalization.CornerRadius - 1));
                    border.Transitions = RepositionTransitions();
                    EnsureInteractiveElement(border);
                    break;
            }
        }
        else if (root is Button button)
        {
            EnsureInteractiveElement(button);
            if (button.Tag is string sectionTag && IsConnectionSectionTag(sectionTag))
            {
                button.Transitions = RepositionTransitions();
            }
        }
        else if (root is FrameworkElement element)
        {
            if (string.Equals(element.Tag as string, "RootPage", StringComparison.Ordinal))
            {
                element.Transitions = EntranceTransitions();
            }
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            RefreshPersonalizedVisuals(VisualTreeHelper.GetChild(root, i));
        }
    }

    private void RefreshConnectionTabsForCurrentSection()
    {
        if (_connectionCreateTab is null || _connectionJoinTab is null || _connectionManageTab is null)
        {
            return;
        }

        SetConnectionTabState(_connectionCreateTab, _connectionCreateSection?.Visibility == Visibility.Visible);
        SetConnectionTabState(_connectionJoinTab, _connectionJoinSection?.Visibility == Visibility.Visible);
        SetConnectionTabState(_connectionManageTab, _connectionManageSection?.Visibility == Visibility.Visible);
    }

    private static bool IsConnectionSectionTag(string tag)
    {
        return string.Equals(tag, "Create", StringComparison.Ordinal)
            || string.Equals(tag, "Join", StringComparison.Ordinal)
            || string.Equals(tag, "Manage", StringComparison.Ordinal);
    }

    private void ApplyDefaultConnectionMode()
    {
        if (_connectionModeCombo is null)
        {
            return;
        }
        SetComboSelection(_connectionModeCombo, _personalization.DefaultConnectionMode);
    }

    private static string AccentHexFromLabel(string? label) => label switch
    {
        "青 #2DD4BF" => "#2DD4BF",
        "绿 #39D58A" => "#39D58A",
        "紫 #8B5CF6" => "#8B5CF6",
        "橙 #F59E0B" => "#F59E0B",
        "粉 #EC4899" => "#EC4899",
        _ => "#4F8CFF"
    };

    private static string AccentLabelFromHex(string hex) => hex.ToUpperInvariant() switch
    {
        "#2DD4BF" => "青 #2DD4BF",
        "#39D58A" => "绿 #39D58A",
        "#8B5CF6" => "紫 #8B5CF6",
        "#F59E0B" => "橙 #F59E0B",
        "#EC4899" => "粉 #EC4899",
        _ => "蓝 #4F8CFF"
    };

    private static string StartPageTag(string? label) => label switch
    {
        "设备" or "仪表盘" => "Dashboard",
        "文件" or "文件传输" => "FileTransfer",
        "桌面" or "远程桌面" => "RemoteDesktop",
        "游戏" or "游戏联机" => "Game",
        "开发者" => "Developer",
        "设置" => "Settings",
        _ => "Connection"
    };

    private static string StartPageLabel(string tag) => tag switch
    {
        "Dashboard" => "设备",
        "FileTransfer" => "文件",
        "RemoteDesktop" => "桌面",
        "Game" => "游戏",
        "Developer" => "开发者",
        "Settings" => "设置",
        _ => "首页"
    };

    private Grid PageBase(string title, string subtitle, out TextBlock subtitleBlock)
    {
        var page = new Grid { Visibility = title is "首页" or "连接" ? Visibility.Visible : Visibility.Collapsed };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new StackPanel { Spacing = 6 };
        header.Children.Add(new TextBlock { Text = title, FontSize = 30, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("White") });
        subtitleBlock = new TextBlock { Text = subtitle, Foreground = Brush("#B8FFFFFF") };
        header.Children.Add(subtitleBlock);
        page.Children.Add(header);
        return page;
    }

    private static Border Card => new()
    {
        Tag = "GlassPanel",
        Padding = SettingPadding(),
        Background = Brush(GlassPanelColor()),
        BorderBrush = Brush(GlassBorderColor()),
        BorderThickness = new Thickness(1),
        Transitions = RepositionTransitions(),
        CornerRadius = new CornerRadius(_activePersonalization.CornerRadius)
    };

    private static Thickness SettingPadding() => _activePersonalization.CompactLayout
        ? new Thickness(14)
        : new Thickness(18);

    private static double SettingSpacing() => _activePersonalization.CompactLayout ? 10 : 14;

    private static string RootOverlayColor()
    {
        return string.Equals(_activePersonalization.BackdropMaterial, "纯色高性能", StringComparison.Ordinal)
            ? "#FF0D1117"
            : "#1F101620";
    }

    private static string TitleBarColor()
    {
        return string.Equals(_activePersonalization.BackdropMaterial, "纯色高性能", StringComparison.Ordinal)
            ? "#DD0F3278"
            : "#780F3278";
    }

    private static string GlassPanelColor()
    {
        var alpha = Math.Clamp(100 - _activePersonalization.GlassTransparency, 15, 65);
        if (string.Equals(_activePersonalization.BackdropMaterial, "纯色高性能", StringComparison.Ordinal))
        {
            alpha = Math.Max(alpha, 78);
        }
        return $"#{alpha:X2}151A22";
    }

    private static string GlassWeakColor()
    {
        var alpha = Math.Clamp(78 - _activePersonalization.GlassTransparency, 8, 38);
        return $"#{alpha:X2}151A22";
    }

    private static string GlassStrongColor()
    {
        var alpha = Math.Clamp(128 - _activePersonalization.GlassTransparency, 45, 92);
        if (string.Equals(_activePersonalization.BackdropMaterial, "纯色高性能", StringComparison.Ordinal))
        {
            alpha = Math.Max(alpha, 86);
        }
        return $"#{alpha:X2}151A22";
    }

    private static string GlassBorderColor()
    {
        var alpha = Math.Clamp(_activePersonalization.GlassTransparency / 2, 18, 46);
        return $"#{alpha:X2}FFFFFF";
    }

    private static string AccentPanelColor()
    {
        var color = _activePersonalization.AccentColor.TrimStart('#');
        return color.Length == 6 ? $"66{color}" : "#664F8CFF";
    }

    private static Border CardWithTitle(string title, UIElement body)
    {
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("White") });
        stack.Children.Add(body);
        var card = Card;
        card.Child = stack;
        return card;
    }

    private static TransitionCollection EntranceTransitions()
    {
        var transitions = new TransitionCollection();
        if (string.Equals(_activePersonalization.AnimationLevel, "关闭", StringComparison.Ordinal))
        {
            return transitions;
        }

        transitions.Add(new EntranceThemeTransition
        {
            FromVerticalOffset = _activePersonalization.AnimationLevel switch
            {
                "轻量" => 10,
                "极致" => 26,
                _ => 18
            },
            IsStaggeringEnabled = false
        });
        return transitions;
    }

    private static TransitionCollection RepositionTransitions()
    {
        var transitions = new TransitionCollection();
        if (string.Equals(_activePersonalization.AnimationLevel, "关闭", StringComparison.Ordinal))
        {
            return transitions;
        }
        transitions.Add(new RepositionThemeTransition());
        return transitions;
    }

    private static double AnimationOffset(bool horizontal)
    {
        if (string.Equals(_activePersonalization.AnimationLevel, "关闭", StringComparison.Ordinal))
        {
            return 0;
        }

        var distance = _activePersonalization.AnimationLevel switch
        {
            "轻量" => 24,
            "极致" => 64,
            _ => 42
        };
        return horizontal ? distance : distance * 0.78;
    }

    private static int AnimationDurationMilliseconds()
    {
        return _activePersonalization.AnimationLevel switch
        {
            "关闭" => 0,
            "轻量" => 180,
            "极致" => 360,
            _ => 260
        };
    }

    private static void AnimateElementIn(FrameworkElement element, bool horizontal)
    {
        TransitionElements(null, element, horizontal, forward: true, completed: null);
    }

    private static void TransitionElements(FrameworkElement? outgoing, FrameworkElement incoming, bool horizontal, bool forward, Action? completed)
    {
        var duration = AnimationDurationMilliseconds();
        if (duration <= 0)
        {
            if (outgoing is not null)
            {
                outgoing.Visibility = Visibility.Collapsed;
                ResetAnimatedElement(outgoing);
            }
            incoming.Visibility = Visibility.Visible;
            ResetAnimatedElement(incoming);
            completed?.Invoke();
            return;
        }

        incoming.Visibility = Visibility.Visible;
        incoming.IsHitTestVisible = false;
        Canvas.SetZIndex(incoming, 2);
        var offset = AnimationOffset(horizontal);
        var incomingTransform = PrepareAnimatedElement(incoming);
        incoming.Opacity = 0;
        incomingTransform.TranslateX = horizontal ? (forward ? offset : -offset) : 0;
        incomingTransform.TranslateY = horizontal ? 0 : (forward ? offset : -offset);
        incomingTransform.ScaleX = 0.985;
        incomingTransform.ScaleY = 0.985;

        CompositeTransform? outgoingTransform = null;
        if (outgoing is not null && !ReferenceEquals(outgoing, incoming))
        {
            outgoing.Visibility = Visibility.Visible;
            outgoing.IsHitTestVisible = false;
            Canvas.SetZIndex(outgoing, 1);
            outgoingTransform = PrepareAnimatedElement(outgoing);
            outgoing.Opacity = 1;
            outgoingTransform.TranslateX = 0;
            outgoingTransform.TranslateY = 0;
            outgoingTransform.ScaleX = 1;
            outgoingTransform.ScaleY = 1;
        }

        var enterEasing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var exitEasing = new CubicEase { EasingMode = EasingMode.EaseIn };
        var storyboard = new Storyboard();

        AddDoubleAnimation(storyboard, incoming, "Opacity", 0, 1, duration, enterEasing);
        AddDoubleAnimation(storyboard, incomingTransform, horizontal ? "TranslateX" : "TranslateY", forward ? offset : -offset, 0, duration, enterEasing, dependent: true);
        AddDoubleAnimation(storyboard, incomingTransform, "ScaleX", 0.985, 1, duration, enterEasing, dependent: true);
        AddDoubleAnimation(storyboard, incomingTransform, "ScaleY", 0.985, 1, duration, enterEasing, dependent: true);

        if (outgoing is not null && outgoingTransform is not null && !ReferenceEquals(outgoing, incoming))
        {
            var exitDuration = Math.Max(120, (int)(duration * 0.78));
            var exitOffset = forward ? -offset : offset;
            AddDoubleAnimation(storyboard, outgoing, "Opacity", 1, 0, exitDuration, exitEasing);
            AddDoubleAnimation(storyboard, outgoingTransform, horizontal ? "TranslateX" : "TranslateY", 0, exitOffset, exitDuration, exitEasing, dependent: true);
            AddDoubleAnimation(storyboard, outgoingTransform, "ScaleX", 1, 0.985, exitDuration, exitEasing, dependent: true);
            AddDoubleAnimation(storyboard, outgoingTransform, "ScaleY", 1, 0.985, exitDuration, exitEasing, dependent: true);
        }

        storyboard.Completed += (_, _) =>
        {
            if (outgoing is not null && !ReferenceEquals(outgoing, incoming))
            {
                outgoing.Visibility = Visibility.Collapsed;
                outgoing.IsHitTestVisible = true;
                ResetAnimatedElement(outgoing);
            }
            incoming.Visibility = Visibility.Visible;
            incoming.IsHitTestVisible = true;
            ResetAnimatedElement(incoming);
            completed?.Invoke();
        };
        storyboard.Begin();
    }

    private static CompositeTransform PrepareAnimatedElement(FrameworkElement element)
    {
        var transform = element.RenderTransform as CompositeTransform;
        if (transform is null)
        {
            transform = new CompositeTransform();
            element.RenderTransform = transform;
        }

        element.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        return transform;
    }

    private static void ResetAnimatedElement(FrameworkElement element)
    {
        element.Opacity = 1;
        if (element.RenderTransform is CompositeTransform transform)
        {
            transform.TranslateX = 0;
            transform.TranslateY = 0;
            transform.ScaleX = 1;
            transform.ScaleY = 1;
        }
    }

    private static void AddDoubleAnimation(Storyboard storyboard, DependencyObject target, string property, double from, double to, int milliseconds, EasingFunctionBase easing, bool dependent = false)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(milliseconds)),
            EasingFunction = easing,
            EnableDependentAnimation = dependent
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }

    private static ScrollViewer ScrollPage(Grid page, bool isVisible)
    {
        page.Visibility = Visibility.Visible;
        return new ScrollViewer
        {
            Tag = "RootPage",
            Content = page,
            Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
            ZoomMode = ZoomMode.Disabled
        };
    }

    private static TextBox TextBox(string? header, string text)
    {
        return new TextBox { Header = header, Text = text, TextWrapping = TextWrapping.Wrap };
    }

    private static NumberBox NumberBox(string? header, double value)
    {
        return new NumberBox { Header = header, Value = value, Minimum = 1, Maximum = 65535, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    }

    private Button PresetButton(string text, string tag)
    {
        var button = new Button { Content = text, Tag = tag };
        button.Click += Preset_Click;
        return button;
    }

    private static void AddToGrid(Grid grid, FrameworkElement element, int row, int column, int rowSpan = 1, int columnSpan = 1)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        if (rowSpan > 1) Grid.SetRowSpan(element, rowSpan);
        if (columnSpan > 1) Grid.SetColumnSpan(element, columnSpan);
        grid.Children.Add(element);
    }

    private static SolidColorBrush Brush(string color)
    {
        if (color == "White")
        {
            return new SolidColorBrush(Colors.White);
        }
        color = color.TrimStart('#');
        byte a = 255;
        var offset = 0;
        if (color.Length == 8)
        {
            a = Convert.ToByte(color[..2], 16);
            offset = 2;
        }
        var r = Convert.ToByte(color.Substring(offset, 2), 16);
        var g = Convert.ToByte(color.Substring(offset + 2, 2), 16);
        var b = Convert.ToByte(color.Substring(offset + 4, 2), 16);
        return new SolidColorBrush(Windows.UI.Color.FromArgb(a, r, g, b));
    }

    private void InitializeUiData()
    {
        _members.Add(new MemberViewItem("本机", "尚未生成", "0 ms", "在线"));
        AppendDiagnostic("准备就绪：默认会先尝试 P2P，必要时回退到自建公网网关。");
        _ = LoadCreatedProfilesAsync();
    }

    private async Task LoadCreatedProfilesAsync()
    {
        try
        {
            var profiles = await _profileStore.LoadAsync();
            DispatcherQueue.TryEnqueue(() =>
            {
                _createdProfiles.Clear();
                foreach (var profile in profiles)
                {
                    _createdProfiles.Add(profile);
                }

                RefreshConnectionListRows();
                if (_createdProfiles.Count > 0)
                {
                    AppendDiagnostic($"已载入 {_createdProfiles.Count} 条已创建连接。");
                }
            });
        }
        catch (Exception ex)
        {
            DispatcherQueue.TryEnqueue(() => AppendDiagnostic($"读取已创建连接失败：{ex.Message}"));
        }
    }

    private async Task RememberCreatedConnectionAsync(ProxyProfileDefinition profile)
    {
        var existingIndex = _createdProfiles
            .Select((value, index) => new { value, index })
            .FirstOrDefault(x => string.Equals(x.value.Id, profile.Id, StringComparison.Ordinal))?.index ?? -1;
        if (existingIndex >= 0)
        {
            _createdProfiles[existingIndex] = profile;
        }
        else
        {
            _createdProfiles.Add(profile);
        }

        RefreshConnectionListRows();

        try
        {
            await _profileStore.SaveAsync(profile);
        }
        catch (Exception ex)
        {
            AppendDiagnostic($"连接已在当前界面创建，但保存到本机配置失败：{ex.Message}");
        }
    }

    private void AppendDiagnostic(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {message}";
        _diagnostics.Add(line);
        while (_diagnostics.Count > 80)
        {
            _diagnostics.RemoveAt(0);
        }
    }

    private void NavigateTo(string tag)
    {
        var targetItem = (NavigationViewItem?)null;
        foreach (var item in _rootNavigation.MenuItems.OfType<NavigationViewItem>())
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.Ordinal))
            {
                targetItem = item;
                break;
            }
        }

        if (targetItem is not null && !ReferenceEquals(_rootNavigation.SelectedItem, targetItem))
        {
            _suppressNavigationSelection = true;
            try
            {
                _rootNavigation.SelectedItem = targetItem;
            }
            finally
            {
                _suppressNavigationSelection = false;
            }
        }

        ShowRootPage(tag, animate: true);
    }

    private void ShowRootPage(string tag, bool animate)
    {
        if (_connectionPage is null || _dashboardPage is null || _gamePage is null || _remoteDesktopPage is null || _fileTransferPage is null || _developerPage is null || _settingsPage is null)
        {
            return;
        }

        if (_isRootTransitioning)
        {
            return;
        }

        var targetTag = PageForTag(tag) is null ? "Connection" : tag;
        var changed = !string.Equals(_currentRootPageTag, targetTag, StringComparison.Ordinal);
        var targetPage = PageForTag(targetTag)!;
        var previousTag = _currentRootPageTag;
        var previousPage = PageForTag(previousTag);
        targetPage.Transitions = EntranceTransitions();

        if (!changed || !animate || AnimationDurationMilliseconds() <= 0)
        {
            SetRootPageVisibility(targetTag);
            return;
        }

        _isRootTransitioning = true;
        _rootNavigation.IsEnabled = false;
        CollapseRootPagesExcept(previousPage, targetPage);
        var forward = RootPageIndex(targetTag) >= RootPageIndex(previousTag);
        TransitionElements(previousPage, targetPage, horizontal: true, forward: forward, () =>
        {
            _currentRootPageTag = targetTag;
            SetRootPageVisibility(targetTag);
            _rootNavigation.IsEnabled = true;
            _isRootTransitioning = false;
        });
    }

    private void SetRootPageVisibility(string tag)
    {
        var targetTag = PageForTag(tag) is null ? "Connection" : tag;
        _connectionPage.Visibility = targetTag == "Connection" ? Visibility.Visible : Visibility.Collapsed;
        _dashboardPage.Visibility = targetTag == "Dashboard" ? Visibility.Visible : Visibility.Collapsed;
        _gamePage.Visibility = targetTag == "Game" ? Visibility.Visible : Visibility.Collapsed;
        _remoteDesktopPage.Visibility = targetTag == "RemoteDesktop" ? Visibility.Visible : Visibility.Collapsed;
        _fileTransferPage.Visibility = targetTag == "FileTransfer" ? Visibility.Visible : Visibility.Collapsed;
        _developerPage.Visibility = targetTag == "Developer" ? Visibility.Visible : Visibility.Collapsed;
        _settingsPage.Visibility = targetTag == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        ResetAnimatedElement(_connectionPage);
        ResetAnimatedElement(_dashboardPage);
        ResetAnimatedElement(_gamePage);
        ResetAnimatedElement(_remoteDesktopPage);
        ResetAnimatedElement(_fileTransferPage);
        ResetAnimatedElement(_developerPage);
        ResetAnimatedElement(_settingsPage);
        _currentRootPageTag = targetTag;
    }

    private void CollapseRootPagesExcept(FrameworkElement? outgoing, FrameworkElement incoming)
    {
        foreach (var page in new[] { _connectionPage, _dashboardPage, _gamePage, _remoteDesktopPage, _fileTransferPage, _developerPage, _settingsPage })
        {
            if (page is null || ReferenceEquals(page, outgoing) || ReferenceEquals(page, incoming))
            {
                continue;
            }

            page.Visibility = Visibility.Collapsed;
            ResetAnimatedElement(page);
        }
    }

    private FrameworkElement? PageForTag(string tag)
    {
        return tag switch
        {
            "Connection" => _connectionPage,
            "Dashboard" => _dashboardPage,
            "Game" => _gamePage,
            "RemoteDesktop" => _remoteDesktopPage,
            "FileTransfer" => _fileTransferPage,
            "Developer" => _developerPage,
            "Settings" => _settingsPage,
            _ => null
        };
    }

    private static int RootPageIndex(string tag)
    {
        return tag switch
        {
            "Connection" => 0,
            "Dashboard" => 1,
            "Game" => 2,
            "RemoteDesktop" => 3,
            "FileTransfer" => 4,
            "Developer" => 5,
            "Settings" => 6,
            _ => 0
        };
    }

    private void RefreshLinkSummary()
    {
        if (_linkSummaryText is null)
        {
            return;
        }

        var roomText = string.IsNullOrWhiteSpace(_currentRoomCode) ? "未创建" : _currentRoomCode;
        var passwordState = string.IsNullOrWhiteSpace(_roomPasswordBox?.Text) ? "未设置密码" : "已设置密码";
        _linkSummaryText.Text = $"当前链接：{roomText}  凭据：房间码 + 房间密码（{passwordState}）  模式：{_connectionModeCombo?.SelectedItem ?? "自动"}";
        if (_healthStatusText is not null)
        {
            _healthStatusText.Text = _gatewayHandle == 0
                ? "健康度：等待连接。若 P2P 失败，配置自建网关即可覆盖 FRP 场景。"
                : "健康度：公网网关在线，可为 TCP 映射提供稳定入口。";
        }
    }

    private string CreateInviteCode()
    {
        var protocol = _gameProtocolCombo?.SelectedIndex switch
        {
            1 => "TCP",
            2 => "UDP",
            _ => "TCP+UDP"
        };
        var peerHost = GetPreferredShareAddress();
        return string.Join("|",
            "TPCWEIROOM1",
            _currentRoomCode,
            EncodePackageValue(_roomPasswordBox?.Text.Trim() ?? ""),
            EncodePackageValue(_currentPublicCode),
            protocol,
            ToPort(_localPortBox.Value, 8080).ToString(),
            ToPort(_publicPortBox.Value, ToPort(_localPortBox.Value, 8080)).ToString(),
            _gatewayHostBox.Text,
            ToPort(_gatewayPortBox.Value, 7000).ToString(),
            peerHost);
    }

    private string GetPreferredShareAddress()
    {
        if (_personalization.PreferPublicIpv4Direct)
        {
            // 用户手动填的公网 IP 优先级最高。
            // 自动检测不一定知道路由器外面的地址，用户自己填的通常更准。
            var manual = FirstNonEmpty(_manualPublicIpv4Box?.Text, _personalization.ManualPublicIpv4);
            if (IsPublicIpv4(manual))
            {
                return manual;
            }

            var localPublic = GetLocalPublicIpv4();
            if (!string.IsNullOrWhiteSpace(localPublic))
            {
                return localPublic;
            }
        }

        if (!string.IsNullOrWhiteSpace(_simplePeerHostBox?.Text) && !IsLoopbackOrWildcardHost(_simplePeerHostBox.Text))
        {
            return _simplePeerHostBox.Text.Trim();
        }

        return GetShareableLocalAddress();
    }

    private static string GetShareableLocalAddress()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up
                    || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var props = nic.GetIPProperties();
                var hasGateway = props.GatewayAddresses.Any(x => x.Address.AddressFamily == AddressFamily.InterNetwork);
                foreach (var address in props.UnicastAddresses.Select(x => x.Address))
                {
                    if (address.AddressFamily == AddressFamily.InterNetwork
                        && !IPAddress.IsLoopback(address)
                        && (hasGateway || IsPrivateIpv4(address)))
                    {
                        return address.ToString();
                    }
                }
            }
        }
        catch
        {
            // Keep invite creation usable even if Windows cannot enumerate adapters.
        }

        return "";
    }

    private static string GetLocalPublicIpv4()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up
                    || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                foreach (var address in nic.GetIPProperties().UnicastAddresses.Select(x => x.Address))
                {
                    if (address.AddressFamily == AddressFamily.InterNetwork && IsPublicIpv4(address.ToString()))
                    {
                        return address.ToString();
                    }
                }
            }
        }
        catch
        {
            // Adapter enumeration can fail on restricted systems; callers will continue without direct IPv4.
        }

        return "";
    }

    private static bool IsPublicIpv4(string value)
    {
        if (!IPAddress.TryParse(value.Trim(), out var address) || address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes.Length == 4
            && bytes[0] != 10
            && !(bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            && !(bytes[0] == 192 && bytes[1] == 168)
            && !(bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
            && !(bytes[0] == 169 && bytes[1] == 254)
            && bytes[0] != 0
            && bytes[0] < 224
            && bytes[0] != 127;
    }

    private static bool IsPrivateIpv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4
            && (bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168));
    }

    private async Task PreparePublicIpv4ForRoomAsync()
    {
        if (!_personalization.PreferPublicIpv4Direct)
        {
            return;
        }

        var localPort = ToPort(_localPortBox.Value, 25565);
        var publicPort = ToPort(_publicPortBox.Value, localPort);
        var protocol = _gameProtocolCombo?.SelectedIndex == 2 ? "UDP" : "TCP";
        var preferred = GetPreferredShareAddress();
        if (IsPublicIpv4(preferred))
        {
            // 能直接拿到公网地址，就先写进房间信息。
            // 这一步只是把“朋友该连哪里”说清楚，不等于已经连通。
            _simplePeerHostBox.Text = preferred;
            _peerHostBox.Text = preferred;
            AppendDiagnostic($"公网 IPv4 直连已准备：{preferred}:{publicPort}。请确认防火墙和路由器端口已放行。");
            return;
        }

        try
        {
            // 没有现成公网地址时，试一下让路由器自动开端口。
            // 很多家用路由器会允许 UPnP；不允许也没关系，后面会给用户明确提示。
            await NativeInterop.InitializeAsync();
            var mapped = await NativeInterop.CreateUpnpMappingAsync(localPort, publicPort, protocol);
            if (mapped)
            {
                var endpoint = await NativeInterop.GetExternalEndpointAsync();
                if (IsPublicIpv4(endpoint.Address))
                {
                    _simplePeerHostBox.Text = endpoint.Address;
                    _peerHostBox.Text = endpoint.Address;
                    if (endpoint.Port > 0)
                    {
                        _publicPortBox.Value = endpoint.Port;
                    }
                    AppendDiagnostic($"UPnP 已建立公网 IPv4 映射：{endpoint.Address}:{(endpoint.Port > 0 ? endpoint.Port : publicPort)}。");
                    return;
                }
            }

            AppendDiagnostic($"未检测到可写入分享包的公网 IPv4。若公网 IPv4 在路由器上，请手动转发 {protocol}/{publicPort}，或在设置里填写公网 IPv4。");
        }
        catch (Exception ex)
        {
            AppendDiagnostic($"公网 IPv4 / UPnP 准备失败：{ex.Message}。仍会创建房间，但异地朋友可能需要手动端口转发或自建节点。");
        }
    }

    private void ApplyInviteCode(string invite)
    {
        _currentInviteCode = invite;
        var parts = invite.Split('|');
        if (parts.Length >= 9 && parts[0] == "TPCWEIROOM1")
        {
            _currentRoomCode = parts[1];
            var roomPassword = DecodePackageValue(parts[2]);
            _currentPublicCode = DecodePackageValue(parts[3]);
            _currentPrivateCode = "";
            _roomCodeBox.Text = _currentRoomCode;
            _publicCodeBox.Text = _currentPublicCode;
            if (_roomPasswordBox is not null)
            {
                _roomPasswordBox.Text = roomPassword;
            }
            if (_joinPublicCodeBox is not null)
            {
                _joinPublicCodeBox.Text = _currentRoomCode;
            }
            if (_joinPrivateCodeBox is not null)
            {
                _joinPrivateCodeBox.Text = roomPassword;
            }
            _inviteCodeBox.Text = invite;
            if (ushort.TryParse(parts[5], out var roomLocalPort))
            {
                _localPortBox.Value = roomLocalPort;
            }
            if (ushort.TryParse(parts[6], out var roomPublicPort))
            {
                _publicPortBox.Value = roomPublicPort;
                _peerPortBox.Value = roomPublicPort;
            }
            _gatewayHostBox.Text = parts[7];
            if (ushort.TryParse(parts[8], out var roomGatewayPort))
            {
                _gatewayPortBox.Value = roomGatewayPort;
            }
            if (parts.Length >= 10 && !string.IsNullOrWhiteSpace(parts[9]))
            {
                _simplePeerHostBox.Text = parts[9].Trim();
                _peerHostBox.Text = parts[9].Trim();
            }
            AppendDiagnostic("已导入房间码和房间密码，请在列表中点击“启动”。");
            RefreshLinkSummary();
            return;
        }

        if (parts.Length >= 9 && parts[0] is "TPCWEIKEY1" or "TPCWEIKEY2")
        {
            _currentRoomCode = parts[1];
            _currentPublicCode = DecodePackageValue(parts[2]);
            _currentPrivateCode = DecodePackageValue(parts[3]);
            _roomCodeBox.Text = _currentRoomCode;
            _publicCodeBox.Text = _currentPublicCode;
            if (_currentPrivateCode.Contains('\n'))
            {
                _groupPrivateCodesBox.Text = _currentPrivateCode.Replace("\n", Environment.NewLine);
                AddPrivateCodesToList(ParsePrivateCodeLines(_currentPrivateCode));
            }
            else
            {
                _privateCodeBox.Text = _currentPrivateCode;
            }
            _inviteCodeBox.Text = invite;
            if (ushort.TryParse(parts[5], out var keyLocalPort))
            {
                _localPortBox.Value = keyLocalPort;
            }
            if (ushort.TryParse(parts[6], out var keyPublicPort))
            {
                _publicPortBox.Value = keyPublicPort;
            }
            _gatewayHostBox.Text = parts[7];
            if (ushort.TryParse(parts[8], out var keyGatewayPort))
            {
                _gatewayPortBox.Value = keyGatewayPort;
            }
            if (parts[0] == "TPCWEIKEY2" && parts.Length >= 10 && !string.IsNullOrWhiteSpace(parts[9]))
            {
                _simplePeerHostBox.Text = parts[9].Trim();
                _peerHostBox.Text = parts[9].Trim();
                if (_filePeerHostBox is not null)
                {
                    _filePeerHostBox.Text = parts[9].Trim();
                }
            }
            AppendDiagnostic("已导入完整连接信息，请在列表中点击“启动”。");
            RefreshLinkSummary();
            return;
        }

        if (parts.Length < 8 || parts[0] != "TPCWEI1")
        {
            AppendDiagnostic("连接信息格式不正确。");
            return;
        }

        _currentRoomCode = parts[1];
        _currentPublicCode = parts[2];
        _roomCodeBox.Text = _currentRoomCode;
        _publicCodeBox.Text = _currentPublicCode;
        _inviteCodeBox.Text = invite;
        if (ushort.TryParse(parts[4], out var localPort))
        {
            _localPortBox.Value = localPort;
        }
        if (ushort.TryParse(parts[5], out var publicPort))
        {
            _publicPortBox.Value = publicPort;
        }
        _gatewayHostBox.Text = parts[6];
        if (ushort.TryParse(parts[7], out var gatewayPort))
        {
            _gatewayPortBox.Value = gatewayPort;
        }
        AppendDiagnostic("已导入旧版链接包，请在列表中点击“启动”。");
        RefreshLinkSummary();
    }

    private static string EncodePackageValue(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));
    }

    private static string DecodePackageValue(string value)
    {
        return Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }

    private async void StartCore_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await StartKernelOneClickAsync();
        }
        catch (Exception ex)
        {
            _nodeStatusText.Text = $"启动失败：{ex.Message}";
            if (_healthStatusText is not null)
            {
                _healthStatusText.Text = $"状态：内核启动失败。{ex.Message}";
            }
            AppendDiagnostic($"一键启动内核失败：{ex.Message}");
        }
    }

    private async void StopCore_Click(object sender, RoutedEventArgs e)
    {
        if (_nodeHandle == 0)
        {
            _nodeStatusText.Text = "核心未启动";
            return;
        }

        await NativeInterop.StopNodeAsync(_nodeHandle);
        _nodeHandle = 0;
        _nodeStatusText.Text = "核心已停止";
    }

    private async Task EnsureNodeStartedAsync()
    {
        if (_nodeHandle != 0)
        {
            _nodeStatusText.Text = "节点运行中";
            return;
        }

        await NativeInterop.InitializeAsync();
        _nodeHandle = await NativeInterop.StartNodeAsync(port: 0, lanDiscovery: true, upnp: true);
        var metrics = await NativeInterop.GetNodeMetricsAsync(_nodeHandle);
        _nodeStatusText.Text = $"节点运行中：UDP/TCP {metrics.UdpPort}";
    }

    private async Task StartKernelOneClickAsync()
    {
        _nodeStatusText.Text = "内核启动中...";
        if (_healthStatusText is not null)
        {
            _healthStatusText.Text = "状态：正在启动 Agent、P2P 核心和局域网发现。";
        }

        var agent = await CallAgentWithAutoStartAsync("metrics.snapshot", new { });
        if (!agent.Ok)
        {
            throw new InvalidOperationException(agent.Error ?? "后台 Agent 无法启动。");
        }

        await EnsureNodeStartedAsync();

        if (!HasVirtualLanDriver())
        {
            AppendDiagnostic("未检测到完整虚拟局域网驱动；Minecraft 专用远程局域网模式仍可用。后续安装驱动后可启用完整虚拟局域网。");
        }

        var advertised = 0;
        foreach (var profile in _createdProfiles.ToArray())
        {
            var discoveryKey = DiscoveryKeyForProfile(profile);
            if (string.IsNullOrWhiteSpace(discoveryKey))
            {
                continue;
            }

            var role = IsGuestProfile(profile) ? "房客" : "房主";
            var response = await StartLanAdvertisementOnlyAsync(profile, discoveryKey, role);
            if (response.Ok)
            {
                advertised++;
            }
            else
            {
                AppendDiagnostic($"连接 {profile.Name} 的局域网发现启动失败：{response.Error ?? "无返回"}");
            }
        }

        var message = advertised > 0
            ? $"内核已启动，并已为 {advertised} 条连接开启局域网发现。"
            : "内核已启动。创建或加入连接后会自动开启局域网发现。";
        _nodeStatusText.Text = "内核已启动";
        if (_healthStatusText is not null)
        {
            _healthStatusText.Text = $"状态：{message}";
        }
        AppendDiagnostic(message);
    }

    private static bool HasVirtualLanDriver()
    {
        var baseDir = AppContext.BaseDirectory;
        return File.Exists(Path.Combine(baseDir, "wintun.dll"))
            || File.Exists(Path.Combine(baseDir, "drivers", "wintun.dll"))
            || Directory.EnumerateFiles(baseDir, "*.inf", SearchOption.TopDirectoryOnly)
                .Any(x => Path.GetFileName(x).Contains("tap", StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(x).Contains("tun", StringComparison.OrdinalIgnoreCase));
    }

    private void RootNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_suppressNavigationSelection || _connectionPage is null || _dashboardPage is null || _gamePage is null || _remoteDesktopPage is null || _fileTransferPage is null || _developerPage is null || _settingsPage is null)
        {
            return;
        }

        var tag = (args.SelectedItem as NavigationViewItem)?.Tag?.ToString() ?? "Connection";
        ShowRootPage(tag, animate: true);
    }

    private async void StartTunnel_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await EnsureNodeStartedAsync();
            var protocol = _tunnelProtocolCombo.SelectedIndex == 1 ? TunnelProtocol.Udp : TunnelProtocol.Tcp;
            var item = await StartLocalTunnelAsync(protocol, "P2P/直连");
            _tunnelStatusText.Text = $"映射已启动：{item.RouteText}";
            AppendDiagnostic($"本地映射已启动：{item.ProtocolText} {item.RouteText}");
        }
        catch (Exception ex)
        {
            _tunnelStatusText.Text = $"启动映射失败：{ex.Message}";
            AppendDiagnostic($"启动映射失败：{ex.Message}");
        }
    }

    private async Task<TunnelViewItem> StartLocalTunnelAsync(TunnelProtocol protocol, string label)
    {
        var localPort = ToPort(_localPortBox.Value, 8080);
        var peerPort = ToPort(_peerPortBox.Value, 80);
        var peerHost = !string.IsNullOrWhiteSpace(_simplePeerHostBox?.Text) ? _simplePeerHostBox.Text : _peerHostBox.Text;
        var handle = await NativeInterop.StartTunnelAsync(
            _nodeHandle,
            _allowLanClientsBox.IsChecked == true ? "0.0.0.0" : "127.0.0.1",
            localPort,
            peerHost,
            peerPort,
            protocol,
            _allowLanClientsBox.IsChecked == true);

        var item = new TunnelViewItem
        {
            Handle = handle,
            Name = $"{(string.IsNullOrWhiteSpace(_tunnelNameBox.Text) ? "未命名映射" : _tunnelNameBox.Text)} / {label}",
            Protocol = protocol,
            LocalPort = localPort,
            PeerHost = peerHost,
            PeerPort = peerPort,
            Running = true
        };
        _tunnels.Add(item);
        _tunnelList.SelectedItem = item;
        RefreshConnectionListRows();
        return item;
    }

    private async void StopTunnel_Click(object sender, RoutedEventArgs e)
    {
        await StopSelectedTunnelAsync();
    }

    private async Task StopSelectedTunnelAsync()
    {
        if (_tunnelList.SelectedItem is not TunnelViewItem item)
        {
            _tunnelStatusText.Text = "请先选择一个映射";
            return;
        }

        try
        {
            await NativeInterop.StopTunnelAsync(item.Handle);
            item.Running = false;
            item.StatusText = "已停止";
            _tunnelStatusText.Text = $"映射已停止：{item.Name}";
        }
        catch (Exception ex)
        {
            _tunnelStatusText.Text = $"停止失败：{ex.Message}";
        }

        RefreshConnectionListRows();
    }

    private void TunnelList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_tunnelList.SelectedItem is TunnelViewItem item)
        {
            _trafficChart.SetSamples(item.Samples);
        }
    }

    private void ToggleAdvanced_Click(object sender, RoutedEventArgs e)
    {
        _advancedTunnelCard.Visibility = _advancedTunnelCard.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private async void StartSmartConnection_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var profile = _createdProfiles.LastOrDefault(x =>
                x.DisplayStatus is "已加入，等待启动" or "等待朋友加入" or "连接失败" or "网关连接失败" or "已停止")
                ?? CreateCurrentProfile();
            if (string.IsNullOrWhiteSpace(profile.RoomCode) && !string.IsNullOrWhiteSpace(_currentRoomCode))
            {
                profile.RoomCode = _currentRoomCode;
            }
            if (string.IsNullOrWhiteSpace(profile.Id))
            {
                profile.Id = Guid.NewGuid().ToString("N");
            }

            await RememberCreatedConnectionAsync(profile);
            await StartFriendConnectionAsync(profile);
        }
        catch (Exception ex)
        {
            _tunnelStatusText.Text = $"一键连接失败：{ex.Message}";
            AppendDiagnostic($"一键连接失败：{ex.Message}");
        }
    }

    private async void ConnectGateway_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await EnsureGatewayConnectedAsync();
        }
        catch (Exception ex)
        {
            AppendDiagnostic($"连接网关失败：{ex.Message}");
        }
    }

    private async Task EnsureGatewayConnectedAsync()
    {
        if (_gatewayHandle != 0)
        {
            return;
        }

        await NativeInterop.InitializeAsync();
        AppendDiagnostic($"连接自建公网网关：{_gatewayHostBox.Text}:{ToPort(_gatewayPortBox.Value, 7000)}");
        _gatewayHandle = await NativeInterop.ConnectGatewayAsync(
            _gatewayHostBox.Text,
            ToPort(_gatewayPortBox.Value, 7000),
            _gatewayTokenBox.Text,
            string.IsNullOrWhiteSpace(_currentRoomCode) ? _roomCodeBox.Text : _currentRoomCode,
            _currentPublicCode);
        AppendDiagnostic("公网网关已连接。");
        RefreshLinkSummary();
    }

    private void CopyInvite_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentInviteCode))
        {
            _currentInviteCode = CreateInviteCode();
            _inviteCodeBox.Text = _currentInviteCode;
        }

        CopyShareTextToClipboard(TryFormatKeyPairShareText(_currentInviteCode, out var keyText) ? keyText : _currentInviteCode);
        AppendDiagnostic(_currentInviteCode.StartsWith("TPCWEIROOM1", StringComparison.OrdinalIgnoreCase)
            ? "房间码和房间密码已复制到剪贴板。"
            : "旧版公钥和私钥已复制到剪贴板。");
    }

    private async void PasteInvite_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Text))
            {
                var text = await content.GetTextAsync();
                var pasted = text.Trim();
                if (pasted.StartsWith("TPCWEI", StringComparison.OrdinalIgnoreCase)
                    || IsShortRoomCode(pasted))
                {
                    _inviteCodeBox.Text = pasted;
                    ApplyInviteCode(pasted);
                    return;
                }

                if (!TryFillJoinKeysFromText(pasted))
                {
                    _inviteCodeBox.Text = pasted;
                }
            }
        }
        catch (Exception ex)
        {
            AppendDiagnostic($"读取剪贴板失败：{ex.Message}");
        }
    }

    private async void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var profile = CreateCurrentProfile();
            await _profileStore.SaveAsync(profile);
            await new SecureSecretStore().SaveSecretAsync("gateway-token", _gatewayTokenBox.Text);
            AppendDiagnostic($"规则已保存到本机配置：{profile.Name}");
        }
        catch (Exception ex)
        {
            AppendDiagnostic($"保存规则失败：{ex.Message}");
        }
    }

    private async void StartAgentProfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var profile = CreateCurrentProfile();
            await RememberCreatedConnectionAsync(profile);
            await StartFriendConnectionAsync(profile);
        }
        catch (Exception ex)
        {
            AppendDiagnostic($"后台启动失败：{ex.Message}");
        }
    }

    private async void ImportFrp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".toml");
            picker.FileTypeFilter.Add(".ini");
            picker.FileTypeFilter.Add(".conf");
            picker.FileTypeFilter.Add(".txt");
            InitializeWithWindow.Initialize(picker, _hwnd);
            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            var result = await new FrpImportService().ImportFileAsync(file.Path);
            foreach (var profile in result.Profiles)
            {
                await _profileStore.SaveAsync(profile);
            }

            AppendDiagnostic($"已导入 FRP 配置：{result.Profiles.Count} 条规则；未迁移项 {result.UnsupportedItems.Count} 个。");
            if (result.Profiles.FirstOrDefault() is { } first)
            {
                ApplyProfileToForm(first);
            }
        }
        catch (Exception ex)
        {
            AppendDiagnostic($"导入 FRP 配置失败：{ex.Message}");
        }
    }

    private void CopyGatewayDeploy_Click(object sender, RoutedEventArgs e)
    {
        var plan = _gatewayDeployService.CreatePlan(new GatewayDeployOptions
        {
            GatewayHost = _gatewayHostBox.Text,
            ControlPort = ToPort(_gatewayPortBox.Value, 7000),
            Token = _gatewayTokenBox.Text
        });

        var text = "Windows:\n" + plan.WindowsCommand +
                   "\n\nLinux systemd:\n" + plan.LinuxSystemdCommand +
                   "\n\nDocker:\n" + plan.DockerCommand +
                   "\n\ngateway.json:\n" + plan.GatewayJson;
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        AppendDiagnostic("网关部署命令和 gateway.json 已复制到剪贴板。");
    }

    private async void SelfTest_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await RunSelfTestAsync();
        }
        catch (Exception ex)
        {
            AppendDiagnostic($"一键自测失败：{ex.Message}");
        }
    }

    private ProxyProfileDefinition CreateCurrentProfile()
    {
        return new ProxyProfileDefinition
        {
            Name = string.IsNullOrWhiteSpace(_tunnelNameBox.Text) ? "TPC 规则" : _tunnelNameBox.Text,
            RoomCode = _currentRoomCode,
            RoomPasswordHash = RoomDiscoveryHash(_currentRoomCode, _roomPasswordBox?.Text ?? ""),
            TrustedPackageHash = StableHash(_currentInviteCode),
            TrustedPackage = _currentInviteCode,
            DisplayStatus = string.IsNullOrWhiteSpace(_currentRoomCode) ? "草稿" : "等待朋友加入",
            CreatedAt = DateTimeOffset.Now,
            Type = SelectedProxyType(),
            Mode = SelectedProxyMode(),
            LocalHost = _allowLanClientsBox.IsChecked == true ? "0.0.0.0" : "127.0.0.1",
            LocalPort = ToPort(_localPortBox.Value, 8080),
            PeerHost = string.IsNullOrWhiteSpace(_simplePeerHostBox.Text) ? _peerHostBox.Text : _simplePeerHostBox.Text,
            RemotePort = ToPort(_peerPortBox.Value, 80),
            PublicPort = ToPort(_publicPortBox.Value, ToPort(_localPortBox.Value, 8080)),
            GatewayHost = _gatewayHostBox.Text.Trim(),
            GatewayControlPort = ToPort(_gatewayPortBox.Value, 7000),
            GatewayToken = _gatewayTokenBox.Text,
            PreferP2p = _preferP2pSwitch.IsOn && _connectionModeCombo.SelectedIndex != 2,
            AllowGatewayFallback = _allowGatewayFallbackSwitch.IsOn && _connectionModeCombo.SelectedIndex != 1,
            HealthCheck = _healthCheckCombo.SelectedItem?.ToString() ?? (SelectedProxyType() == ProxyRuleType.Http ? "http" : "tcp"),
            Compression = _compressionSwitch.IsOn,
            Encryption = _encryptionSwitch.IsOn
        };
    }

    private static string StableHash(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string RoomDiscoveryHash(string roomCode, string roomPassword)
    {
        var room = roomCode.Trim();
        var password = roomPassword.Trim();
        return string.IsNullOrWhiteSpace(password)
            ? StableHash(room)
            : StableHash($"{room}|{password}");
    }

    private ProxyRuleType SelectedProxyType()
    {
        return _quickScenarioCombo.SelectedIndex switch
        {
            0 => ProxyRuleType.Http,
            1 => ProxyRuleType.Https,
            4 => _tunnelProtocolCombo.SelectedIndex == 1 ? ProxyRuleType.Udp : ProxyRuleType.Tcp,
            6 => _tunnelProtocolCombo.SelectedIndex == 1 ? ProxyRuleType.Sudp : ProxyRuleType.Stcp,
            7 => ProxyRuleType.Xtcp,
            8 => ProxyRuleType.PortRange,
            _ => _tunnelProtocolCombo.SelectedIndex == 1 ? ProxyRuleType.Udp : ProxyRuleType.Tcp
        };
    }

    private ProxyRuleMode SelectedProxyMode()
    {
        return _connectionModeCombo.SelectedIndex switch
        {
            1 => ProxyRuleMode.P2P,
            2 => ProxyRuleMode.Gateway,
            3 => ProxyRuleMode.Secret,
            4 => ProxyRuleMode.SmartDirect,
            _ => _quickScenarioCombo.SelectedIndex switch
            {
                6 => ProxyRuleMode.Secret,
                7 => ProxyRuleMode.SmartDirect,
                _ => ProxyRuleMode.Auto
            }
        };
    }

    private void ApplyProfileToForm(ProxyProfileDefinition profile)
    {
        _currentRoomCode = profile.RoomCode;
        _tunnelNameBox.Text = profile.Name;
        _localPortBox.Value = profile.LocalPort;
        _peerPortBox.Value = profile.RemotePort;
        _publicPortBox.Value = profile.PublicPort;
        _simplePeerHostBox.Text = profile.PeerHost;
        _gatewayHostBox.Text = profile.GatewayHost;
        _gatewayPortBox.Value = profile.GatewayControlPort == 0 ? 7000 : profile.GatewayControlPort;
        if (!string.IsNullOrWhiteSpace(profile.GatewayToken))
        {
            _gatewayTokenBox.Text = profile.GatewayToken;
        }
        if (!string.IsNullOrWhiteSpace(profile.TrustedPackage))
        {
            _currentInviteCode = profile.TrustedPackage;
            _inviteCodeBox.Text = profile.TrustedPackage;
        }
        _tunnelProtocolCombo.SelectedIndex = profile.Type is ProxyRuleType.Udp or ProxyRuleType.Sudp ? 1 : 0;
        _preferP2pSwitch.IsOn = profile.PreferP2p;
        _allowGatewayFallbackSwitch.IsOn = profile.AllowGatewayFallback;
        _compressionSwitch.IsOn = profile.Compression;
        _encryptionSwitch.IsOn = profile.Encryption;
        var healthItems = _healthCheckCombo.Items.Cast<object>().Select(x => x.ToString()).ToList();
        var healthIndex = healthItems.FindIndex(x => string.Equals(x, profile.HealthCheck, StringComparison.OrdinalIgnoreCase));
        _healthCheckCombo.SelectedIndex = healthIndex >= 0 ? healthIndex : 0;
        _tunnelStatusText.Text = $"已载入规则：{profile.Name}";
        RefreshLinkSummary();
    }

    private void EnsureAgentProcess()
    {
        if (Process.GetProcessesByName("TPC.Agent").Any())
        {
            return;
        }

        var agentPath = Path.Combine(AppContext.BaseDirectory, "TPC.Agent.exe");
        if (!File.Exists(agentPath))
        {
            throw new FileNotFoundException("发布目录中没有 TPC.Agent.exe", agentPath);
        }

        Process.Start(new ProcessStartInfo(agentPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        });
        AppendDiagnostic("后台 Agent 已启动。");
    }

    private async Task RunSelfTestAsync()
    {
        var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var tcpPort = ((IPEndPoint)tcp.LocalEndpoint).Port;
        var tcpTask = Task.Run(async () =>
        {
            using var client = await tcp.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            var buffer = Encoding.UTF8.GetBytes("TPCwei self test ok");
            await stream.WriteAsync(buffer);
        });

        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, tcpPort);
            var buffer = new byte[64];
            var read = await client.GetStream().ReadAsync(buffer);
            AppendDiagnostic($"TCP 自测成功：{Encoding.UTF8.GetString(buffer, 0, read)}");
        }
        tcp.Stop();
        await tcpTask;

        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var udpPort = ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
        var payload = Encoding.UTF8.GetBytes("udp-ok");
        await udp.SendAsync(payload, payload.Length, new IPEndPoint(IPAddress.Loopback, udpPort));
        var received = await udp.ReceiveAsync();
        AppendDiagnostic($"UDP 自测成功：{Encoding.UTF8.GetString(received.Buffer)}");

        var profile = CreateCurrentProfile();
        var message = await NativeInterop.ValidateProxyProfileAsync(ProfileStore.ToNativeJson(profile));
        AppendDiagnostic(message);
        _healthStatusText.Text = "健康度：本机 TCP/UDP 和 Profile 校验通过。";
    }

    private void SyncPrivateListToGroup_Click(object sender, RoutedEventArgs e)
    {
        SyncPrivateListToGroupText();
        AppendDiagnostic($"已同步 {_privateCodeItems.Count} 组私钥到群组私钥输入框。");
    }

    private async void GenerateGroupPublic_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await GeneratePublicFromCurrentPrivateInputsAsync(requireGroup: true);
            AppendDiagnostic("群组公钥已生成。");
        }
        catch (Exception ex)
        {
            AppendDiagnostic($"生成群组公钥失败：{ex.Message}");
        }
    }

    private async void GeneratePublic_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await GeneratePublicFromCurrentPrivateInputsAsync(requireGroup: false);
            AppendDiagnostic($"公钥已生成：{ShortCode(result.PublicCode)}，哈希 {ShortCode(result.PublicHashHex)}。");
        }
        catch (Exception ex)
        {
            AppendDiagnostic($"生成公钥失败：{ex.Message}");
            if (_tunnelStatusText is not null)
            {
                _tunnelStatusText.Text = $"生成公钥失败：{ex.Message}";
            }
        }
    }

    private int AddPrivateCodesToList(IEnumerable<string> privateCodes)
    {
        var added = 0;
        foreach (var code in privateCodes.Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (_privateCodeItems.Any(x => string.Equals(x, code, StringComparison.Ordinal)))
            {
                continue;
            }
            _privateCodeItems.Add(code);
            added++;
        }
        UpdatePrivateListCount();
        SyncPrivateListToGroupText();
        return added;
    }

    private void UpdatePrivateListCount()
    {
    }

    private void SyncPrivateListToGroupText()
    {
        if (_groupPrivateCodesBox is not null)
        {
            _groupPrivateCodesBox.Text = string.Join(Environment.NewLine, _privateCodeItems);
        }
    }

    private static List<string> ParsePrivateCodeLines(string text)
    {
        return text
            .Split(["\r\n", "\n", "\r"], StringSplitOptions.None)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }

    private async Task<SecurityPublicResult> GeneratePublicFromCurrentPrivateInputsAsync(bool requireGroup)
    {
        await NativeInterop.InitializeAsync();
        var groupPrivateCodes = ParsePrivateCodeLines(_groupPrivateCodesBox.Text);
        if (groupPrivateCodes.Count >= 2)
        {
            var uniqueGroupCodes = groupPrivateCodes
                .Where(IsPrivateDisplayCode)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (uniqueGroupCodes.Count >= 2 && uniqueGroupCodes.Count == groupPrivateCodes.Distinct(StringComparer.Ordinal).Count())
            {
                var result = await NativeInterop.PrivateGroupToPublicAsync(uniqueGroupCodes);
                _currentPrivateCode = string.Join("\n", uniqueGroupCodes);
                _currentPublicCode = result.PublicCode;
                _currentPublicHash = result.PublicHashHex;
                _publicCodeBox.Text = result.PublicCode;
                _members.Clear();
                _members.Add(new MemberViewItem("群组身份", ShortCode(result.PublicCode), "0 ms", $"已合成 {uniqueGroupCodes.Count} 组私钥"));
                _members.Add(new MemberViewItem("对端", "等待可信设备", "等待", "未连接"));
                RefreshLinkSummary();
                return result;
            }

            if (requireGroup)
            {
                throw new InvalidOperationException("至少需要 2 组有效且不重复的 64 位私钥才能生成群组公钥。");
            }

            AppendDiagnostic("群组私钥不完整或有重复，创建链接已自动改用本机单私钥。");
        }

        if (requireGroup)
        {
            throw new InvalidOperationException("至少需要 2 组有效私钥才能生成群组公钥。");
        }

        _currentPrivateCode = groupPrivateCodes.Count == 1
            ? groupPrivateCodes[0]
            : FirstNonEmpty(_privateCodeBox?.Text, _currentPrivateCode);
        if (string.IsNullOrWhiteSpace(_currentPrivateCode))
        {
            var generated = await GeneratePrivateCodeForCurrentDeviceAsync();
            _currentPrivateCode = generated.PrivateCode;
            AppendDiagnostic("未检测到私钥，已自动生成一组本机私钥并继续生成公钥。");
        }

        _currentPrivateCode = _currentPrivateCode.Trim();
        if (!IsPrivateDisplayCode(_currentPrivateCode))
        {
            AppendDiagnostic("检测到无效私钥，已自动生成新的本机私钥继续创建链接。");
            var generated = await GeneratePrivateCodeForCurrentDeviceAsync();
            _currentPrivateCode = generated.PrivateCode;
        }

        var singleResult = await NativeInterop.PrivateToPublicAsync(_currentPrivateCode);
        _currentPublicCode = singleResult.PublicCode;
        _currentPublicHash = singleResult.PublicHashHex;
        _publicCodeBox.Text = singleResult.PublicCode;
        if (_privateCodeBox is not null)
        {
            _privateCodeBox.Text = _currentPrivateCode;
        }
        if (_members.Count == 0 || _members[0].Name != "群组身份")
        {
            _members.Clear();
            _members.Add(new MemberViewItem("本机", ShortCode(singleResult.PublicCode), "0 ms", "本机身份"));
            _members.Add(new MemberViewItem("对端", "等待可信设备", "等待", "未连接"));
        }
        RefreshLinkSummary();
        return singleResult;
    }

    private async Task<NativeInterop.SecurityPairingResult> GeneratePrivateCodeForCurrentDeviceAsync()
    {
        var deviceCode = FirstNonEmpty(_deviceName, Environment.MachineName, "TPC-Device");
        var result = await NativeInterop.GeneratePairingCodesAsync(deviceCode);
        if (_privateCodeBox is not null)
        {
            _privateCodeBox.Text = result.PrivateCode;
        }
        return result;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "";
    }

    private static bool IsPrivateDisplayCode(string value)
    {
        return value.Length == 64 && value.All(char.IsLetterOrDigit);
    }

    private void ApplySelectedScenario()
    {
        switch (_quickScenarioCombo?.SelectedIndex ?? 0)
        {
            case 1:
                ApplyTunnelPreset("HTTPS/SNI 服务", 8443, 443, false);
                _publicPortBox.Value = 443;
                break;
            case 2:
                ApplyTunnelPreset("SSH 管理", 2222, 22, false);
                _publicPortBox.Value = 2222;
                break;
            case 3:
                ApplyTunnelPreset("远程桌面", 3389, 3389, false);
                _publicPortBox.Value = 3389;
                break;
            case 4:
                if (_gameProtocolCombo?.SelectedIndex == 2)
                {
                    ApplyTunnelPreset("Minecraft Bedrock 联机", 19132, 19132, true);
                    _publicPortBox.Value = 19132;
                }
                else
                {
                    ApplyTunnelPreset("Minecraft Java 联机", 25565, 25565, false);
                    _publicPortBox.Value = 25565;
                }
                break;
            case 5:
                ApplyTunnelPreset("文件传输", 9090, 9090, false);
                _publicPortBox.Value = 9090;
                break;
            case 6:
                ApplyTunnelPreset("私密访问", 7001, 7001, false);
                _connectionModeCombo.SelectedIndex = 3;
                _publicPortBox.Value = 7001;
                break;
            case 7:
                ApplyTunnelPreset("智能直连", 7002, 7002, false);
                _connectionModeCombo.SelectedIndex = 4;
                _publicPortBox.Value = 7002;
                break;
            case 8:
                ApplyTunnelPreset("端口范围", 10000, 10000, false);
                _publicPortBox.Value = 10000;
                break;
            case 9:
                break;
            default:
                ApplyTunnelPreset("Web 服务", 8080, 80, false);
                _publicPortBox.Value = 8080;
                break;
        }
    }

    private void ApplyGameProtocolToConnection()
    {
        if (_quickScenarioCombo?.SelectedIndex == 4)
        {
            if (_gameProtocolCombo?.SelectedIndex == 2)
            {
                ApplyTunnelPreset("Minecraft Bedrock 联机", 19132, 19132, true);
                _publicPortBox.Value = 19132;
                _tunnelStatusText.Text = "Minecraft Bedrock 已设置为 UDP 19132。";
                return;
            }

            ApplyTunnelPreset("Minecraft Java 联机", 25565, 25565, false);
            _publicPortBox.Value = 25565;
            _tunnelStatusText.Text = "Minecraft Java 已设置为 TCP 25565。";
            return;
        }

        switch (_gameProtocolCombo?.SelectedIndex ?? 0)
        {
            case 1:
                _tunnelProtocolCombo.SelectedIndex = 0;
                _tunnelStatusText.Text = "游戏联机协议已设置为 TCP。";
                break;
            case 2:
                _tunnelProtocolCombo.SelectedIndex = 1;
                _tunnelStatusText.Text = "游戏联机协议已设置为 UDP。";
                break;
            default:
                _tunnelProtocolCombo.SelectedIndex = 0;
                _tunnelStatusText.Text = "游戏联机协议为自动：会优先启动 TCP，需要 UDP 时可在高级设置切换。";
                break;
        }
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as FrameworkElement)?.Tag?.ToString();
        switch (tag)
        {
            case "SSH":
                ApplyTunnelPreset("SSH 管理", 2222, 22, false);
                break;
            case "RDP":
                ApplyTunnelPreset("远程桌面", 3389, 3389, false);
                break;
            case "GameUdp":
                ApplyTunnelPreset("Minecraft Bedrock 联机", 19132, 19132, true);
                _publicPortBox.Value = 19132;
                break;
            default:
                ApplyTunnelPreset("Web 服务", 8080, 80, false);
                break;
        }
    }

    private void ApplyTunnelPreset(string name, ushort localPort, ushort peerPort, bool udp)
    {
        _tunnelNameBox.Text = name;
        _localPortBox.Value = localPort;
        _peerPortBox.Value = peerPort;
        _tunnelProtocolCombo.SelectedIndex = udp ? 1 : 0;
        _tunnelStatusText.Text = $"已套用{name}预设。确认远端地址后点击“启动映射”。";
    }

    private void OnTunnelEvent(object? sender, TunnelEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var item = _tunnels.FirstOrDefault(x => x.Handle == e.Tunnel);
            if (item is null || e.Metrics is not { } metrics)
            {
                return;
            }

            item.BytesUp = metrics.BytesUp;
            item.BytesDown = metrics.BytesDown;
            item.ActiveConnections = metrics.ActiveConnections;
            item.Running = metrics.Running != 0;
            item.Samples.Add((metrics.BytesUp, metrics.BytesDown));
            while (item.Samples.Count > 80)
            {
                item.Samples.RemoveAt(0);
            }
            item.RefreshText();

            if (ReferenceEquals(_tunnelList.SelectedItem, item))
            {
                _trafficChart.SetSamples(item.Samples);
            }

            RefreshConnectionListRows();
        });
    }

    private void OnGatewayEvent(object? sender, GatewayEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var message = string.IsNullOrWhiteSpace(e.Message) ? e.EventType.ToString() : e.Message;
            AppendDiagnostic($"网关：{message}");
            if (e.Metrics is { } metrics && _healthStatusText is not null)
            {
                _healthStatusText.Text = $"健康度：网关在线={metrics.Running != 0}  活跃连接={metrics.ActiveConnections}  错误={metrics.ErrorCount}";
            }
        });
    }

    private void OnProxyEvent(object? sender, ProxyEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var message = string.IsNullOrWhiteSpace(e.Message) ? e.EventType.ToString() : e.Message;
            AppendDiagnostic($"代理规则：{message}");
            if (e.Metrics is { } metrics && _healthStatusText is not null)
            {
                _healthStatusText.Text = $"健康度：{metrics.HealthScore}/100  活跃连接={metrics.ActiveConnections}  错误={metrics.ErrorCount}";
            }
        });
    }

    private async void CreateRoom_Click(object sender, RoutedEventArgs e)
    {
        await CreateFriendConnectionFromDefaultsAsync();
    }

    private async Task CreateFriendConnectionFromDefaultsAsync()
    {
        try
        {
            await GeneratePublicFromCurrentPrivateInputsAsync(requireGroup: false);
            _currentRoomCode = $"ROOM-{DateTimeOffset.Now:yyyyMMdd-HHmmss}";
            ApplyGameProtocolToConnection();
            await PreparePublicIpv4ForRoomAsync();
            _currentInviteCode = CreateInviteCode();

            _roomCodeBox.Text = _currentRoomCode;
            _publicCodeBox.Text = _currentPublicCode;
            if (!string.IsNullOrWhiteSpace(_currentPrivateCode) && !_currentPrivateCode.Contains('\n'))
            {
                _privateCodeBox.Text = _currentPrivateCode;
            }
            _inviteCodeBox.Text = _currentInviteCode;

            if (_members.Count == 0 || _members.All(x => x.Name != "群组身份"))
            {
                _members.Clear();
                _members.Add(new MemberViewItem("本机", ShortCode(_currentPublicCode), "0 ms", "房主"));
                _members.Add(new MemberViewItem("对端", "等待可信设备", "等待", "未连接"));
            }

            var profile = CreateCurrentProfile();
            profile.Role = "房主";
            profile.RoomCode = _currentRoomCode;
            profile.RoomPasswordHash = RoomDiscoveryHash(_currentRoomCode, _roomPasswordBox?.Text ?? "");
            profile.TrustedPackageHash = StableHash(_currentInviteCode);
            profile.TrustedPackage = _currentInviteCode;
            profile.DisplayStatus = "等待朋友加入";
            profile.ConnectionPath = "局域网发现 / 自建节点兜底";
            profile.CreatedAt = DateTimeOffset.Now;
            if (!profile.Name.Contains(_currentRoomCode, StringComparison.Ordinal))
            {
                profile.Name = $"{profile.Name} - {_currentRoomCode}";
            }
            await RememberCreatedConnectionAsync(profile);
            try
            {
                await TryResolveLanPeerAsync(profile);
            }
            catch (Exception discoveryEx)
            {
                AppendDiagnostic($"连接已创建，但局域网自动发现没有启动：{discoveryEx.Message}");
            }
            CopyShareTextToClipboard(ShareTextForProfile(profile));
            RefreshLinkSummary();
            RefreshConnectionListRows();
            var passwordNote = string.IsNullOrWhiteSpace(_roomPasswordBox?.Text)
                ? "房间密码为空，已允许加入但安全性较低。"
                : _roomPasswordBox.Text.Trim().Length < 6
                    ? "房间密码少于 6 位，已允许加入但建议改长一点。"
                    : "房间密码已设置。";
            AppendDiagnostic($"连接已创建：{_currentRoomCode}。已复制房间码和房间密码，请发给朋友；{passwordNote} Minecraft Java 默认端口 25565，基岩版默认 UDP 19132。");
            _tunnelStatusText.Text = $"等待朋友加入：{profile.Name}";
            NavigateTo("Connection");
            ShowConnectionSection("List");
        }
        catch (Exception ex)
        {
            AppendDiagnostic($"创建链接失败：{ex.Message}");
            if (_tunnelStatusText is not null)
            {
                _tunnelStatusText.Text = $"创建链接失败：{ex.Message}";
            }
        }
    }

    private bool TryFillJoinKeysFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var lines = text
            .Split(["\r\n", "\n", "\r"], StringSplitOptions.None)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        var roomCode = "";
        var roomPassword = "";
        var publicCode = "";
        var privateCode = "";
        foreach (var line in lines)
        {
            var normalized = line.Replace("：", ":", StringComparison.Ordinal);
            var separator = normalized.IndexOf(':');
            var value = separator >= 0 ? normalized[(separator + 1)..].Trim() : normalized.Trim();
            if (normalized.Contains("房间码", StringComparison.OrdinalIgnoreCase) || normalized.Contains("房间", StringComparison.OrdinalIgnoreCase))
            {
                roomCode = value;
            }
            else if (normalized.Contains("房间密码", StringComparison.OrdinalIgnoreCase) || normalized.Contains("密码", StringComparison.OrdinalIgnoreCase))
            {
                roomPassword = value;
            }
            else if (normalized.Contains("公钥", StringComparison.OrdinalIgnoreCase))
            {
                publicCode = value;
            }
            else if (normalized.Contains("私钥", StringComparison.OrdinalIgnoreCase))
            {
                privateCode = value;
            }
        }

        if (!string.IsNullOrWhiteSpace(roomCode))
        {
            _joinPublicCodeBox.Text = roomCode;
            _joinPrivateCodeBox.Text = roomPassword;
            AppendDiagnostic("已从剪贴板识别房间码和房间密码，点击“加入”即可。");
            return true;
        }

        if (string.IsNullOrWhiteSpace(publicCode) || string.IsNullOrWhiteSpace(privateCode))
        {
            var rawValues = lines
                .Select(x => x.Contains(':') ? x[(x.IndexOf(':') + 1)..].Trim() : x)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
            if (rawValues.Count >= 2)
            {
                publicCode = string.IsNullOrWhiteSpace(publicCode) ? rawValues[0] : publicCode;
                privateCode = string.IsNullOrWhiteSpace(privateCode) ? rawValues[1] : privateCode;
            }
        }

        if (string.IsNullOrWhiteSpace(publicCode) || string.IsNullOrWhiteSpace(privateCode))
        {
            return false;
        }

        _joinPublicCodeBox.Text = publicCode;
        _joinPrivateCodeBox.Text = privateCode;
        AppendDiagnostic("已从剪贴板识别旧版公钥和私钥，点击“加入”即可。");
        return true;
    }

    private async Task JoinWithRoomAsync(string roomCode, string roomPassword)
    {
        roomCode = roomCode.Trim();
        roomPassword = roomPassword.Trim();
        if (string.IsNullOrWhiteSpace(roomCode))
        {
            AppendDiagnostic("加入失败：房间码为空。");
            return;
        }

        _currentRoomCode = roomCode;
        _currentPublicCode = "";
        _currentPrivateCode = "";
        _currentPublicHash = RoomDiscoveryHash(roomCode, roomPassword);
        _roomCodeBox.Text = roomCode;
        if (_roomPasswordBox is not null)
        {
            _roomPasswordBox.Text = roomPassword;
        }

        _currentInviteCode = CreateRoomPackage(roomCode, roomPassword, "");
        _inviteCodeBox.Text = "";

        ApplyGameProtocolToConnection();
        var profile = CreateCurrentProfile();
        profile.Id = Guid.NewGuid().ToString("N");
        profile.Name = $"Minecraft 远程局域网 - {roomCode}";
        profile.Role = "房客";
        profile.RoomCode = roomCode;
        profile.RoomPasswordHash = RoomDiscoveryHash(roomCode, roomPassword);
        profile.TrustedPackageHash = StableHash(_currentInviteCode);
        profile.TrustedPackage = _currentInviteCode;
        profile.DisplayStatus = "已加入，等待启动";
        profile.ConnectionPath = "等待路径竞速";
        profile.CreatedAt = DateTimeOffset.Now;
        await RememberCreatedConnectionAsync(profile);
        RefreshLinkSummary();
        RefreshConnectionListRows();
        AppendDiagnostic("已通过房间码和房间密码加入。启动时会优先同 Wi-Fi 自动发现，失败后尝试自建节点兜底。");
        NavigateTo("Connection");
        ShowConnectionSection("List");
    }

    private async Task JoinWithPublicPrivateAsync(string publicCode, string privateCode)
    {
        publicCode = publicCode.Trim();
        privateCode = privateCode.Trim();
        if (string.IsNullOrWhiteSpace(publicCode))
        {
            AppendDiagnostic("加入失败：朋友公钥为空。");
            return;
        }

        if (!IsPrivateDisplayCode(privateCode))
        {
            AppendDiagnostic("加入失败：朋友私钥应为 64 位字母或数字。");
            return;
        }

        _currentRoomCode = $"KEY-{DateTimeOffset.Now:yyyyMMdd-HHmmss}";
        _currentPublicCode = publicCode;
        _currentPrivateCode = privateCode;
        _currentPublicHash = StableHash(publicCode);
        _roomCodeBox.Text = _currentRoomCode;
        _publicCodeBox.Text = publicCode;
        _privateCodeBox.Text = privateCode;
        _currentInviteCode = CreateKeyPairPackage(publicCode, privateCode);
        _inviteCodeBox.Text = "";

        var profile = CreateCurrentProfile();
        profile.Id = Guid.NewGuid().ToString("N");
        profile.Name = $"朋友连接 - {_currentRoomCode}";
        profile.Role = "房客";
        profile.RoomCode = _currentRoomCode;
        profile.TrustedPackageHash = StableHash(_currentInviteCode);
        profile.TrustedPackage = _currentInviteCode;
        profile.DisplayStatus = "已加入，等待启动";
        profile.CreatedAt = DateTimeOffset.Now;
        await RememberCreatedConnectionAsync(profile);
        RefreshLinkSummary();
        RefreshConnectionListRows();
        AppendDiagnostic("已通过旧版公钥和私钥加入。新手推荐改用房间码和房间密码；启动时会优先局域网发现，异地网络请配置自建节点。");
        NavigateTo("Connection");
        ShowConnectionSection("List");
    }

    private string CreateKeyPairPackage(string publicCode, string privateCode)
    {
        return string.Join("|",
            "TPCWEIKEYPAIR",
            _currentRoomCode,
            EncodePackageValue(publicCode),
            EncodePackageValue(privateCode),
            ToPort(_localPortBox.Value, 8080).ToString(),
            ToPort(_publicPortBox.Value, ToPort(_localPortBox.Value, 8080)).ToString());
    }

    private string CreateRoomPackage(string roomCode, string roomPassword, string publicCode)
    {
        var protocol = _gameProtocolCombo?.SelectedIndex switch
        {
            1 => "TCP",
            2 => "UDP",
            _ => "TCP+UDP"
        };
        var peerHost = GetPreferredShareAddress();
        return string.Join("|",
            "TPCWEIROOM1",
            roomCode.Trim(),
            EncodePackageValue(roomPassword.Trim()),
            EncodePackageValue(publicCode.Trim()),
            protocol,
            ToPort(_localPortBox.Value, 25565).ToString(),
            ToPort(_publicPortBox.Value, ToPort(_localPortBox.Value, 25565)).ToString(),
            _gatewayHostBox.Text,
            ToPort(_gatewayPortBox.Value, 7000).ToString(),
            peerHost);
    }

    private async void JoinRoom_Click(object sender, RoutedEventArgs e)
    {
        var input = _inviteCodeBox.Text.Trim();
        var manualRoomCode = _joinPublicCodeBox?.Text.Trim() ?? "";
        var manualRoomPassword = _joinPrivateCodeBox?.Text.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(input)
            && !string.IsNullOrWhiteSpace(manualRoomCode))
        {
            await JoinWithRoomAsync(manualRoomCode, manualRoomPassword);
            return;
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            AppendDiagnostic("请填写房间码和房间密码，或粘贴完整房间包。");
            if (_tunnelStatusText is not null)
            {
                _tunnelStatusText.Text = "加入失败：缺少房间码。";
            }
            return;
        }

        if (!string.IsNullOrWhiteSpace(input) && IsShortRoomCode(input))
        {
            await JoinWithRoomAsync(input, manualRoomPassword);
            return;
        }

        _members.Add(new MemberViewItem("对端", ShortCode(_publicCodeBox.Text), "等待", "连接中"));
        if (!string.IsNullOrWhiteSpace(input))
        {
            ApplyInviteCode(input);
            if (HasCreatedCurrentLink())
            {
                var profile = CreateCurrentProfile();
                profile.Id = Guid.NewGuid().ToString("N");
                profile.Role = "房客";
                profile.RoomCode = _currentRoomCode;
                profile.TrustedPackageHash = StableHash(input);
                profile.TrustedPackage = input;
                profile.DisplayStatus = "已加入，等待启动";
                profile.CreatedAt = DateTimeOffset.Now;
                if (!profile.Name.Contains(_currentRoomCode, StringComparison.Ordinal))
                {
                    profile.Name = $"{profile.Name} - {_currentRoomCode}";
                }
                await RememberCreatedConnectionAsync(profile);
            }
            RefreshConnectionListRows();
            NavigateTo("Connection");
            ShowConnectionSection("List");
        }
        else
        {
            AppendDiagnostic("请先输入房间码和房间密码，或粘贴完整连接信息。");
        }
    }

    private static bool IsShortRoomCode(string value)
    {
        var text = value.Trim();
        return text.StartsWith("ROOM-", StringComparison.OrdinalIgnoreCase)
            && !text.Contains('|', StringComparison.Ordinal);
    }

    private async void RemoteConnect_Click(object sender, RoutedEventArgs e)
    {
        if (_remoteConnected)
        {
            _remoteConnected = false;
            _remoteConnectButton.Content = "创建 RDP 映射并连接";
            _remoteStatusText.Text = "远程桌面未连接。";
            AppendDiagnostic("远程桌面会话已标记为断开；如需停止映射，请到连接管理中停止选中项。");
            return;
        }

        try
        {
            await RunWithButtonStateAsync(_remoteConnectButton, "正在创建 RDP 映射...", async () =>
            {
                NavigateTo("Connection");
                ShowConnectionSection("Manage");
                _quickScenarioCombo.SelectedIndex = 3;
                _gameProtocolCombo.SelectedIndex = 1;
                ApplySelectedScenario();
                _tunnelNameBox.Text = "远程桌面 RDP";
                _localPortBox.Value = 13389;
                _peerPortBox.Value = 3389;
                _publicPortBox.Value = 13389;
                _tunnelProtocolCombo.SelectedIndex = 0;
                _simplePeerHostBox.Text = string.IsNullOrWhiteSpace(_simplePeerHostBox.Text) ? "127.0.0.1" : _simplePeerHostBox.Text;

                await EnsureNodeStartedAsync();
                var item = await StartLocalTunnelAsync(TunnelProtocol.Tcp, "远程桌面 RDP");
                var profile = CreateCurrentProfile();
                profile.Name = "远程桌面 RDP";
                profile.Type = ProxyRuleType.Tcp;
                profile.LocalPort = 13389;
                profile.RemotePort = 3389;
                profile.PublicPort = 13389;
                await RememberCreatedConnectionAsync(profile);

                _remoteConnected = true;
                _remoteStatusText.Text = $"RDP 映射已创建：{BuildRemoteDesktopAddress()}";
                _tunnelStatusText.Text = $"远程桌面映射已启动：{item.RouteText}";
                AppendDiagnostic($"远程桌面 RDP 映射已创建，连接地址：{BuildRemoteDesktopAddress()}");
                Process.Start(new ProcessStartInfo("mstsc.exe", "/v:" + BuildRemoteDesktopAddress()) { UseShellExecute = true });
                AppendDiagnostic("已启动 Windows 远程桌面客户端。");
            });
            _remoteConnectButton.Content = "断开 RDP 状态";
        }
        catch (Exception ex)
        {
            _remoteConnected = false;
            _remoteConnectButton.Content = "创建 RDP 映射并连接";
            _remoteStatusText.Text = $"远程桌面启动失败：{ex.Message}";
            AppendDiagnostic($"远程桌面启动失败：{ex.Message}");
        }
    }

    private async void StartExperimentalRemote_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureAgentProcess();
            var response = await _agentClient.CallAsync("remote.start", new
            {
                mode = "experimental",
                capture = "screen",
                transport = "current-profile",
                clipboard = true,
                audio = false,
                note = "自研远桌实验通道；稳定连接请优先使用 RDP 映射"
            });
            _remoteStatusText.Text = response.Ok
                ? "实验远桌通道已建立，当前用于会话状态和控制链路验证。"
                : $"实验远桌通道启动失败：{response.Error}";
            AppendDiagnostic(response.Ok
                ? $"实验远桌通道：{response.Result?.GetRawText()}"
                : $"实验远桌通道失败：{response.Error}");
        }
        catch (Exception ex)
        {
            _remoteStatusText.Text = $"实验远桌通道启动失败：{ex.Message}";
            AppendDiagnostic($"实验远桌通道启动失败：{ex.Message}");
        }
    }

    private void CopyRemoteDesktopAddress_Click(object sender, RoutedEventArgs e)
    {
        var address = BuildRemoteDesktopAddress();
        var package = new DataPackage();
        package.SetText("mstsc /v:" + address);
        Clipboard.SetContent(package);
        _remoteStatusText.Text = $"已复制：mstsc /v:{address}";
        AppendDiagnostic($"远程桌面连接命令已复制：mstsc /v:{address}");
    }

    private string BuildRemoteDesktopAddress()
    {
        if (_gatewayHandle != 0 && !string.IsNullOrWhiteSpace(_gatewayHostBox?.Text))
        {
            return $"{_gatewayHostBox.Text}:{ToPort(_publicPortBox?.Value ?? 13389, 13389)}";
        }

        return $"127.0.0.1:{ToPort(_localPortBox?.Value ?? 13389, 13389)}";
    }

    private async void SendFile_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_filePathBox.Text))
        {
            _transfers.Add(new TransferViewItem("请选择文件", "-", 0, "-", "等待"));
            return;
        }

        try
        {
            await EnsureNodeStartedAsync();
            var handle = await NativeInterop.SendFileAsync(_nodeHandle, _filePathBox.Text, _filePeerHostBox.Text, ToPort(_filePeerPortBox.Value, 9090));
            var fileInfo = new FileInfo(_filePathBox.Text);
            _transfers.Add(new TransferViewItem(Path.GetFileName(_filePathBox.Text), fileInfo.Exists ? FormatBytes((ulong)fileInfo.Length) : "-", 0, "-", "发送中")
            {
                Handle = handle,
                LocalPath = _filePathBox.Text,
                PeerHost = _filePeerHostBox.Text,
                PeerPort = ToPort(_filePeerPortBox.Value, 9090),
                Direction = "send"
            });
        }
        catch (Exception ex)
        {
            _transfers.Add(new TransferViewItem(Path.GetFileName(_filePathBox.Text), "-", 0, "-", $"失败：{ex.Message}"));
        }
    }

    private async void ReceiveFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await EnsureNodeStartedAsync();
            var target = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var handle = await NativeInterop.ReceiveFileAsync(_nodeHandle, target, ToPort(_filePeerPortBox.Value, 9090));
            _transfers.Add(new TransferViewItem("等待对端文件", "-", 0, "-", "监听接收")
            {
                Handle = handle,
                LocalPath = target,
                PeerPort = ToPort(_filePeerPortBox.Value, 9090),
                Direction = "receive"
            });
        }
        catch (Exception ex)
        {
            _transfers.Add(new TransferViewItem("接收监听", "-", 0, "-", $"失败：{ex.Message}"));
        }
    }

    private void FileTransfer_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
    }

    private async void CancelSelectedTransfer_Click(object sender, RoutedEventArgs e)
    {
        if (_transferList.SelectedItem is not TransferViewItem item || item.Handle == 0)
        {
            AppendDiagnostic("请先在传输列表中选择一个正在运行的任务。");
            return;
        }

        try
        {
            await NativeInterop.CancelTransferAsync(item.Handle);
            item.Status = "已取消";
            AppendDiagnostic($"传输任务已取消：{item.FileName}");
        }
        catch (Exception ex)
        {
            item.Status = $"取消失败：{ex.Message}";
            AppendDiagnostic($"取消传输失败：{ex.Message}");
        }
    }

    private async void RetrySelectedTransfer_Click(object sender, RoutedEventArgs e)
    {
        if (_transferList.SelectedItem is not TransferViewItem item)
        {
            AppendDiagnostic("请先选择要重试的传输任务。");
            return;
        }

        if (string.Equals(item.Direction, "receive", StringComparison.OrdinalIgnoreCase))
        {
            ReceiveFile_Click(sender, e);
            return;
        }

        if (string.IsNullOrWhiteSpace(item.LocalPath) || !File.Exists(item.LocalPath))
        {
            AppendDiagnostic("无法重试：原文件路径不存在，请重新选择文件。");
            item.Status = "重试失败：文件不存在";
            return;
        }

        _filePathBox.Text = item.LocalPath;
        _filePeerHostBox.Text = string.IsNullOrWhiteSpace(item.PeerHost) ? _filePeerHostBox.Text : item.PeerHost;
        _filePeerPortBox.Value = item.PeerPort == 0 ? _filePeerPortBox.Value : item.PeerPort;
        await Task.Yield();
        SendFile_Click(sender, e);
    }

    private async void FileTransfer_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            var items = await e.DataView.GetStorageItemsAsync();
            var file = items.OfType<Windows.Storage.StorageFile>().FirstOrDefault();
            if (file is null)
            {
                return;
            }

            _filePathBox.Text = file.Path;
            _transfers.Add(new TransferViewItem(file.Name, "-", 0, "-", "已拖入，点击发送文件开始") { LocalPath = file.Path, Direction = "send" });
            AppendDiagnostic($"已拖入文件：{file.Name}");
        }
        catch (Exception ex)
        {
            AppendDiagnostic($"拖拽文件失败：{ex.Message}");
        }
    }

    private void OnTransferEvent(object? sender, TransferEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var item = _transfers.FirstOrDefault(x => x.Handle == e.Transfer);
            if (item is null || e.Progress is not { } progress)
            {
                return;
            }

            item.Progress = Math.Clamp(progress.Progress * 100, 0, 100);
            item.Speed = FormatBytes(progress.BytesPerSecond) + "/s";
            item.Status = e.EventType.ToString();
        });
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private static ushort ToPort(double value, ushort fallback)
    {
        if (double.IsNaN(value) || value < 1 || value > 65535)
        {
            return fallback;
        }
        return (ushort)Math.Round(value);
    }

    private static int ToInt(double? value, int fallback)
    {
        if (value is null || double.IsNaN(value.Value))
        {
            return fallback;
        }

        return (int)Math.Round(value.Value);
    }

    private static string ShortCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "尚未生成";
        }
        return value.Length <= 16 ? value : value[..16] + "...";
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

public sealed class TunnelViewItem : NotifyObject
{
    private string _statusText = "运行中";
    private string _trafficText = "0 B / 0 B";

    public ulong Handle { get; set; }
    public string Name { get; set; } = "";
    public TunnelProtocol Protocol { get; set; }
    public ushort LocalPort { get; set; }
    public string PeerHost { get; set; } = "";
    public ushort PeerPort { get; set; }
    public bool Running { get; set; }
    public ulong BytesUp { get; set; }
    public ulong BytesDown { get; set; }
    public uint ActiveConnections { get; set; }
    public List<(double Up, double Down)> Samples { get; } = new();
    public string ProtocolText => Protocol == TunnelProtocol.Udp ? "UDP" : "TCP";
    public string RouteText => $"127.0.0.1:{LocalPort} -> {PeerHost}:{PeerPort}";

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string TrafficText
    {
        get => _trafficText;
        set => SetProperty(ref _trafficText, value);
    }

    public void RefreshText()
    {
        StatusText = Running ? $"{ActiveConnections} 个连接" : "已停止";
        TrafficText = $"{Format(BytesUp)} ↑ / {Format(BytesDown)} ↓";
    }

    public override string ToString() => $"{Name}  {ProtocolText}  {RouteText}  {StatusText}  {TrafficText}";

    private static string Format(ulong bytes)
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

public sealed record MemberViewItem(string Name, string PublicKey, string Latency, string Status)
{
    public override string ToString() => $"{Name}    {Latency}    {Status}    {PublicKey}";
}

public sealed class TransferViewItem : NotifyObject
{
    private double _progress;
    private string _speed;
    private string _status;

    public TransferViewItem(string fileName, string size, double progress, string speed, string status)
    {
        FileName = fileName;
        Size = size;
        _progress = progress;
        _speed = speed;
        _status = status;
    }

    public ulong Handle { get; set; }
    public string LocalPath { get; set; } = "";
    public string PeerHost { get; set; } = "";
    public ushort PeerPort { get; set; }
    public string Direction { get; set; } = "";
    public string FileName { get; }
    public string Size { get; }

    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    public string Speed
    {
        get => _speed;
        set => SetProperty(ref _speed, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public override string ToString() => $"{FileName}    {Progress:0}%    {Speed}    {Status}";
}

public abstract class NotifyObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
