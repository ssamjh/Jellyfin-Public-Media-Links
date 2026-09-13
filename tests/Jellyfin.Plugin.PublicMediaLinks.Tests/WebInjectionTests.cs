using Jellyfin.Plugin.PublicMediaLinks.Web;
using Xunit;

namespace Jellyfin.Plugin.PublicMediaLinks.Tests;

public class WebInjectionTests
{
    private const string Page = "<!DOCTYPE html><html><head><title>Jellyfin</title></head><body><div id=\"app\"></div></body></html>";

    private static string Transform(string? contents)
        => WebInjection.Transform(new TransformationPayload { Contents = contents });

    [Fact]
    public void InjectsScriptBeforeClosingBody()
    {
        var result = Transform(Page);

        Assert.Contains("public-media-links-context-menu", result, System.StringComparison.Ordinal);
        Assert.True(
            result.IndexOf("public-media-links-context-menu", System.StringComparison.Ordinal)
            < result.LastIndexOf("</body>", System.StringComparison.Ordinal),
            "script should be inside the body");
    }

    [Fact]
    public void IncludesTheActualScriptBody()
    {
        var result = Transform(Page);

        Assert.Contains("Copy Public Share Link", result, System.StringComparison.Ordinal);
        Assert.Contains("PublicMediaLinks/Links", result, System.StringComparison.Ordinal);
    }

    [Fact]
    public void PreservesTheOriginalDocument()
    {
        var result = Transform(Page);

        Assert.StartsWith("<!DOCTYPE html><html><head>", result, System.StringComparison.Ordinal);
        Assert.EndsWith("</body></html>", result, System.StringComparison.Ordinal);
        Assert.Contains("<div id=\"app\"></div>", result, System.StringComparison.Ordinal);
    }

    [Fact]
    public void IsIdempotent()
    {
        // The middleware may transform the same file more than once across requests.
        var once = Transform(Page);
        var twice = Transform(once);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void AppendsWhenThereIsNoClosingBodyTag()
    {
        var result = Transform("<html><p>no body tag</p>");

        Assert.StartsWith("<html><p>no body tag</p>", result, System.StringComparison.Ordinal);
        Assert.Contains("public-media-links-context-menu", result, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void LeavesEmptyContentsAlone(string? contents)
    {
        Assert.Equal(string.Empty, Transform(contents));
    }

    [Fact]
    public void DoesNotThrowOnNullPayload()
    {
        Assert.Equal(string.Empty, WebInjection.Transform(null!));
    }
}
