using System;
using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.PublicMediaLinks.Security;

/// <summary>
/// Decides whether a request is one that a share token is allowed to authorise.
/// </summary>
/// <remarks>
/// This is the security boundary for HLS. Serving HLS means letting Jellyfin's own
/// <c>/Videos/...</c> endpoints handle the request, which in turn means the share token has to
/// present itself as an authenticated user. That is only safe if the set of requests a token
/// can authorise is tightly bounded, so this allows an explicit, minimal list of paths and
/// nothing else:
/// <list type="bullet">
/// <item><description><c>GET|HEAD /Videos/{itemId}/master.m3u8</c></description></item>
/// <item><description><c>GET|HEAD /Videos/{itemId}/main.m3u8</c></description></item>
/// <item><description><c>GET /Videos/{itemId}/hls1/{playlistId}/{segment}.{ts|mp4}</c></description></item>
/// </list>
/// In every case the item id in the path must match the item the token was issued for, so a
/// token for one item can never be replayed against another. Subtitle and trickplay routes are
/// deliberately excluded to keep the surface as small as possible.
/// </remarks>
public static class StreamScope
{
    /// <summary>
    /// Attempts to match a request against the allowed set.
    /// </summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="itemId">The item the matched request targets.</param>
    /// <returns><c>true</c> if a share token may authorise this request.</returns>
    public static bool TryGetItemId(HttpRequest request, out Guid itemId)
    {
        itemId = Guid.Empty;

        if (request is null)
        {
            return false;
        }

        var isHead = HttpMethods.IsHead(request.Method);
        if (!HttpMethods.IsGet(request.Method) && !isHead)
        {
            return false;
        }

        var path = request.Path.Value;
        if (string.IsNullOrEmpty(path)
            || path.Contains('\\', StringComparison.Ordinal)
            || path.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        // ["", "Videos", "{id}", ...]
        var segments = path.Split('/');
        if (segments.Length < 4
            || segments[0].Length != 0
            || !segments[1].Equals("Videos", StringComparison.OrdinalIgnoreCase)
            || !TryParseRouteGuid(segments[2], out var pathItemId))
        {
            return false;
        }

        if (segments.Length == 4 && IsPlaylist(segments[3]))
        {
            itemId = pathItemId;
            return true;
        }

        // Segment requests only ever arrive as GET.
        if (!isHead
            && segments.Length == 6
            && segments[3].Equals("hls1", StringComparison.OrdinalIgnoreCase)
            && IsSafePlaylistId(segments[4])
            && IsSegmentFile(segments[5]))
        {
            itemId = pathItemId;
            return true;
        }

        return false;
    }

    private static bool IsPlaylist(string value)
        => value.Equals("master.m3u8", StringComparison.OrdinalIgnoreCase)
           || value.Equals("main.m3u8", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The playlist id is used by Jellyfin to name transcode working files, so restrict it to
    /// a conservative character set rather than passing anything through.
    /// </summary>
    private static bool IsSafePlaylistId(string value)
    {
        if (value.Length is 0 or > 64)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSegmentFile(string filename)
    {
        var dot = filename.LastIndexOf('.');
        if (dot <= 0 || dot == filename.Length - 1)
        {
            return false;
        }

        var name = filename[..dot];
        var extension = filename[(dot + 1)..];

        if (!extension.Equals("ts", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals("mp4", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // "-1.mp4" is the fMP4 initialisation segment.
        return name == "-1" || IsNonNegativeInt(name);
    }

    private static bool IsNonNegativeInt(string value)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) && result >= 0;

    private static bool TryParseRouteGuid(string value, out Guid id)
        => Guid.TryParseExact(value, "N", out id) || Guid.TryParseExact(value, "D", out id);
}
