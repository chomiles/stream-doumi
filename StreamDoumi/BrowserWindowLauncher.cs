using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace StreamDoumi;

public static class BrowserWindowLauncher
{
    private const int SW_RESTORE = 9;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> BrowserLocks = new();

    public static async Task LaunchAndPlaceAsync(ReadyItem item, ReadyProfile profile)
    {
        if (item.Type != ReadyItemType.Browser || string.IsNullOrWhiteSpace(item.Url))
        {
            return;
        }

        var browserLock = BrowserLocks.GetOrAdd(item.Browser, _ => new SemaphoreSlim(1, 1));
        await browserLock.WaitAsync();
        try
        {
            await LaunchAndPlaceCoreAsync(item, profile);
        }
        finally
        {
            browserLock.Release();
        }
    }

    private static async Task LaunchAndPlaceCoreAsync(ReadyItem item, ReadyProfile profile)
    {
        var processName = BrowserProcessName(profile, item.Browser);
        var beforeWindows = processName is null
            ? new HashSet<IntPtr>()
            : EnumerateBrowserWindows(processName).Select(w => w.Handle).ToHashSet();

        var executable = ResolveBrowserPath(item.Browser, profile.BrowserPaths);
        if (item.Browser != "Default" && string.IsNullOrWhiteSpace(executable))
        {
            throw new FileNotFoundException($"{item.Browser} 실행 파일을 찾을 수 없습니다. 설정 탭에서 브라우저 경로를 확인해 주세요.");
        }

        var startInfo = CreateStartInfo(item, executable);
        Process.Start(startInfo);

        if (processName is null)
        {
            return;
        }

        var handle = await WaitForNewBrowserWindowAsync(processName, beforeWindows);
        if (handle == IntPtr.Zero)
        {
            return;
        }

        await PlaceWindowWithRetriesAsync(handle, item);
    }

    private static ProcessStartInfo CreateStartInfo(ReadyItem item, string? executable)
    {
        var url = NormalizeUrl(item.Url);
        if (string.IsNullOrWhiteSpace(executable))
        {
            return new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
        }

        var args = new List<string>();

        if (item.Browser is "Edge" or "Chrome" or "Brave")
        {
            args.Add("--new-window");
            args.Add($"--window-position={item.X},{item.Y}");
            args.Add($"--window-size={item.Width},{item.Height}");
            if (!string.IsNullOrWhiteSpace(item.BrowserProfile))
            {
                args.Add($"--profile-directory=\"{item.BrowserProfile}\"");
            }

            if (!string.IsNullOrWhiteSpace(item.Arguments))
            {
                args.Add(item.Arguments);
            }

            args.Add($"\"{url}\"");
        }
        else if (item.Browser == "Firefox")
        {
            args.Add("-new-window");
            if (!string.IsNullOrWhiteSpace(item.BrowserProfile))
            {
                args.Add($"-P \"{item.BrowserProfile}\"");
            }

            if (!string.IsNullOrWhiteSpace(item.Arguments))
            {
                args.Add(item.Arguments);
            }

            args.Add($"\"{url}\"");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(item.Arguments))
            {
                args.Add(item.Arguments);
            }

            args.Add($"\"{url}\"");
        }

        return new ProcessStartInfo
        {
            FileName = executable,
            Arguments = string.Join(" ", args),
            UseShellExecute = true
        };
    }

    private static string NormalizeUrl(string url)
    {
        var value = url.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) &&
            !string.IsNullOrWhiteSpace(absolute.Scheme))
        {
            return value;
        }

        if (value.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        return $"https://{value}";
    }

    private static string? ResolveBrowserPath(string browser, IReadOnlyDictionary<string, string> browserPaths)
    {
        if (browser == "Default")
        {
            return null;
        }

        return browserPaths.TryGetValue(browser, out var path) && File.Exists(path)
            ? path
            : null;
    }

    private static string? BrowserProcessName(ReadyProfile profile, string browser)
    {
        return BrowserCatalog.ProcessName(profile, browser);
    }

    private static async Task<IntPtr> WaitForNewBrowserWindowAsync(string processName, HashSet<IntPtr> beforeWindows)
    {
        for (var i = 0; i < 70; i++)
        {
            var newWindow = EnumerateBrowserWindows(processName)
                .FirstOrDefault(w => !beforeWindows.Contains(w.Handle));

            if (newWindow.Handle != IntPtr.Zero)
            {
                return newWindow.Handle;
            }

            await Task.Delay(150);
        }

        return IntPtr.Zero;
    }

    private static async Task PlaceWindowWithRetriesAsync(IntPtr handle, ReadyItem item)
    {
        for (var i = 0; i < 14; i++)
        {
            if (!IsWindow(handle))
            {
                return;
            }

            ShowWindow(handle, SW_RESTORE);
            SetWindowPos(handle, IntPtr.Zero, item.X, item.Y, item.Width, item.Height, SWP_NOZORDER | SWP_SHOWWINDOW);
            await Task.Delay(i < 4 ? 120 : 220);
        }
    }

    private static List<WindowInfo> EnumerateBrowserWindows(string processName)
    {
        var windows = new List<WindowInfo>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle))
            {
                return true;
            }

            var title = GetWindowTitle(handle);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            GetWindowThreadProcessId(handle, out var processId);
            try
            {
                using var process = Process.GetProcessById((int)processId);
                if (string.Equals(process.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
                {
                    windows.Add(new WindowInfo(handle, title));
                }
            }
            catch
            {
                // Process may exit while windows are being enumerated.
            }

            return true;
        }, IntPtr.Zero);

        return windows;
    }

    private static string GetWindowTitle(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return "";
        }

        var builder = new StringBuilder(length + 1);
        GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private readonly record struct WindowInfo(IntPtr Handle, string Title);

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr handle, int command);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
