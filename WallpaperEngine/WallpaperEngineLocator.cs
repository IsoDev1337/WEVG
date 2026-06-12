using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WEVisualizer.WallpaperEngine;

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

    /// <summary>
    /// Returns the active wallpaper's project.json. The exact config.json schema varies
    /// across WE versions, so "selectedwallpapers" → "file" is searched recursively.
    /// </summary>
    public static string? FindActiveProjectJson(WallpaperEngineInstall install)
    {
        try
        {
            if (!File.Exists(install.ConfigPath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(install.ConfigPath));
            var found = new List<string>();
            Walk(doc.RootElement, found);
            // The config points at the scene file (scene.pkg, .mp4, index.html...);
            // the project.json with title/preview/type always sits next to it.
            return found
                .Select(f => f.EndsWith("project.json", StringComparison.OrdinalIgnoreCase)
                    ? f
                    : Path.Combine(Path.GetDirectoryName(f) ?? "", "project.json"))
                .FirstOrDefault(File.Exists);
        }
        catch
        {
            return null;
        }
    }

    private static void Walk(JsonElement element, List<string> results)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.Name.Equals("selectedwallpapers", StringComparison.OrdinalIgnoreCase)
                        && prop.Value.ValueKind == JsonValueKind.Object)
                    {
                        // One entry per monitor, each holding the wallpaper file path.
                        foreach (var monitor in prop.Value.EnumerateObject())
                            if (monitor.Value.ValueKind == JsonValueKind.Object
                                && monitor.Value.TryGetProperty("file", out var file)
                                && file.ValueKind == JsonValueKind.String)
                                results.Add(file.GetString()!);
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
