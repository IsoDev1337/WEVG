using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace WEVisualizer.Recording;

/// <summary>
/// Plays the song through the default output device. This matters: it's what
/// Wallpaper Engine "hears" so audio-reactive wallpapers move with the music.
/// The video never gets this playback — it gets the original file, losslessly.
/// </summary>
public sealed class AudioPlayer : IDisposable
{
    private readonly AudioFileReader _reader; // supports WAV and MP3
    private readonly WasapiOut _out;

    public TimeSpan Duration => _reader.TotalTime;

    public AudioPlayer(string path)
    {
        _reader = new AudioFileReader(path);
        _out = new WasapiOut(AudioClientShareMode.Shared, 200);
        _out.Init(_reader);
    }

    public void Play() => _out.Play();

    public void Stop()
    {
        try { _out.Stop(); } catch { }
    }

    public void Dispose()
    {
        try { _out.Dispose(); } catch { }
        try { _reader.Dispose(); } catch { }
    }
}
