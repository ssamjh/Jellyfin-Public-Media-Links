using System;
using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.PublicMediaLinks.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the base64 HMAC signing key used to sign share tokens.
    /// Rotating this value invalidates every previously issued link.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the default lifetime, in hours, applied to newly created links.
    /// </summary>
    public int DefaultTtlHours { get; set; } = 3;

    /// <summary>
    /// Gets or sets the maximum lifetime, in hours, that a link may be issued for.
    /// </summary>
    public int MaxTtlHours { get; set; } = 168;

    /// <summary>
    /// Gets or sets the externally reachable base URL (for example https://jellyfin.example.com).
    /// When empty the URL of the incoming request is used instead.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether anonymous holders of a link may download
    /// the original file as an attachment in addition to streaming it.
    /// </summary>
    public bool AllowDownload { get; set; } = true;

    /// <summary>
    /// Gets or sets the links that have been issued and are still valid.
    /// A token is only honoured while its entry is present here, so deleting an
    /// entry revokes that single link immediately.
    /// </summary>
    public List<IssuedLink> IssuedLinks { get; set; } = new List<IssuedLink>();
}

/// <summary>
/// A single issued share link. Contains no secrets: the nonce is only meaningful
/// in combination with a valid HMAC signature.
/// </summary>
public class IssuedLink
{
    /// <summary>
    /// Gets or sets the random per-link nonce, hex encoded.
    /// </summary>
    public string Nonce { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the library item this link grants access to.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets a human readable label for the item, shown in the dashboard.
    /// </summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the UTC time the link was created.
    /// </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC time the link stops working.
    /// </summary>
    public DateTime ExpiresUtc { get; set; }

    /// <summary>
    /// Gets or sets the name of the administrator who created the link.
    /// </summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a free text note describing who the link was shared with.
    /// </summary>
    public string Note { get; set; } = string.Empty;
}
