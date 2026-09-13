using System;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PublicMediaLinks.Security;

/// <summary>
/// Lets a share token authorise Jellyfin's own HLS endpoints, so shared links can be played
/// by clients that need HLS rather than a raw file.
/// </summary>
/// <remarks>
/// This decorates Jellyfin's <see cref="IAuthorizationContext"/>. When a request carries one of
/// our tokens as <c>api_key</c> and targets a path <see cref="StreamScope"/> allows, it is
/// presented as the user who created the link. Every other request falls through to Jellyfin's
/// normal authentication untouched.
///
/// The checks below are deliberately ordered cheapest-first and all must pass:
/// the plugin is loaded, HLS is enabled, the token is authentic and unexpired, the link has not
/// been revoked, the path is in scope, the path's item matches the token's item, and the
/// creating user still exists and can still see the item.
/// </remarks>
public class ShareTokenAuthorizationContext : IAuthorizationContext
{
    private readonly IAuthorizationContext _inner;
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<ShareTokenAuthorizationContext> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShareTokenAuthorizationContext"/> class.
    /// </summary>
    /// <param name="inner">The authorization context being decorated.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{TCategoryName}"/> interface.</param>
    public ShareTokenAuthorizationContext(
        IAuthorizationContext inner,
        IUserManager userManager,
        ILibraryManager libraryManager,
        ILogger<ShareTokenAuthorizationContext> logger)
    {
        _inner = inner;
        _userManager = userManager;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<AuthorizationInfo> GetAuthorizationInfo(HttpContext requestContext)
        => TryAuthorize(requestContext?.Request) ?? _inner.GetAuthorizationInfo(requestContext!);

    /// <inheritdoc />
    public Task<AuthorizationInfo> GetAuthorizationInfo(HttpRequest requestContext)
        => TryAuthorize(requestContext) ?? _inner.GetAuthorizationInfo(requestContext);

    /// <summary>
    /// Returns null when this is not one of our tokens, meaning "not my business, carry on".
    /// </summary>
    private Task<AuthorizationInfo>? TryAuthorize(HttpRequest? request)
    {
        if (request is null)
        {
            return null;
        }

        var plugin = Plugin.Instance;
        if (plugin is null || !plugin.Configuration.EnableHls)
        {
            return null;
        }

        if (!TryGetToken(request, out var token))
        {
            return null;
        }

        if (!TokenService.TryValidate(plugin.Configuration.SigningKey, token, DateTime.UtcNow, out var parsed))
        {
            return null;
        }

        if (!LinkManager.IsIssued(parsed))
        {
            return null;
        }

        if (!StreamScope.TryGetItemId(request, out var pathItemId))
        {
            _logger.LogWarning(
                "A share link token was presented for {Path}, which share links may not access.",
                request.Path);
            return null;
        }

        // A token names exactly one item. Without this, any valid token would unlock any item.
        if (pathItemId != parsed.ItemId)
        {
            _logger.LogWarning("A share link token for one item was presented for a different item.");
            return null;
        }

        var link = LinkManager.Find(parsed.Nonce);
        if (link is null || link.UserId.Equals(default))
        {
            return null;
        }

        var user = _userManager.GetUserById(link.UserId);
        if (user is null)
        {
            // The creating account is gone, so the link dies with it.
            return null;
        }

        // Re-check library visibility as that user; their access may have changed since.
        if (_libraryManager.GetItemById<MediaBrowser.Controller.Entities.BaseItem>(parsed.ItemId, user) is null)
        {
            return null;
        }

        return Task.FromResult(new AuthorizationInfo
        {
            IsAuthenticated = true,
            User = user,
            Token = token,
            IsApiKey = false,
            DeviceId = "public-media-links-" + parsed.Nonce,
            Device = "Public Media Links",
            Client = "Public Media Links"
        });
    }

    /// <summary>
    /// Reads the token from the query string. Requires exactly one of the two spellings so a
    /// request cannot present a real key alongside a share token.
    /// </summary>
    private static bool TryGetToken(HttpRequest request, out string token)
    {
        token = string.Empty;

        var hasLegacy = request.Query.TryGetValue("api_key", out var legacy);
        var hasModern = request.Query.TryGetValue("ApiKey", out var modern);

        if (hasLegacy == hasModern)
        {
            return false;
        }

        var values = hasModern ? modern : legacy;
        if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
        {
            return false;
        }

        token = values[0]!;
        return true;
    }
}
