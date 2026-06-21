using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WEVG.WallpaperEngine;

public record WallpaperInfo(string ProjectJsonPath, string Title, string? PreviewImagePath, string? Type);

public class WallpaperEngineInstall
{
    public required string InstallDir { get; init; }
    public required string ExePath { get; init; }
    public string ConfigPath => Path.Combine(InstallDir, "config.json");
}

public static class WallpaperEngineLocator
{
    /// <summary>Looks for wallpaper64.exe in every Steam library declared in libraryfolders.vdf.</summary>
    public static WallpaperEngineInstall? FindInstall()
    {
        foreach (var library in GetSteamLibraries())
        {
            var dir = Path.Combine(library, "steamapps", "common", "wallpaper_engine");
            foreach (var exe in new[] { "wallpaper64.exe", "wallpaper32.exe" })
            {
                var path = Path.Combine(dir, exe);
                if (File.Exists(path))
                    return new WallpaperEngineInstall { InstallDir = dir, ExePath = path };
            }
        }
        return null;
    }

    private static IEnumerable<string> GetSteamLibraries()
    {
        // Steam path from the registry (HKCU first, HKLM as fallback).
        string? steamPath =
            Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
            ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
        if (steamPath == null) yield break;

        steamPath = steamPath.Replace('/', '\\');
        yield return steamPath;

        // libraryfolders.vdf lists additional libraries; a regex over "path" is enough.
        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;

        foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
            yield return m.Groups[1].Value.Replace("\\\\", "\\");
    }

    /// <summary>Returns the active wallpaper's project.json (first monitor's).</summary>
    public static string? FindActiveProjectJson(WallpaperEngineInstall install)
        => GetCurrentWallpapers(install).FirstOrDefault().ProjectJsonPath;

    /// <summary>
    /// Current wallpaper per monitor, from WE's config.json. The schema varies across
    /// versions, so "selectedwallpapers" → "MonitorN" → "file" is searched recursively —
    /// but the "wallpaperconfigrecent" history block is skipped so only what is actually
    /// on screen right now is returned. Used to restore the desktop after recording.
    /// </summary>
    public static List<(int MonitorIndex, string ProjectJsonPath)> GetCurrentWallpapers(WallpaperEngineInstall install)
    {
        var result = new List<(int, string)>();
        try
        {
            if (!File.Exists(install.ConfigPath)) return result;
            using var doc = JsonDocument.Parse(File.ReadAllText(install.ConfigPath));
            var entries = new List<(string Monitor, string File)>();
            Walk(doc.RootElement, entries);

            foreach (var (monitor, file) in entries)
            {
                // The config points at the scene file (scene.pkg, .mp4, index.html...);
                // the project.json with title/preview/type always sits next to it.
                var project = file.EndsWith("project.json", StringComparison.OrdinalIgnoreCase)
                    ? file
                    : Path.Combine(Path.GetDirectoryName(file) ?? "", "project.json");
                if (!File.Exists(project)) continue;

                var digits = new string(monitor.Where(char.IsDigit).ToArray());
                int index = int.TryParse(digits, out var n) ? n : 0;
                if (!result.Any(r => r.Item1 == index))
                    result.Add((index, project));
            }
        }
        catch { /* unreadable config → empty list */ }
        return result;
    }

    private static void Walk(JsonElement element, List<(string Monitor, string File)> results)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    // Skip the recently-used history: it holds wallpapers that are NOT on screen.
                    if (prop.Name.Equals("wallpaperconfigrecent", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (prop.Name.Equals("selectedwallpapers", StringComparison.OrdinalIgnoreCase)
                        && prop.Value.ValueKind == JsonValueKind.Object)
                    {
                        // One entry per monitor, each holding the wallpaper file path.
                        foreach (var monitor in prop.Value.EnumerateObject())
                            if (monitor.Value.ValueKind == JsonValueKind.Object
                                && monitor.Value.TryGetProperty("file", out var file)
                                && file.ValueKind == JsonValueKind.String)
                                results.Add((monitor.Name, file.GetString()!));
                    }
                    Walk(prop.Value, results);
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) Walk(item, results);
                break;
        }
    }

    /// <summary>Reads title, type and preview image from the wallpaper's project.json.</summary>
    public static WallpaperInfo ReadProjectInfo(string projectJsonPath)
    {
        string title = Path.GetFileName(Path.GetDirectoryName(projectJsonPath)) ?? "Wallpaper";
        string? preview = null, type = null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(projectJsonPath));
            var root = doc.RootElement;
            if (root.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String)
                title = t.GetString() ?? title;
            if (root.TryGetProperty("type", out var ty) && ty.ValueKind == JsonValueKind.String)
                type = ty.GetString();
            if (root.TryGetProperty("preview", out var p) && p.ValueKind == JsonValueKind.String)
            {
                var full = Path.Combine(Path.GetDirectoryName(projectJsonPath)!, p.GetString()!);
                if (File.Exists(full)) preview = full;
            }
        }
        catch { /* the extra info is optional */ }
        return new WallpaperInfo(projectJsonPath, title, preview, type);
    }
}
