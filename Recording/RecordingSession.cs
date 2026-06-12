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
        using var ffmpeg = new FfmpegRecorder();
        WindowCapture? capture = null;
        AudioPlayer? audio = null;
        bool succeeded = false;

        try
        {
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
            audio = new AudioPlayer(audioPath);
            long totalFrames = (long)Math.Ceiling(audio.Duration.TotalSeconds * settings.Fps);
            ffmpeg.Start(ffmpegPath, settings, audioPath, outputPath, capture.Width, capture.Height);

            byte[] frame = new byte[capture.Stride * capture.Height];
            double frameMs = 1000.0 / settings.Fps;

            // 5. Audio and the video clock start at the same instant. Playback exists
            //    only so audio-reactive wallpapers can "hear" the music: the video gets
            //    the original file muxed in, never what comes out of the speakers.
            if (settings.PlayAudioDuringCapture) audio.Play();
            var clock = Stopwatch.StartNew();

            NativeMethods.TimeBeginPeriod(1); // 1 ms clock: no cadence micro-stutter
            try
            {
                await Task.Run(() =>
                {
                    bool behindWarned = false;
                    for (long i = 0; i < totalFrames; i++)
                    {
                        ct.ThrowIfCancellationRequested();

                        capture.TryCopyLatest(frame); // no new frame -> repeat the last one
                        try
                        {
                            ffmpeg.WriteFrame(frame);
                        }
                        catch (IOException)
                        {
                            throw new InvalidOperationException("FFmpeg closed the pipe:\n" + ffmpeg.TailLog(15));
                        }

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
                }, ct);
            }
            finally
            {
                NativeMethods.TimeEndPeriod(1);
            }

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
            capture?.Dispose();
            // Clean up the window on failure/cancel; on success, honor the user's choice.
            if (!succeeded || settings.CloseWindowWhenDone) CloseCaptureWindow();
        }
    }

    /// <summary>
    /// Closes only the recording window via Wallpaper Engine's own command:
    /// WE itself and the desktop wallpaper keep running untouched.
    /// </summary>
    private void CloseCaptureWindow()
    {
        if (_we == null) return;
        try { RunWeCommand(_we, $"-control closeWallpaper -playInWindow \"{CaptureWindowTitle}\""); } catch { }
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
