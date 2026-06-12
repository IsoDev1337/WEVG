using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using WEVisualizer.Capture;
using WEVisualizer.Models;
using WEVisualizer.Native;
using WEVisualizer.WallpaperEngine;

namespace WEVisualizer.Recording;

/// <summary>
/// Orchestrates a full recording: opens the wallpaper in a window (optionally hidden
/// off-screen), captures it, plays the song and feeds frames to FFmpeg at a fixed cadence.
/// </summary>
public sealed class RecordingSession
{
    private const string CaptureWindowTitle = "WEVisualizerCapture";

    private WallpaperEngineInstall? _we;
    private IntPtr _hwnd;
    private List<(int MonitorIndex, string ProjectJsonPath)> _desktopWallpapers = new();

    public async Task RunAsync(
        WallpaperEngineInstall we,
        string projectJsonPath,
        string audioPath,
        VisualizerSettings settings,
        string ffmpegPath,
        string outputPath,
        IProgress<(double Fraction, string Status)> progress,
        CancellationToken ct)
    {
        _we = we;
        // Snapshot what's on each monitor BEFORE touching anything, to restore it later.
        _desktopWallpapers = WallpaperEngineLocator.GetCurrentWallpapers(we);

        using var ffmpeg = new FfmpegRecorder();
        WindowCapture? capture = null;
        AudioPlayer? audio = null;
        DefaultAudioDeviceScope? audioRoute = null;
        bool succeeded = false;

        try
        {
            // 0. Route audio FIRST: Wallpaper Engine hooks its audio capture to the
            //    default device at the moment the wallpaper window is created, so the
            //    switch must already be in effect when the window opens — switching
            //    afterwards leaves the wallpaper listening to the old device (no reaction).
            if (settings.PlaybackDeviceId != null)
            {
                audioRoute = new DefaultAudioDeviceScope(settings.PlaybackDeviceId);
                await Task.Delay(300, ct); // let Windows propagate the device change
            }

            // 1. Ask Wallpaper Engine to render the wallpaper in its own window.
            progress.Report((0, "Setting up the wallpaper..."));
            RunWeCommand(we,
                $"-control openWallpaper -file \"{projectJsonPath}\" " +
                $"-playInWindow \"{CaptureWindowTitle}\" -width {settings.Width} -height {settings.Height}");

            _hwnd = await WaitForWindowAsync(CaptureWindowTitle, TimeSpan.FromSeconds(20), ct)
                   ?? throw new InvalidOperationException(
                       "Wallpaper Engine didn't open the preview window. " +
                       "Check that WE is working and that the wallpaper isn't of type 'application'.");

            // 2. Strip borders/title bar and set the exact size so only the wallpaper
            //    is captured, at the requested resolution.
            //    When hidden, the window goes OFF-SCREEN (left of the monitor): Windows
            //    Graphics Capture composes it via DWM even outside the visible area
            //    (it only fails when minimized). If the result stutters on some systems,
            //    the user can untick "hide" so the window renders fully on-screen.
            NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_STYLE,
                new IntPtr(NativeMethods.WS_POPUP | NativeMethods.WS_VISIBLE));
            int posX = settings.HideCaptureWindow ? -settings.Width - 200 : 0;
            NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, posX, 0, settings.Width, settings.Height,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED);
            await Task.Delay(400, ct); // let the render settle

            // 3. Start capturing (at the window's REAL size) and wait for the first frame.
            progress.Report((0, "Starting capture..."));
            capture = new WindowCapture(_hwnd);
            var warmup = Stopwatch.StartNew();
            while (!capture.HasFrame)
            {
                if (warmup.Elapsed > TimeSpan.FromSeconds(10))
                    throw new InvalidOperationException("Capture produced no frames (window minimized?).");
                await Task.Delay(30, ct);
            }

            // 4. The song's duration fixes the video's total frame count, so audio and
            //    video always end up exactly the same length.
            audio = new AudioPlayer(audioPath, settings.PlaybackDeviceId);
            long totalFrames = (long)Math.Ceiling(audio.Duration.TotalSeconds * settings.Fps);
            ffmpeg.Start(ffmpegPath, settings, audioPath, outputPath, capture.Width, capture.Height);

            // Bounded frame queue between the sampler (fixed cadence) and the writer
            // (FFmpeg's stdin): brief encoder hiccups no longer disturb frame timing.
            // Capacity is capped at ~64 MB so 4K doesn't eat RAM.
            int frameBytes = capture.Stride * capture.Height;
            int queueCapacity = Math.Clamp((int)(64L * 1024 * 1024 / frameBytes), 2, 8);
            using var queue = new BlockingCollection<byte[]>(queueCapacity);
            var spare = new ConcurrentBag<byte[]>(); // recycled buffers

            // 5. Audio and the video clock start at the same instant. Playback exists
            //    only so audio-reactive wallpapers can "hear" the music: the video gets
            //    the original file muxed in, never what comes out of the speakers.
            audio.Play();
            var clock = Stopwatch.StartNew();

