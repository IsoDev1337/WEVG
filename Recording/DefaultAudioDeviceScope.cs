using NAudio.CoreAudioApi;
using WEVG.Native;

namespace WEVG.Recording;

/// <summary>
/// Temporarily makes another output device the system default — Wallpaper Engine
/// listens to the default device, so the song can play somewhere the user can't
/// hear (e.g. an unused HDMI/digital output) while the wallpaper still reacts.
/// Disposing restores the previous default.
/// </summary>
internal sealed class DefaultAudioDeviceScope : IDisposable
{
    private readonly string? _previousId;

    public DefaultAudioDeviceScope(string newDeviceId)
    {
        using var devices = new MMDeviceEnumerator();
        string currentId = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID;
        if (currentId == newDeviceId) return; // nothing to switch

        _previousId = currentId;
        SetDefault(newDeviceId);
    }

    public void Dispose()
    {
        if (_previousId == null) return;
        try { SetDefault(_previousId); } catch { /* best effort: never break cleanup */ }
    }

    private static void SetDefault(string deviceId)
    {
        var policy = (IPolicyConfig)new PolicyConfigClient();
        policy.SetDefaultEndpoint(deviceId, ERole.Console);
        policy.SetDefaultEndpoint(deviceId, ERole.Multimedia);
    }
}
