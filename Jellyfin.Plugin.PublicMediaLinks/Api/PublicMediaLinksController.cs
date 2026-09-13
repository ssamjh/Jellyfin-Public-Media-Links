using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.PublicMediaLinks.Configuration;
using Jellyfin.Plugin.PublicMediaLinks.Models;
using Jellyfin.Plugin.PublicMediaLinks.Security;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.PublicMediaLinks.Api;

/// <summary>
/// Administrator endpoints for issuing and revoking share links.
/// </summary>
[ApiController]
[Route("PublicMediaLinks")]
[Authorize(Policy = Policies.RequiresElevation)]
public class PublicMediaLinksController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="PublicMediaLinksController"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    public PublicMediaLinksController(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    private static PluginConfiguration Config
        => Plugin.Instance?.Configuration ?? throw new InvalidOperationException("Plugin is not loaded.");

    /// <summary>
    /// Lists every link that has not yet expired.
    /// </summary>
    /// <response code="200">Links returned.</response>
    /// <returns>The active links.</returns>
    [HttpGet("Links")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<LinkDto>> GetLinks()
    {
        var config = Config;
        var baseUrl = ResolveBaseUrl(config);

        return Ok(LinkManager.GetActive()
            .Select(link => ToDto(link, config, baseUrl))
            .ToList());
    }

    /// <summary>
    /// Creates a new share link for a library item.
    /// </summary>
    /// <param name="request">The link to create.</param>
    /// <response code="200">Link created.</response>
    /// <response code="404">Item not found.</response>
    /// <response code="400">Item cannot be shared.</response>
    /// <returns>The created link.</returns>
    [HttpPost("Links")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<LinkDto> CreateLink([FromBody] CreateLinkRequest request)
    {
        var config = Config;

        var item = _libraryManager.GetItemById(request.ItemId);
        if (item is null)
        {
            return NotFound();
        }

        if (item is Folder)
        {
            return BadRequest("Folders cannot be shared, pick an individual item.");
        }

        if (!item.IsFileProtocol || string.IsNullOrEmpty(item.Path))
        {
            return BadRequest("This item is not backed by a file on disk and cannot be shared.");
        }

        var (link, token) = LinkManager.Issue(
            item.Id,
            item.Name ?? item.Id.ToString(),
            request.TtlHours ?? config.DefaultTtlHours,
            User.Identity?.Name ?? "unknown",
            GetCurrentUserId(),
            request.Note ?? string.Empty);

        return Ok(ToDto(link, config, ResolveBaseUrl(config), token));
    }

    /// <summary>
    /// Revokes a single link immediately.
    /// </summary>
    /// <param name="nonce">The nonce identifying the link.</param>
    /// <response code="204">Link revoked.</response>
    /// <response code="404">No such link.</response>
    /// <returns>A <see cref="NoContentResult"/>.</returns>
    [HttpDelete("Links/{nonce}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult RevokeLink([FromRoute] string nonce)
        => LinkManager.Revoke(nonce) ? NoContent() : NotFound();

    /// <summary>
    /// Rotates the signing key, invalidating every link that has ever been issued.
    /// </summary>
    /// <response code="204">All links revoked.</response>
    /// <returns>A <see cref="NoContentResult"/>.</returns>
    [HttpPost("RevokeAll")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult RevokeAllLinks()
    {
        LinkManager.RevokeAll();
        return NoContent();
    }

    private static LinkDto ToDto(IssuedLink link, PluginConfiguration config, string baseUrl, string? token = null)
    {
        // The token is a pure function of the key, item, expiry and nonce, so it can be
        // rebuilt for the dashboard without ever persisting it.
        token ??= TokenService.Create(config.SigningKey, link.ItemId, link.ExpiresUtc, link.Nonce);

        return new LinkDto
        {
            Nonce = link.Nonce,
            ItemId = link.ItemId,
            ItemName = link.ItemName,
            // Round-tripping through the XML config can lose DateTimeKind, which would make
            // the JSON lack a "Z" and the dashboard read these as local time.
            CreatedUtc = DateTime.SpecifyKind(link.CreatedUtc, DateTimeKind.Utc),
            ExpiresUtc = DateTime.SpecifyKind(link.ExpiresUtc, DateTimeKind.Utc),
            CreatedBy = link.CreatedBy,
            Note = link.Note,
            WatchUrl = $"{baseUrl}/PublicMediaLinks/w/{token}",
            StreamUrl = $"{baseUrl}/PublicMediaLinks/s/{token}",
            DownloadUrl = config.AllowDownload ? $"{baseUrl}/PublicMediaLinks/d/{token}" : null,
            HlsUrl = config.EnableHls ? ShareUrls.BuildHls(baseUrl, link.ItemId, token, config) : null
        };
    }

    /// <summary>
    /// Reads the calling user's id from the Jellyfin auth claim. Plugins cannot reference
    /// Jellyfin.Api, so the claim is read directly rather than via its extension method.
    /// </summary>
    private Guid GetCurrentUserId()
    {
        var value = User.FindFirst("Jellyfin-UserId")?.Value;

        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }

    private string ResolveBaseUrl(PluginConfiguration config)
    {
        if (!string.IsNullOrWhiteSpace(config.PublicBaseUrl))
        {
            return config.PublicBaseUrl.TrimEnd('/');
        }

        return $"{Request.Scheme}://{Request.Host}{Request.PathBase}".TrimEnd('/');
    }
}
