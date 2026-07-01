using System.IO;
using System.Text.Json;

namespace StreamDoumi;

public static class ProfileStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static string ProfilePath =>
        Path.Combine(AppContext.BaseDirectory, "StreamDoumiSave.dat");

    public static ReadyProfile Load()
    {
        var profile = LoadSavedProfile() ?? CreateDefaultProfile();
        BrowserCatalog.EnsureBrowserPaths(profile);
        Save(profile, allowEmpty: true);
        return profile;
    }

    public static void Save(ReadyProfile profile, bool allowEmpty = false)
    {
        var json = JsonSerializer.Serialize(profile, Options);
        WriteProfile(ProfilePath, json);
    }

    public static ReadyProfile? LoadSavedProfile()
    {
        return LoadFromPath(ProfilePath);
    }

    public static ReadyProfile? LoadBestSavedProfile()
    {
        return LoadSavedProfile();
    }

    private static ReadyProfile? LoadFromPath(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ReadyProfile>(json, Options);
        }
        catch
        {
            return null;
        }
    }

    private static void WriteProfile(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Copy(tempPath, path, true);
        File.Delete(tempPath);
    }

    private static ReadyProfile CreateDefaultProfile()
    {
        var profile = new ReadyProfile();
        BrowserCatalog.EnsureBrowserPaths(profile);
        return profile;
    }
}
