using System.Runtime.InteropServices;

namespace WEVG.Native;

/// <summary>
/// Undocumented Windows API for PER-APP default audio endpoint routing — the same
/// machinery behind Settings → Volume mixer's per-app output picker (and EarTrumpet).
/// Used to keep the user's apps playing on their real speakers while the recording
/// temporarily flips the system default to a silent device. Two interface IDs exist
/// (Windows changed it in 21H2); the vtable layout is identical.
/// </summary>
[ComImport, Guid("ab3d4648-e242-459f-b02f-541c70306324"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioPolicyConfigVariant
{
    // IInspectable
    int GetIids(out int count, out IntPtr iids);
    int GetRuntimeClassName(out IntPtr name);
    int GetTrustLevel(out int level);
    // Unused slots kept only for vtable alignment
    int U1(); int U2(); int U3(); int U4(); int U5(); int U6(); int U7(); int U8(); int U9(); int U10();
    int U11(); int U12(); int U13(); int U14(); int U15(); int U16(); int U17(); int U18(); int U19();
    int SetPersistedDefaultAudioEndpoint(uint processId, int flow, int role, IntPtr deviceIdHstring);
    int GetPersistedDefaultAudioEndpoint(uint processId, int flow, int role, out IntPtr deviceIdHstring);
    int ClearAllPersistedApplicationDefaultEndpoints();
}

[ComImport, Guid("2a59116d-6c4f-45e0-a74f-707e3fef9258"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioPolicyConfigLegacy
{
    int GetIids(out int count, out IntPtr iids);
    int GetRuntimeClassName(out IntPtr name);
    int GetTrustLevel(out int level);
    int U1(); int U2(); int U3(); int U4(); int U5(); int U6(); int U7(); int U8(); int U9(); int U10();
    int U11(); int U12(); int U13(); int U14(); int U15(); int U16(); int U17(); int U18(); int U19();
    int SetPersistedDefaultAudioEndpoint(uint processId, int flow, int role, IntPtr deviceIdHstring);
    int GetPersistedDefaultAudioEndpoint(uint processId, int flow, int role, out IntPtr deviceIdHstring);
    int ClearAllPersistedApplicationDefaultEndpoints();
}

/// <summary>Per-app default render device get/set. All methods are best-effort.</summary>
internal sealed class AppAudioPolicy
{
    private const int ERender = 0;
    private const int RoleConsole = 0;
    private const int RoleMultimedia = 1;
    private const string DeviceInterfaceRender = "{e6327cad-dcec-4949-ae8a-991e976a79d2}";

    private readonly object _factory;
    private readonly bool _isVariant;

    private AppAudioPolicy(object factory, bool isVariant)
    {
        _factory = factory;
        _isVariant = isVariant;
    }

    /// <summary>Null if the API isn't available on this Windows build.</summary>
    public static AppAudioPolicy? TryCreate()
    {
        try
        {
            IntPtr cls = Hstring.Create("Windows.Media.Internal.AudioPolicyConfig");
            try
            {
                var iid = typeof(IAudioPolicyConfigVariant).GUID;
                if (RoGetActivationFactory(cls, ref iid, out IntPtr raw) == 0)
                    return new AppAudioPolicy(Marshal.GetObjectForIUnknown(raw), isVariant: true);

                iid = typeof(IAudioPolicyConfigLegacy).GUID;
                if (RoGetActivationFactory(cls, ref iid, out raw) == 0)
                    return new AppAudioPolicy(Marshal.GetObjectForIUnknown(raw), isVariant: false);
            }
            finally
            {
                Hstring.Delete(cls);
            }
        }
        catch { }
        return null;
    }

    /// <summary>Pins a process's output to a device (or clears the pin with null).</summary>
    public bool SetRenderDevice(uint processId, string? mmDeviceId)
    {
        try
        {
            IntPtr hDev = mmDeviceId == null
                ? IntPtr.Zero
                : Hstring.Create($@"\\?\SWD#MMDEVAPI#{mmDeviceId}#{DeviceInterfaceRender}");
            try
            {
                int hr1 = Set(processId, RoleMultimedia, hDev);
                int hr2 = Set(processId, RoleConsole, hDev);
                return hr1 == 0 && hr2 == 0;
            }
            finally
            {
                Hstring.Delete(hDev);
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>True if the process already has a user-set output pin (don't touch those).</summary>
    public bool HasRenderPin(uint processId)
    {
        try
        {
            int hr = _isVariant
                ? ((IAudioPolicyConfigVariant)_factory).GetPersistedDefaultAudioEndpoint(processId, ERender, RoleMultimedia, out IntPtr h)
                : ((IAudioPolicyConfigLegacy)_factory).GetPersistedDefaultAudioEndpoint(processId, ERender, RoleMultimedia, out h);
            if (hr != 0) return false;
            bool has = Hstring.Read(h) != null;
            Hstring.Delete(h);
            return has;
        }
        catch
        {
            return false;
        }
    }

    private int Set(uint pid, int role, IntPtr hDev) => _isVariant
        ? ((IAudioPolicyConfigVariant)_factory).SetPersistedDefaultAudioEndpoint(pid, ERender, role, hDev)
        : ((IAudioPolicyConfigLegacy)_factory).SetPersistedDefaultAudioEndpoint(pid, ERender, role, hDev);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);
}

internal static class Hstring
{
    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string src, int len, out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out int len);

    public static IntPtr Create(string s)
    {
        Marshal.ThrowExceptionForHR(WindowsCreateString(s, s.Length, out IntPtr h));
        return h;
    }

    public static void Delete(IntPtr h)
    {
        if (h != IntPtr.Zero) WindowsDeleteString(h);
    }

    public static string? Read(IntPtr h)
    {
        if (h == IntPtr.Zero) return null;
        IntPtr buf = WindowsGetStringRawBuffer(h, out int len);
        return len == 0 ? null : Marshal.PtrToStringUni(buf, len);
    }
}
