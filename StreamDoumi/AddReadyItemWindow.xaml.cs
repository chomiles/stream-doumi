using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace StreamDoumi;

public partial class AddReadyItemWindow : Window
{
    private readonly ReadyProfile _profile;
    private readonly ReadyItem? _editingItem;
    private readonly string _language;

    public ReadyItem? Result { get; private set; }

    public AddReadyItemWindow() : this(ProfileStore.Load())
    {
    }

    public AddReadyItemWindow(ReadyProfile profile)
    {
        _profile = profile;
        _language = NormalizeLanguage(profile.Language);
        BrowserCatalog.EnsureBrowserPaths(_profile);
        InitializeComponent();
        PopulateBrowserComboBox();
        ApplyBrowserAvailability();
        ApplyLanguage();
        ApplyTypeUi();
    }

    public AddReadyItemWindow(ReadyItem item, ReadyProfile profile) : this(profile)
    {
        _editingItem = item;
        LoadItem(item);
    }

    private void LoadItem(ReadyItem item)
    {
        Title = T("EditTitle");
        DialogTitleText.Text = T("EditTitle");
        SaveButton.Content = T("Save");
        SelectComboBoxItem(TypeComboBox, item.Type switch
        {
            ReadyItemType.Browser => "Browser",
            ReadyItemType.Shortcut => "Shortcut",
            _ => "App"
        });

        NameTextBox.Text = item.Name;
        PathTextBox.Text = item.Path;
        ArgumentsTextBox.Text = item.Arguments;
        RunAsAdminCheckBox.IsChecked = item.RunAsAdmin;
        MinimizeAfterLaunchCheckBox.IsChecked = item.MinimizeAfterLaunch;
        SelectComboBoxItem(BrowserComboBox, item.Browser);
        BrowserProfileTextBox.Text = item.BrowserProfile;
        UrlTextBox.Text = item.Url;
        ApplyBrowserAvailability();
        ApplyTypeUi();
    }

