namespace WEVisualizer.Models;

public enum CoverAspect
{
    /// <summary>Keep the captured resolution's aspect ratio (no crop).</summary>
    Original,
    /// <summary>1:1 — largest centered square.</summary>
    Square,
    /// <summary>Arbitrary W:H — largest centered rectangle of that ratio.</summary>
    Custom
}

/// <summary>Options for the single-frame cover screenshot.</summary>
public class ScreenshotSettings
{
    /// <summary>Capture size (the wallpaper is rendered at this resolution before cropping).</summary>
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;

    public CoverAspect Aspect { get; set; } = CoverAspect.Original;
    public double CustomRatioW { get; set; } = 4;
    public double CustomRatioH { get; set; } = 5;

    public bool HideCaptureWindow { get; set; } = true;
    public bool CloseWindowWhenDone { get; set; } = true;
    public string OutputDirectory { get; set; } = "";

    /// <summary>Target width/height ratio, or null for "keep original" (no crop).</summary>
    public double? TargetRatio => Aspect switch
    {
        CoverAspect.Square => 1.0,
        CoverAspect.Custom => CustomRatioH > 0 ? CustomRatioW / CustomRatioH : null,
        _ => null
    };
}
