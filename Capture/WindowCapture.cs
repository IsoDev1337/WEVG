using System.Runtime.InteropServices;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace WEVisualizer.Capture;

/// <summary>
/// Captures a window with Windows.Graphics.Capture and always keeps a CPU copy
/// of the latest BGRA frame, ready to be sent to FFmpeg.
/// </summary>
public sealed class WindowCapture : IDisposable
{
    private readonly SharpDX.Direct3D11.Device _device;
    private readonly IDirect3DDevice _winrtDevice;
    private readonly Texture2D _staging;
    private readonly Direct3D11CaptureFramePool _framePool;
    private readonly GraphicsCaptureSession _session;
    private readonly byte[] _latest;
    private readonly object _sync = new();
    private volatile bool _hasFrame;
    private bool _disposed;

    public int Width { get; }
    public int Height { get; }
    public int Stride => Width * 4;
    public bool HasFrame => _hasFrame;

    public static bool IsSupported() => GraphicsCaptureSession.IsSupported();

    public WindowCapture(IntPtr hwnd)
    {
        // The REAL window dictates the size (it can differ from what was requested,
        // due to DPI or WE itself): capturing at any other size misaligns the pixel
        // rows and skews the image.
        var item = CaptureInterop.CreateItemForWindow(hwnd);
        Width = item.Size.Width;
        Height = item.Size.Height;
        _latest = new byte[Stride * Height];

        // BgraSupport is mandatory to interop with Windows.Graphics.Capture.
        _device = new SharpDX.Direct3D11.Device(DriverType.Hardware, DeviceCreationFlags.BgraSupport);
        _winrtDevice = CaptureInterop.CreateWinRtDevice(_device);

        // "Staging" texture: the only texture type the CPU can read (Map).
        _staging = new Texture2D(_device, new Texture2DDescription
        {
            Width = Width,
            Height = Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = SharpDX.DXGI.Format.B8G8R8A8_UNorm,
            SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            CpuAccessFlags = CpuAccessFlags.Read,
            BindFlags = BindFlags.None,
            OptionFlags = ResourceOptionFlags.None
        });

        // FreeThreaded: frames arrive on a dedicated thread, independent of WPF's dispatcher.
        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2,
            new SizeInt32 { Width = Width, Height = Height });
        _framePool.FrameArrived += OnFrameArrived;

        _session = _framePool.CreateCaptureSession(item);
        try { _session.IsCursorCaptureEnabled = false; } catch { /* needs Win10 2004+ */ }
        _session.StartCapture();
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        try
        {
            using var frame = sender.TryGetNextFrame();
            if (frame == null) return;

            lock (_sync)
            {
                if (_disposed) return;

                using var tex = CaptureInterop.GetTexture(frame.Surface);
                var ctx = _device.ImmediateContext;

                // Region copy: if the frame texture doesn't exactly match the staging
                // one (the window was resized), CopyResource would be undefined.
                var desc = tex.Description;
                int w = Math.Min(desc.Width, Width);
                int h = Math.Min(desc.Height, Height);
                ctx.CopySubresourceRegion(tex, 0, new ResourceRegion(0, 0, 0, w, h, 1), _staging, 0);

                var box = ctx.MapSubresource(_staging, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
                try
                {
                    // RowPitch may include per-row padding: copy row by row.
                    int copyBytes = Math.Min(Math.Min(Stride, box.RowPitch), w * 4);
                    for (int y = 0; y < h; y++)
                        Marshal.Copy(IntPtr.Add(box.DataPointer, y * box.RowPitch), _latest, y * Stride, copyBytes);
                }
                finally
                {
                    ctx.UnmapSubresource(_staging, 0);
                }
                _hasFrame = true;
            }
        }
        catch
        {
            // Never let an exception escape a foreign-thread callback (it would kill the process).
        }
    }

    /// <summary>Copies the latest captured frame (BGRA, Stride*Height bytes). Returns false if none yet.</summary>
    public bool TryCopyLatest(byte[] destination)
    {
        if (!_hasFrame) return false;
        lock (_sync)
        {
            System.Buffer.BlockCopy(_latest, 0, destination, 0, _latest.Length);
        }
        return true;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
        }
        try { _session.Dispose(); } catch { }
        try { _framePool.Dispose(); } catch { }
        try { _winrtDevice.Dispose(); } catch { }
        _staging.Dispose();
        _device.Dispose();
    }
}
