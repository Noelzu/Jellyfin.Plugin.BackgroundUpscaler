using System;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.BackgroundUpscaler.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public bool Enabled { get; set; } = true;
    public string[] SelectedLibraryIds { get; set; } = Array.Empty<string>();
    public string UpscalerExecutable { get; set; } = "/opt/realesrgan/realesrgan-ncnn-vulkan";
    public string ModelPath { get; set; } = string.Empty;
    public string ModelName { get; set; } = "realesr-animevideov3";
    public int TileSize { get; set; }
    public int GpuId { get; set; } = -1;
    public bool EnableTta { get; set; }
    public bool KeepOriginalBackup { get; set; } = true;
    public bool Include720pBackdrops { get; set; }
    public bool SingleImageTestMode { get; set; }
}
