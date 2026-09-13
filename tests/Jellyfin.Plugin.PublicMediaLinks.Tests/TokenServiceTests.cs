using System;
using Jellyfin.Plugin.PublicMediaLinks.Security;
using Xunit;

namespace Jellyfin.Plugin.PublicMediaLinks.Tests;

public class TokenServiceTests
{
    private static readonly string _key = TokenService.GenerateSigningKey();
    private static readonly DateTime _now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static string Make(Guid item, DateTime expires, out string nonce)
    {
        nonce = TokenService.GenerateNonce();
        return TokenService.Create(_key, item, expires, nonce);
    }

    [Fact]
    public void RoundTripsItemExpiryAndNonce()
    {
        var item = Guid.NewGuid();
        var expires = _now.AddHours(3);
        var token = Make(item, expires, out var nonce);

        Assert.True(TokenService.TryValidate(_key, token, _now, out var parsed));
        Assert.Equal(item, parsed.ItemId);
        Assert.Equal(expires, parsed.ExpiresUtc);
        Assert.Equal(nonce, parsed.Nonce);
    }

    [Fact]
    public void IsDeterministic_SoUrlsCanBeRebuiltWithoutStoringTheToken()
    {
        var item = Guid.NewGuid();
        var expires = _now.AddHours(3);
        var nonce = TokenService.GenerateNonce();

        Assert.Equal(
            TokenService.Create(_key, item, expires, nonce),
            TokenService.Create(_key, item, expires, nonce));
    }

    [Fact]
    public void RejectsTokenAfterExpiry()
    {
        var token = Make(Guid.NewGuid(), _now.AddHours(3), out _);

        Assert.True(TokenService.TryValidate(_key, token, _now.AddHours(2.99), out _));
        Assert.False(TokenService.TryValidate(_key, token, _now.AddHours(3.01), out _));
    }

    [Fact]
    public void RejectsTokenSignedWithAnotherKey()
    {
        var token = Make(Guid.NewGuid(), _now.AddHours(3), out _);

        Assert.False(TokenService.TryValidate(TokenService.GenerateSigningKey(), token, _now, out _));
    }

    [Theory]
    [InlineData(0)]   // version byte
    [InlineData(5)]   // item id
    [InlineData(20)]  // expiry
    [InlineData(30)]  // nonce
    [InlineData(40)]  // mac
    public void RejectsTamperedToken(int byteIndex)
    {
        var token = Make(Guid.NewGuid(), _now.AddHours(3), out _);

        var raw = Convert.FromBase64String(
            token.Replace('-', '+').Replace('_', '/').PadRight((token.Length + 3) / 4 * 4, '='));
        raw[byteIndex] ^= 0xFF;

        var tampered = Convert.ToBase64String(raw).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.False(TokenService.TryValidate(_key, tampered, _now, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")]
    public void RejectsMalformedInputWithoutThrowing(string token)
    {
        Assert.False(TokenService.TryValidate(_key, token, _now, out _));
    }

    [Fact]
    public void RejectsWhenNoSigningKeyConfigured()
    {
        var token = Make(Guid.NewGuid(), _now.AddHours(3), out _);

        Assert.False(TokenService.TryValidate(string.Empty, token, _now, out _));
    }
}
