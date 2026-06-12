using System.IO;
using System.Text.Json;

namespace WEVisualizer.Models;

/// <summary>
/// User preferences persisted between sessions in %APPDATA%\WEVisualizer\settings.json:
/// default output folder, quality choices and recording options.
/// </summary>
public class UserPrefs
{
    public string? OutputDirectory { get; set; }
    public int ResolutionIndex { get; set; } = 2; // 1920×1080
    public int FpsIndex { get; set; } = 1;        // 60
    /// <summary>-1 = auto-detect the best encoder at startup.</summary>
    public int EncoderIndex { get; set; } = -1;
    public int Quality { get; set; } = 16;
    public int AudioModeIndex { get; set; } = 0;  // AAC 320 → .mp4
    public string? PlaybackDeviceId { get; set; }
    public bool HideWindow { get; set; } = true;
    public bool CloseWindow { get; set; } = true;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WEVisualizer", "settings.json");

    public static UserPrefs Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<UserPrefs>(File.ReadAllText(FilePath)) ?? new UserPrefs();
        }
        catch { /* corrupt settings → defaults */ }
        return new UserPrefs();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* saving preferences must never break the app */ }
    }
}
