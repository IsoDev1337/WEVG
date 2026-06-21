namespace WEVG.Models;

public enum VideoEncoder { X264, Nvenc, Qsv, Amf }

public enum AudioMode
{
    /// <summary>AAC 320 kbps in .mp4 (maximum compatibility).</summary>
    Aac320,
    /// <summary>WAV → FLAC (lossless) or MP3 → bit-exact copy, in .mkv.</summary>
    Lossless
}

/// <summary>Quality and behavior options chosen in the UI.</summary>
public class VisualizerSettings
{
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int Fps { get; set; } = 60;
    public VideoEncoder Encoder { get; set; } = VideoEncoder.X264;

    /// <summary>CRF/CQ: lower = better quality (0 = lossless on x264). Default 16 ≈ visually transparent.</summary>
    public int Quality { get; set; } = 16;

    public AudioMode AudioMode { get; set; } = AudioMode.Aac320;
    public string OutputDirectory { get; set; } = "";

    /// <summary>Output device to play the song on (null = system default). The song
    /// always plays — audio-reactive wallpapers react to real playback. Picking an
    /// unused device (e.g. a monitor's HDMI output) allows silent recording: the app
    /// temporarily makes it the system default so the wallpaper still "hears" it.</summary>
    public string? PlaybackDeviceId { get; set; }

    /// <summary>Move the recording window off-screen so the user never sees it.
    /// Untick if the result stutters: some systems throttle off-screen windows.</summary>
    public bool HideCaptureWindow { get; set; } = true;

    /// <summary>Close the recording window when done (the desktop wallpaper is
    /// re-applied afterwards in case WE also stopped it).</summary>
    public bool CloseWindowWhenDone { get; set; } = true;

    /// <summary>The container follows the audio mode: MKV holds FLAC/MP3 without re-encoding.</summary>
    public string ContainerExtension => AudioMode == AudioMode.Lossless ? ".mkv" : ".mp4";
}
