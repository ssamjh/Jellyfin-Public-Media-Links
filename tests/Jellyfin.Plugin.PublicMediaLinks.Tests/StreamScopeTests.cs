using System;
using Jellyfin.Plugin.PublicMediaLinks.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Jellyfin.Plugin.PublicMediaLinks.Tests;

/// <summary>
/// StreamScope decides which requests a share token may authorise. Anything it wrongly allows
/// becomes reachable by every link holder, so the negative cases matter more than the positive.
/// </summary>
public class StreamScopeTests
{
    private const string Id = "0123456789abcdef0123456789abcdef";

    private static readonly Guid _expected = Guid.ParseExact(Id, "N");

    private static HttpRequest Request(string path, string method = "GET", string? query = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = new PathString(path);

        if (query is not null)
        {
            context.Request.QueryString = new QueryString("?" + query);
        }

        return context.Request;
    }

    private static bool Allows(string path, string method = "GET", string? query = null)
        => StreamScope.TryGetItemId(Request(path, method, query), out _);

    // ------------------------------------------------------------------ allowed

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public void AllowsMasterPlaylist(string method)
        => Assert.True(Allows($"/Videos/{Id}/master.m3u8", method));

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public void AllowsMainPlaylist(string method)
        => Assert.True(Allows($"/Videos/{Id}/main.m3u8", method));

    [Theory]
    [InlineData("0.ts")]
    [InlineData("42.ts")]
    [InlineData("7.mp4")]
    [InlineData("-1.mp4")] // fMP4 init segment
    public void AllowsSegments(string file)
        => Assert.True(Allows($"/Videos/{Id}/hls1/main/{file}"));

    [Fact]
    public void ReturnsTheItemIdFromThePath()
    {
        Assert.True(StreamScope.TryGetItemId(Request($"/Videos/{Id}/master.m3u8"), out var itemId));
        Assert.Equal(_expected, itemId);
    }

    [Fact]
    public void AcceptsDashedGuidForm()
    {
        var dashed = _expected.ToString("D");
        Assert.True(StreamScope.TryGetItemId(Request($"/Videos/{dashed}/master.m3u8"), out var itemId));
        Assert.Equal(_expected, itemId);
    }

    // ------------------------------------------------------------------ rejected

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    [InlineData("OPTIONS")]
    public void RejectsWriteMethods(string method)
        => Assert.False(Allows($"/Videos/{Id}/master.m3u8", method));

    [Fact]
    public void RejectsHeadOnSegments()
        => Assert.False(Allows($"/Videos/{Id}/hls1/main/0.ts", "HEAD"));

    [Theory]
    // Other endpoints on the same item must stay out of reach.
    [InlineData("/Videos/{id}/stream")]
    [InlineData("/Videos/{id}/stream.mkv")]
    [InlineData("/Videos/{id}/live.m3u8")]
    [InlineData("/Videos/{id}/{id}/Subtitles/0/stream.vtt")]
    [InlineData("/Videos/{id}/Trickplay/320/0.jpg")]
    // Unrelated API surface must stay out of reach.
    [InlineData("/Users")]
    [InlineData("/Items")]
    [InlineData("/System/Info")]
    [InlineData("/Sessions")]
    [InlineData("/Library/VirtualFolders")]
    public void RejectsEverythingElse(string template)
        => Assert.False(Allows(template.Replace("{id}", Id, StringComparison.Ordinal)));

    [Fact]
    public void RejectsADifferentItemsPlaylistShape()
    {
        // Still parses as a playlist, but the caller is responsible for comparing the id.
        // Here we assert the id is surfaced so that comparison is possible.
        var other = Guid.NewGuid().ToString("N");
        Assert.True(StreamScope.TryGetItemId(Request($"/Videos/{other}/master.m3u8"), out var itemId));
        Assert.NotEqual(_expected, itemId);
    }

    [Theory]
    [InlineData("/Videos/not-a-guid/master.m3u8")]
    [InlineData("/Videos//master.m3u8")]
    [InlineData("/Videos")]
    [InlineData("/")]
    [InlineData("")]
    public void RejectsMalformedPaths(string path)
        => Assert.False(Allows(path));

    [Theory]
    [InlineData("/Videos/{id}/hls1/../../../etc/passwd")]
    [InlineData("/Videos/{id}/hls1/main/../0.ts")]
    [InlineData("/Videos/{id}/hls1/ma\\in/0.ts")]
    public void RejectsTraversalAttempts(string template)
        => Assert.False(Allows(template.Replace("{id}", Id, StringComparison.Ordinal)));

    [Theory]
    [InlineData("main;rm")]
    [InlineData("main file")]
    [InlineData("main/sub")]
    [InlineData("")]
    public void RejectsUnsafePlaylistIds(string playlistId)
        => Assert.False(Allows($"/Videos/{Id}/hls1/{playlistId}/0.ts"));

    [Theory]
    [InlineData("0.exe")]
    [InlineData("0.ts.exe")]
    [InlineData("abc.ts")]
    [InlineData("-2.mp4")]
    [InlineData(".ts")]
    [InlineData("0.")]
    [InlineData("0")]
    public void RejectsUnsafeSegmentNames(string file)
        => Assert.False(Allows($"/Videos/{Id}/hls1/main/{file}"));

    [Fact]
    public void RejectsNullRequest()
        => Assert.False(StreamScope.TryGetItemId(null!, out _));

    [Fact]
    public void QueryStringCannotWidenScope()
    {
        // A denied path stays denied regardless of what is tacked onto the query.
        Assert.False(Allows($"/Videos/{Id}/stream", query: "static=true&api_key=x"));
    }
}
