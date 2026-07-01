using System.IO;

namespace StreamDoumi;

public sealed record BrowserDefinition(string Id, string DisplayName, string ProcessName, string[] DefaultPaths);

public static class BrowserCatalog
{
    public static IReadOnlyList<BrowserDefinition> Browsers { get; } =
    [
        new(
            "Edge",
            "Microsoft Edge",
            "msedge",
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe")
            ]),
        new(
            "Chrome",
            "Google Chrome",
            "chrome",
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe")
            ]),
        new(
            "Brave",
            "Brave",
            "brave",
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BraveSoftware", "Brave-Browser", "Application", "brave.exe")
            ]),
        new(
            "Firefox",
            "Firefox",
            "firefox",
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Mozilla Firefox", "firefox.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Mozilla Firefox", "firefox.exe")
            ])
    ];

    public static void EnsureBrowserPaths(ReadyProfile profile)
    {
        foreach (var browser in Browsers)
        {
            if (!profile.BrowserPaths.TryGetValue(browser.Id, out var path) || string.IsNullOrWhiteSpace(path))
            {
                profile.BrowserPaths[browser.Id] = DetectPath(browser.Id) ?? "";
            }
        }

        foreach (var browser in profile.CustomBrowsers)
        {
            if (string.IsNullOrWhiteSpace(browser.Id))
            {
                browser.Id = $"Custom_{Guid.NewGuid():N}";
            }

            profile.BrowserPaths[browser.Id] = browser.Path;
        }
    }

    public static string? DetectPath(string browserId)
    {
        return Browsers
            .FirstOrDefault(browser => browser.Id == browserId)?
            .DefaultPaths
            .FirstOrDefault(File.Exists);
    }

    public static IEnumerable<BrowserDefinition> AllBrowsers(ReadyProfile profile)
    {
        foreach (var browser in Browsers)
        {
            yield return browser;
        }

        foreach (var browser in profile.CustomBrowsers)
        {
            if (string.IsNullOrWhiteSpace(browser.Name) || string.IsNullOrWhiteSpace(browser.Path))
            {
                continue;
            }

            yield return new BrowserDefinition(
                browser.Id,
                browser.Name,
                Path.GetFileNameWithoutExtension(browser.Path),
                [browser.Path]);
        }
    }

    public static string? DisplayName(ReadyProfile profile, string browserId)
    {
        return AllBrowsers(profile).FirstOrDefault(browser => browser.Id == browserId)?.DisplayName;
    }

    public static string? ProcessName(ReadyProfile profile, string browserId)
    {
        return AllBrowsers(profile).FirstOrDefault(browser => browser.Id == browserId)?.ProcessName;
    }

    public static bool IsExecutableAvailable(IReadOnlyDictionary<string, string> browserPaths, string browserId)
    {
        return browserPaths.TryGetValue(browserId, out var path) && File.Exists(path);
    }
}
