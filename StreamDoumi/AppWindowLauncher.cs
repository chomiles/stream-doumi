using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace StreamDoumi;

public static class AppWindowLauncher
{
    private const int SW_RESTORE = 9;
    private const int SW_MINIMIZE = 6;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_SHOWWINDOW = 0x0040;

    public static async Task LaunchAndPlaceAsync(ReadyItem item)
    {
        if (item.Type is not (ReadyItemType.App or ReadyItemType.Shortcut) || string.IsNullOrWhiteSpace(item.Path))
        {
            return;
        }

        if (!File.Exists(item.Path))
        {
            throw new FileNotFoundException("실행 파일을 찾을 수 없습니다.", item.Path);
        }

        var launchTarget = CreateLaunchTarget(item);
        var beforeWindows = EnumerateTopLevelWindows().Select(window => window.Handle).ToHashSet();
        var process = Process.Start(launchTarget.StartInfo);
        var handle = await WaitForWindowAsync(process, beforeWindows, launchTarget.ExpectedProcessName);

        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (item.Type == ReadyItemType.App && item.MinimizeAfterLaunch)
        {
            ShowWindow(handle, SW_MINIMIZE);
            return;
        }

        await PlaceWindowWithRetriesAsync(handle, item);
    }

    private static LaunchTarget CreateLaunchTarget(ReadyItem item)
    {
        if (item.Type == ReadyItemType.Shortcut &&
            Path.GetExtension(item.Path).Equals(".lnk", StringComparison.OrdinalIgnoreCase) &&
            ShortcutResolver.TryResolve(item.Path, out var shortcut) &&
            File.Exists(shortcut.TargetPath))
        {
            if (item.RunAsAdmin && !Path.GetExtension(shortcut.TargetPath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "이 바로가기는 실행 파일이 아니라 문서/프로젝트 파일을 가리키고 있어 관리자 권한으로 직접 실행할 수 없습니다.\n" +
                    "관리자 권한이 필요하면 실제 프로그램 exe를 앱으로 등록하고, 문서 파일 경로를 실행 인자에 넣어 주세요.");
            }

            var shortcutStartInfo = new ProcessStartInfo
            {
                FileName = shortcut.TargetPath,
                Arguments = shortcut.Arguments,
                UseShellExecute = true,
                WorkingDirectory = string.IsNullOrWhiteSpace(shortcut.WorkingDirectory)
                    ? ResolveWorkingDirectory(shortcut.TargetPath)
                    : shortcut.WorkingDirectory
            };

            if (item.RunAsAdmin)
            {
                shortcutStartInfo.Verb = "runas";
            }

            return new LaunchTarget(shortcutStartInfo, Path.GetFileNameWithoutExtension(shortcut.TargetPath));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = item.Path,
            Arguments = item.Type == ReadyItemType.App ? item.Arguments : "",
            UseShellExecute = true,
            WorkingDirectory = ResolveWorkingDirectory(item.Path)
        };

        if (item.RunAsAdmin)
        {
            startInfo.Verb = "runas";
        }

        var expectedProcessName = Path.GetExtension(item.Path).Equals(".exe", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(item.Path)
            : null;
        return new LaunchTarget(startInfo, expectedProcessName);
    }

    private static string ResolveWorkingDirectory(string path)
    {
        if (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
            Path.GetExtension(path).Equals(".url", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        return Path.GetDirectoryName(path) ?? "";
    }

    private static async Task<IntPtr> WaitForWindowAsync(Process? process, HashSet<IntPtr> beforeWindows, string? expectedProcessName)
    {
        for (var i = 0; i < 80; i++)
        {
            if (process is not null)
            {
                try
                {
                    process.Refresh();
                    if (process.MainWindowHandle != IntPtr.Zero && IsWindowVisible(process.MainWindowHandle))
                    {
                        return process.MainWindowHandle;
                    }

                    var processWindow = EnumerateTopLevelWindows()
                        .FirstOrDefault(window => window.ProcessId == process.Id);

                    if (processWindow.Handle != IntPtr.Zero)
                    {
                        return processWindow.Handle;
                    }
                }
                catch
                {
                    // Some shell-launched processes exit quickly after handing work to another process.
                }
            }

            if (!string.IsNullOrWhiteSpace(expectedProcessName))
            {
                var expectedWindow = EnumerateTopLevelWindows()
                    .FirstOrDefault(window => IsProcessName(window.ProcessId, expectedProcessName));

                if (expectedWindow.Handle != IntPtr.Zero)
                {
                    return expectedWindow.Handle;
                }
            }

            if (string.IsNullOrWhiteSpace(expectedProcessName))
            {
                var newWindow = EnumerateTopLevelWindows()
                    .FirstOrDefault(window => !beforeWindows.Contains(window.Handle));

                if (newWindow.Handle != IntPtr.Zero)
                {
                    return newWindow.Handle;
                }
            }

            await Task.Delay(200);
        }

        return IntPtr.Zero;
    }

    private static async Task PlaceWindowWithRetriesAsync(IntPtr handle, ReadyItem item)
    {
        for (var i = 0; i < 18; i++)
        {
            if (!IsWindow(handle))
            {
                return;
            }

            ShowWindow(handle, SW_RESTORE);
            SetWindowPos(handle, IntPtr.Zero, item.X, item.Y, item.Width, item.Height, SWP_NOZORDER | SWP_SHOWWINDOW);
            await Task.Delay(i < 6 ? 150 : 300);
        }
    }

    private static bool IsProcessName(int processId, string processName)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return string.Equals(process.ProcessName, processName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static List<WindowInfo> EnumerateTopLevelWindows()
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
            windows.Add(new WindowInfo(handle, (int)processId, title));
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

    private readonly record struct LaunchTarget(ProcessStartInfo StartInfo, string? ExpectedProcessName);

    private readonly record struct WindowInfo(IntPtr Handle, int ProcessId, string Title);

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

public sealed record ShortcutTarget(string TargetPath, string Arguments, string WorkingDirectory);

internal static class ShortcutResolver
{
    public static bool TryResolve(string shortcutPath, out ShortcutTarget target)
    {
        target = new ShortcutTarget("", "", "");
        try
        {
            var shellLinkObject = new ShellLink();
            var shellLink = (IShellLinkW)(object)shellLinkObject;
            ((System.Runtime.InteropServices.ComTypes.IPersistFile)(object)shellLinkObject).Load(shortcutPath, 0);

            var path = new StringBuilder(1024);
            shellLink.GetPath(path, path.Capacity, IntPtr.Zero, 0);

            var arguments = new StringBuilder(1024);
            shellLink.GetArguments(arguments, arguments.Capacity);

            var workingDirectory = new StringBuilder(1024);
            shellLink.GetWorkingDirectory(workingDirectory, workingDirectory.Capacity);

            var targetPath = path.ToString();
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return false;
            }

            target = new ShortcutTarget(targetPath, arguments.ToString(), workingDirectory.ToString());
            return true;
        }
        catch
        {
            return false;
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
