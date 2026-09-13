using System;
using System.Globalization;
using Jellyfin.Plugin.PublicMediaLinks.Configuration;

namespace Jellyfin.Plugin.PublicMediaLinks.Security;

/// <summary>
/// Builds the outward facing URLs for a share link.
/// </summary>
public static class ShareUrls
{
    /// <summary>
    /// Builds a URL for Jellyfin's own HLS endpoint, authorised by the share token.
    /// </summary>
    /// <remarks>
    /// These parameters ask for a remux rather than a re-encode: naming the source codecs and
    /// allowing stream copy lets Jellyfin pass video and audio through untouched when they
    /// already match, which is the normal case for H.264 + AAC. Deliberately absent are any
    /// bitrate, resolution, profile or level limits, since each of those would force a
    /// re-encode even when the source is already fine.
    /// </remarks>
    /// <param name="baseUrl">The externally reachable server base URL, without a trailing slash.</param>
    /// <param name="itemId">The item being shared.</param>
    /// <param name="token">The share token.</param>
    /// <param name="config">Plugin configuration.</param>
    /// <returns>The HLS playlist URL.</returns>
    public static string BuildHls(string baseUrl, Guid itemId, string token, PluginConfiguration config)
    {
        var id = itemId.ToString("N", CultureInfo.InvariantCulture);

        var query = string.Join(
            '&',
            $"mediaSourceId={id}",
            "videoCodec=h264",
            "audioCodec=aac",
            "enableAutoStreamCopy=true",
            "allowVideoStreamCopy=true",
            "allowAudioStreamCopy=true",
            $"segmentContainer={Uri.EscapeDataString(config.HlsSegmentContainer)}",
            "enableAdaptiveBitrateStreaming=false",
            "enableTrickplay=false",
            $"api_key={Uri.EscapeDataString(token)}");

        return $"{baseUrl}/Videos/{id}/master.m3u8?{query}";
    }
}
