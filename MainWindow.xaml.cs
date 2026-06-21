using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using WEVG.Capture;
using WEVG.Models;
using WEVG.Recording;
using WEVG.WallpaperEngine;

namespace WEVG;

public partial class MainWindow : Window
{
    private WallpaperEngineInstall? _install;
    private WallpaperInfo? _wallpaper;
    private string? _ffmpegPath;
    private CancellationTokenSource? _cts;
    private UserPrefs _prefs = new();
    private bool _applyingPrefs;     // suppresses SelectionChanged while loading prefs
    private bool _encoderUserSet;    // true once the user (or saved prefs) picked an encoder

    public MainWindow() => InitializeComponent();

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!WindowCapture.IsSupported())
        {
            WeStatusText.Text = "❌ This system doesn't support Windows Graphics Capture (requires Windows 10 1903+).";
            GenerateButton.IsEnabled = false;
            return;
        }

        PopulatePlaybackDevices();
        ApplyPrefs(UserPrefs.Load());

        _ffmpegPath = FfmpegRecorder.FindFfmpeg();
        if (_ffmpegPath == null)
            StatusText.Text = "⚠ ffmpeg.exe not found. Place it next to this executable or add it to PATH.";
        else if (!_encoderUserSet)
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
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) => SavePrefs();

    // ---- Preferences ----------------------------------------------------------

    private void PopulatePlaybackDevices()
    {
        PlaybackDeviceCombo.Items.Add(new ComboBoxItem
        {
            Content = "System default (you'll hear the song)",
            Tag = null
        });
        try
        {
            using var devices = new MMDeviceEnumerator();
            foreach (var device in devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                PlaybackDeviceCombo.Items.Add(new ComboBoxItem { Content = device.FriendlyName, Tag = device.ID });
        }
        catch { /* enumeration failure → default only */ }
        PlaybackDeviceCombo.SelectedIndex = 0;
    }

    private void ApplyPrefs(UserPrefs prefs)
    {
        _prefs = prefs;
        _applyingPrefs = true;
        try
        {
            if (prefs.OutputDirectory != null && Directory.Exists(prefs.OutputDirectory))
                OutputDirBox.Text = prefs.OutputDirectory;
            else
                OutputDirBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

            SelectIndex(ResolutionCombo, prefs.ResolutionIndex);
            SelectIndex(FpsCombo, prefs.FpsIndex);
            if (prefs.EncoderIndex >= 0)
            {
                SelectIndex(EncoderCombo, prefs.EncoderIndex);
                _encoderUserSet = true;
            }
            QualitySlider.Value = Math.Clamp(prefs.Quality, (int)QualitySlider.Minimum, (int)QualitySlider.Maximum);
            SelectIndex(AudioModeCombo, prefs.AudioModeIndex);
            HideWindowCheck.IsChecked = prefs.HideWindow;
            CloseWindowCheck.IsChecked = prefs.CloseWindow;

            SelectIndex(AspectCombo, prefs.AspectIndex);
            var parts = (prefs.CustomRatio ?? "4:5").Split(':');
            if (parts.Length == 2) { RatioWBox.Text = parts[0]; RatioHBox.Text = parts[1]; }
            UpdateCustomRatioVisibility();

            if (prefs.PlaybackDeviceId != null)
                foreach (ComboBoxItem item in PlaybackDeviceCombo.Items)
                    if (Equals(item.Tag, prefs.PlaybackDeviceId))
                    {
                        PlaybackDeviceCombo.SelectedItem = item;
                        break; // device gone since last run → stays on default
                    }
        }
        finally
        {
            _applyingPrefs = false;
        }
    }

    private static void SelectIndex(ComboBox combo, int index)
    {
        if (index >= 0 && index < combo.Items.Count) combo.SelectedIndex = index;
    }

    private void SavePrefs()
    {
        _prefs.OutputDirectory = OutputDirBox.Text;
        _prefs.ResolutionIndex = ResolutionCombo.SelectedIndex;
        _prefs.FpsIndex = FpsCombo.SelectedIndex;
        _prefs.EncoderIndex = _encoderUserSet ? EncoderCombo.SelectedIndex : -1; // -1 keeps auto-detect
        _prefs.Quality = (int)QualitySlider.Value;
        _prefs.AudioModeIndex = AudioModeCombo.SelectedIndex;
        _prefs.PlaybackDeviceId = (PlaybackDeviceCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        _prefs.HideWindow = HideWindowCheck.IsChecked == true;
        _prefs.CloseWindow = CloseWindowCheck.IsChecked == true;
        _prefs.AspectIndex = AspectCombo.SelectedIndex;
        _prefs.CustomRatio = $"{RatioWBox.Text.Trim()}:{RatioHBox.Text.Trim()}";
        _prefs.Save();
    }

    // ---- Encoder auto-detection ------------------------------------------------

    /// <summary>Probes ffmpeg for GPU encoders and preselects the best one available.</summary>
    private async Task SelectBestEncoderAsync(string ffmpegPath)
    {
        try
        {
            var best = await FfmpegRecorder.DetectBestEncoderAsync(ffmpegPath);
            if (best != VideoEncoder.X264 && _cts == null && !_encoderUserSet)
            {
                _applyingPrefs = true;
                EncoderCombo.SelectedIndex = best switch
                {
                    VideoEncoder.Nvenc => 1,
                    VideoEncoder.Qsv => 2,
                    _ => 3
                };
                _applyingPrefs = false;
            }
        }
        catch { /* probing is best-effort; x264 always works */ }
    }

    private void EncoderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_applyingPrefs && IsLoaded) _encoderUserSet = true;
    }

    // ---- Wallpaper / file pickers ----------------------------------------------

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
        var dlg = new OpenFolderDialog { Title = "Folder to save videos in (remembered as default)" };
        if (dlg.ShowDialog() == true)
        {
            OutputDirBox.Text = dlg.FolderName;
            SavePrefs(); // becomes the default export folder from now on
        }
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
            PlaybackDeviceId = (PlaybackDeviceCombo.SelectedItem as ComboBoxItem)?.Tag as string,
            HideCaptureWindow = HideWindowCheck.IsChecked == true,
            CloseWindowWhenDone = CloseWindowCheck.IsChecked == true
        };
    }

    // ---- Recording ---------------------------------------------------------------

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        // While recording, the same button acts as "Cancel".
        if (_cts != null) { _cts.Cancel(); return; }

        if (_install == null || _wallpaper == null) { ResultDialog.Show(this, "Select a wallpaper first.", "", success: false); return; }
        if (!File.Exists(AudioPathBox.Text)) { ResultDialog.Show(this, "Select a WAV or MP3 audio file.", "", success: false); return; }
        _ffmpegPath ??= FfmpegRecorder.FindFfmpeg();
        if (_ffmpegPath == null) { ResultDialog.Show(this, "ffmpeg.exe not found", "Place it next to the executable or on your PATH.", success: false); return; }

        var settings = ReadSettings();
        SavePrefs(); // remember the user's choices for next time

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
            // Preflight: confirm the chosen encoder works at this exact resolution
            // (some GPUs reject 4K, old drivers fail, etc.) — fall back to x264.
            if (settings.Encoder != VideoEncoder.X264)
            {
                StatusText.Text = "Checking encoder...";
                if (!await FfmpegRecorder.CanEncodeAsync(_ffmpegPath, settings.Encoder, settings.Width, settings.Height))
                {
                    settings.Encoder = VideoEncoder.X264;
                    StatusText.Text = $"⚠ The selected GPU encoder failed at {settings.Width}×{settings.Height} — using x264 instead.";
                }
            }

            await new RecordingSession().RunAsync(
                _install, _wallpaper.ProjectJsonPath, AudioPathBox.Text,
                settings, _ffmpegPath, outputPath, progress, _cts.Token);

            StatusText.Text = $"✔ Exported: {outputPath}";
            if (ResultDialog.Show(this, "Video exported", Path.GetFileName(outputPath), "Open folder"))
                Process.Start("explorer.exe", $"/select,\"{outputPath}\"");
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Canceled.";
            try { File.Delete(outputPath); } catch { }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error.";
            ResultDialog.Show(this, "Something went wrong", ex.Message, success: false);
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

    // ---- Cover screenshot --------------------------------------------------------

    private void AspectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateCustomRatioVisibility();

    private void UpdateCustomRatioVisibility()
    {
        if (CustomRatioPanel == null) return; // still initializing
        bool custom = (AspectCombo.SelectedItem as ComboBoxItem)?.Tag as string == "Custom";
        CustomRatioPanel.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Cover_Click(object sender, RoutedEventArgs e)
    {
        if (_cts != null) return; // a recording is in progress
        if (_install == null || _wallpaper == null) { ResultDialog.Show(this, "Select a wallpaper first.", "", success: false); return; }

        var res = ((ComboBoxItem)ResolutionCombo.SelectedItem).Tag!.ToString()!.Split('x');
        var settings = new ScreenshotSettings
        {
            Width = int.Parse(res[0]),
            Height = int.Parse(res[1]),
            Aspect = Enum.Parse<CoverAspect>(((ComboBoxItem)AspectCombo.SelectedItem).Tag!.ToString()!),
            HideCaptureWindow = HideWindowCheck.IsChecked == true,
            CloseWindowWhenDone = CloseWindowCheck.IsChecked == true,
            OutputDirectory = OutputDirBox.Text
        };

        if (settings.Aspect == CoverAspect.Custom)
        {
            if (!double.TryParse(RatioWBox.Text.Trim(), out var rw) ||
                !double.TryParse(RatioHBox.Text.Trim(), out var rh) || rw <= 0 || rh <= 0)
            {
                ResultDialog.Show(this, "Enter a valid custom ratio", "For example 4 : 5.", success: false);
                return;
            }
            settings.CustomRatioW = rw;
            settings.CustomRatioH = rh;
        }

        SavePrefs(); // remember aspect choice

        string suffix = settings.Aspect switch
        {
            CoverAspect.Square => " - cover (1x1)",
            CoverAspect.Custom => $" - cover ({settings.CustomRatioW:0.##}x{settings.CustomRatioH:0.##})",
            _ => " - cover"
        };
        string outputPath = UniquePath(Path.Combine(settings.OutputDirectory, $"{Sanitize(_wallpaper.Title)}{suffix}.png"));

        CoverButton.IsEnabled = false;
        GenerateButton.IsEnabled = false;
        SetInputsEnabled(false);
        var progress = new Progress<string>(s => StatusText.Text = s);

        try
        {
            await new RecordingSession().CaptureScreenshotAsync(
                _install, _wallpaper.ProjectJsonPath, settings, outputPath, progress, CancellationToken.None);

            StatusText.Text = $"✔ Cover saved: {outputPath}";
            if (ResultDialog.Show(this, "Cover image saved", Path.GetFileName(outputPath), "Open folder"))
                Process.Start("explorer.exe", $"/select,\"{outputPath}\"");
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error.";
            ResultDialog.Show(this, "Something went wrong", ex.Message, success: false);
        }
        finally
        {
            CoverButton.IsEnabled = true;
            GenerateButton.IsEnabled = true;
            SetInputsEnabled(true);
        }
    }

    private void SetInputsEnabled(bool enabled)
    {
        foreach (var c in new Control[] { ResolutionCombo, FpsCombo, EncoderCombo, AudioModeCombo,
                                          PlaybackDeviceCombo, AspectCombo, CoverButton })
            c.IsEnabled = enabled;
        QualitySlider.IsEnabled = enabled;
        AudioPathBox.IsEnabled = enabled;
        OutputDirBox.IsEnabled = enabled;
        HideWindowCheck.IsEnabled = enabled;
        CloseWindowCheck.IsEnabled = enabled;
        RatioWBox.IsEnabled = enabled;
        RatioHBox.IsEnabled = enabled;
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
