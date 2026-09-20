# Background Upscaler for Jellyfin 12.1

Background Upscaler is a Jellyfin 12.1 / .NET 10 plugin that uses **Real-ESRGAN ncnn Vulkan** to improve local artwork while upscaling it from **exactly 1920×1080** to **exactly 3840×2160**.

## Features

- Scheduled task: **Upscale exact 1080p artwork to 4K**
- Per-library enable/disable checkboxes
- Only processes local JPG/JPEG/PNG/WebP files whose real dimensions are exactly 1920×1080
- Uses Real-ESRGAN 2× AI super-resolution rather than ordinary resizing
- Validates 3840×2160 output before replacing the source
- Optional original backup
- GPU ID, tile size, model directory, model and TTA settings
- Repairs Jellyfin image metadata if the file was already upscaled but metadata was not saved

The default schedule is Sunday at 04:00 and can be changed in Jellyfin's Scheduled Tasks page.

## Install from the combined Noelzu plugin repository

The repository feed contains all currently published Noelzu Jellyfin plugins:

- **Anime Season Collections**
- **Background Upscaler**

Add this URL in **Dashboard → Plugins → Repositories**:

```text
https://raw.githubusercontent.com/Noelzu/Jellyfin.Plugin.BackgroundUpscaler/main/manifest.json
```

The existing Anime Season Collections manifest URL contains the same combined catalog:

```text
https://raw.githubusercontent.com/Noelzu/Jellyfin.Plugin.AnimeSeasonCollections/main/manifest.json
```

Use a repository name such as **Noelzu Jellyfin Plugins**. You only need to add one of the two URLs.

## Requirements

- Jellyfin Server 12.1.x
- `realesrgan-ncnn-vulkan` and its model files available inside the Jellyfin host/container
- Vulkan-capable GPU/driver
- Write permission for the artwork files

Example Docker mount:

```yaml
volumes:
  - /docker/tools/realesrgan:/opt/realesrgan:ro
```

Typical configuration:

```text
Executable: /opt/realesrgan/realesrgan-ncnn-vulkan
Model directory: /opt/realesrgan/models
```

## Configuration

Open **Dashboard → Plugins → Background Upscaler**.

Select the libraries that may be processed, then configure the Real-ESRGAN executable, model, model directory, tile size, GPU ID, TTA and backup behavior.

If no libraries are selected, the scheduled task does nothing.

## Safety behavior

A source is replaced only when:

1. It belongs to a selected library.
2. It is a local supported image file.
3. Its actual dimensions are exactly 1920×1080.
4. Real-ESRGAN completes successfully.
5. The output exists and is non-empty.
6. Jellyfin reads the output as exactly 3840×2160.

Already-4K images and every other source resolution are skipped.

## Build

Requires the .NET 10 SDK.

Linux:

```bash
chmod +x build.sh
./build.sh
```

Windows PowerShell:

```powershell
./build.ps1
```

The release ZIP is written as:

```text
dist/BackgroundUpscaler_12.1.0.1.zip
```

## Manual install

Extract the release ZIP into a Jellyfin plugin directory such as:

```text
/config/data/plugins/Background Upscaler_12.1.0.1/
```

Restart Jellyfin after installation.

## Compatibility

- Jellyfin Server: 12.1.x
- Target framework: net10.0
- Jellyfin API packages: 12.1.0
- Current plugin version: 12.1.0.1
