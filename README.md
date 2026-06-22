# WEVG — Wallpaper Engine Visualizer Generator

**English** | [Español](README.es.md)

A small open-source Windows app that records whatever wallpaper you have running in **Wallpaper Engine** and combines it with a song (WAV or MP3) to produce a high-quality **music visualizer video** — an `.mp4` or `.mkv` that lasts exactly as long as the song.

[![Latest release](https://img.shields.io/github/v/release/IsoDev1337/WEVG?label=Download%20.exe&style=for-the-badge)](https://github.com/IsoDev1337/WEVG/releases/latest)
[![Build & Release](https://img.shields.io/github/actions/workflow/status/IsoDev1337/WEVG/release.yml?style=for-the-badge&label=build)](https://github.com/IsoDev1337/WEVG/actions)

---

## Download

Head to the [**Releases page**](https://github.com/IsoDev1337/WEVG/releases/latest), grab the latest `WEVG.exe`, and double-click it. **No installer, no .NET to install** — the runtime is bundled in.

The only thing you need is **Wallpaper Engine** (Steam), which is detected automatically. The first time you create a video, WEVG offers to **download FFmpeg for you** (one click, ~104 MB, once) and drops it next to the exe — nothing to find or set up by hand.

Requires **Windows 10 1903 (build 18362) or newer** (the capture API needs it).

---

## What it does

1. On launch it auto-detects your Wallpaper Engine install (Steam registry + `libraryfolders.vdf`) and your active wallpaper (WE's `config.json`), showing its title and preview.
2. You pick a song, press **Generate visualizer**, and watch the progress bar.
3. You get a video with perfect A/V sync: the total frame count is fixed by the song's duration, and the **original audio file is muxed in** — never re-recorded, with its exact original volume.

## Cover image

Besides the video, the **Capture cover** button saves a single high-quality PNG of the wallpaper — perfect for cover art. Choose the aspect ratio:

- **Original** — same as the selected resolution.
- **Square 1:1** — largest centered square.
- **Custom** — any `W:H` ratio (e.g. `4:5` Instagram, `16:9` banner, `3:1` wide).

It always crops the **largest area** of the chosen ratio from the center of the capture — no scaling, no black bars.

## How it works

1. **Detection** — Steam via registry → libraries from `libraryfolders.vdf` → `wallpaper_engine\wallpaper64.exe`. The active wallpaper comes from `selectedwallpapers → file` in WE's `config.json`.
2. **Hidden window** — `wallpaper64.exe -control openWallpaper -playInWindow ...` renders the wallpaper in a borderless window which is moved off-screen, so you never see it (optional — see below).
3. **Capture** — Windows Graphics Capture (DWM) on that window, at the window's real size; each BGRA frame is copied to a CPU buffer.
4. **Audio** — NAudio plays the song through the default output (that's what WE "hears", so audio-reactive wallpapers move with the music). The video gets the **original file**, losslessly.
5. **Encoding** — frames are piped into FFmpeg's stdin at a fixed 1 ms-precision cadence; if the window size differs from the target resolution, FFmpeg rescales with lanczos.

## Options

- **Playback device selector** — the song must play for audio-reactive wallpapers to react, but it doesn't have to be heard: pick an output with nothing connected (e.g. a monitor's HDMI output) and the app temporarily makes it the system default, restoring yours when done. The switch happens *before* the wallpaper window opens, which is when WE hooks its audio capture. If the wallpaper doesn't react, set Wallpaper Engine's audio device to "Default" in WE's settings.
- **Your audio keeps playing** — apps that are playing when the recording starts (Spotify, browser...) are automatically pinned to your real device (the same per-app routing as Windows' Volume mixer) so they aren't dragged to the silent output; the pins are removed when the recording ends.
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
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true
```

The single self-contained exe lands in `bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\` (~75 MB, no .NET install required). For a smaller, framework-dependent build, use `--self-contained false`.

## License

MIT — see [LICENSE](LICENSE).
