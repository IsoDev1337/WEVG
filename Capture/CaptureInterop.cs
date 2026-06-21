using System.Runtime.InteropServices;
using SharpDX.Direct3D11;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace WEVG.Capture;

/// <summary>COM factory that creates a GraphicsCaptureItem from a classic HWND.</summary>
[ComImport]
[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IGraphicsCaptureItemInterop
{
    IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
    IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
}

/// <summary>Access to the DXGI interface underlying a WinRT Direct3D object.</summary>
[ComImport]
[Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDirect3DDxgiInterfaceAccess
{
    IntPtr GetInterface([In] ref Guid iid);
}

internal static class CaptureInterop
{
    // IID of IGraphicsCaptureItem (not readonly: passed by ref).
    private static Guid _graphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    /// <summary>Creates the WinRT GraphicsCaptureItem from the Wallpaper Engine window's HWND.</summary>
    public static GraphicsCaptureItem CreateItemForWindow(IntPtr hwnd)
    {
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        IntPtr abi = interop.CreateForWindow(hwnd, ref _graphicsCaptureItemIid);
        try { return GraphicsCaptureItem.FromAbi(abi); }
        finally { Marshal.Release(abi); }
    }

    /// <summary>Wraps a classic D3D11 device as a WinRT IDirect3DDevice (required by the frame pool).</summary>
    public static IDirect3DDevice CreateWinRtDevice(SharpDX.Direct3D11.Device device)
    {
        using var dxgiDevice = device.QueryInterface<SharpDX.DXGI.Device>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out IntPtr abi));
        try { return MarshalInterface<IDirect3DDevice>.FromAbi(abi); }
        finally { Marshal.Release(abi); }
    }

    /// <summary>Extracts the real ID3D11Texture2D inside each captured frame's IDirect3DSurface.</summary>
    public static Texture2D GetTexture(IDirect3DSurface surface)
    {
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        Guid iid = SharpDX.Utilities.GetGuidFromType(typeof(Texture2D));
        IntPtr ptr = access.GetInterface(ref iid);
        return new Texture2D(ptr); // the ctor takes ownership of the COM reference
    }
}
