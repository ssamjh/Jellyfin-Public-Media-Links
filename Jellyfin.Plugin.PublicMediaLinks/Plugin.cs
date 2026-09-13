using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.PublicMediaLinks.Configuration;
using Jellyfin.Plugin.PublicMediaLinks.Security;
using Jellyfin.Plugin.PublicMediaLinks.Web;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PublicMediaLinks;

/// <summary>
/// Public Media Links: creates signed, self-expiring direct play URLs for library items
/// that can be handed to people who do not have a Jellyfin account.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{TCategoryName}"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogger<Plugin> logger)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        Logger = logger;

        // A fresh install has no key. Create one now so the first link can be signed.
        if (string.IsNullOrEmpty(Configuration.SigningKey))
        {
            Configuration.SigningKey = TokenService.GenerateSigningKey();
            SaveConfiguration();
        }

        WebInjection.Register(Id, logger);
    }

    /// <summary>
    /// Gets the plugin logger.
    /// </summary>
    public ILogger<Plugin>? Logger { get; private set; }

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "Public Media Links";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("2b9c4f61-7a3d-4e58-9d1c-0f5a6c8e2b74");

    /// <inheritdoc />
    public override string Description
        => "Share expiring, account-free direct play links to individual library items.";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                DisplayName = Name,
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Configuration.configPage.html",
                    GetType().Namespace)
            }
        ];
    }
}
