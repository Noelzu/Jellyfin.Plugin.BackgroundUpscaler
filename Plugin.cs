using System;
using System.Collections.Generic;
using Jellyfin.Plugin.BackgroundUpscaler.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.BackgroundUpscaler;

public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override Guid Id => Guid.Parse("4e0caa51-128e-4faa-8267-0d9d2b256a1e");
    public override string Name => "Background Upscaler";
    public override string Description => "AI-upscales exact 1920x1080 Jellyfin artwork to 3840x2160 in selected libraries.";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            DisplayName = Name,
            EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
        };
    }
}
