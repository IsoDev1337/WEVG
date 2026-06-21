using System.Runtime.InteropServices;

namespace WEVG.Native;

internal enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

/// <summary>
/// Undocumented but stable-since-Vista COM interface Windows itself uses to change
/// the default audio device (same technique as SoundSwitch/EarTrumpet). Only
/// SetDefaultEndpoint is called; the earlier slots exist to keep the vtable aligned.
/// </summary>
[ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    int GetMixFormat(IntPtr deviceId, IntPtr format);
    int GetDeviceFormat(IntPtr deviceId, int isDefault, IntPtr format);
    int ResetDeviceFormat(IntPtr deviceId);
    int SetDeviceFormat(IntPtr deviceId, IntPtr endpointFormat, IntPtr mixFormat);
    int GetProcessingPeriod(IntPtr deviceId, int isDefault, IntPtr defaultPeriod, IntPtr minimumPeriod);
    int SetProcessingPeriod(IntPtr deviceId, IntPtr period);
    int GetShareMode(IntPtr deviceId, IntPtr mode);
    int SetShareMode(IntPtr deviceId, IntPtr mode);
    int GetPropertyValue(IntPtr deviceId, int storeType, IntPtr key, IntPtr value);
    int SetPropertyValue(IntPtr deviceId, int storeType, IntPtr key, IntPtr value);
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    int SetEndpointVisibility(IntPtr deviceId, int visible);
}

[ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
internal class PolicyConfigClient
{
}
