using System;
using Jellyfin.Plugin.PublicMediaLinks.Configuration;
using Jellyfin.Plugin.PublicMediaLinks.Security;
using Xunit;

namespace Jellyfin.Plugin.PublicMediaLinks.Tests;

public class ShareUrlsTests
{
    private static readonly Guid _itemId = Guid.ParseExact("0123456789abcdef0123456789abcdef", "N");

    private static string Build(string container = "ts")
        => ShareUrls.BuildHls(
            "https://jf.example.com",
            _itemId,
            "TOKEN-value_123",
            new PluginConfiguration { HlsSegmentContainer = container });

    [Fact]
    public void PointsAtJellyfinsOwnHlsEndpoint()
        => Assert.StartsWith(
            "https://jf.example.com/Videos/0123456789abcdef0123456789abcdef/master.m3u8?",
            Build(),
            StringComparison.Ordinal);

    [Theory]
    [InlineData("videoCodec=h264")]
    [InlineData("audioCodec=aac")]
    [InlineData("enableAutoStreamCopy=true")]
    [InlineData("allowVideoStreamCopy=true")]
    [InlineData("allowAudioStreamCopy=true")]
    public void RequestsAStreamCopy(string expected)
        => Assert.Contains(expected, Build(), StringComparison.Ordinal);

    [Theory]
    // Any of these would force a re-encode even when the source already matches.
    [InlineData("videoBitRate")]
    [InlineData("audioBitRate")]
    [InlineData("maxWidth")]
    [InlineData("maxHeight")]
    [InlineData("profile")]
    [InlineData("level")]
    [InlineData("maxVideoBitDepth")]
    [InlineData("maxAudioChannels")]
    [InlineData("requireAvc")]
    public void DoesNotConstrainTheOutput(string param)
        => Assert.DoesNotContain(param, Build(), StringComparison.Ordinal);

    [Fact]
    public void CarriesTheShareTokenAsTheApiKey()
        => Assert.Contains("api_key=TOKEN-value_123", Build(), StringComparison.Ordinal);

    [Fact]
    public void EscapesTheToken()
    {
        var url = ShareUrls.BuildHls(
            "https://jf.example.com",
            _itemId,
            "a b&c=d",
            new PluginConfiguration());

        Assert.Contains("api_key=a%20b%26c%3Dd", url, StringComparison.Ordinal);
        // The escaped token must not introduce extra query parameters.
        Assert.Equal(9, url.Split('&').Length - 1);
    }

    [Fact]
    public void UsesTheConfiguredSegmentContainer()
    {
        Assert.Contains("segmentContainer=ts", Build("ts"), StringComparison.Ordinal);
        Assert.Contains("segmentContainer=mp4", Build("mp4"), StringComparison.Ordinal);
    }

    [Fact]
    public void MediaSourceIdMatchesTheItem()
        => Assert.Contains("mediaSourceId=0123456789abcdef0123456789abcdef", Build(), StringComparison.Ordinal);
}
