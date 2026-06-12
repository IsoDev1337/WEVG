# WE Visualizer

**English** | [Español](README.es.md)

A small open-source Windows app that records whatever wallpaper you have running in **Wallpaper Engine** and combines it with a song (WAV or MP3) to produce a high-quality **music visualizer video** — an `.mp4` or `.mkv` that lasts exactly as long as the song.

[![Latest release](https://img.shields.io/github/v/release/IsoDev1337/WE_Visualizer_Generator?label=Download%20.exe&style=for-the-badge)](https://github.com/IsoDev1337/WE_Visualizer_Generator/releases/latest)
[![Build & Release](https://img.shields.io/github/actions/workflow/status/IsoDev1337/WE_Visualizer_Generator/release.yml?style=for-the-badge&label=build)](https://github.com/IsoDev1337/WE_Visualizer_Generator/actions)

---

## Download

Head to the [**Releases page**](https://github.com/IsoDev1337/WE_Visualizer_Generator/releases/latest) and grab the latest `WEVisualizer.exe`. No installer.

You also need:

- **Windows 10 1903 (build 18362) or newer** — the capture API requires it.
- **Wallpaper Engine** (Steam) — detected automatically.
- **[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)** — Windows offers to install it on first launch if missing.
- **`ffmpeg.exe`** next to `WEVisualizer.exe` or on your `PATH` ([gyan.dev](https://www.gyan.dev/ffmpeg/builds/) or [BtbN](https://github.com/BtbN/FFmpeg-Builds/releases) builds). Not bundled to keep the download small.

---

## What it does

1. On launch it auto-detects your Wallpaper Engine install (Steam registry + `libraryfolders.vdf`) and your active wallpaper (WE's `config.json`), showing its title and preview.
2. You pick a song, press **Generate visualizer**, and watch the progress bar.
3. You get a video with perfect A/V sync: the total frame count is fixed by the song's duration, and the **original audio file is muxed in** — never re-recorded, with its exact original volume.

## How it works

1. **Detection** — Steam via registry → libraries from `libraryfolders.vdf` → `wallpaper_engine\wallpaper64.exe`. The active wallpaper comes from `selectedwallpapers → file` in WE's `config.json`.
2. **Hidden window** — `wallpaper64.exe -control openWallpaper -playInWindow ...` renders the wallpaper in a borderless window which is moved off-screen, so you never see it (optional — see below).
3. **Capture** — Windows Graphics Capture (DWM) on that window, at the window's real size; each BGRA frame is copied to a CPU buffer.
4. **Audio** — NAudio plays the song through the default output (that's what WE "hears", so audio-reactive wallpapers move with the music). The video gets the **original file**, losslessly.
5. **Encoding** — frames are piped into FFmpeg's stdin at a fixed 1 ms-precision cadence; if the window size differs from the target resolution, FFmpeg rescales with lanczos.

## Options

- **Play the song out loud while recording** — required for audio-reactive wallpapers; untick it to record non-reactive wallpapers in silence.
- **Hide the wallpaper window while recording** — keeps the recording window off-screen. If the result stutters or barely reacts to the music, untick it: some systems throttle the rendering of off-screen windows.
- **Close the wallpaper window when finished** — uses Wallpaper Engine's own `closeWallpaper` command, so WE itself and your desktop wallpaper keep running.
- **Encoder auto-detection** — on startup the app probes FFmpeg and preselects the best hardware encoder available (NVENC → Quick Sync → AMF), falling back to x264.

## Quality

- **Video**: x264 with configurable CRF (16 by default ≈ visually transparent; 0 = lossless), or GPU encoders (NVENC / Quick Sync / AMF). For 4K or 60 fps a GPU encoder is recommended: encoding happens in real time, and if the encoder can't keep up the video keeps the right duration but may repeat frames (the app warns you live when that happens).
- **Audio**: AAC 320 kbps in `.mp4`, or **lossless** in `.mkv` (WAV → FLAC, MP3 → bit-exact copy of the original).

## Limitations & notes

- **Don't minimize** the wallpaper window during recording if it's visible (Windows doesn't compose minimized windows). It can be covered by other windows without issue.
- On Windows 10 a yellow capture border may appear around the window (it doesn't show in the video).
- **application**-type wallpapers can't be opened in a window; *scene*, *video* and *web* types work.
- Audio-reactive wallpapers react to whatever plays through the default output device, so the song plays out loud while recording. To record silently anyway, set up a virtual audio cable (e.g. VB-Cable) as the default device.
- Respect the rights of Workshop wallpaper authors and of the music you use.

## Build from source

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

The exe lands in `bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\`. Use `--self-contained true` to avoid depending on the installed runtime (much bigger exe).

## License

MIT — see [LICENSE](LICENSE).
