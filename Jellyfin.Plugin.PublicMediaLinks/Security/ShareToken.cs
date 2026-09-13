using System;

namespace Jellyfin.Plugin.PublicMediaLinks.Security;

/// <summary>
/// The decoded contents of a share token.
/// </summary>
/// <param name="ItemId">The library item the token grants access to.</param>
/// <param name="ExpiresUtc">The UTC instant the token stops being valid.</param>
/// <param name="Nonce">The hex encoded per-link nonce.</param>
public readonly record struct ShareToken(Guid ItemId, DateTime ExpiresUtc, string Nonce);
