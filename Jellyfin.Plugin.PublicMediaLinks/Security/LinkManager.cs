using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.PublicMediaLinks.Configuration;

namespace Jellyfin.Plugin.PublicMediaLinks.Security;

/// <summary>
/// Owns the list of issued links stored in plugin configuration.
/// </summary>
/// <remarks>
/// Issued links are an allow list, not a revocation list: a token is only accepted while a
/// matching entry exists, so removing an entry kills that link and nothing else. Expired
/// entries are pruned opportunistically because the signed expiry already rejects them.
/// </remarks>
public static class LinkManager
{
    private static readonly object _lock = new object();

    /// <summary>
    /// Issues a new link for an item.
    /// </summary>
    /// <param name="itemId">The library item.</param>
    /// <param name="itemName">A display name for the dashboard.</param>
    /// <param name="ttlHours">Requested lifetime in hours.</param>
    /// <param name="createdBy">The administrator creating the link.</param>
    /// <param name="note">An optional note about who the link is for.</param>
    /// <returns>The stored entry and its token.</returns>
    public static (IssuedLink Link, string Token) Issue(
        Guid itemId,
        string itemName,
        int ttlHours,
        string createdBy,
        string note)
    {
        var plugin = Plugin.Instance ?? throw new InvalidOperationException("Plugin is not loaded.");

        lock (_lock)
        {
            var config = plugin.Configuration;
            var ttl = Math.Clamp(ttlHours, 1, Math.Max(1, config.MaxTtlHours));
            var now = DateTime.UtcNow;

            var link = new IssuedLink
            {
                Nonce = TokenService.GenerateNonce(),
                ItemId = itemId,
                ItemName = itemName,
                CreatedUtc = now,
                ExpiresUtc = now.AddHours(ttl),
                CreatedBy = createdBy,
                Note = note
            };

            var token = TokenService.Create(config.SigningKey, itemId, link.ExpiresUtc, link.Nonce);

            config.IssuedLinks.RemoveAll(l => l.ExpiresUtc <= now);
            config.IssuedLinks.Add(link);
            plugin.SaveConfiguration();

            return (link, token);
        }
    }

    /// <summary>
    /// Gets the links that have not yet expired, newest first.
    /// </summary>
    /// <returns>The active links.</returns>
    public static IReadOnlyList<IssuedLink> GetActive()
    {
        var plugin = Plugin.Instance ?? throw new InvalidOperationException("Plugin is not loaded.");
        var now = DateTime.UtcNow;

        lock (_lock)
        {
            return plugin.Configuration.IssuedLinks
                .Where(l => l.ExpiresUtc > now)
                .OrderByDescending(l => l.CreatedUtc)
                .ToList();
        }
    }

    /// <summary>
    /// Revokes a single link by nonce.
    /// </summary>
    /// <param name="nonce">The nonce of the link to revoke.</param>
    /// <returns><c>true</c> if a link was removed.</returns>
    public static bool Revoke(string nonce)
    {
        var plugin = Plugin.Instance ?? throw new InvalidOperationException("Plugin is not loaded.");

        lock (_lock)
        {
            var removed = plugin.Configuration.IssuedLinks
                .RemoveAll(l => string.Equals(l.Nonce, nonce, StringComparison.OrdinalIgnoreCase));

            if (removed > 0)
            {
                plugin.SaveConfiguration();
            }

            return removed > 0;
        }
    }

    /// <summary>
    /// Revokes every link by rotating the signing key and clearing the allow list.
    /// </summary>
    public static void RevokeAll()
    {
        var plugin = Plugin.Instance ?? throw new InvalidOperationException("Plugin is not loaded.");

        lock (_lock)
        {
            plugin.Configuration.SigningKey = TokenService.GenerateSigningKey();
            plugin.Configuration.IssuedLinks.Clear();
            plugin.SaveConfiguration();
        }
    }

    /// <summary>
    /// Checks a token against the allow list.
    /// </summary>
    /// <param name="token">The already signature-validated token.</param>
    /// <returns><c>true</c> if the link is still issued.</returns>
    public static bool IsIssued(ShareToken token)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return false;
        }

        lock (_lock)
        {
            return plugin.Configuration.IssuedLinks.Any(l =>
                string.Equals(l.Nonce, token.Nonce, StringComparison.OrdinalIgnoreCase)
                && l.ItemId.Equals(token.ItemId));
        }
    }
}
