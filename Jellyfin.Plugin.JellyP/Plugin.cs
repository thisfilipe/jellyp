using JellyPConfiguration = Jellyfin.Plugin.JellyP.Configuration.PluginConfiguration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyP;

/// <summary>
/// JellyP's Jellyfin plugin entry point.
/// </summary>
public sealed class Plugin : BasePlugin<JellyPConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Jellyfin's application paths.</param>
    /// <param name="xmlSerializer">Jellyfin's XML serializer.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>Gets the active plugin instance.</summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "JellyP";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("0928e334-8971-4205-a455-f42a76527f9e");

    /// <inheritdoc />
    public override string Description => "Reduce artwork storage with verified WebP conversion and bounded rollback backups.";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages() =>
    [
        new PluginPageInfo
        {
            Name = "jellyp",
            EmbeddedResourcePath = "Jellyfin.Plugin.JellyP.Web.config.html",
        },
    ];
}
