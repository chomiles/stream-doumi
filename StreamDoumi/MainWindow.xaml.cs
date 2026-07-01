using Microsoft.Win32;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace StreamDoumi;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new();
    private readonly DispatcherTimer _miniBlinkTimer = new();
    private readonly Random _random = new();
    private readonly SystemMetricsReader _metricsReader = new();
    private readonly List<double> _downloadSamples = new();
    private readonly List<double> _uploadSamples = new();
    private ReadyProfile _profile = new();
    private ReadyItem? _selectedItem;
    private Border? _selectedReadyCard;
    private bool _isDragging;
    private bool _isMiniMode;
    private Point _dragStart;
    private double _boxStartLeft;
    private double _boxStartTop;
    private double _normalWidth = 1280;
    private double _normalHeight = 820;
    private string _cpuName = "CPU";
    private string _language = "ko";
    private bool _miniBlinkOn = true;
    private bool _isAdministrator;
    private bool _isLoadingSettings;
    private int _lastMiniCpu;
    private int _lastMiniMemory;
    private double _lastMiniUpload;
    private double _lastMiniDownload;
    private int _lastMiniGpu0;
    private int _lastMiniGpu1;
    private Rect _virtualScreen;
    private double _layoutScale = 1;
    private double _layoutOffsetX;
    private double _layoutOffsetY;
    private List<MonitorLayoutDisplay> _displayMonitors = [];

    public MainWindow()
    {
        _isLoadingSettings = true;
        InitializeComponent();
        _isLoadingSettings = false;

        GpuList.ItemsSource = new List<GpuIndicator>
        {
            new("GPU 0 - NVIDIA GeForce", 61),
            new("GPU 1 - Intel Graphics", 23)
        };

        _timer.Interval = TimeSpan.FromMilliseconds(500);
        _timer.Tick += (_, _) => UpdateIndicators();
        _timer.Start();

        _miniBlinkTimer.Interval = TimeSpan.FromMilliseconds(700);
        _miniBlinkTimer.Tick += (_, _) =>
        {
            _miniBlinkOn = !_miniBlinkOn;
            UpdateMiniGaugeStatus(_lastMiniCpu, _lastMiniMemory, _lastMiniUpload, _lastMiniDownload, _lastMiniGpu0, _lastMiniGpu1);
        };
        _miniBlinkTimer.Start();

        Loaded += (_, _) =>
        {
            _cpuName = GetCpuName();
            CpuNameText.Text = _cpuName;
            CpuNameText.Foreground = VendorBrushForName(_cpuName);
            _profile = ProfileStore.Load();
            _language = NormalizeLanguage(_profile.Language);
            _isLoadingSettings = true;
            SelectLanguageComboBoxItem(_language);
            _isLoadingSettings = false;
            BrowserCatalog.EnsureBrowserPaths(_profile);
            ApplyLaunchSettingsToUi();
            ApplyBrowserPathsToUi();
            ApplyLanguage();
            UpdateAdminStatus();
            RefreshCustomBrowserList();
            RenderReadyItems();
            SelectReadyItem(_profile.Items.FirstOrDefault());
            SeedNetworkChart();
            RenderMonitorLayout();
            DrawNetworkChart();
            ShowAdminModeWarningIfNeeded();
        };

        Closing += (_, e) =>
        {
            if (_isMiniMode)
            {
                e.Cancel = true;
                ExitMiniMode();
                return;
            }

            SaveCurrentProfile();
            _miniBlinkTimer.Stop();
            _timer.Stop();
            _metricsReader.Dispose();
        };
    }

    private void UpdateAdminStatus()
    {
        _isAdministrator = IsRunningAsAdministrator();

        if (_isAdministrator)
        {
            AdminBadge.Background = new SolidColorBrush(Color.FromRgb(25, 52, 38));
            AdminBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(66, 211, 146));
            AdminBadgeText.Text = T("AdminApplied");
            AdminBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(143, 239, 188));
        }
        else
        {
            AdminBadge.Background = new SolidColorBrush(Color.FromRgb(64, 28, 35));
            AdminBadge.BorderBrush = Brushes.IndianRed;
            AdminBadgeText.Text = T("AdminNotApplied");
            AdminBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(255, 178, 189));
        }
    }

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private void ShowAdminModeWarningIfNeeded()
    {
        if (_isAdministrator)
        {
            return;
        }

        MessageBox.Show(
            T("AdminWarning"),
            T("AdminNotApplied"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void ApplyLanguage()
    {
        Title = "StreamDoumi";
        HeaderSubtitleText.Text = T("HeaderSubtitle");
        SettingsButton.Content = T("Settings");
        MiniModeButton.Content = _isMiniMode ? T("Full") : T("MiniMode");
        SaveProfileButton.Content = T("SaveProfile");
        StartPrepButton.Content = T("StartPrep");
        ReadyListTitle.Text = T("ReadyList");
        EditReadyItemButton.Content = T("EditItem");
        RemoveReadyItemButton.Content = T("RemoveItem");
        AddReadyItemButton.Content = T("AddItem");

        LayoutTab.Header = T("LayoutTab");
        SettingsTab.Header = T("SettingsTab");
        InfoTab.Header = T("InfoTab");
        LayoutTitle.Text = T("LayoutTitle");
        LayoutDescription.Text = T("LayoutDescription");
        SelectScreenRegionButton.Content = T("SelectScreen");
        ResetPlacementButton.Content = T("ResetPlacement");
        SnapCheckBox.Content = T("WindowSnap");

        LaunchModeTitle.Text = T("LaunchModeTitle");
        LaunchModeDescription.Text = T("LaunchModeDescription");
        SequentialLaunchRadio.Content = T("SequentialLaunch");
        ParallelLaunchRadio.Content = T("ParallelLaunch");
        UseLaunchDelayCheckBox.Content = T("UseLaunchDelay");
        LaunchDelayLabel.Text = T("LaunchDelay");
        LaunchDelaySecondsLabel.Text = T("Seconds");
        NetworkSpeedTitle.Text = T("NetworkSpeedTitle");
        NetworkSpeedDescription.Text = T("NetworkSpeedDescription");
        MaxDownloadSpeedLabel.Text = T("MaxDownloadSpeed");
        MaxUploadSpeedLabel.Text = T("MaxUploadSpeed");
        BrowserPathTab.Header = T("BrowserPathTab");
        BrowserPathTitle.Text = T("BrowserPathTitle");
        BrowserPathDescription.Text = T("BrowserPathDescription");
        BrowseEdgeButton.Content = T("Browse");
        BrowseChromeButton.Content = T("Browse");
        BrowseBraveButton.Content = T("Browse");
        BrowseFirefoxButton.Content = T("Browse");
        CustomBrowserTab.Header = T("CustomBrowserTab");
        CustomBrowserTitle.Text = T("CustomBrowserTitle");
        CustomBrowserDescription.Text = T("CustomBrowserDescription");
        CustomBrowserNameLabel.Text = T("DisplayName");
        CustomBrowserPathLabel.Text = T("ExecutablePath");
        BrowseCustomBrowserButton.Content = T("Browse");
        AddCustomBrowserButton.Content = T("AddCustomBrowser");

        InfoTab.Header = T("InfoTab");
        InfoPurposeText.Text = T("InfoPurpose");
        InfoUsageTitle.Text = T("InfoUsageTitle");
        InfoUsageText.Text = T("InfoUsage");
        InfoNoticeTitle.Text = T("InfoNoticeTitle");
        InfoNoticeText.Text = T("InfoNotice");
        InfoGithubTitle.Text = "GitHub";
        InfoCreditText.Text = T("Credit");

        IndicatorTitle.Text = T("IndicatorTitle");
        IndicatorDescription.Text = T("IndicatorDescription");
        NetworkTitleText.Text = T("Network");
        DownloadLabelText.Text = T("Download");
        UploadLabelText.Text = T("Upload");
        MiniUpLabel.Text = T("MiniUpload");
        MiniDownLabel.Text = T("MiniDownload");
        MemoryNameText.Text = T("Memory");

        UpdateAdminStatus();
        UpdateLayoutReadout();
        RenderReadyItems();
    }

    private string T(string key)
    {
        return _language switch
        {
            "en" => key switch
            {
                "HeaderSubtitle" => "Prepare apps, browsers, and window placement before going live.",
                "AdminApplied" => "Administrator mode active",
                "AdminNotApplied" => "Administrator mode inactive",
                "AdminWarning" => "StreamDoumi is not running as administrator. Without administrator privileges, some features may be limited, such as launching OBS as administrator.",
                "Settings" => "Settings",
                "MiniMode" => "Gauge mode",
                "Full" => "Full",
                "SaveProfile" => "Save profile",
                "StartPrep" => "Start setup",
                "ReadyList" => "Ready list",
                "EditItem" => "Edit item",
                "RemoveItem" => "Remove item",
                "AddItem" => "Add item",
                "LayoutTab" => "Layout",
                "SettingsTab" => "Settings",
                "InfoTab" => "Info",
                "LayoutTitle" => "Window layout",
                "LayoutDescription" => "Adjust the selected item's position and size on the real monitor layout.",
                "SelectScreen" => "Pick on screen",
                "ResetPlacement" => "Reset position",
                "WindowSnap" => "Enable window move snap",
                "LaunchModeTitle" => "Launch mode",
                "LaunchModeDescription" => "Choose how registered items launch when setup starts.",
                "SequentialLaunch" => "Sequential",
                "ParallelLaunch" => "Simultaneous",
                "UseLaunchDelay" => "Use delay for sequential launch",
                "LaunchDelay" => "Launch delay",
                "Seconds" => "sec",
                "NetworkSpeedTitle" => "Internet max speed",
                "NetworkSpeedDescription" => "Used as the basis for gauge mode and network status. Enter values in MB/s.",
                "MaxDownloadSpeed" => "Max download speed",
                "MaxUploadSpeed" => "Max upload speed",
                "BrowserPathTab" => "Browser paths",
                "BrowserPathTitle" => "Browser paths",
                "BrowserPathDescription" => "Detected executable paths. You can edit them if a browser update or install path changes.",
                "CustomBrowserTab" => "Custom browser",
                "CustomBrowserTitle" => "Add custom browser",
                "CustomBrowserDescription" => "Add browsers that are not in the default list, such as Opera GX.",
                "DisplayName" => "Display name",
                "ExecutablePath" => "Executable path",
                "Browse" => "Browse",
                "AddCustomBrowser" => "Add custom browser",
                "InfoPurpose" => "A personal tool that prepares app launches, browser pages, and window placement for streaming setup.",
                "InfoUsageTitle" => "How to use",
                "InfoUsage" => "1. Add apps, browsers, or shortcuts to the ready list.\n2. Adjust position and size in the Layout tab.\n3. Choose sequential or simultaneous launch in Settings.\n4. Press Start setup to launch registered items.",
                "InfoNoticeTitle" => "Notice",
                "InfoNotice" => "This program uses administrator launch, external app launch, and window movement features.\nAs a personal app, it may be flagged by some antivirus or malware detection tools.\nIf you are unsure, check the source code on GitHub before using it.",
                "Credit" => "Cheering for every beginning - Chomiles",
                "IndicatorTitle" => "System Status",
                "IndicatorDescription" => "Updates every 0.5 seconds. Values at 90% or higher are shown in red.",
                "Memory" => "Memory",
                "RemoveConfirm" => "Remove the selected item?",
                "RemoveTitle" => "Remove item",
                "TargetNone" => "Target: none",
                "PositionEmpty" => "Position X - / Y -",
                "SizeEmpty" => "Size W - / H -",
                "MonitorEmpty" => "Monitor -",
                "NoLayout" => "Position: not arranged",
                "MinimizedLaunch" => "Size: launch minimized",
                "Target" => "Target",
                "Position" => "Position",
                "Size" => "Size",
                "Monitor" => "Monitor",
                "Network" => "Network",
                "Download" => "Download",
                "Upload" => "Upload",
                "MiniUpload" => "NET UP",
                "MiniDownload" => "NET DOWN",
                "NetworkGood" => "Good",
                "NetworkCaution" => "Caution",
                "NetworkHigh" => "High",
                "App" => "App",
                "Browser" => "Browser",
                "Shortcut" => "Shortcut",
                _ => key
            },
            "ja" => key switch
            {
                "HeaderSubtitle" => "配信前のアプリ起動、ブラウザー起動、ウィンドウ配置をまとめて準備します。",
                "AdminApplied" => "管理者権限が適用されています",
                "AdminNotApplied" => "管理者モード未適用",
                "AdminWarning" => "管理者権限で実行されていません。管理者権限がない場合、OBSの管理者権限起動など一部機能が制限されることがあります。",
                "Settings" => "設定",
                "MiniMode" => "メーターモード",
                "Full" => "全体",
                "SaveProfile" => "プロファイル保存",
                "StartPrep" => "配信準備開始",
                "ReadyList" => "準備リスト",
                "EditItem" => "プロパティ変更",
                "RemoveItem" => "項目削除",
                "AddItem" => "項目追加",
                "LayoutTab" => "配置",
                "SettingsTab" => "設定",
                "InfoTab" => "情報",
                "LayoutTitle" => "ウィンドウ配置編集",
                "LayoutDescription" => "実際のモニターレイアウト上で選択した項目の位置とサイズを調整します。",
                "SelectScreen" => "画面から指定",
                "ResetPlacement" => "位置初期化",
                "WindowSnap" => "ウィンドウ移動スナップを有効化",
                "LaunchModeTitle" => "実行方式",
                "LaunchModeDescription" => "配信準備開始ボタンを押したときの項目実行順を設定します。",
                "SequentialLaunch" => "順次実行",
                "ParallelLaunch" => "同時実行",
                "UseLaunchDelay" => "順次実行時に間隔を使用",
                "LaunchDelay" => "実行間隔",
                "Seconds" => "秒",
                "NetworkSpeedTitle" => "インターネット最高速度",
                "NetworkSpeedDescription" => "メーターモードとネットワーク状態表示の基準です。MB/s単位で入力します。",
                "MaxDownloadSpeed" => "ダウンロード最高速度",
                "MaxUploadSpeed" => "アップロード最高速度",
                "BrowserPathTab" => "ブラウザー経路",
                "BrowserPathTitle" => "ブラウザー経路",
                "BrowserPathDescription" => "検出された実行ファイルの経路です。更新やインストール場所の変更時に直接修正できます。",
                "CustomBrowserTab" => "カスタムブラウザー",
                "CustomBrowserTitle" => "カスタムブラウザー追加",
                "CustomBrowserDescription" => "Opera GXのように基本一覧にないブラウザーを追加します。",
                "DisplayName" => "表示名",
                "ExecutablePath" => "実行ファイル経路",
                "Browse" => "参照",
                "AddCustomBrowser" => "カスタムブラウザー追加",
                "InfoPurpose" => "配信準備に必要なアプリ起動、ブラウザー表示、ウィンドウ配置をまとめて準備する個人制作ツールです。",
                "InfoUsageTitle" => "使い方",
                "InfoUsage" => "1. 準備リストにアプリ、ブラウザー、ショートカットを追加します。\n2. 配置タブで位置とサイズを調整します。\n3. 設定タブで順次実行または同時実行を選びます。\n4. 配信準備開始ボタンで登録項目を実行します。",
                "InfoNoticeTitle" => "注意事項",
                "InfoNotice" => "このプログラムは管理者権限実行、外部アプリ起動、ウィンドウ移動機能を使用します。\n個人制作アプリのため、一部のウイルス対策やマルウェア検出ツールで誤検出される場合があります。\n不安な場合はGitHubで公開されているソースコードを確認してから使用してください。",
                "Credit" => "すべての始まりを応援します - ジョマイルズ",
                "IndicatorTitle" => "システム状態",
                "IndicatorDescription" => "0.5秒ごとに更新。90%以上は赤色で表示します。",
                "Memory" => "メモリ",
                "RemoveConfirm" => "選択した項目を削除しますか？",
                "RemoveTitle" => "項目削除",
                "TargetNone" => "対象: なし",
                "PositionEmpty" => "位置 X - / Y -",
                "SizeEmpty" => "サイズ W - / H -",
                "MonitorEmpty" => "モニター -",
                "NoLayout" => "位置: 配置しない",
                "MinimizedLaunch" => "サイズ: 最小化して実行",
                "Target" => "対象",
                "Position" => "位置",
                "Size" => "サイズ",
                "Monitor" => "モニター",
                "Network" => "ネットワーク",
                "Download" => "ダウンロード",
                "Upload" => "アップロード",
                "MiniUpload" => "NET UP",
                "MiniDownload" => "NET DOWN",
                "NetworkGood" => "良好",
                "NetworkCaution" => "注意",
                "NetworkHigh" => "高い",
                "App" => "アプリ",
                "Browser" => "ブラウザー",
                "Shortcut" => "ショートカット",
                _ => key
            },
            _ => key switch
            {
                "HeaderSubtitle" => "방송 전 앱 실행, 브라우저 열기, 창 배치를 한 번에 준비합니다.",
                "AdminApplied" => "관리자 권한 적용됨",
                "AdminNotApplied" => "관리자모드 적용안됨",
                "AdminWarning" => "관리자권한으로 실행이 안되었습니다. 관리자권한이 없는경우 일부 기능이 제약될수있습니다(OBS 관리자권한 실행등)",
                "Settings" => "설정",
                "MiniMode" => "계기판 모드",
                "Full" => "전체",
                "SaveProfile" => "프로필 저장",
                "StartPrep" => "방송 준비 시작",
                "ReadyList" => "준비 목록",
                "EditItem" => "속성 변경",
                "RemoveItem" => "항목 제거",
                "AddItem" => "항목 추가",
                "LayoutTab" => "배치",
                "SettingsTab" => "설정",
                "InfoTab" => "정보",
                "LayoutTitle" => "창 배치 편집",
                "LayoutDescription" => "실제 모니터 레이아웃에서 선택한 항목의 위치와 크기를 조정합니다.",
                "SelectScreen" => "화면에서 지정",
                "ResetPlacement" => "위치 초기화",
                "WindowSnap" => "창 이동 스냅기능 활성화",
                "LaunchModeTitle" => "실행 방식",
                "LaunchModeDescription" => "방송 준비 시작 버튼을 눌렀을 때 등록된 항목을 실행하는 순서를 정합니다.",
                "SequentialLaunch" => "순차 실행",
                "ParallelLaunch" => "동시 실행",
                "UseLaunchDelay" => "순차 실행 시 간격 사용",
                "LaunchDelay" => "실행 간격",
                "Seconds" => "초",
                "NetworkSpeedTitle" => "인터넷 최고속도",
                "NetworkSpeedDescription" => "계기판 모드와 네트워크 상태 표시의 기준입니다. MB/s 단위로 입력합니다.",
                "MaxDownloadSpeed" => "다운로드 최고속도",
                "MaxUploadSpeed" => "업로드 최고속도",
                "BrowserPathTab" => "기본 브라우저 경로",
                "BrowserPathTitle" => "브라우저 경로",
                "BrowserPathDescription" => "감지된 실행 파일 경로입니다. 브라우저 업데이트나 설치 위치 변경 시 직접 수정할 수 있습니다.",
                "CustomBrowserTab" => "커스텀 브라우저 추가",
                "CustomBrowserTitle" => "커스텀 브라우저 추가",
                "CustomBrowserDescription" => "Opera GX처럼 기본 목록에 없는 브라우저를 직접 추가합니다.",
                "DisplayName" => "표시 이름",
                "ExecutablePath" => "실행 파일 경로",
                "Browse" => "찾기",
                "AddCustomBrowser" => "커스텀 브라우저 추가",
                "InfoPurpose" => "방송 준비에 필요한 앱 실행, 브라우저 열기, 창 위치 배치를 한 번에 준비하는 개인 제작 도구입니다.",
                "InfoUsageTitle" => "사용 방법",
                "InfoUsage" => "1. 준비 목록에 앱, 브라우저, 바로가기를 추가합니다.\n2. 배치 탭에서 실행 후 놓일 위치와 크기를 조정합니다.\n3. 설정 탭에서 순차 실행 또는 동시 실행 방식을 선택합니다.\n4. 방송 준비 시작 버튼으로 등록한 항목을 실행합니다.",
                "InfoNoticeTitle" => "주의 사항",
                "InfoNotice" => "이 프로그램은 관리자 권한 실행, 외부 앱 실행, 창 위치 이동 기능을 사용합니다.\n개인 제작 앱 특성상 일부 백신이나 멀웨어 감지 프로그램에서 오탐으로 감지될 수 있습니다.\n불안한 경우 GitHub에 공개된 소스코드를 확인한 뒤 사용해 주세요.",
                "Credit" => "모든 시작을 응원하며 - 조마일즈",
                "IndicatorTitle" => "시스템 상태",
                "IndicatorDescription" => "0.5초 단위 갱신 기준. 90% 이상은 빨간색으로 표시합니다.",
                "Memory" => "메모리",
                "RemoveConfirm" => "선택한 항목을 제거할까요?",
                "RemoveTitle" => "항목 제거",
                "TargetNone" => "대상: 없음",
                "PositionEmpty" => "위치 X - / Y -",
                "SizeEmpty" => "크기 W - / H -",
                "MonitorEmpty" => "모니터 -",
                "NoLayout" => "위치: 배치 안 함",
                "MinimizedLaunch" => "크기: 최소화 실행",
                "Target" => "대상",
                "Position" => "위치",
                "Size" => "크기",
                "Monitor" => "모니터",
                "Network" => "네트워크",
                "Download" => "다운로드",
                "Upload" => "업로드",
                "MiniUpload" => "NET UP",
                "MiniDownload" => "NET DOWN",
                "NetworkGood" => "좋음",
                "NetworkCaution" => "주의",
                "NetworkHigh" => "높음",
                "App" => "앱",
                "Browser" => "브라우저",
                "Shortcut" => "바로가기",
                _ => key
            }
        };
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageComboBox.SelectedItem is not ComboBoxItem item ||
            item.Tag?.ToString() is not { } language)
        {
            return;
        }

        _language = NormalizeLanguage(language);
        _profile.Language = _language;

        if (!IsLoaded || _isLoadingSettings)
        {
            return;
        }

        ApplyLanguage();
        SaveCurrentProfile();
    }

    private void SelectLanguageComboBoxItem(string language)
    {
        foreach (var item in LanguageComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), language, StringComparison.OrdinalIgnoreCase))
            {
                LanguageComboBox.SelectedItem = item;
                return;
            }
        }
    }

    private static string NormalizeLanguage(string? language)
    {
        return language?.ToLowerInvariant() switch
        {
            "en" => "en",
            "ja" => "ja",
            _ => "ko"
        };
    }

    private string LocalizedTypeLabel(ReadyItemType type)
    {
        return type switch
        {
            ReadyItemType.Browser => T("Browser"),
            ReadyItemType.Shortcut => T("Shortcut"),
            _ => T("App")
        };
    }

    private string LocalizedItemDetail(ReadyItem item)
    {
        return item.Type switch
        {
            ReadyItemType.App => string.IsNullOrWhiteSpace(item.Path) ? T("ExecutablePath") : item.Path,
            ReadyItemType.Browser => $"{item.Browser}  {item.Url}",
            ReadyItemType.Shortcut => string.IsNullOrWhiteSpace(item.Path) ? T("Shortcut") : item.Path,
            _ => ""
        };
    }

    private void RenderReadyItems()
    {
        ReadyItemsPanel.Children.Clear();

        foreach (var item in _profile.Items)
        {
            ReadyItemsPanel.Children.Add(CreateReadyItemCard(item));
        }
    }

    private Border CreateReadyItemCard(ReadyItem item)
    {
        var typeColor = item.Type == ReadyItemType.Browser
            ? new SolidColorBrush(Color.FromRgb(119, 183, 255))
            : new SolidColorBrush(Color.FromRgb(66, 211, 146));

        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(32, 36, 46)),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 10),
            Cursor = Cursors.Hand,
            Tag = item
        };

        var root = new StackPanel();
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        header.Children.Add(new TextBlock
        {
            Text = item.Name,
            FontWeight = FontWeights.SemiBold,
            FontSize = 16
        });

        var typeText = new TextBlock
        {
            Text = LocalizedTypeLabel(item.Type),
            Foreground = typeColor,
            FontWeight = FontWeights.SemiBold
        };
        Grid.SetColumn(typeText, 1);
        header.Children.Add(typeText);

        root.Children.Add(header);
        root.Children.Add(new TextBlock
        {
            Text = LocalizedItemDetail(item),
            Style = (Style)FindResource("MutedText"),
            Margin = new Thickness(0, 8, 0, 0)
        });

        card.Child = root;
        card.MouseLeftButtonDown += ReadyItemCard_MouseLeftButtonDown;
        return card;
    }

    private void ReadyItemCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: ReadyItem item } card)
        {
            SelectReadyItem(item, card);
            if (e.ClickCount >= 2)
            {
                EditSelectedReadyItem();
            }
        }
    }

    private void SelectReadyItem(ReadyItem? item, Border? card = null)
    {
        if (_selectedReadyCard is not null)
        {
            _selectedReadyCard.Background = new SolidColorBrush(Color.FromRgb(32, 36, 46));
            _selectedReadyCard.BorderBrush = Brushes.Transparent;
        }

        _selectedItem = item;
        EditReadyItemButton.Visibility = _selectedItem is null ? Visibility.Collapsed : Visibility.Visible;
        RemoveReadyItemButton.Visibility = _selectedItem is null ? Visibility.Collapsed : Visibility.Visible;
        _selectedReadyCard = card ?? FindCardForItem(item);

        if (_selectedReadyCard is not null)
        {
            _selectedReadyCard.Background = new SolidColorBrush(Color.FromRgb(58, 41, 31));
            _selectedReadyCard.BorderBrush = new SolidColorBrush(Color.FromRgb(154, 91, 46));
        }

        if (_selectedItem is null)
        {
            PlacementBox.Visibility = Visibility.Collapsed;
            TargetText.Text = T("TargetNone");
            return;
        }

        PlacementBox.Visibility = ShouldShowPlacement(_selectedItem) ? Visibility.Visible : Visibility.Collapsed;
        PlacementBoxText.Text = _selectedItem.Name;
        if (ShouldShowPlacement(_selectedItem))
        {
            PositionPlacementBoxFromScreenRect(_selectedItem.Bounds);
        }
        UpdateLayoutReadout();
        RenderMonitorLayout();
    }

    private Border? FindCardForItem(ReadyItem? item)
    {
        if (item is null)
        {
            return null;
        }

        return ReadyItemsPanel.Children
            .OfType<Border>()
            .FirstOrDefault(card => ReferenceEquals(card.Tag, item));
    }

    private void AddReadyItemButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddReadyItemWindow(_profile)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            var item = dialog.Result;
            item.Bounds = _selectedItem?.Bounds ?? DefaultBounds();
            item.Monitor = NativeMethods.GetMonitorNumber(item.Bounds);
            _profile.Items.Add(item);
            RenderReadyItems();
            SelectReadyItem(item);
            SaveCurrentProfile();
        }
    }

    private void EditReadyItemButton_Click(object sender, RoutedEventArgs e)
    {
        EditSelectedReadyItem();
    }

    private void RemoveReadyItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem is null)
        {
            return;
        }

        var result = MessageBox.Show(
            T("RemoveConfirm"),
            T("RemoveTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var index = _profile.Items.IndexOf(_selectedItem);
        _profile.Items.Remove(_selectedItem);
        RenderReadyItems();
        var nextItem = _profile.Items.Count == 0
            ? null
            : _profile.Items[Math.Clamp(index, 0, _profile.Items.Count - 1)];
        SelectReadyItem(nextItem);
        if (_profile.Items.Count == 0)
        {
            ProfileStore.Save(_profile, allowEmpty: true);
        }
        else
        {
            SaveCurrentProfile();
        }
    }

    private void EditSelectedReadyItem()
    {
        if (_selectedItem is null)
        {
            return;
        }

        UpdateSelectedBoundsFromPlacementBox();
        var dialog = new AddReadyItemWindow(_selectedItem, _profile)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            ApplyReadyItemProperties(_selectedItem, dialog.Result);
            SaveCurrentProfile();
            RenderReadyItems();
            SelectReadyItem(_selectedItem);
        }
    }

    private static void ApplyReadyItemProperties(ReadyItem target, ReadyItem source)
    {
        target.Type = source.Type;
        target.Name = source.Name;
        target.Path = source.Path;
        target.Arguments = source.Arguments;
        target.RunAsAdmin = source.RunAsAdmin;
        target.MinimizeAfterLaunch = source.MinimizeAfterLaunch;
        target.Browser = source.Browser;
        target.BrowserProfile = source.BrowserProfile;
        target.Url = source.Url;
    }

    private Int32Rect DefaultBounds()
    {
        var monitor = NativeMethods.GetMonitors().FirstOrDefault();
        if (monitor is null)
        {
            return new Int32Rect(0, 0, 1280, 720);
        }

        return new Int32Rect((int)monitor.Bounds.X, (int)monitor.Bounds.Y, 1280, 720);
    }

    private void SaveProfileButton_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentProfile(showResult: true);
    }

    private async void StartPrepButton_Click(object sender, RoutedEventArgs e)
    {
        ShowAdminModeWarningIfNeeded();
        SaveCurrentProfile();

        if (HasOfflineMonitorLaunchItems())
        {
            var result = MessageBox.Show(
                "추가모니터가 오프라인상태입니다. 실행시 창의 위치가 올바르지 않을 수 있습니다. 실행하시겠습니까?",
                "오프라인 모니터",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        StartPrepButton.IsEnabled = false;
        try
        {
            await LaunchRegisteredItemsAsync();
        }
        finally
        {
            StartPrepButton.IsEnabled = true;
        }
    }

    private void SaveCurrentProfile(bool showResult = false)
    {
        if (_profile is null)
        {
            return;
        }

        UpdateSelectedBoundsFromPlacementBox();
        ApplyLaunchSettingsFromUi();
        ApplyBrowserPathsFromUi();
        UpdateStoredMonitors(NativeMethods.GetMonitors());
        try
        {
            ProfileStore.Save(_profile);
            if (showResult)
            {
                var saved = ProfileStore.LoadSavedProfile();
                var savedCount = saved?.Items.Count ?? -1;
                var isEmpty = _profile.Items.Count == 0;
                var message = savedCount == _profile.Items.Count
                    ? $"프로필 저장 완료\n현재 항목 수: {_profile.Items.Count}\n저장된 항목 수: {savedCount}\n{ProfileStore.ProfilePath}"
                    : $"프로필 저장 확인 실패\n현재 항목 수: {_profile.Items.Count}\n저장된 항목 수: {savedCount}\n{ProfileStore.ProfilePath}";

                if (isEmpty)
                {
                    message = $"프로필은 저장됐지만 현재 준비 목록이 비어 있습니다.\n현재 항목 수: 0\n저장된 항목 수: {savedCount}\n{ProfileStore.ProfilePath}";
                }

                MessageBox.Show(
                    message,
                    "StreamDoumi",
                    MessageBoxButton.OK,
                    savedCount == _profile.Items.Count && !isEmpty ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"프로필 저장에 실패했습니다.\n{ProfileStore.ProfilePath}\n{ex.Message}",
                "StreamDoumi",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private bool HasOfflineMonitorLaunchItems()
    {
        var activeMonitorNumbers = NativeMethods.GetMonitors()
            .Select(monitor => monitor.Number)
            .ToHashSet();

        return _profile.Items.Any(item =>
            ShouldShowPlacement(item) &&
            item.Monitor > 0 &&
            !activeMonitorNumbers.Contains(item.Monitor));
    }

    private async Task LaunchRegisteredItemsAsync()
    {
        var launchItems = _profile.Items.ToList();

        if (launchItems.Count == 0)
        {
            MessageBox.Show(
                "테스트할 브라우저 항목이 없습니다.\n브라우저 항목을 등록한 뒤 다시 실행해 주세요.",
                "StreamDoumi",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (_profile.LaunchSequentially)
        {
            foreach (var item in launchItems)
            {
                await LaunchReadyItemAsync(item);

                if (_profile.SequentialDelaySeconds > 0 && item != launchItems[^1])
                {
                    await Task.Delay(TimeSpan.FromSeconds(_profile.SequentialDelaySeconds));
                }
            }
        }
        else
        {
            await Task.WhenAll(launchItems.Select(LaunchReadyItemAsync));
        }
    }

    private async Task LaunchReadyItemAsync(ReadyItem item)
    {
        try
        {
            if (item.Type == ReadyItemType.Browser)
            {
                await BrowserWindowLauncher.LaunchAndPlaceAsync(item, _profile);
            }
            else
            {
                await AppWindowLauncher.LaunchAndPlaceAsync(item);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"{item.Name} 실행에 실패했습니다.\n{ex.Message}",
                "StreamDoumi",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void SeedNetworkChart()
    {
        _downloadSamples.Clear();
        _uploadSamples.Clear();

        for (var i = 0; i < 45; i++)
        {
            _downloadSamples.Add(10 + _random.NextDouble() * 32);
            _uploadSamples.Add(2 + _random.NextDouble() * 14);
        }
    }

    private void UpdateIndicators()
    {
        var metrics = _metricsReader.Read();
        var download = metrics.DownloadMbps;
        var upload = metrics.UploadMbps;
        var cpu = (int)Math.Round(metrics.CpuLoad);
        var memory = (int)Math.Round(metrics.MemoryLoad);
        var gpus = metrics.Gpus.Count > 0
            ? metrics.Gpus
            : [new GpuMetric("GPU 0", 0), new GpuMetric("GPU 1", 0)];
        var gpu0 = gpus.ElementAtOrDefault(0)?.Load ?? 0;
        var gpu1 = gpus.ElementAtOrDefault(1)?.Load ?? 0;
        _lastMiniCpu = cpu;
        _lastMiniMemory = memory;
        _lastMiniUpload = upload;
        _lastMiniDownload = download;
        _lastMiniGpu0 = gpu0;
        _lastMiniGpu1 = gpu1;

        AddSample(_downloadSamples, download);
        AddSample(_uploadSamples, upload);

        DownloadText.Text = $"{download:0.0} MB/s";
        UploadText.Text = $"{upload:0.0} MB/s";
        CpuText.Text = $"{cpu}%";
        CpuText.Foreground = cpu >= 95 ? Brushes.IndianRed : Brushes.WhiteSmoke;
        CpuBar.Value = cpu;
        MemoryText.Text = $"{memory}%";
        MemoryText.Foreground = memory >= 95 ? Brushes.IndianRed : Brushes.WhiteSmoke;
        MemoryBar.Value = memory;

        var uploadLoad = NetworkLoad(upload, _profile.MaxUploadMegabytesPerSecond);
        var downloadLoad = NetworkLoad(download, _profile.MaxDownloadMegabytesPerSecond);
        var networkLoad = Math.Max(uploadLoad, downloadLoad);
        NetworkStateText.Text = networkLoad >= 90
            ? T("NetworkHigh")
            : networkLoad > 50
                ? T("NetworkCaution")
                : T("NetworkGood");
        NetworkStateText.Foreground = NetworkStatusBrush(networkLoad);

        GpuList.ItemsSource = gpus
            .Select((gpu, index) => new GpuIndicator($"{gpu.Name}", gpu.Load))
            .ToList();
        MiniSummaryText.Text = $"D {download:0.0}  U {upload:0.0}  CPU {cpu}%  GPU {Math.Max(gpu0, gpu1)}%";
        MiniSummaryText.Foreground = cpu >= 95 || gpu0 >= 95 || gpu1 >= 95
            ? Brushes.IndianRed
            : Brushes.WhiteSmoke;
        MiniCpuValueText.Text = $"{cpu}%";
        MiniMemoryValueText.Text = $"{memory}%";
        MiniUpValueText.Text = $"{upload:0.0} MB/s";
        MiniDownValueText.Text = $"{download:0.0} MB/s";
        MiniGpu0ValueText.Text = $"{gpu0}%";
        MiniGpu1ValueText.Text = $"{gpu1}%";
        UpdateMiniGaugeStatus(cpu, memory, upload, download, gpu0, gpu1);

        DrawNetworkChart();
    }

    private void UpdateMiniGaugeStatus(int cpu, int memory, double upload, double download, int gpu0, int gpu1)
    {
        var uploadLoad = NetworkLoad(upload, _profile.MaxUploadMegabytesPerSecond);
        var downloadLoad = NetworkLoad(download, _profile.MaxDownloadMegabytesPerSecond);

        UpdateMiniDot(MiniCpuDot, cpu);
        UpdateMiniDot(MiniMemoryDot, memory);
        UpdateMiniDot(MiniUploadDot, uploadLoad);
        UpdateMiniDot(MiniDownloadDot, downloadLoad);
        UpdateMiniDot(MiniGpu0Dot, gpu0);
        UpdateMiniDot(MiniGpu1Dot, gpu1);

        UpdateMiniLabel(MiniCpuLabel, cpu);
        UpdateMiniLabel(MiniMemoryLabel, memory);
        UpdateMiniLabel(MiniUpLabel, uploadLoad);
        UpdateMiniLabel(MiniDownLabel, downloadLoad);
        UpdateMiniLabel(MiniGpu0Label, gpu0);
        UpdateMiniLabel(MiniGpu1Label, gpu1);

        var normalBrush = new SolidColorBrush(Color.FromRgb(221, 228, 255));
        MiniCpuValueText.Foreground = cpu >= 90 ? Brushes.IndianRed : normalBrush;
        MiniMemoryValueText.Foreground = memory >= 90 ? Brushes.IndianRed : normalBrush;
        MiniUpValueText.Foreground = uploadLoad >= 90 ? Brushes.IndianRed : normalBrush;
        MiniDownValueText.Foreground = downloadLoad >= 90 ? Brushes.IndianRed : normalBrush;
        MiniGpu0ValueText.Foreground = gpu0 >= 90 ? Brushes.IndianRed : normalBrush;
        MiniGpu1ValueText.Foreground = gpu1 >= 90 ? Brushes.IndianRed : normalBrush;
    }

    private void UpdateMiniDot(Ellipse dot, double load)
    {
        dot.Fill = load >= 90 && !_miniBlinkOn
            ? new SolidColorBrush(Color.FromRgb(94, 28, 34))
            : MiniStatusBrush(load);
    }

    private void UpdateMiniLabel(TextBlock label, double load)
    {
        label.Foreground = load >= 90
            ? Brushes.IndianRed
            : new SolidColorBrush(Color.FromRgb(82, 229, 215));
    }

    private static Brush MiniStatusBrush(double load)
    {
        if (load >= 90)
        {
            return Brushes.IndianRed;
        }

        if (load <= 50)
        {
            return new SolidColorBrush(Color.FromRgb(66, 211, 146));
        }

        return new SolidColorBrush(Color.FromRgb(229, 210, 83));
    }

    private static double NetworkLoad(double currentMegabytesPerSecond, double maxMegabytesPerSecond)
    {
        return Math.Clamp(currentMegabytesPerSecond / Math.Max(0.1, maxMegabytesPerSecond) * 100.0, 0, 100);
    }

    private static Brush NetworkStatusBrush(double load)
    {
        if (load >= 90)
        {
            return Brushes.IndianRed;
        }

        if (load > 50)
        {
            return new SolidColorBrush(Color.FromRgb(229, 210, 83));
        }

        return new SolidColorBrush(Color.FromRgb(66, 211, 146));
    }

    private static void AddSample(List<double> samples, double value)
    {
        samples.Add(value);
        if (samples.Count > 45)
        {
            samples.RemoveAt(0);
        }
    }

    private void DrawNetworkChart()
    {
        NetworkChart.Children.Clear();
        var width = Math.Max(1, NetworkChart.ActualWidth);
        var height = Math.Max(1, NetworkChart.ActualHeight);

        DrawLine(_downloadSamples, width, height, Color.FromRgb(119, 183, 255));
        DrawLine(_uploadSamples, width, height, Color.FromRgb(66, 211, 146));
    }

    private void DrawLine(IReadOnlyList<double> samples, double width, double height, Color color)
    {
        if (samples.Count < 2)
        {
            return;
        }

        var max = Math.Max(1, samples.Max());
        var geometry = new StreamGeometry();

        using (var context = geometry.Open())
        {
            for (var i = 0; i < samples.Count; i++)
            {
                var x = i * width / (samples.Count - 1);
                var y = height - samples[i] / max * height;

                if (i == 0)
                {
                    context.BeginFigure(new Point(x, y), false, false);
                }
                else
                {
                    context.LineTo(new Point(x, y), true, false);
                }
            }
        }

        NetworkChart.Children.Add(new Path
        {
            Data = geometry,
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 2,
            SnapsToDevicePixels = true
        });
    }

    private void PlacementBox_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        _dragStart = e.GetPosition(LayoutCanvas);
        _boxStartLeft = Canvas.GetLeft(PlacementBox);
        _boxStartTop = Canvas.GetTop(PlacementBox);
        PlacementBox.CaptureMouse();
        e.Handled = true;
    }

    private void PlacementBox_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        var position = e.GetPosition(LayoutCanvas);
        MovePlacementBox(_boxStartLeft + position.X - _dragStart.X, _boxStartTop + position.Y - _dragStart.Y);
    }

    private void PlacementBox_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        PlacementBox.ReleaseMouseCapture();
        UpdateSelectedBoundsFromPlacementBox();
        RenderReadyItems();
        SelectReadyItem(_selectedItem);
    }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb thumb)
        {
            return;
        }

        ResizePlacementBox(thumb.Tag?.ToString() ?? "BottomRight", e.HorizontalChange, e.VerticalChange);
    }

    private void ResizePlacementBox(string edge, double canvasDeltaX, double canvasDeltaY)
    {
        var rect = ScreenRectFromPlacementBox();
        var deltaX = (int)Math.Round(canvasDeltaX / Math.Max(0.001, _layoutScale));
        var deltaY = (int)Math.Round(canvasDeltaY / Math.Max(0.001, _layoutScale));
        const int minWidth = 120;
        const int minHeight = 80;

        var x = rect.X;
        var y = rect.Y;
        var width = rect.Width;
        var height = rect.Height;

        if (edge.Contains("Left", StringComparison.OrdinalIgnoreCase))
        {
            var nextWidth = Math.Max(minWidth, width - deltaX);
            x += width - nextWidth;
            width = nextWidth;
        }

        if (edge.Contains("Right", StringComparison.OrdinalIgnoreCase))
        {
            width = Math.Max(minWidth, width + deltaX);
        }

        if (edge.Contains("Top", StringComparison.OrdinalIgnoreCase))
        {
            var nextHeight = Math.Max(minHeight, height - deltaY);
            y += height - nextHeight;
            height = nextHeight;
        }

        if (edge.Contains("Bottom", StringComparison.OrdinalIgnoreCase))
        {
            height = Math.Max(minHeight, height + deltaY);
        }

        var nextRect = new Int32Rect(x, y, width, height);
        if (_profile.WindowSnapEnabled)
        {
            nextRect = SnapResizeRectToNeighborEdges(nextRect, edge);
        }

        var clamped = ClampRectToVisibleLayout(nextRect);
        PositionPlacementBoxFromScreenRect(clamped);
        UpdateSelectedBoundsFromPlacementBox();
        if (_selectedItem is not null)
        {
            _selectedItem.Monitor = NativeMethods.GetMonitorNumber(_selectedItem.Bounds);
        }
        UpdateLayoutReadout();
    }

    private void LayoutCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_selectedItem is null)
        {
            return;
        }

        var position = e.GetPosition(LayoutCanvas);
        if (!IsCanvasPointInsideAnyMonitor(position))
        {
            return;
        }

        MovePlacementBox(position.X - PlacementBox.Width / 2, position.Y - PlacementBox.Height / 2);
    }

    private void LayoutCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RenderMonitorLayout();
        if (_selectedItem is not null)
        {
            PositionPlacementBoxFromScreenRect(_selectedItem.Bounds);
        }
        DrawNetworkChart();
    }

    private void MovePlacementBox(double left, double top)
    {
        var rect = ScreenRectFromCanvasRect(left, top, PlacementBox.Width, PlacementBox.Height);
        if (_profile.WindowSnapEnabled)
        {
            rect = SnapMoveRectToNeighborEdges(rect);
        }

        var clamped = ClampRectToVisibleLayout(rect);
        PositionPlacementBoxFromScreenRect(clamped);
        UpdateSelectedBoundsFromPlacementBox();
        UpdateLayoutReadout();
    }

    private Int32Rect SnapMoveRectToNeighborEdges(Int32Rect rect)
    {
        var threshold = SnapThresholdScreenPixels();
        var bestDx = 0;
        var bestDy = 0;
        var bestXDistance = threshold + 1;
        var bestYDistance = threshold + 1;

        foreach (var neighbor in SnapNeighborRects())
        {
            foreach (var sourceX in new[] { rect.X, rect.X + rect.Width })
            {
                foreach (var targetX in new[] { neighbor.X, neighbor.X + neighbor.Width })
                {
                    var distance = Math.Abs(sourceX - targetX);
                    if (distance <= threshold && distance < bestXDistance)
                    {
                        bestXDistance = distance;
                        bestDx = targetX - sourceX;
                    }
                }
            }

            foreach (var sourceY in new[] { rect.Y, rect.Y + rect.Height })
            {
                foreach (var targetY in new[] { neighbor.Y, neighbor.Y + neighbor.Height })
                {
                    var distance = Math.Abs(sourceY - targetY);
                    if (distance <= threshold && distance < bestYDistance)
                    {
                        bestYDistance = distance;
                        bestDy = targetY - sourceY;
                    }
                }
            }
        }

        return new Int32Rect(rect.X + bestDx, rect.Y + bestDy, rect.Width, rect.Height);
    }

    private Int32Rect SnapResizeRectToNeighborEdges(Int32Rect rect, string edge)
    {
        var threshold = SnapThresholdScreenPixels();
        var x = rect.X;
        var y = rect.Y;
        var width = rect.Width;
        var height = rect.Height;

        var snapLeft = edge.Contains("Left", StringComparison.OrdinalIgnoreCase);
        var snapRight = edge.Contains("Right", StringComparison.OrdinalIgnoreCase);
        var snapTop = edge.Contains("Top", StringComparison.OrdinalIgnoreCase);
        var snapBottom = edge.Contains("Bottom", StringComparison.OrdinalIgnoreCase);

        foreach (var neighbor in SnapNeighborRects())
        {
            foreach (var targetX in new[] { neighbor.X, neighbor.X + neighbor.Width })
            {
                if (snapLeft && Math.Abs(x - targetX) <= threshold)
                {
                    var right = x + width;
                    x = targetX;
                    width = Math.Max(120, right - x);
                }

                if (snapRight && Math.Abs(x + width - targetX) <= threshold)
                {
                    width = Math.Max(120, targetX - x);
                }
            }

            foreach (var targetY in new[] { neighbor.Y, neighbor.Y + neighbor.Height })
            {
                if (snapTop && Math.Abs(y - targetY) <= threshold)
                {
                    var bottom = y + height;
                    y = targetY;
                    height = Math.Max(80, bottom - y);
                }

                if (snapBottom && Math.Abs(y + height - targetY) <= threshold)
                {
                    height = Math.Max(80, targetY - y);
                }
            }
        }

        return new Int32Rect(x, y, width, height);
    }

    private int SnapThresholdScreenPixels()
    {
        return Math.Max(8, (int)Math.Round(12 / Math.Max(0.001, _layoutScale)));
    }

    private IEnumerable<Int32Rect> SnapNeighborRects()
    {
        if (_selectedItem is null)
        {
            yield break;
        }

        foreach (var item in _profile.Items)
        {
            if (ReferenceEquals(item, _selectedItem) || !ShouldShowPlacement(item))
            {
                continue;
            }

            yield return item.Bounds;
        }
    }

    private void UpdateSelectedBoundsFromPlacementBox()
    {
        if (_selectedItem is null || LayoutCanvas.ActualWidth <= 0 || !ShouldShowPlacement(_selectedItem) || PlacementBox.Visibility != Visibility.Visible)
        {
            return;
        }

        _selectedItem.Bounds = ScreenRectFromPlacementBox();
        _selectedItem.Monitor = NativeMethods.GetMonitorNumber(_selectedItem.Bounds);
    }

    private static bool ShouldShowPlacement(ReadyItem item)
    {
        return item.Type != ReadyItemType.App || !item.MinimizeAfterLaunch;
    }

    private void UpdateLayoutReadout()
    {
        if (_selectedItem is null)
        {
            PositionText.Text = T("PositionEmpty");
            SizeText.Text = T("SizeEmpty");
            TargetText.Text = T("TargetNone");
            MonitorText.Text = T("MonitorEmpty");
            return;
        }

        if (!ShouldShowPlacement(_selectedItem))
        {
            PositionText.Text = T("NoLayout");
            SizeText.Text = T("MinimizedLaunch");
            TargetText.Text = $"{T("Target")} : {_selectedItem.Name}";
            MonitorText.Text = T("MonitorEmpty");
            return;
        }

        var rect = _selectedItem.Bounds;
        PositionText.Text = $"{T("Position")} : X {rect.X} / Y {rect.Y}";
        SizeText.Text = $"{T("Size")} : W {rect.Width} / H {rect.Height}";
        TargetText.Text = $"{T("Target")} : {_selectedItem.Name}";
        MonitorText.Text = $"{T("Monitor")} {_selectedItem.Monitor}";
    }

    private void SelectScreenRegionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem is null)
        {
            return;
        }

        var overlay = new ScreenSelectionOverlayWindow
        (
            _selectedItem.Bounds
        )
        {
            Owner = this
        };

        overlay.SelectionCompleted += result =>
        {
            _selectedItem.Bounds = result.Rect;
            _selectedItem.Monitor = result.MonitorNumber;
            PositionPlacementBoxFromScreenRect(result.Rect);
            UpdateLayoutReadout();
            RenderMonitorLayout();
            ProfileStore.Save(_profile);
        };

        overlay.ShowDialog();
    }

    private void ResetPlacementButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem is null)
        {
            return;
        }

        var rect = CenterOnPrimaryMonitor(640, 480);
        _selectedItem.Bounds = rect;
        _selectedItem.Monitor = NativeMethods.GetMonitorNumber(rect);
        PositionPlacementBoxFromScreenRect(rect);
        UpdateLayoutReadout();
        RenderMonitorLayout();
        ProfileStore.Save(_profile);
    }

    private void RenderMonitorLayout()
    {
        if (LayoutCanvas.ActualWidth <= 0 || LayoutCanvas.ActualHeight <= 0)
        {
            return;
        }

        LayoutCanvas.Children.Clear();
        var activeMonitors = NativeMethods.GetMonitors();
        UpdateStoredMonitors(activeMonitors);
        _displayMonitors = BuildDisplayMonitors(activeMonitors).ToList();
        _virtualScreen = GetDisplayVirtualScreenRect(_displayMonitors);
        const double padding = 34;
        _layoutScale = Math.Min(
            (LayoutCanvas.ActualWidth - padding * 2) / Math.Max(1, _virtualScreen.Width),
            (LayoutCanvas.ActualHeight - padding * 2) / Math.Max(1, _virtualScreen.Height));
        _layoutScale = Math.Max(0.04, _layoutScale);
        _layoutOffsetX = (LayoutCanvas.ActualWidth - _virtualScreen.Width * _layoutScale) / 2;
        _layoutOffsetY = (LayoutCanvas.ActualHeight - _virtualScreen.Height * _layoutScale) / 2;

        foreach (var monitor in _displayMonitors)
        {
            var monitorBox = CreateMonitorBox(monitor);
            Canvas.SetLeft(monitorBox, ToCanvasX(monitor.Bounds.X));
            Canvas.SetTop(monitorBox, ToCanvasY(monitor.Bounds.Y));
            Canvas.SetZIndex(monitorBox, 0);
            LayoutCanvas.Children.Add(monitorBox);
        }

        for (var i = _profile.Items.Count - 1; i >= 0; i--)
        {
            var item = _profile.Items[i];
            if (ReferenceEquals(item, _selectedItem) || !ShouldShowPlacement(item))
            {
                continue;
            }

            AddPreviewPlacement(item, i);
        }

        if (_selectedItem is not null && ShouldShowPlacement(_selectedItem))
        {
            Canvas.SetZIndex(PlacementBox, 100);
            LayoutCanvas.Children.Add(PlacementBox);
            PositionPlacementBoxFromScreenRect(_selectedItem.Bounds);
        }
    }

    private void UpdateStoredMonitors(IReadOnlyList<MonitorDisplay> activeMonitors)
    {
        foreach (var monitor in activeMonitors)
        {
            var stored = _profile.LastMonitors.FirstOrDefault(item => item.Number == monitor.Number);
            if (stored is null)
            {
                _profile.LastMonitors.Add(new StoredMonitor
                {
                    Number = monitor.Number
                });
                stored = _profile.LastMonitors[^1];
            }

            stored.X = monitor.Bounds.X;
            stored.Y = monitor.Bounds.Y;
            stored.Width = monitor.Bounds.Width;
            stored.Height = monitor.Bounds.Height;
            stored.IsPrimary = monitor.IsPrimary;
        }

    }

    private IEnumerable<MonitorLayoutDisplay> BuildDisplayMonitors(IReadOnlyList<MonitorDisplay> activeMonitors)
    {
        foreach (var monitor in activeMonitors)
        {
            yield return new MonitorLayoutDisplay(monitor.Number, monitor.Bounds, monitor.IsPrimary, true);
        }

        foreach (var stored in _profile.LastMonitors.OrderBy(monitor => monitor.Number))
        {
            if (activeMonitors.Any(monitor => monitor.Number == stored.Number))
            {
                continue;
            }

            yield return new MonitorLayoutDisplay(stored.Number, stored.Bounds, stored.IsPrimary, false);
        }
    }

    private static Rect GetDisplayVirtualScreenRect(IReadOnlyList<MonitorLayoutDisplay> monitors)
    {
        if (monitors.Count == 0)
        {
            return NativeMethods.GetVirtualScreenRect();
        }

        var left = monitors.Min(monitor => monitor.Bounds.Left);
        var top = monitors.Min(monitor => monitor.Bounds.Top);
        var right = monitors.Max(monitor => monitor.Bounds.Right);
        var bottom = monitors.Max(monitor => monitor.Bounds.Bottom);
        return new Rect(left, top, right - left, bottom - top);
    }

    private void AddPreviewPlacement(ReadyItem item, int index)
    {
        if (!ShouldShowPlacement(item))
        {
            return;
        }

        var box = new Border
        {
            Width = Math.Max(24, item.Width * _layoutScale),
            Height = Math.Max(24, item.Height * _layoutScale),
            Background = new SolidColorBrush(Color.FromArgb(62, 119, 183, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(180, 119, 183, 255)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(6),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = item.Name,
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(10),
                VerticalAlignment = VerticalAlignment.Top
            }
        };

        Canvas.SetLeft(box, ToCanvasX(item.X));
        Canvas.SetTop(box, ToCanvasY(item.Y));
        Canvas.SetZIndex(box, 30 + (_profile.Items.Count - index));
        LayoutCanvas.Children.Add(box);
    }

    private Border CreateMonitorBox(MonitorLayoutDisplay monitor)
    {
        var width = Math.Max(48, monitor.Bounds.Width * _layoutScale);
        var height = Math.Max(48, monitor.Bounds.Height * _layoutScale);
        var background = monitor.IsPrimary
            ? new SolidColorBrush(Color.FromRgb(173, 80, 196))
            : new SolidColorBrush(Color.FromRgb(45, 45, 45));

        if (!monitor.IsOnline)
        {
            background = new SolidColorBrush(Color.FromRgb(25, 25, 28));
        }

        return new Border
        {
            Width = width,
            Height = height,
            Background = background,
            BorderBrush = monitor.Number == _selectedItem?.Monitor
                ? new SolidColorBrush(Color.FromRgb(66, 211, 146))
                : monitor.IsOnline
                    ? new SolidColorBrush(Color.FromRgb(70, 74, 84))
                    : new SolidColorBrush(Color.FromRgb(52, 55, 62)),
            BorderThickness = monitor.Number == _selectedItem?.Monitor ? new Thickness(3) : new Thickness(1),
            Opacity = monitor.IsOnline ? 1.0 : 0.62,
            CornerRadius = new CornerRadius(6),
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = monitor.Number.ToString(),
                        Foreground = monitor.IsOnline ? Brushes.White : new SolidColorBrush(Color.FromRgb(150, 154, 164)),
                        FontSize = 26,
                        FontWeight = FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = monitor.IsOnline ? "" : "오프라인",
                        Foreground = new SolidColorBrush(Color.FromRgb(156, 162, 174)),
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 2, 0, 0)
                    }
                }
            }
        };
    }

    private void PositionPlacementBoxFromScreenRect(Int32Rect rect)
    {
        PlacementBox.Width = Math.Max(24, rect.Width * _layoutScale);
        PlacementBox.Height = Math.Max(24, rect.Height * _layoutScale);
        Canvas.SetLeft(PlacementBox, ToCanvasX(rect.X));
        Canvas.SetTop(PlacementBox, ToCanvasY(rect.Y));
    }

    private bool IsCanvasPointInsideAnyMonitor(Point canvasPoint)
    {
        var screenX = FromCanvasX(canvasPoint.X);
        var screenY = FromCanvasY(canvasPoint.Y);
        return NativeMethods.GetMonitors().Any(m =>
            screenX >= m.Bounds.Left &&
            screenX <= m.Bounds.Right &&
            screenY >= m.Bounds.Top &&
            screenY <= m.Bounds.Bottom);
    }

    private Int32Rect ScreenRectFromCanvasRect(double left, double top, double width, double height)
    {
        return new Int32Rect(
            (int)Math.Round(FromCanvasX(left)),
            (int)Math.Round(FromCanvasY(top)),
            Math.Max(1, (int)Math.Round(width / Math.Max(0.001, _layoutScale))),
            Math.Max(1, (int)Math.Round(height / Math.Max(0.001, _layoutScale))));
    }

    private Int32Rect ClampRectToVisibleLayout(Int32Rect rect)
    {
        var bounds = _displayMonitors.Count > 0
            ? GetDisplayVirtualScreenRect(_displayMonitors)
            : GetActiveVirtualScreenRect();

        if (bounds.IsEmpty)
        {
            return rect;
        }

        var width = Math.Min(rect.Width, Math.Max(1, (int)Math.Round(bounds.Width)));
        var height = Math.Min(rect.Height, Math.Max(1, (int)Math.Round(bounds.Height)));
        var x = Math.Clamp(rect.X, (int)Math.Round(bounds.Left), (int)Math.Round(bounds.Right) - width);
        var y = Math.Clamp(rect.Y, (int)Math.Round(bounds.Top), (int)Math.Round(bounds.Bottom) - height);
        return new Int32Rect(x, y, width, height);
    }

    private static Rect GetActiveVirtualScreenRect()
    {
        var monitors = NativeMethods.GetMonitors();
        if (monitors.Count == 0)
        {
            return Rect.Empty;
        }

        var left = monitors.Min(monitor => monitor.Bounds.Left);
        var top = monitors.Min(monitor => monitor.Bounds.Top);
        var right = monitors.Max(monitor => monitor.Bounds.Right);
        var bottom = monitors.Max(monitor => monitor.Bounds.Bottom);
        return new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private Int32Rect CenterOnPrimaryMonitor(int width, int height)
    {
        var monitor = NativeMethods.GetMonitors().FirstOrDefault(m => m.IsPrimary)
            ?? NativeMethods.GetMonitors().FirstOrDefault();

        if (monitor is null)
        {
            return new Int32Rect(0, 0, width, height);
        }

        var clampedWidth = Math.Min(width, (int)monitor.Bounds.Width);
        var clampedHeight = Math.Min(height, (int)monitor.Bounds.Height);
        var x = (int)Math.Round(monitor.Bounds.Left + (monitor.Bounds.Width - clampedWidth) / 2.0);
        var y = (int)Math.Round(monitor.Bounds.Top + (monitor.Bounds.Height - clampedHeight) / 2.0);
        return new Int32Rect(x, y, clampedWidth, clampedHeight);
    }

    private Int32Rect ScreenRectFromPlacementBox()
    {
        var left = FromCanvasX(Canvas.GetLeft(PlacementBox));
        var top = FromCanvasY(Canvas.GetTop(PlacementBox));
        var width = PlacementBox.Width / Math.Max(0.001, _layoutScale);
        var height = PlacementBox.Height / Math.Max(0.001, _layoutScale);

        return new Int32Rect(
            (int)Math.Round(left),
            (int)Math.Round(top),
            Math.Max(1, (int)Math.Round(width)),
            Math.Max(1, (int)Math.Round(height)));
    }

    private double ToCanvasX(double screenX) => _layoutOffsetX + (screenX - _virtualScreen.X) * _layoutScale;

    private double ToCanvasY(double screenY) => _layoutOffsetY + (screenY - _virtualScreen.Y) * _layoutScale;

    private double FromCanvasX(double canvasX) => (canvasX - _layoutOffsetX) / Math.Max(0.001, _layoutScale) + _virtualScreen.X;

    private double FromCanvasY(double canvasY) => (canvasY - _layoutOffsetY) / Math.Max(0.001, _layoutScale) + _virtualScreen.Y;

    private void ApplyLaunchSettingsToUi()
    {
        _isLoadingSettings = true;
        SequentialLaunchRadio.IsChecked = _profile.LaunchSequentially;
        ParallelLaunchRadio.IsChecked = !_profile.LaunchSequentially;
        UseLaunchDelayCheckBox.IsChecked = _profile.SequentialDelaySeconds > 0;
        LaunchDelayTextBox.Text = Math.Max(0, _profile.SequentialDelaySeconds).ToString();
        LaunchDelayTextBox.IsEnabled = _profile.LaunchSequentially && UseLaunchDelayCheckBox.IsChecked == true;
        SnapCheckBox.IsChecked = _profile.WindowSnapEnabled;
        MaxDownloadSpeedTextBox.Text = FormatSpeed(_profile.MaxDownloadMegabytesPerSecond);
        MaxUploadSpeedTextBox.Text = FormatSpeed(_profile.MaxUploadMegabytesPerSecond);
        _isLoadingSettings = false;
    }

    private void ApplyBrowserPathsToUi()
    {
        _isLoadingSettings = true;
        EdgePathTextBox.Text = BrowserPath("Edge");
        ChromePathTextBox.Text = BrowserPath("Chrome");
        BravePathTextBox.Text = BrowserPath("Brave");
        FirefoxPathTextBox.Text = BrowserPath("Firefox");
        _isLoadingSettings = false;
    }

    private string BrowserPath(string browser)
    {
        return _profile.BrowserPaths.TryGetValue(browser, out var path) ? path : "";
    }

    private void ApplyLaunchSettingsFromUi()
    {
        _profile.LaunchSequentially = SequentialLaunchRadio.IsChecked == true;
        _profile.WindowSnapEnabled = SnapCheckBox.IsChecked == true;
        _profile.SequentialDelaySeconds = _profile.LaunchSequentially && UseLaunchDelayCheckBox.IsChecked == true && int.TryParse(LaunchDelayTextBox.Text, out var seconds)
            ? Math.Clamp(seconds, 0, 3600)
            : 0;
        if (TryParseSpeed(MaxDownloadSpeedTextBox.Text, out var maxDownload))
        {
            _profile.MaxDownloadMegabytesPerSecond = maxDownload;
        }

        if (TryParseSpeed(MaxUploadSpeedTextBox.Text, out var maxUpload))
        {
            _profile.MaxUploadMegabytesPerSecond = maxUpload;
        }

        LaunchDelayTextBox.IsEnabled = _profile.LaunchSequentially && UseLaunchDelayCheckBox.IsChecked == true;
    }

    private static string FormatSpeed(double value)
    {
        return Math.Max(0.1, value).ToString("0.##");
    }

    private static bool TryParseSpeed(string text, out double speed)
    {
        text = text.Trim().Replace(',', '.');
        return double.TryParse(
            text,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out speed) && speed > 0;
    }

    private void ApplyBrowserPathsFromUi()
    {
        _profile.BrowserPaths["Edge"] = EdgePathTextBox.Text.Trim();
        _profile.BrowserPaths["Chrome"] = ChromePathTextBox.Text.Trim();
        _profile.BrowserPaths["Brave"] = BravePathTextBox.Text.Trim();
        _profile.BrowserPaths["Firefox"] = FirefoxPathTextBox.Text.Trim();
        foreach (var browser in _profile.CustomBrowsers)
        {
            _profile.BrowserPaths[browser.Id] = browser.Path;
        }
    }

    private void RefreshCustomBrowserList()
    {
        CustomBrowserList.ItemsSource = null;
        CustomBrowserList.ItemsSource = _profile.CustomBrowsers.ToList();
    }

    private void LaunchSettings_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        ApplyLaunchSettingsFromUi();
        ProfileStore.Save(_profile);
    }

    private void SnapCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        ApplyLaunchSettingsFromUi();
        ProfileStore.Save(_profile);
    }

    private void BrowserPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingSettings || _profile is null)
        {
            return;
        }

        ApplyBrowserPathsFromUi();
        ProfileStore.Save(_profile);
    }

    private void BrowseBrowserPathButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string browser })
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "실행 파일|*.exe|모든 파일|*.*",
            FileName = BrowserPath(browser)
        };

        if (dialog.ShowDialog(this) == true)
        {
            BrowserPathTextBox(browser).Text = dialog.FileName;
            ApplyBrowserPathsFromUi();
            ProfileStore.Save(_profile);
        }
    }

    private TextBox BrowserPathTextBox(string browser)
    {
        return browser switch
        {
            "Chrome" => ChromePathTextBox,
            "Brave" => BravePathTextBox,
            "Firefox" => FirefoxPathTextBox,
            _ => EdgePathTextBox
        };
    }

    private void BrowseCustomBrowserPathButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "실행 파일|*.exe|모든 파일|*.*",
            FileName = CustomBrowserPathTextBox.Text
        };

        if (dialog.ShowDialog(this) == true)
        {
            CustomBrowserPathTextBox.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(CustomBrowserNameTextBox.Text))
            {
                CustomBrowserNameTextBox.Text = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
            }
        }
    }

    private void AddCustomBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        var name = CustomBrowserNameTextBox.Text.Trim();
        var path = CustomBrowserPathTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path))
        {
            MessageBox.Show(this, "표시 이름과 실행 파일 경로를 입력해 주세요.", "커스텀 브라우저 추가", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!System.IO.File.Exists(path))
        {
            MessageBox.Show(this, "선택한 실행 파일을 찾을 수 없습니다.", "커스텀 브라우저 추가", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var browser = new CustomBrowser
        {
            Name = name,
            Path = path
        };

        _profile.CustomBrowsers.Add(browser);
        _profile.BrowserPaths[browser.Id] = browser.Path;
        ProfileStore.Save(_profile);
        CustomBrowserNameTextBox.Text = "";
        CustomBrowserPathTextBox.Text = "";
        RefreshCustomBrowserList();
    }

    private void RemoveCustomBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id })
        {
            return;
        }

        _profile.CustomBrowsers.RemoveAll(browser => browser.Id == id);
        _profile.BrowserPaths.Remove(id);
        foreach (var item in _profile.Items.Where(item => item.Browser == id))
        {
            item.Browser = "Edge";
        }

        ProfileStore.Save(_profile);
        RefreshCustomBrowserList();
        RenderReadyItems();
    }

    private void LaunchDelayTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingSettings || _profile is null)
        {
            return;
        }

        ApplyLaunchSettingsFromUi();
        ProfileStore.Save(_profile);
    }

    private void NetworkSpeedTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingSettings || _profile is null)
        {
            return;
        }

        ApplyLaunchSettingsFromUi();
        ProfileStore.Save(_profile);
        UpdateIndicators();
    }

    private void MiniModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isMiniMode)
        {
            ExitMiniMode();
        }
        else
        {
            EnterMiniMode();
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        MainTabControl.SelectedIndex = 1;
    }

    private void EnterMiniMode()
    {
        _normalWidth = Width;
        _normalHeight = Height;
        _isMiniMode = true;

        RootGrid.Margin = new Thickness(6);
        HeaderPanel.Visibility = Visibility.Collapsed;
        ContentPanel.Margin = new Thickness(0);
        TitlePanel.Visibility = Visibility.Collapsed;
        AdminBadge.Visibility = Visibility.Collapsed;
        SettingsButton.Visibility = Visibility.Collapsed;
        SaveProfileButton.Visibility = Visibility.Collapsed;
        StartPrepButton.Visibility = Visibility.Collapsed;
        ReadyListPanel.Visibility = Visibility.Collapsed;
        LayoutEditorPanel.Visibility = Visibility.Collapsed;
        ReadyListColumn.Width = new GridLength(0);
        LayoutEditorColumn.Width = new GridLength(0);
        IndicatorColumn.Width = new GridLength(210);
        IndicatorPanel.Padding = new Thickness(0);
        IndicatorPanel.Background = Brushes.Transparent;
        IndicatorPanel.BorderThickness = new Thickness(0);
        IndicatorScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        IndicatorTitle.Visibility = Visibility.Collapsed;
        IndicatorDescription.Visibility = Visibility.Collapsed;
        MiniSummaryText.Visibility = Visibility.Collapsed;
        MiniGaugePanel.Visibility = Visibility.Visible;
        NetworkCard.Visibility = Visibility.Collapsed;
        CpuCard.Visibility = Visibility.Collapsed;
        GpuList.Visibility = Visibility.Collapsed;
        IndicatorFootnote.Visibility = Visibility.Collapsed;

        MiniModeButton.Content = T("Full");
        MiniModeButton.Padding = new Thickness(9, 5, 9, 5);
        MiniModeButton.MinWidth = 54;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        MinWidth = 236;
        MinHeight = 236;
        Width = 242;
        Height = 244;
    }

    private void ExitMiniMode()
    {
        _isMiniMode = false;

        RootGrid.Margin = new Thickness(24);
        HeaderPanel.Visibility = Visibility.Visible;
        ContentPanel.Margin = new Thickness(0, 22, 0, 0);
        TitlePanel.Visibility = Visibility.Visible;
        AdminBadge.Visibility = Visibility.Visible;
        SettingsButton.Visibility = Visibility.Visible;
        SaveProfileButton.Visibility = Visibility.Visible;
        StartPrepButton.Visibility = Visibility.Visible;
        ReadyListPanel.Visibility = Visibility.Visible;
        LayoutEditorPanel.Visibility = Visibility.Visible;
        ReadyListColumn.Width = new GridLength(320);
        LayoutEditorColumn.Width = new GridLength(1, GridUnitType.Star);
        IndicatorColumn.Width = new GridLength(330);
        IndicatorPanel.Padding = new Thickness(18);
        IndicatorPanel.Background = (Brush)FindResource("PanelBrush");
        IndicatorPanel.BorderThickness = new Thickness(1);
        IndicatorScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        IndicatorTitle.Visibility = Visibility.Visible;
        IndicatorDescription.Visibility = Visibility.Collapsed;
        MiniSummaryText.Visibility = Visibility.Collapsed;
        MiniGaugePanel.Visibility = Visibility.Collapsed;
        NetworkCard.Visibility = Visibility.Visible;
        CpuCard.Visibility = Visibility.Visible;
        GpuList.Visibility = Visibility.Visible;
        IndicatorFootnote.Visibility = Visibility.Collapsed;

        MiniModeButton.Content = T("MiniMode");
        MiniModeButton.Padding = new Thickness(14, 9, 14, 9);
        MiniModeButton.MinWidth = 92;
        Topmost = false;
        ResizeMode = ResizeMode.CanResize;
        MinWidth = 1080;
        MinHeight = 720;
        Width = Math.Max(1080, _normalWidth);
        Height = Math.Max(720, _normalHeight);
    }

    public static Brush VendorBrushForName(string name)
    {
        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
        {
            return new SolidColorBrush(Color.FromRgb(122, 255, 120));
        }

        if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("ATI", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RADEON", StringComparison.OrdinalIgnoreCase))
        {
            return new SolidColorBrush(Color.FromRgb(255, 116, 116));
        }

        if (name.Contains("INTEL", StringComparison.OrdinalIgnoreCase))
        {
            return new SolidColorBrush(Color.FromRgb(105, 190, 255));
        }

        return Brushes.WhiteSmoke;
    }

    private static string GetCpuName()
    {
        try
        {
            return Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0",
                "ProcessorNameString",
                "CPU")?.ToString()?.Trim() ?? "CPU";
        }
        catch
        {
            return "CPU";
        }
    }
}

public sealed record GpuIndicator(string Name, int Load)
{
    public string LoadText => $"{Load}%";

    public Brush LoadBrush => Load >= 95 ? Brushes.IndianRed : Brushes.WhiteSmoke;

    public Brush NameBrush => MainWindow.VendorBrushForName(Name);
}

public sealed record MonitorLayoutDisplay(int Number, Rect Bounds, bool IsPrimary, bool IsOnline);
