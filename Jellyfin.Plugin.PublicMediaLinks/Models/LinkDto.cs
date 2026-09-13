using System;

namespace Jellyfin.Plugin.PublicMediaLinks.Models;

/// <summary>
/// A share link as returned to the dashboard.
/// </summary>
public class LinkDto
{
    /// <summary>
    /// Gets or sets the per-link nonce, used to revoke the link.
    /// </summary>
    public string Nonce { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the shared item id.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets the shared item's display name.
    /// </summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the UTC creation time.
    /// </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC expiry time.
    /// </summary>
    public DateTime ExpiresUtc { get; set; }

    /// <summary>
    /// Gets or sets the administrator who created the link.
    /// </summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the note recorded against the link.
    /// </summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the shareable page URL. Re-derived on demand from the signing
    /// key and the stored nonce, so no token is ever written to disk.
    /// </summary>
    public string? WatchUrl { get; set; }

    /// <summary>
    /// Gets or sets the raw stream URL for players such as VLC or mpv.
    /// </summary>
    public string? StreamUrl { get; set; }

    /// <summary>
    /// Gets or sets the download URL, when downloads are enabled.
    /// </summary>
    public string? DownloadUrl { get; set; }
}
