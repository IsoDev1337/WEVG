using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using WEVG.Native;

namespace WEVG.Recording;

/// <summary>
/// Keeps the user's audio alive during recording: every app currently playing on the
/// default device gets pinned to that exact device (per-app routing, same as Windows'
/// Volume mixer) BEFORE the system default flips to the silent recording device.
/// Windows migrates the pinned streams seamlessly — music never stops. Disposing
/// removes only the pins this scope created, so user-made pins are untouched.
/// Everything is best-effort: if the API is unavailable, recording works as before.
/// </summary>
internal sealed class KeepAppsAudibleScope : IDisposable
{
    private readonly AppAudioPolicy? _policy;
    private readonly List<uint> _pinned = new();

    public KeepAppsAudibleScope()
    {
        try
        {
            _policy = AppAudioPolicy.TryCreate();
            if (_policy == null) return;

            using var devices = new MMDeviceEnumerator();
            using var current = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            uint self = (uint)Environment.ProcessId;

            var sessions = current.AudioSessionManager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (session.State != AudioSessionState.AudioSessionStateActive) continue;
                if (session.IsSystemSoundsSession) continue;

                uint pid = session.GetProcessID;
                if (pid == 0 || pid == self || _pinned.Contains(pid)) continue;
                if (_policy.HasRenderPin(pid)) continue; // the user pinned it themselves

                if (_policy.SetRenderDevice(pid, current.ID))
                    _pinned.Add(pid);
            }
        }
        catch { /* never block a recording over audio-comfort plumbing */ }
    }

    public void Dispose()
    {
        if (_policy == null) return;
        foreach (uint pid in _pinned)
            _policy.SetRenderDevice(pid, null); // back to "follow the default"
    }
}
