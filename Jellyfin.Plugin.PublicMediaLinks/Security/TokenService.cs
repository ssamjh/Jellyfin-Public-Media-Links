using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Jellyfin.Plugin.PublicMediaLinks.Configuration;

namespace Jellyfin.Plugin.PublicMediaLinks.Security;

/// <summary>
/// Creates and validates the signed, self-expiring tokens that appear in share URLs.
/// </summary>
/// <remarks>
/// A token is <c>base64url(payload || mac)</c> where the payload carries the item id,
/// the expiry and a random nonce, and the mac is the first 16 bytes of
/// HMAC-SHA256(signing key, payload). Because the expiry is inside the signed payload a
/// token cannot be extended by whoever holds it, and because no user credential is
/// involved the link grants access to exactly one item and nothing else.
/// </remarks>
public static class TokenService
{
    private const byte Version = 1;
    private const int GuidLength = 16;
    private const int NonceLength = 8;
    private const int MacLength = 16;
    private const int PayloadLength = 1 + GuidLength + sizeof(long) + NonceLength;
    private const int TokenLength = PayloadLength + MacLength;

    /// <summary>
    /// Generates a new random signing key, base64 encoded.
    /// </summary>
    /// <returns>The new key.</returns>
    public static string GenerateSigningKey()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// Generates a new random per-link nonce, hex encoded.
    /// </summary>
    /// <returns>The new nonce.</returns>
    public static string GenerateNonce()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(NonceLength));

    /// <summary>
    /// Builds a signed token.
    /// </summary>
    /// <param name="signingKey">The base64 signing key from <see cref="PluginConfiguration.SigningKey"/>.</param>
    /// <param name="itemId">The library item to grant access to.</param>
    /// <param name="expiresUtc">The UTC expiry.</param>
    /// <param name="nonce">The hex encoded nonce, from <see cref="GenerateNonce"/>.</param>
    /// <returns>The base64url encoded token.</returns>
    public static string Create(string signingKey, Guid itemId, DateTime expiresUtc, string nonce)
    {
        ArgumentException.ThrowIfNullOrEmpty(signingKey);

        Span<byte> token = stackalloc byte[TokenLength];
        WritePayload(token, itemId, expiresUtc, nonce);
        Sign(signingKey, token[..PayloadLength], token.Slice(PayloadLength, MacLength));

        return Base64UrlEncode(token);
    }

    /// <summary>
    /// Validates a token's signature and expiry and returns its contents.
    /// </summary>
    /// <param name="signingKey">The base64 signing key.</param>
    /// <param name="token">The token from the URL.</param>
    /// <param name="utcNow">The current UTC time.</param>
    /// <param name="result">The decoded token when validation succeeds.</param>
    /// <returns><c>true</c> if the token is authentic and unexpired.</returns>
    public static bool TryValidate(string signingKey, string? token, DateTime utcNow, out ShareToken result)
    {
        result = default;

        if (string.IsNullOrEmpty(signingKey) || string.IsNullOrEmpty(token))
        {
            return false;
        }

        Span<byte> decoded = stackalloc byte[TokenLength];
        if (!TryBase64UrlDecode(token, decoded))
        {
            return false;
        }

        if (decoded[0] != Version)
        {
            return false;
        }

        Span<byte> expected = stackalloc byte[MacLength];
        Sign(signingKey, decoded[..PayloadLength], expected);

        // Fixed-time comparison so a caller cannot brute force the mac byte by byte.
        if (!CryptographicOperations.FixedTimeEquals(expected, decoded.Slice(PayloadLength, MacLength)))
        {
            return false;
        }

        var itemId = new Guid(decoded.Slice(1, GuidLength));
        var expiresUtc = DateTime.UnixEpoch.AddSeconds(
            BinaryPrimitives.ReadInt64BigEndian(decoded.Slice(1 + GuidLength, sizeof(long))));

        if (expiresUtc <= utcNow)
        {
            return false;
        }

        var nonce = Convert.ToHexString(decoded.Slice(PayloadLength - NonceLength, NonceLength));
        result = new ShareToken(itemId, expiresUtc, nonce);
        return true;
    }

    private static void WritePayload(Span<byte> destination, Guid itemId, DateTime expiresUtc, string nonce)
    {
        var nonceBytes = Convert.FromHexString(nonce);
        if (nonceBytes.Length != NonceLength)
        {
            throw new ArgumentException("Nonce must be 8 bytes.", nameof(nonce));
        }

        destination[0] = Version;
        if (!itemId.TryWriteBytes(destination.Slice(1, GuidLength)))
        {
            throw new ArgumentException("Could not serialize item id.", nameof(itemId));
        }

        BinaryPrimitives.WriteInt64BigEndian(
            destination.Slice(1 + GuidLength, sizeof(long)),
            new DateTimeOffset(DateTime.SpecifyKind(expiresUtc, DateTimeKind.Utc)).ToUnixTimeSeconds());

        nonceBytes.CopyTo(destination.Slice(1 + GuidLength + sizeof(long), NonceLength));
    }

    private static void Sign(string signingKey, ReadOnlySpan<byte> payload, Span<byte> destination)
    {
        Span<byte> full = stackalloc byte[32];
        HMACSHA256.HashData(Convert.FromBase64String(signingKey), payload, full);
        full[..MacLength].CopyTo(destination);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryBase64UrlDecode(string value, Span<byte> destination)
    {
        // A TokenLength byte token is always exactly this many unpadded base64 characters.
        if (value.Length != (TokenLength * 4 + 2) / 3)
        {
            return false;
        }

        var padded = value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '=');
        return Convert.TryFromBase64String(padded, destination, out var written) && written == TokenLength;
    }
}