    private void ApplyBrowserAvailability()
    {
        PopulateBrowserComboBox();
        foreach (var item in BrowserComboBox.Items.OfType<ComboBoxItem>())
        {
            var browser = item.Tag?.ToString() ?? "";
            if (browser == "Default")
            {
                item.IsEnabled = true;
                continue;
            }

            var definition = BrowserCatalog.AllBrowsers(_profile).FirstOrDefault(candidate => candidate.Id == browser);
            var displayName = definition?.DisplayName ?? browser;
            var available = BrowserCatalog.IsExecutableAvailable(_profile.BrowserPaths, browser);
            item.Content = available ? displayName : $"{displayName} ({T("NotDetected")})";
            item.Foreground = available
                ? System.Windows.Media.Brushes.Black
                : System.Windows.Media.Brushes.Gray;
            item.IsEnabled = available;
        }

        if (BrowserComboBox.SelectedItem is ComboBoxItem { IsEnabled: false })
        {
            BrowserComboBox.SelectedItem = BrowserComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.IsEnabled);
        }
    }

    private void PopulateBrowserComboBox()
    {
        var selected = (BrowserComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        BrowserComboBox.Items.Clear();

        foreach (var browser in BrowserCatalog.AllBrowsers(_profile))
        {
            BrowserComboBox.Items.Add(new ComboBoxItem
            {
                Content = browser.DisplayName,
                Tag = browser.Id
            });
        }

        BrowserComboBox.Items.Add(new ComboBoxItem
        {
            Content = T("DefaultBrowser"),
            Tag = "Default"
        });

        SelectComboBoxItem(BrowserComboBox, selected ?? "Edge");
    }

    private void TypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyTypeUi();
    }

    private void ApplyTypeUi()
    {
        if (NameTextBox is null ||
            PathPanel is null ||
            PathLabel is null ||
            BrowserPanel is null ||
            UrlPanel is null ||
            ArgumentsPanel is null ||
            ArgumentsLabel is null ||
            MinimizeAfterLaunchCheckBox is null ||
            RunAsAdminCheckBox is null)
        {
            return;
        }

        var type = SelectedType();
        BrowserPanel.Visibility = type == ReadyItemType.Browser ? Visibility.Visible : Visibility.Collapsed;
        UrlPanel.Visibility = type == ReadyItemType.Browser ? Visibility.Visible : Visibility.Collapsed;
        PathPanel.Visibility = type == ReadyItemType.Browser ? Visibility.Collapsed : Visibility.Visible;
        ArgumentsPanel.Visibility = Visibility.Visible;
        RunAsAdminCheckBox.Visibility = type == ReadyItemType.Browser ? Visibility.Collapsed : Visibility.Visible;
        MinimizeAfterLaunchCheckBox.Visibility = type == ReadyItemType.App ? Visibility.Visible : Visibility.Collapsed;
        ArgumentsLabel.Text = type == ReadyItemType.Browser ? T("ArgumentsOptional") : T("Arguments");
        PathLabel.Text = type == ReadyItemType.Shortcut ? T("ShortcutFile") : T("ExecutableFile");

        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
        {
            NameTextBox.Text = type switch
            {
                ReadyItemType.App => T("NewApp"),
                ReadyItemType.Browser => T("NewBrowser"),
                ReadyItemType.Shortcut => T("NewShortcut"),
                _ => T("NewItem")
            };
        }
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var type = SelectedType();
        var dialog = new OpenFileDialog
        {
            Filter = type == ReadyItemType.Shortcut
                ? T("ShortcutFilter")
                : T("ExecutableAndShortcutFilter"),
            DereferenceLinks = false,
            CheckFileExists = true
        };

        AddDesktopPlaces(dialog, type);

        if (dialog.ShowDialog(this) == true)
        {
            var extension = System.IO.Path.GetExtension(dialog.FileName);
            if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
            {
                SelectComboBoxItem(TypeComboBox, "Shortcut");
                ApplyTypeUi();
            }

            PathTextBox.Text = dialog.FileName;
            if (NameTextBox.Text == T("NewApp") || NameTextBox.Text == T("NewShortcut"))
            {
                NameTextBox.Text = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
            }
        }
    }

    private static void AddDesktopPlaces(OpenFileDialog dialog, ReadyItemType type)
    {
        var userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

        if (Directory.Exists(userDesktop))
        {
            dialog.CustomPlaces.Add(new FileDialogCustomPlace(userDesktop));
        }

        if (Directory.Exists(publicDesktop))
        {
            dialog.CustomPlaces.Add(new FileDialogCustomPlace(publicDesktop));
            if (type == ReadyItemType.Shortcut)
            {
                dialog.InitialDirectory = publicDesktop;
            }
        }
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var type = SelectedType();
        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
        {
            MessageBox.Show(this, T("NameRequired"), T("RequiredInfo"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (type == ReadyItemType.Browser && string.IsNullOrWhiteSpace(UrlTextBox.Text))
        {
            MessageBox.Show(this, T("UrlRequired"), T("RequiredInfo"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (type != ReadyItemType.Browser && string.IsNullOrWhiteSpace(PathTextBox.Text))
        {
            MessageBox.Show(this, T("PathRequired"), T("RequiredInfo"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (type == ReadyItemType.Shortcut && RunAsAdminCheckBox.IsChecked == true)
        {
            MessageBox.Show(this, T("ShortcutAdminWarning"), T("ShortcutAdminTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        Result = new ReadyItem
        {
            Id = _editingItem?.Id ?? Guid.NewGuid().ToString("N"),
            Type = type,
            Name = NameTextBox.Text.Trim(),
            Path = PathTextBox.Text.Trim(),
            Arguments = ArgumentsTextBox.Text.Trim(),
            RunAsAdmin = RunAsAdminCheckBox.IsChecked == true,
            MinimizeAfterLaunch = type == ReadyItemType.App && MinimizeAfterLaunchCheckBox.IsChecked == true,
            Browser = SelectedBrowser(),
            BrowserProfile = BrowserProfileTextBox.Text.Trim(),
            Url = UrlTextBox.Text.Trim(),
            Monitor = _editingItem?.Monitor ?? 1,
            X = _editingItem?.X ?? 0,
            Y = _editingItem?.Y ?? 0,
            Width = _editingItem?.Width ?? 1280,
            Height = _editingItem?.Height ?? 720
        };

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private ReadyItemType SelectedType()
    {
        var tag = (TypeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        return tag switch
        {
            "Browser" => ReadyItemType.Browser,
            "Shortcut" => ReadyItemType.Shortcut,
            _ => ReadyItemType.App
        };
    }

    private string SelectedBrowser()
    {
        return (BrowserComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Edge";
    }

    private void ApplyLanguage()
    {
        Title = T("AddTitle");
        DialogTitleText.Text = T("AddHeader");
        DialogDescriptionText.Text = T("Description");
        TypeLabel.Text = T("Type");
        AppTypeItem.Content = T("App");
        BrowserTypeItem.Content = T("Browser");
        ShortcutTypeItem.Content = T("Shortcut");
        BrowserLabel.Text = T("Browser");
        NameLabel.Text = T("DisplayName");
        BrowseButton.Content = T("Browse");
        UrlLabel.Text = "URL";
        BrowserProfileLabel.Text = T("BrowserProfileOptional");
        RunAsAdminCheckBox.Content = T("RunAsAdmin");
        MinimizeAfterLaunchCheckBox.Content = T("MinimizeAfterLaunch");
        CancelButton.Content = T("Cancel");
        SaveButton.Content = T("Add");
        ApplyTypeUi();
    }

    private string T(string key)
    {
        return _language switch
        {
            "en" => key switch
            {
                "AddTitle" => "Add item",
                "AddHeader" => "Add ready item",
                "EditTitle" => "Edit item",
                "Description" => "Register an app, browser, or shortcut.",
                "Type" => "Type",
                "App" => "App",
                "Browser" => "Browser",
                "Shortcut" => "Shortcut",
                "DisplayName" => "Display name",
                "Browse" => "Browse",
                "BrowserProfileOptional" => "Browser profile (optional)",
                "RunAsAdmin" => "Run as administrator",
                "MinimizeAfterLaunch" => "Minimize after launch",
                "Cancel" => "Cancel",
                "Add" => "Add",
                "Save" => "Save",
                "DefaultBrowser" => "Default browser",
                "NotDetected" => "not detected",
                "ArgumentsOptional" => "Arguments (optional)",
                "Arguments" => "Arguments",
                "ShortcutFile" => "Shortcut file",
                "ExecutableFile" => "Executable file",
                "NewApp" => "New app",
                "NewBrowser" => "New browser",
                "NewShortcut" => "New shortcut",
                "NewItem" => "New item",
                "ShortcutFilter" => "Shortcuts and scripts|*.lnk;*.url;*.bat;*.cmd|All files|*.*",
                "ExeFilter" => "Executable files|*.exe|All files|*.*",
                "ExecutableAndShortcutFilter" => "Executable files and shortcuts|*.exe;*.lnk;*.url;*.bat;*.cmd|All files|*.*",
                "RequiredInfo" => "Required information",
                "NameRequired" => "Enter a name.",
                "UrlRequired" => "Enter a URL.",
                "PathRequired" => "Select a file to launch.",
                "ShortcutAdminTitle" => "Shortcut administrator launch",
                "ShortcutAdminWarning" => "Shortcut items may fail when launched as administrator.",
                _ => key
            },
            "ja" => key switch
            {
                "AddTitle" => "項目追加",
                "AddHeader" => "準備項目追加",
                "EditTitle" => "項目編集",
                "Description" => "アプリ、ブラウザー、ショートカットを登録します。",
                "Type" => "種類",
                "App" => "アプリ",
                "Browser" => "ブラウザー",
                "Shortcut" => "ショートカット",
                "DisplayName" => "表示名",
                "Browse" => "参照",
                "BrowserProfileOptional" => "ブラウザープロファイル (任意)",
                "RunAsAdmin" => "管理者権限で実行",
                "MinimizeAfterLaunch" => "実行後に最小化",
                "Cancel" => "キャンセル",
                "Add" => "登録",
                "Save" => "保存",
                "DefaultBrowser" => "既定のブラウザー",
                "NotDetected" => "未検出",
                "ArgumentsOptional" => "実行引数 (任意)",
                "Arguments" => "実行引数",
                "ShortcutFile" => "ショートカットファイル",
                "ExecutableFile" => "実行ファイル",
                "NewApp" => "新しいアプリ",
                "NewBrowser" => "新しいブラウザー",
                "NewShortcut" => "新しいショートカット",
                "NewItem" => "新しい項目",
                "ShortcutFilter" => "ショートカットとスクリプト|*.lnk;*.url;*.bat;*.cmd|すべてのファイル|*.*",
                "ExeFilter" => "実行ファイル|*.exe|すべてのファイル|*.*",
                "ExecutableAndShortcutFilter" => "実行ファイルとショートカット|*.exe;*.lnk;*.url;*.bat;*.cmd|すべてのファイル|*.*",
                "RequiredInfo" => "必要情報",
                "NameRequired" => "名前を入力してください。",
                "UrlRequired" => "URLを入力してください。",
                "PathRequired" => "実行するファイルを選択してください。",
                "ShortcutAdminTitle" => "ショートカットの管理者実行",
                "ShortcutAdminWarning" => "ショートカット項目は管理者モードで実行するとエラーが発生する場合があります。",
                _ => key
            },
            _ => key switch
            {
                "AddTitle" => "항목 추가",
                "AddHeader" => "준비 항목 추가",
                "EditTitle" => "속성 변경",
                "Description" => "앱, 브라우저, 바로가기 중 하나를 등록합니다.",
                "Type" => "종류",
                "App" => "앱",
                "Browser" => "브라우저",
                "Shortcut" => "바로가기",
                "DisplayName" => "표시 이름",
                "Browse" => "찾기",
                "BrowserProfileOptional" => "브라우저 프로필 (선택)",
                "RunAsAdmin" => "관리자 권한으로 실행",
                "MinimizeAfterLaunch" => "실행 후 최소화",
                "Cancel" => "취소",
                "Add" => "등록",
                "Save" => "저장",
                "DefaultBrowser" => "기본 브라우저",
                "NotDetected" => "감지되지 않음",
                "ArgumentsOptional" => "실행 인자 (선택)",
                "Arguments" => "실행 인자",
                "ShortcutFile" => "바로가기 파일",
                "ExecutableFile" => "실행 파일",
                "NewApp" => "새 앱",
                "NewBrowser" => "새 브라우저",
                "NewShortcut" => "새 바로가기",
                "NewItem" => "새 항목",
                "ShortcutFilter" => "바로가기 및 스크립트|*.lnk;*.url;*.bat;*.cmd|모든 파일|*.*",
                "ExeFilter" => "실행 파일|*.exe|모든 파일|*.*",
                "ExecutableAndShortcutFilter" => "실행 파일 및 바로가기|*.exe;*.lnk;*.url;*.bat;*.cmd|모든 파일|*.*",
                "RequiredInfo" => "등록 필요 정보",
                "NameRequired" => "이름을 입력해주세요.",
                "UrlRequired" => "URL을 입력해주세요.",
                "PathRequired" => "실행할 파일을 선택해주세요.",
                "ShortcutAdminTitle" => "바로가기 관리자 실행",
                "ShortcutAdminWarning" => "바로가기 아이콘은 관리자모드 실행시 오류가 발생할 수 있습니다.",
                _ => key
            }
        };
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

    private static void SelectComboBoxItem(ComboBox comboBox, string tag)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }
}
