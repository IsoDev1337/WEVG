# WE Visualizer

[English](README.md) | **Español**

Miniapp open source para Windows que graba el wallpaper que tienes puesto en **Wallpaper Engine** y lo combina con una canción (WAV o MP3) para generar un **visualizer en vídeo** de alta calidad — un `.mp4` o `.mkv` que dura exactamente lo que dura la canción.

[![Latest release](https://img.shields.io/github/v/release/IsoDev1337/WE_Visualizer_Generator?label=Download%20.exe&style=for-the-badge)](https://github.com/IsoDev1337/WE_Visualizer_Generator/releases/latest)
[![Build & Release](https://img.shields.io/github/actions/workflow/status/IsoDev1337/WE_Visualizer_Generator/release.yml?style=for-the-badge&label=build)](https://github.com/IsoDev1337/WE_Visualizer_Generator/actions)

---

## Descarga

Ve a la [**página de Releases**](https://github.com/IsoDev1337/WE_Visualizer_Generator/releases/latest) y baja el último `WEVisualizer.exe`. Sin instalador.

También necesitas:

- **Windows 10 1903 (build 18362) o superior** — lo exige la API de captura.
- **Wallpaper Engine** (Steam) — se detecta automáticamente.
- **[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)** — Windows ofrece instalarlo al primer arranque si falta.
- **`ffmpeg.exe`** junto a `WEVisualizer.exe` o en el `PATH` (builds de [gyan.dev](https://www.gyan.dev/ffmpeg/builds/) o [BtbN](https://github.com/BtbN/FFmpeg-Builds/releases)). No se incluye para mantener la descarga ligera.

---

## Qué hace

1. Al abrirla detecta tu instalación de Wallpaper Engine (registro de Steam + `libraryfolders.vdf`) y tu wallpaper activo (`config.json` de WE), mostrando su título y vista previa.
2. Eliges una canción, pulsas **Generate visualizer** y ves avanzar la barra de progreso.
3. Obtienes un vídeo con sincronía A/V perfecta: el número total de frames lo fija la duración de la canción, y al vídeo se muxea **el archivo de audio original** — nunca una re-grabación, con su volumen exacto.

## Cómo funciona

1. **Detección** — Steam vía registro → bibliotecas de `libraryfolders.vdf` → `wallpaper_engine\wallpaper64.exe`. El wallpaper activo sale de `selectedwallpapers → file` en el `config.json` de WE.
2. **Ventana oculta** — `wallpaper64.exe -control openWallpaper -playInWindow ...` renderiza el wallpaper en una ventana sin bordes que se mueve fuera de la pantalla, así nunca la ves (opcional — ver abajo).
3. **Captura** — Windows Graphics Capture (DWM) sobre esa ventana, a su tamaño real; cada frame BGRA se copia a un búfer de CPU.
4. **Audio** — NAudio reproduce la canción por la salida por defecto (es lo que WE "escucha", para que los wallpapers audio-reactivos se muevan con la música). Al vídeo va el **archivo original**, sin pérdida.
5. **Codificación** — los frames entran por el stdin de FFmpeg con cadencia de precisión de 1 ms; si el tamaño de la ventana difiere de la resolución pedida, FFmpeg reescala con lanczos.

## Opciones

- **Selector de dispositivo de reproducción** — la canción debe sonar para que los wallpapers audio-reactivos reaccionen, pero no hace falta oírla: elige una salida sin nada conectado (p. ej. el HDMI del monitor) y la app la convierte temporalmente en predeterminada, restaurando la tuya al acabar. El cambio se hace *antes* de abrir la ventana del wallpaper, que es cuando WE engancha su captura de audio. Si el wallpaper no reacciona, pon el dispositivo de audio de Wallpaper Engine en "Predeterminado" en sus ajustes.
- **Ocultar la ventana del wallpaper durante la grabación** — mantiene la ventana fuera de pantalla. Si el resultado va a tirones o apenas reacciona a la música, desmárcala: algunos sistemas limitan el renderizado de ventanas fuera de pantalla.
- **Cerrar la ventana del wallpaper al terminar** — usa el comando `closeWallpaper` del propio Wallpaper Engine, así WE y tu fondo de escritorio siguen funcionando.
- **Detección automática de codificador** — al arrancar, la app prueba FFmpeg y preselecciona el mejor codificador por hardware disponible (NVENC → Quick Sync → AMF), con x264 como respaldo.

## Calidad

- **Vídeo**: x264 con CRF configurable (16 por defecto ≈ visualmente transparente; 0 = sin pérdida) o codificadores por GPU (NVENC / Quick Sync / AMF). Para 4K o 60 fps se recomienda GPU: la codificación es en tiempo real y, si no llega, el vídeo mantiene la duración correcta pero puede repetir frames (la app te avisa en vivo si ocurre).
- **Audio**: AAC 320 kbps en `.mp4`, o **sin pérdida** en `.mkv` (WAV → FLAC, MP3 → copia bit a bit del original).

## Limitaciones y notas

- **No minimices** la ventana del wallpaper durante la grabación si está visible (Windows no compone ventanas minimizadas). Puede estar tapada por otras sin problema.
- En Windows 10 puede aparecer el borde amarillo de captura alrededor de la ventana (no sale en el vídeo).
- Los wallpapers de tipo **application** no pueden abrirse en ventana; los tipos *scene*, *video* y *web* funcionan.
- Los wallpapers audio-reactivos reaccionan a lo que suena por el dispositivo de salida por defecto, así que la canción se reproduce en alto durante la grabación. Para grabar en silencio, configura un cable de audio virtual (p. ej. VB-Cable) como dispositivo por defecto.
- Respeta los derechos de los autores de los wallpapers del Workshop y de la música que utilices.

## Compilar desde el código

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

El exe queda en `bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\`. Usa `--self-contained true` para no depender del runtime instalado (el exe pesa bastante más).

## Licencia

MIT — ver [LICENSE](LICENSE).