            NativeMethods.TimeBeginPeriod(1); // 1 ms clock: no cadence micro-stutter
            using var pump = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Exception? writerError = null;

            var writer = Task.Run(() =>
            {
                Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
                try
                {
                    foreach (var buf in queue.GetConsumingEnumerable())
                    {
                        ffmpeg.WriteFrame(buf);
                        spare.Add(buf);
                    }
                }
                catch (Exception ex)
                {
                    writerError = ex;
                    pump.Cancel(); // unblock the sampler right away
                }
            });

            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        Thread.CurrentThread.Priority = ThreadPriority.Highest;
                        var token = pump.Token;
                        double frameMs = 1000.0 / settings.Fps;
                        bool behindWarned = false;

                        for (long i = 0; i < totalFrames; i++)
                        {
                            token.ThrowIfCancellationRequested();

                            if (!spare.TryTake(out var buf)) buf = new byte[frameBytes];
                            capture.TryCopyLatest(buf); // no new frame -> repeat the last one
                            queue.Add(buf, token);      // blocks only if the encoder is truly saturated

                            double aheadMs = (i + 1) * frameMs - clock.Elapsed.TotalMilliseconds;

                            if (i % settings.Fps == 0)
                            {
                                var done = TimeSpan.FromSeconds(i / (double)settings.Fps);
                                // If the encoder can't keep up the video will judder — warn.
                                if (aheadMs < -1000) behindWarned = true;
                                string warn = behindWarned
                                    ? "  ⚠ your PC can't keep up — try a GPU encoder or lower resolution/FPS"
                                    : "";
                                progress.Report(((double)i / totalFrames,
                                    $"Recording... {done:mm\\:ss} / {audio.Duration:mm\\:ss}{warn}"));
                            }

                            // Fixed cadence: sleep until the next frame's theoretical instant.
                            if (aheadMs > 1) Thread.Sleep((int)aheadMs);
                        }
                    }
                    finally
                    {
                        queue.CompleteAdding(); // always lets the writer drain and finish
                    }
                }, CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                // Either the user canceled or the writer faulted — resolved below.
            }
            finally
            {
                NativeMethods.TimeEndPeriod(1);
            }

            await writer;
            ct.ThrowIfCancellationRequested(); // user cancellation wins
            if (writerError != null)
                throw writerError as IOException != null
                    ? new InvalidOperationException("FFmpeg closed the pipe:\n" + ffmpeg.TailLog(15))
                    : writerError;

            // 6. Closing stdin makes FFmpeg finalize the container; -shortest trims to the audio.
            progress.Report((1, "Finalizing file..."));
            audio.Stop();
            int exitCode = ffmpeg.Finish(TimeSpan.FromMinutes(2));
            if (exitCode != 0)
                throw new InvalidOperationException("FFmpeg exited with an error:\n" + ffmpeg.TailLog(15));

            progress.Report((1, "Done!"));
            succeeded = true;
        }
        finally
        {
            try { audio?.Stop(); } catch { }
            audio?.Dispose();
            audioRoute?.Dispose(); // restore the user's default audio device
            capture?.Dispose();
            // Clean up the window on failure/cancel; on success, honor the user's choice.
            // Runs off the UI thread because it waits for WE to process the command.
            if (!succeeded || settings.CloseWindowWhenDone)
                await Task.Run(CloseWindowAndRestoreDesktop);
        }
    }

    /// <summary>
    /// Closes the recording window and re-applies the desktop wallpaper each monitor
    /// had before: some WE versions also stop the desktop wallpaper when a windowed
    /// one is closed, which would leave the user staring at a plain background.
    /// </summary>
    private void CloseWindowAndRestoreDesktop()
    {
        if (_we == null) return;

        // 1) WE's own command for the named window.
        try { RunWeCommand(_we, $"-control closeWallpaper -playInWindow \"{CaptureWindowTitle}\""); } catch { }

        // 2) If it survived, close just that window (precise target, nothing else).
        for (int i = 0; i < 15 && _hwnd != IntPtr.Zero && NativeMethods.IsWindow(_hwnd); i++)
            Thread.Sleep(100);
        if (_hwnd != IntPtr.Zero && NativeMethods.IsWindow(_hwnd))
            try { NativeMethods.PostMessage(_hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); } catch { }

        // 3) Put back what each monitor had, so the desktop ends up exactly as it was.
        foreach (var (monitor, project) in _desktopWallpapers)
            try { RunWeCommand(_we, $"-control openWallpaper -file \"{project}\" -monitor {monitor}"); } catch { }
    }

    private static void RunWeCommand(WallpaperEngineInstall we, string arguments)
    {
        using var p = Process.Start(new ProcessStartInfo(we.ExePath, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    private static async Task<IntPtr?> WaitForWindowAsync(string title, TimeSpan timeout, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            IntPtr h = NativeMethods.FindWindow(null, title);
            if (h != IntPtr.Zero) return h;
            await Task.Delay(150, ct);
        }
        return null;
    }
}
