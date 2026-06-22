using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace WEVG.Recording;

/// <summary>
/// One-time, in-app fetch of ffmpeg.exe so the user never has to install anything
/// by hand: downloads a pinned official build (gyan.dev, essentials) and drops just
/// ffmpeg.exe next to the executable. Reports progress and is fully cancelable.
/// </summary>
public static class FfmpegInstaller
{
    // Pinned official build (gyan.dev), served directly from GitHub release assets.
    private const string DownloadUrl =
        "https://github.com/GyanD/codexffmpeg/releases/download/8.1.1/ffmpeg-8.1.1-essentials_build.zip";

    /// <summary>Approximate download size, shown to the user before confirming.</summary>
    public const int ApproxMb = 104;

    /// <summary>
    /// Downloads and installs ffmpeg.exe next to the running executable, returning its path.
    /// </summary>
    public static async Task<string> InstallAsync(
        IProgress<(double Fraction, string Status)> progress, CancellationToken ct)
    {
        string destExe = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        string tmpZip = Path.Combine(Path.GetTempPath(), $"wevg_ffmpeg_{Guid.NewGuid():N}.zip");

        try
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
            using (var resp = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                long? total = resp.Content.Headers.ContentLength;

                using var src = await resp.Content.ReadAsStreamAsync(ct);
                using var dst = File.Create(tmpZip);
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                    read += n;
                    if (total is long t && t > 0)
                        progress.Report((0.95 * read / t,
                            $"Downloading FFmpeg... {read / 1048576} / {t / 1048576} MB"));
                    else
                        progress.Report((0, $"Downloading FFmpeg... {read / 1048576} MB"));
                }
            }

            progress.Report((0.97, "Extracting FFmpeg..."));
            using (var zip = ZipFile.OpenRead(tmpZip))
            {
                var entry = zip.Entries.FirstOrDefault(e =>
                    e.FullName.EndsWith("bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("ffmpeg.exe was not found inside the downloaded archive.");
                entry.ExtractToFile(destExe, overwrite: true);
            }

            progress.Report((1, "FFmpeg ready."));
            return destExe;
        }
        finally
        {
            try { if (File.Exists(tmpZip)) File.Delete(tmpZip); } catch { }
        }
    }
}
