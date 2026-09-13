using System;
using System.ComponentModel.DataAnnotations;

namespace Jellyfin.Plugin.PublicMediaLinks.Models;

/// <summary>
/// Request body for creating a share link.
/// </summary>
public class CreateLinkRequest
{
    /// <summary>
    /// Gets or sets the library item to share.
    /// </summary>
    [Required]
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets the lifetime in hours. Falls back to the configured default when null.
    /// </summary>
    public int? TtlHours { get; set; }

    /// <summary>
    /// Gets or sets an optional note recording who the link is for.
    /// </summary>
    public string? Note { get; set; }
}
