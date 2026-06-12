using System.Diagnostics;
using System.IO;
using System.Text;
using WEVisualizer.Models;

namespace WEVisualizer.Recording;

/// <summary>
/// Launches FFmpeg and feeds it raw BGRA frames over stdin. The audio enters as a
/// second input straight from the user's original file.
/// </summary>
public sealed class FfmpegRecorder : IDisposable
{
    private Process? _process;
    private Stream? _stdin;
    private readonly StringBuilder _log = new();

    /// <summary>Looks for ffmpeg.exe next to the executable, then on PATH.</summary>
    public static string? FindFfmpeg()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(local)) return local;

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* invalid PATH entries */ }
        }
        return null;
    }

    /// <summary>
    /// Probes which hardware encoder actually works on this machine (drivers and GPU
    /// vary), by encoding a couple of synthetic frames. GPU encoders are preferred:
    /// they keep up with real-time 1080p/60+ effortlessly, which CPU x264 may not.
    /// </summary>
    public static async Task<VideoEncoder> DetectBestEncoderAsync(string ffmpegPath)
    {
        foreach (var (encoder, codec) in new[]
        {
            (VideoEncoder.Nvenc, "h264_nvenc"),
            (VideoEncoder.Qsv, "h264_qsv"),
            (VideoEncoder.Amf, "h264_amf")
        })
        {
            if (await CanEncodeAsync(ffmpegPath, codec)) return encoder;
        }
        return VideoEncoder.X264; // always available
    }

    private static async Task<bool> CanEncodeAsync(string ffmpegPath, string codec)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(ffmpegPath,
                $"-hide_banner -loglevel error -f lavfi -i color=black:s=256x256:r=30:d=0.1 " +
                $"-c:v {codec} -frames:v 2 -f null -")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            })!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await p.WaitForExitAsync(timeout.Token);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public void Start(string ffmpegPath, VisualizerSettings s, string audioPath, string outputPath,
        int inputWidth, int inputHeight)
    {
        // Quality → encoder mapping. For all of them, lower "Quality" = better.
        string vcodec = s.Encoder switch
        {
            VideoEncoder.X264 => $"-c:v libx264 -preset fast -crf {s.Quality}",
            VideoEncoder.Nvenc => $"-c:v h264_nvenc -preset p7 -rc vbr -cq {s.Quality} -b:v 0",
            VideoEncoder.Qsv => $"-c:v h264_qsv -global_quality {Math.Max(1, s.Quality)}",
            VideoEncoder.Amf => $"-c:v h264_amf -quality quality -rc cqp -qp_i {s.Quality} -qp_p {s.Quality}",
            _ => throw new ArgumentOutOfRangeException(nameof(s.Encoder))
        };

        // Audio: AAC 320k for MP4, or lossless into MKV (WAV→FLAC; MP3→bit-exact copy).
        bool isWav = Path.GetExtension(audioPath).Equals(".wav", StringComparison.OrdinalIgnoreCase);
        string acodec = s.AudioMode == AudioMode.Lossless
            ? (isWav ? "-c:a flac -compression_level 8" : "-c:a copy")
            : "-c:a aac -b:a 320k";

        string movflags = s.ContainerExtension == ".mp4" ? "-movflags +faststart " : "";

        // Input arrives at the captured window's REAL size; if it differs from the
        // requested resolution, FFmpeg rescales (lanczos = best rescale quality).
        string scale = inputWidth != s.Width || inputHeight != s.Height
            ? $"-vf scale={s.Width}:{s.Height}:flags=lanczos "
            : "";

        string args =
            $"-y -f rawvideo -pix_fmt bgra -s {inputWidth}x{inputHeight} -framerate {s.Fps} -i pipe:0 " +
            $"-i \"{audioPath}\" -map 0:v:0 -map 1:a:0 " +
            $"{scale}{vcodec} -pix_fmt yuv420p {acodec} {movflags}-shortest \"{outputPath}\"";

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = args,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (_log) _log.AppendLine(e.Data);
        };
        _process.Start();
        _process.BeginErrorReadLine();
        _stdin = _process.StandardInput.BaseStream;
    }

    public void WriteFrame(byte[] bgra) => _stdin!.Write(bgra, 0, bgra.Length);

    /// <summary>Closes stdin (end-of-stream for FFmpeg) and waits for the container to finalize.</summary>
    public int Finish(TimeSpan timeout)
    {
        try { _stdin?.Flush(); _stdin?.Close(); } catch { }
        if (_process == null) return -1;
        if (!_process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { _process.Kill(); } catch { }
        }
        return _process.HasExited ? _process.ExitCode : -1;
    }

    /// <summary>Last lines of FFmpeg's log, for useful error messages.</summary>
    public string TailLog(int lines)
    {
        string[] all;
        lock (_log) all = _log.ToString().Split('\n');
        return string.Join('\n', all.TakeLast(lines));
    }

    public void Dispose()
    {
        try { _stdin?.Dispose(); } catch { }
        try
        {
            if (_process is { HasExited: false }) _process.Kill();
            _process?.Dispose();
        }
        catch { }
    }
}
