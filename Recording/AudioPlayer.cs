using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace WEVG.Recording;

/// <summary>
/// Plays the song through an output device (default, or a specific one chosen for
/// silent recording). This playback is what Wallpaper Engine "hears" so audio-reactive
/// wallpapers move with the music. The video never gets this playback — it gets the
/// original file, losslessly.
/// </summary>
public sealed class AudioPlayer : IDisposable
{
    private readonly AudioFileReader _reader; // supports WAV and MP3
    private readonly WasapiOut _out;

    public TimeSpan Duration => _reader.TotalTime;

    public AudioPlayer(string path, string? deviceId = null)
    {
        _reader = new AudioFileReader(path);

        MMDevice? device = null;
        if (deviceId != null)
        {
            try
            {
                using var devices = new MMDeviceEnumerator();
                device = devices.GetDevice(deviceId);
            }
            catch { /* device unplugged since the list was built → fall back to default */ }
        }

        _out = device != null
            ? new WasapiOut(device, AudioClientShareMode.Shared, true, 200)
            : new WasapiOut(AudioClientShareMode.Shared, 200);
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
