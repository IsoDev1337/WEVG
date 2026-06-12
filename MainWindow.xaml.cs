using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using WEVisualizer.Capture;
using WEVisualizer.Models;
using WEVisualizer.Recording;
using WEVisualizer.WallpaperEngine;

namespace WEVisualizer;

public partial class MainWindow : Window
{
    private WallpaperEngineInstall? _install;
    private WallpaperInfo? _wallpaper;
    private string? _ffmpegPath;
    private CancellationTokenSource? _cts;

    public MainWindow() => InitializeComponent();

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!WindowCapture.IsSupported())
        {
            WeStatusText.Text = "❌ This system doesn't support Windows Graphics Capture (requires Windows 10 1903+).";
            GenerateButton.IsEnabled = false;
            return;
        }

        _ffmpegPath = FfmpegRecorder.FindFfmpeg();
        if (_ffmpegPath == null)
            StatusText.Text = "⚠ ffmpeg.exe not found. Place it next to this executable or add it to PATH.";
        else
            _ = SelectBestEncoderAsync(_ffmpegPath);

        // Auto-detection on startup: WE install and active wallpaper.
        _install = WallpaperEngineLocator.FindInstall();
        if (_install == null)
        {
            WeStatusText.Text = "❌ Wallpaper Engine not found in any Steam library.";
            GenerateButton.IsEnabled = false;
            return;
        }
        WeStatusText.Text = $"✔ Wallpaper Engine detected at {_install.InstallDir}";

        var project = WallpaperEngineLocator.FindActiveProjectJson(_install);
        if (project != null)
            SetWallpaper(WallpaperEngineLocator.ReadProjectInfo(project));
        else
            WallpaperTitleText.Text = "Couldn't detect the active wallpaper — pick it manually.";

        OutputDirBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
    }

    /// <summary>Probes ffmpeg for GPU encoders and preselects the best one available.</summary>
    private async Task SelectBestEncoderAsync(string ffmpegPath)
    {
        try
        {
            var best = await FfmpegRecorder.DetectBestEncoderAsync(ffmpegPath);
            if (best != VideoEncoder.X264 && _cts == null) // don't touch settings mid-recording
                EncoderCombo.SelectedIndex = best switch
                {
                    VideoEncoder.Nvenc => 1,
                    VideoEncoder.Qsv => 2,
                    _ => 3
                };
        }
        catch { /* probing is best-effort; x264 always works */ }
    }

    private void SetWallpaper(WallpaperInfo info)
    {
        _wallpaper = info;
        WallpaperTitleText.Text = info.Title;
        WallpaperTypeText.Text = $"Type: {info.Type ?? "unknown"}";
        if (string.Equals(info.Type, "application", StringComparison.OrdinalIgnoreCase))
            WallpaperTypeText.Text += "  ⚠ can't be opened in a window";

        PreviewImage.Source = null;
        if (info.PreviewImagePath == null) return;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // don't keep the file locked on disk
            bmp.UriSource = new Uri(info.PreviewImagePath);
            bmp.EndInit();
            PreviewImage.Source = bmp;
        }
        catch { /* the preview is optional */ }
    }

    private void BrowseWallpaper_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Wallpaper Engine project|project.json",
            Title = "Select the wallpaper's project.json"
        };
        if (GuessWorkshopDir() is string workshop) dlg.InitialDirectory = workshop;
        if (dlg.ShowDialog() == true)
            SetWallpaper(WallpaperEngineLocator.ReadProjectInfo(dlg.FileName));
    }

    /// <summary>.../steamapps/common/wallpaper_engine → .../steamapps/workshop/content/431960</summary>
    private string? GuessWorkshopDir()
    {
        if (_install == null) return null;
        var steamapps = Path.GetDirectoryName(Path.GetDirectoryName(_install.InstallDir));
        if (steamapps == null) return null;
        var dir = Path.Combine(steamapps, "workshop", "content", "431960");
        return Directory.Exists(dir) ? dir : null;
    }

    private void BrowseAudio_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Audio (*.wav;*.mp3)|*.wav;*.mp3",
            Title = "Select the song"
        };
        if (dlg.ShowDialog() == true) AudioPathBox.Text = dlg.FileName;
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Folder to save the video in" };
        if (dlg.ShowDialog() == true) OutputDirBox.Text = dlg.FolderName;
    }

    private void QualitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (QualityValueText == null) return; // XAML still initializing
        int v = (int)e.NewValue;
        string desc = v == 0 ? "lossless" : v <= 14 ? "superb" : v <= 18 ? "very high" : v <= 23 ? "high" : "medium";
        QualityValueText.Text = $"{v} — {desc}";
    }

    private VisualizerSettings ReadSettings()
    {
        var res = ((ComboBoxItem)ResolutionCombo.SelectedItem).Tag!.ToString()!.Split('x');
        return new VisualizerSettings
        {
            Width = int.Parse(res[0]),
            Height = int.Parse(res[1]),
            Fps = int.Parse(((ComboBoxItem)FpsCombo.SelectedItem).Tag!.ToString()!),
            Encoder = Enum.Parse<VideoEncoder>(((ComboBoxItem)EncoderCombo.SelectedItem).Tag!.ToString()!),
            Quality = (int)QualitySlider.Value,
            AudioMode = Enum.Parse<AudioMode>(((ComboBoxItem)AudioModeCombo.SelectedItem).Tag!.ToString()!),
            OutputDirectory = OutputDirBox.Text,
            PlayAudioDuringCapture = PlayAudioCheck.IsChecked == true,
            HideCaptureWindow = HideWindowCheck.IsChecked == true,
            CloseWindowWhenDone = CloseWindowCheck.IsChecked == true
        };
    }

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        // While recording, the same button acts as "Cancel".
        if (_cts != null) { _cts.Cancel(); return; }

        if (_install == null || _wallpaper == null) { MessageBox.Show("Select a wallpaper first."); return; }
        if (!File.Exists(AudioPathBox.Text)) { MessageBox.Show("Select a WAV or MP3 audio file."); return; }
        _ffmpegPath ??= FfmpegRecorder.FindFfmpeg();
        if (_ffmpegPath == null) { MessageBox.Show("ffmpeg.exe not found (place it next to the executable or on PATH)."); return; }

        var settings = ReadSettings();
        string outName = $"{Sanitize(_wallpaper.Title)} - {Sanitize(Path.GetFileNameWithoutExtension(AudioPathBox.Text))}{settings.ContainerExtension}";
        string outputPath = UniquePath(Path.Combine(settings.OutputDirectory, outName));

        _cts = new CancellationTokenSource();
        GenerateButton.Content = "Cancel";
        SetInputsEnabled(false);

        var progress = new Progress<(double Fraction, string Status)>(p =>
        {
            Progress.Value = p.Fraction;
            StatusText.Text = p.Status;
        });

        try
        {
            await new RecordingSession().RunAsync(
                _install, _wallpaper.ProjectJsonPath, AudioPathBox.Text,
                settings, _ffmpegPath, outputPath, progress, _cts.Token);

            StatusText.Text = $"✔ Exported: {outputPath}";
            MessageBox.Show($"The video was exported successfully:\n\n{outputPath}",
                "WE Visualizer", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Canceled.";
            try { File.Delete(outputPath); } catch { }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error.";
            MessageBox.Show(ex.Message, "WE Visualizer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            GenerateButton.Content = "Generate visualizer";
            SetInputsEnabled(true);
            Progress.Value = 0;
        }
    }

    private void SetInputsEnabled(bool enabled)
    {
        foreach (var c in new Control[] { ResolutionCombo, FpsCombo, EncoderCombo, AudioModeCombo })
            c.IsEnabled = enabled;
        QualitySlider.IsEnabled = enabled;
        AudioPathBox.IsEnabled = enabled;
        OutputDirBox.IsEnabled = enabled;
        PlayAudioCheck.IsEnabled = enabled;
        HideWindowCheck.IsEnabled = enabled;
        CloseWindowCheck.IsEnabled = enabled;
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Trim();
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }
}
