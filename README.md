# Background Upscaler for Jellyfin 12.1

Background Upscaler is a Jellyfin 12.1 / .NET 10 plugin that uses **Real-ESRGAN ncnn Vulkan** to improve Jellyfin **Backdrop** images while upscaling exact **1920×1080** Backdrops to **3840×2160**, with an optional toggle for exact **1280×720** Backdrops.

## Features

- Scheduled task: **Upscale exact 1080p Backdrops to 4K**
- Per-library enable/disable selection
- **Backdrop-only** processing: Primary/poster, Thumb, Logo, Banner and other image types are ignored
- Exact 1920×1080 Backdrops are always eligible and use 2× scaling
- Optional **Include 1280×720 Backdrops** toggle; when enabled those use 3× scaling to reach 3840×2160
- Real-ESRGAN 2× AI super-resolution rather than ordinary resizing
- Validates the generated image as exactly 3840×2160 before replacing the source
- Optional original backup, enabled by default for new configurations
- **Single-image test mode** to process at most one eligible Backdrop per task run
- GPU ID, tile size, model directory, model and TTA settings
- Per-image elapsed-time logging
- Repairs Jellyfin image metadata when a file is already 3840×2160 but its stored dimensions are stale

The default schedule is Sunday at 04:00 and can be changed in Jellyfin's normal Scheduled Tasks page.

## Install from the combined Noelzu plugin repository

The combined feed currently contains:

- **Anime Season Collections**
- **Background Upscaler**

Add one of these URLs in **Dashboard → Plugins → Repositories**:

```text
https://raw.githubusercontent.com/Noelzu/Jellyfin.Plugin.BackgroundUpscaler/main/manifest.json
```

or:

```text
https://raw.githubusercontent.com/Noelzu/Jellyfin.Plugin.AnimeSeasonCollections/main/manifest.json
```

Use a repository name such as **Noelzu Jellyfin Plugins**. Both URLs expose the same combined catalog, so only one is needed.

## Real-ESRGAN files

A typical host layout is:

```text
/docker/tools/realesrgan/
├── realesrgan-ncnn-vulkan
└── models/
    ├── realesr-animevideov3-x2.bin
    ├── realesr-animevideov3-x2.param
    ├── realesr-animevideov3-x3.bin
    ├── realesr-animevideov3-x3.param
    ├── realesr-animevideov3-x4.bin
    ├── realesr-animevideov3-x4.param
    ├── realesrgan-x4plus-anime.bin
    ├── realesrgan-x4plus-anime.param
    ├── realesrgan-x4plus.bin
    └── realesrgan-x4plus.param
```

Mount it into Jellyfin:

```yaml
volumes:
  - /docker/tools/realesrgan:/opt/realesrgan:ro
```

## Container requirements

The portable Real-ESRGAN binary still needs the Vulkan/OpenMP runtime inside the Jellyfin container. For the LinuxServer Jellyfin image, install:

```text
libvulkan1
mesa-vulkan-drivers
vulkan-tools
libgomp1
```

Do **not** rely on manually running `apt install` inside an already-running container: those packages disappear when the container is recreated or updated.

### Recommended: custom Jellyfin image

Create a Dockerfile, for example at `/docker/jellyfin-custom/Dockerfile`:

```dockerfile
FROM lscr.io/linuxserver/jellyfin:latest

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        libvulkan1 \
        mesa-vulkan-drivers \
        vulkan-tools \
        libgomp1 \
    && rm -rf /var/lib/apt/lists/*
```

Then use that image from your Compose/Portainer stack:

```yaml
services:
  jellyfin:
    build:
      context: /docker/jellyfin-custom
    image: jellyfin-with-vulkan:latest

    devices:
      - /dev/dri:/dev/dri

    volumes:
      - /docker/tools/realesrgan:/opt/realesrgan:ro
```

This makes the required packages part of the Docker image. They survive container recreation. A plain `apt install` command in the running container does not.

If your Portainer installation cannot build a local Dockerfile from the stack, build it once on the host:

```bash
docker build -t jellyfin-with-vulkan:latest /docker/jellyfin-custom
```

and keep only:

```yaml
image: jellyfin-with-vulkan:latest
```

in the stack.

## Recommended configuration

For anime artwork, especially on lower-power Intel iGPUs, start with:

```text
Executable:          /opt/realesrgan/realesrgan-ncnn-vulkan
Model directory:     /opt/realesrgan/models
Model:               realesr-animevideov3
Tile size:           0
GPU ID:              0
TTA:                 Off
Keep original backup: On
Include 1280×720 Backdrops: Off (enable if desired)
Single-image test mode: On while testing
```

After visually verifying several test results, disable **Single-image test mode** for a normal batch.

### Model notes

- **realesr-animevideov3** — recommended starting point for anime/illustration and lower-power GPUs.
- **realesrgan-x4plus-anime** — heavier anime model. It may be slower or unstable on some weaker/older Vulkan GPUs.
- **realesrgan-x4plus** — heavier general-purpose model.

If a model produces tiled/block-corrupted output on a GPU, stop the task and switch models before processing more images.

## Configuration

Open **Dashboard → Plugins → Background Upscaler**.

Choose the libraries that may be processed. Only Backdrop images from those libraries are considered.

The plugin deliberately ignores:

- Primary/poster images
- Thumbs
- Logos
- Banners
- every other non-Backdrop Jellyfin image type

If no libraries are selected, the task does nothing.

## Safety behavior

A file is replaced only when:

1. It is referenced by Jellyfin as an **ImageType.Backdrop**.
2. It belongs to a selected library.
3. It is a local JPG/JPEG/PNG/WebP file.
4. Its actual dimensions are exactly 1920×1080, or exactly 1280×720 when the optional 720p toggle is enabled.
5. Real-ESRGAN uses 2× for 1080p or 3× for 720p and exits successfully.
6. The output exists and is non-empty.
7. Jellyfin can decode the output.
8. The decoded output is exactly 3840×2160.

Already-4K Backdrops are skipped. With the 720p toggle disabled, every source resolution except exact 1920×1080 is skipped; with it enabled, exact 1280×720 is also accepted.

When backups are enabled, the original is preserved once as:

```text
image.ext.backgroundupscale-original.bak
```

## GPU verification

Inside the Jellyfin container:

```bash
vulkaninfo --summary
```

For an Intel iGPU, verify it appears as a physical GPU and use its GPU ID in the plugin if automatic selection is unreliable.

Check missing runtime libraries with:

```bash
ldd /opt/realesrgan/realesrgan-ncnn-vulkan | grep "not found"
```

No output means all linked libraries were found.

## Build

Requires the .NET 10 SDK.

Linux:

```bash
chmod +x build.sh
./build.sh
```

Windows:

```powershell
./build.ps1
```

The release ZIP is written as:

```text
dist/BackgroundUpscaler_12.1.0.5.zip
```

## Manual install

Extract the release ZIP into a Jellyfin plugin directory such as:

```text
/config/data/plugins/Background Upscaler_12.1.0.5/
```

Restart Jellyfin after installation.

## Compatibility

- Jellyfin Server: 12.1.x
- Target framework: net10.0
- Jellyfin API packages: 12.1.0
- Current plugin version: 12.1.0.5
