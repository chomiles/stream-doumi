using System.Text.Json.Serialization;
using System.Windows;

namespace StreamDoumi;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReadyItemType
{
    App,
    Browser,
    Shortcut
}

public sealed class ReadyItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ReadyItemType Type { get; set; }
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Arguments { get; set; } = "";
    public bool RunAsAdmin { get; set; }
    public bool MinimizeAfterLaunch { get; set; }
    public string Browser { get; set; } = "Edge";
    public string BrowserProfile { get; set; } = "";
    public string Url { get; set; } = "";
    public int Monitor { get; set; } = 1;
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;

    [JsonIgnore]
    public string TypeLabel => Type switch
    {
        ReadyItemType.App => "앱",
        ReadyItemType.Browser => "브라우저",
        ReadyItemType.Shortcut => "바로가기",
        _ => "항목"
    };

    [JsonIgnore]
    public string Detail => Type switch
    {
        ReadyItemType.App => string.IsNullOrWhiteSpace(Path) ? "실행 파일 미지정" : Path,
        ReadyItemType.Browser => $"{Browser}  {Url}",
        ReadyItemType.Shortcut => string.IsNullOrWhiteSpace(Path) ? "바로가기 미지정" : Path,
        _ => ""
    };

    [JsonIgnore]
    public Int32Rect Bounds
    {
        get => new(X, Y, Width, Height);
        set
        {
            X = value.X;
            Y = value.Y;
            Width = value.Width;
            Height = value.Height;
        }
    }
}

public sealed class ReadyProfile
{
    public List<ReadyItem> Items { get; set; } = [];
    public string Language { get; set; } = "ko";
    public bool WindowSnapEnabled { get; set; }
    public bool LaunchSequentially { get; set; } = true;
    public int SequentialDelaySeconds { get; set; } = 3;
    public double MaxDownloadMegabytesPerSecond { get; set; } = 100;
    public double MaxUploadMegabytesPerSecond { get; set; } = 100;
    public Dictionary<string, string> BrowserPaths { get; set; } = new();
    public List<CustomBrowser> CustomBrowsers { get; set; } = [];
    public List<StoredMonitor> LastMonitors { get; set; } = [];
}

public sealed class CustomBrowser
{
    public string Id { get; set; } = $"Custom_{Guid.NewGuid():N}";
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
}

public sealed class StoredMonitor
{
    public int Number { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool IsPrimary { get; set; }

    [JsonIgnore]
    public Rect Bounds => new(X, Y, Width, Height);
}
