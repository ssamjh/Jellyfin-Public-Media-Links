using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jellyfin.Extensions.Json;
using MediaBrowser.Model.Updates;
using Xunit;

namespace Jellyfin.Plugin.PublicMediaLinks.Tests;

/// <summary>
/// Validates manifest.json by deserializing it with the very type Jellyfin uses
/// (<see cref="PackageInfo"/>), so a broken repository manifest fails here rather than in
/// the user's dashboard.
/// </summary>
public class ManifestTests
{
    private const string PluginGuid = "2b9c4f61-7a3d-4e58-9d1c-0f5a6c8e2b74";

    private static PackageInfo[] Load()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "manifest.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        var json = File.ReadAllText(Path.Combine(directory!.FullName, "manifest.json"));

        // JsonDefaults.Options is exactly what InstallationManager uses to read a repository
        // manifest, converters and all, so this parses the file the way the server will.
        var packages = JsonSerializer.Deserialize<PackageInfo[]>(json, JsonDefaults.Options);

        Assert.NotNull(packages);
        return packages!;
    }

    [Fact]
    public void DeserializesWithJellyfinsOwnModel()
    {
        var packages = Load();

        Assert.NotEmpty(packages);
        Assert.All(packages, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Name));
            Assert.False(string.IsNullOrWhiteSpace(p.Description));
            Assert.False(string.IsNullOrWhiteSpace(p.Overview));
            Assert.False(string.IsNullOrWhiteSpace(p.Owner));
            Assert.NotEmpty(p.Versions);
        });
    }

    [Fact]
    public void GuidMatchesThePlugin()
    {
        // A mismatch here installs fine and then silently never updates.
        var package = Load().Single();

        Assert.Equal(Guid.Parse(PluginGuid), package.Id);
    }

    [Fact]
    public void EveryVersionIsInstallable()
    {
        foreach (var version in Load().SelectMany(p => p.Versions))
        {
            // Jellyfin parses both of these with System.Version.
            Assert.True(Version.TryParse(version.Version, out _), $"bad version: {version.Version}");
            Assert.True(Version.TryParse(version.TargetAbi, out _), $"bad targetAbi: {version.TargetAbi}");

            // Checksum is compared against an MD5 hex digest of the downloaded zip.
            Assert.NotNull(version.Checksum);
            Assert.Equal(32, version.Checksum!.Length);
            Assert.True(
                version.Checksum.All(Uri.IsHexDigit),
                $"checksum is not hex: {version.Checksum}");

            Assert.True(
                Uri.TryCreate(version.SourceUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps,
                $"sourceUrl must be absolute https: {version.SourceUrl}");

            Assert.True(
                DateTime.TryParse(
                    version.Timestamp,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal,
                    out _),
                $"bad timestamp: {version.Timestamp}");
        }
    }

    [Fact]
    public void TargetsJellyfin12()
    {
        var abi = Version.Parse(Load().Single().Versions[0].TargetAbi!);

        Assert.Equal(12, abi.Major);
    }

    [Fact]
    public void VersionsAreUniqueAndNewestFirst()
    {
        var versions = Load().Single().Versions
            .Select(v => Version.Parse(v.Version!))
            .ToList();

        Assert.Equal(versions.Count, versions.Distinct().Count());
        Assert.Equal(versions.OrderByDescending(v => v).ToList(), versions);
    }
}
