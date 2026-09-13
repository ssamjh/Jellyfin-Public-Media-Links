using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using Jellyfin.Plugin.PublicMediaLinks.Configuration;
using Jellyfin.Plugin.PublicMediaLinks.Security;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PublicMediaLinks.Api;

/// <summary>
/// The anonymous endpoints that share links point at.
/// </summary>
/// <remarks>
/// Nothing here touches user accounts or API keys. Authorisation comes entirely from the
/// token in the URL, which is signed, carries its own expiry and names exactly one item.
/// </remarks>
[ApiController]
[Route("PublicMediaLinks")]
[AllowAnonymous]
public class PublicShareController : ControllerBase
{
    private const string NotFoundHtml = """
        <!DOCTYPE html>
        <html lang="en"><head><meta charset="utf-8">
        <meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Link unavailable</title>
        <style>body{background:#101010;color:#ddd;font:16px/1.5 system-ui,sans-serif;display:grid;place-items:center;height:100vh;margin:0;text-align:center}</style>
        </head><body><div><h1>Link unavailable</h1>
        <p>This share link is invalid, has expired, or has been revoked.</p></div></body></html>
        """;

    private const string PlayerPage = """
        <!DOCTYPE html>
        <html lang="en"><head><meta charset="utf-8">
        <meta name="viewport" content="width=device-width,initial-scale=1">
        <meta name="robots" content="noindex,nofollow">
        <meta name="referrer" content="no-referrer">
        <title>{{TITLE}}</title>
        <style>
        body{background:#101010;color:#eee;font:16px/1.5 system-ui,-apple-system,sans-serif;margin:0;padding:24px}
        main{max-width:1100px;margin:0 auto}
        h1{font-size:1.25rem;font-weight:600;margin:0 0 16px}
        video,audio{width:100%;background:#000;border-radius:8px}
        .meta{color:#999;font-size:.85rem;margin-top:16px}
        .btn{display:inline-block;margin-top:12px;margin-right:8px;padding:8px 14px;background:#2a2a2a;color:#eee;border-radius:6px;text-decoration:none}
        .btn:hover{background:#3a3a3a}
        code{background:#1e1e1e;padding:2px 6px;border-radius:4px;word-break:break-all}
        </style></head>
        <body><main>
        <h1>{{TITLE}}</h1>
        <{{TAG}} id="player" controls autoplay playsinline preload="metadata" src="../s/{{TOKEN}}"></{{TAG}}>
        <div>{{DOWNLOAD}}</div>
        <p class="meta">This link expires at {{EXPIRES}}.</p>
        <p class="meta">If playback does not start, your browser cannot decode this file.
        Open one of these in VLC, mpv or any player that takes a URL:</p>
        <p class="meta">Original file:<br><code>{{STREAMURL}}</code></p>
        {{HLSBLOCK}}
        <script>
        (function () {
            // Safari and iOS play HLS natively and will handle the original container even
            // when the <video> element cannot. Everything else needs an external player,
            // so no third-party library is pulled in here.
            var hls = "{{HLSURL}}";
            var player = document.getElementById('player');
            if (hls && player && player.canPlayType('application/vnd.apple.mpegurl')) {
                player.src = hls;
            }
        })();
        </script>
        </main></body></html>
        """;

    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<PublicShareController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PublicShareController"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{TCategoryName}"/> interface.</param>
    public PublicShareController(ILibraryManager libraryManager, ILogger<PublicShareController> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Streams the shared file, with range requests enabled so clients can seek.
    /// </summary>
    /// <param name="token">The share token.</param>
    /// <response code="200">The media stream.</response>
    /// <response code="404">The link is invalid, expired or revoked.</response>
    /// <returns>The file.</returns>
    [HttpGet("s/{token}")]
    [HttpHead("s/{token}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult Stream([FromRoute] string token)
        => ServeFile(token, asAttachment: false);

    /// <summary>
    /// Downloads the shared file as an attachment.
    /// </summary>
    /// <param name="token">The share token.</param>
    /// <response code="200">The media file.</response>
    /// <response code="404">The link is invalid, expired, revoked or downloads are disabled.</response>
    /// <returns>The file.</returns>
    [HttpGet("d/{token}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult Download([FromRoute] string token)
    {
        if (Plugin.Instance?.Configuration.AllowDownload != true)
        {
            return NotFoundPage();
        }

        return ServeFile(token, asAttachment: true);
    }

    /// <summary>
    /// Serves a minimal player page for the shared item.
    /// </summary>
    /// <param name="token">The share token.</param>
    /// <response code="200">The player page.</response>
    /// <response code="404">The link is invalid, expired or revoked.</response>
    /// <returns>An HTML page.</returns>
    [HttpGet("w/{token}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult Watch([FromRoute] string token)
    {
        if (!TryResolve(token, out var item, out var config, out var parsed) || item is null || config is null)
        {
            return NotFoundPage();
        }

        NoStore();

        var encoder = HtmlEncoder.Default;
        var safeToken = encoder.Encode(token);
        var origin = $"{Request.Scheme}://{Request.Host}{Request.PathBase}".TrimEnd('/');
        var streamUrl = $"{origin}/PublicMediaLinks/s/{token}";
        var hlsUrl = config.EnableHls ? ShareUrls.BuildHls(origin, item.Id, token, config) : null;

        var isAudio = string.Equals(item.MediaType.ToString(), "Audio", StringComparison.OrdinalIgnoreCase);

        var html = PlayerPage
            .Replace("{{TITLE}}", encoder.Encode(item.Name ?? "Shared media"), StringComparison.Ordinal)
            .Replace("{{TAG}}", isAudio ? "audio" : "video", StringComparison.Ordinal)
            .Replace("{{TOKEN}}", safeToken, StringComparison.Ordinal)
            .Replace("{{STREAMURL}}", encoder.Encode(streamUrl), StringComparison.Ordinal)
            .Replace("{{HLSURL}}", encoder.Encode(hlsUrl ?? string.Empty), StringComparison.Ordinal)
            .Replace(
                "{{HLSBLOCK}}",
                hlsUrl is null
                    ? string.Empty
                    : "<p class=\"meta\">HLS playlist, for players that cannot read the original "
                      + "container:<br><code>" + encoder.Encode(hlsUrl) + "</code></p>",
                StringComparison.Ordinal)
            .Replace(
                "{{EXPIRES}}",
                parsed.ExpiresUtc.ToString("u", CultureInfo.InvariantCulture),
                StringComparison.Ordinal)
            .Replace(
                "{{DOWNLOAD}}",
                config.AllowDownload
                    ? "<a class=\"btn\" href=\"../d/" + safeToken + "\">Download file</a>"
                    : string.Empty,
                StringComparison.Ordinal);

        return Content(html, "text/html; charset=utf-8");
    }

    private ActionResult ServeFile(string token, bool asAttachment)
    {
        if (!TryResolve(token, out var item, out _, out _) || item is null)
        {
            return NotFoundPage();
        }

        var path = item.Path;

        // PhysicalFile does not follow symlinks reliably, so resolve them first.
        var resolved = System.IO.File.ResolveLinkTarget(path, returnFinalTarget: true);
        if (resolved is not null && resolved.Exists)
        {
            path = resolved.FullName;
        }

        if (!System.IO.File.Exists(path))
        {
            _logger.LogWarning("Share token resolved to a missing file for item {ItemId}", item.Id);
            return NotFoundPage();
        }

        NoStore();

        var contentType = MimeTypes.GetMimeType(path);

        if (asAttachment)
        {
            var fileName = Path.GetFileName(path).Replace("\"", string.Empty, StringComparison.Ordinal);
            return PhysicalFile(path, contentType, fileName, enableRangeProcessing: true);
        }

        return PhysicalFile(path, contentType, enableRangeProcessing: true);
    }

    /// <summary>
    /// Validates a token end to end: signature, expiry, allow list, and that the item
    /// still exists and is a shareable file.
    /// </summary>
    private bool TryResolve(
        string token,
        out BaseItem? item,
        out PluginConfiguration? config,
        out ShareToken parsed)
    {
        item = null;
        config = null;
        parsed = default;

        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return false;
        }

        config = plugin.Configuration;

        if (!TokenService.TryValidate(config.SigningKey, token, DateTime.UtcNow, out parsed))
        {
            return false;
        }

        if (!LinkManager.IsIssued(parsed))
        {
            return false;
        }

        var resolvedItem = _libraryManager.GetItemById(parsed.ItemId);
        if (resolvedItem is null
            || resolvedItem is Folder
            || !resolvedItem.IsFileProtocol
            || string.IsNullOrEmpty(resolvedItem.Path))
        {
            return false;
        }

        item = resolvedItem;
        return true;
    }

    private void NoStore()
    {
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
    }

    /// <summary>
    /// Every failure mode returns the same response so a link cannot be probed to learn
    /// whether an item exists, whether a token expired, or whether it was revoked.
    /// </summary>
    private ActionResult NotFoundPage()
    {
        NoStore();
        Response.StatusCode = StatusCodes.Status404NotFound;
        return Content(NotFoundHtml, "text/html; charset=utf-8", Encoding.UTF8);
    }
}
