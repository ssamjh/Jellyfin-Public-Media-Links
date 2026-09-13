using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PublicMediaLinks.Web;

/// <summary>
/// Adds the "Copy Public Share Link" row to jellyfin-web's item context menu by asking the
/// File Transformation plugin to inject a script into index.html as it is served.
/// </summary>
/// <remarks>
/// Jellyfin has no first-party way for a server plugin to extend the web client, and editing
/// files in the jellyfin-web directory is destructive and undone by every server update.
/// File Transformation rewrites the response in memory instead, and lets several plugins
/// stack transforms on the same file.
/// </remarks>
public static class WebInjection
{
    /// <summary>
    /// The marker the injected script is wrapped in, also used to avoid double injection.
    /// </summary>
    private const string Marker = "public-media-links-context-menu";

    private const string ResourceName = "Jellyfin.Plugin.PublicMediaLinks.Web.contextMenu.js";

    /// <summary>
    /// Captured at registration. Transform runs on every index.html request, so it
    /// deliberately does not reach into <see cref="Plugin.Instance"/> for this.
    /// </summary>
    private static ILogger? _logger;

    /// <summary>
    /// Registers the transformation. Safe to call when File Transformation is not installed;
    /// the dashboard page keeps working either way.
    /// </summary>
    /// <param name="pluginId">The plugin id, used as the transformation id.</param>
    /// <param name="logger">Logger.</param>
    public static void Register(Guid pluginId, ILogger logger)
    {
        _logger = logger;

        try
        {
            var assembly = AssemblyLoadContext.All
                .SelectMany(context => context.Assemblies)
                .FirstOrDefault(a => a.FullName?.Contains(".FileTransformation", StringComparison.Ordinal) == true);

            if (assembly is null)
            {
                logger.LogInformation(
                    "File Transformation plugin not found. The dashboard page still works, but the "
                    + "\"Copy Public Share Link\" context menu item will not be available.");
                return;
            }

            var pluginInterface = assembly.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface");
            var register = pluginInterface?.GetMethod("RegisterTransformation", BindingFlags.Public | BindingFlags.Static);

            if (register is null)
            {
                logger.LogWarning("File Transformation was found but RegisterTransformation is missing. Skipping web injection.");
                return;
            }

            var json = JsonSerializer.Serialize(new
            {
                id = pluginId.ToString(),
                fileNamePattern = "index\\.html$",
                callbackAssembly = typeof(WebInjection).Assembly.FullName,
                callbackClass = typeof(WebInjection).FullName,
                callbackMethod = nameof(Transform)
            });

            // RegisterTransformation takes a Newtonsoft JObject. Referencing Newtonsoft
            // ourselves would risk loading a second copy into a different load context, and
            // the resulting type would not match this parameter. Building the argument from
            // the parameter's own type guarantees we hand over the exact type it expects.
            var payloadType = register.GetParameters()[0].ParameterType;
            var parse = payloadType.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, [typeof(string)]);

            if (parse is null)
            {
                logger.LogWarning("Could not build a payload for File Transformation. Skipping web injection.");
                return;
            }

            register.Invoke(null, [parse.Invoke(null, [json])]);
            logger.LogInformation("Registered the context menu injection with File Transformation.");
        }
        catch (Exception ex)
        {
            // A failure here must never stop the plugin loading; the dashboard page is the
            // primary interface and does not depend on any of this.
            logger.LogError(ex, "Failed to register the context menu injection with File Transformation.");
        }
    }

    /// <summary>
    /// Invoked by File Transformation with the contents of index.html.
    /// </summary>
    /// <param name="payload">The original file contents.</param>
    /// <returns>The contents with the script appended.</returns>
    public static string Transform(TransformationPayload payload)
    {
        var contents = payload?.Contents;

        if (string.IsNullOrEmpty(contents))
        {
            return contents ?? string.Empty;
        }

        try
        {
            if (contents.Contains(Marker, StringComparison.Ordinal))
            {
                return contents;
            }

            var script = string.Format(
                CultureInfo.InvariantCulture,
                "<script id=\"{0}\" defer>\n{1}\n</script>",
                Marker,
                ReadScript());

            // Append rather than splice: if </body> is ever absent or renamed the script
            // still loads, and we never risk corrupting the document.
            var closing = contents.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);

            return closing < 0
                ? contents + script
                : contents[..closing] + script + contents[closing..];
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to inject the context menu script into index.html.");

            // Returning the original keeps the web client working even if we break.
            return contents;
        }
    }

    private static string ReadScript()
    {
        using var stream = typeof(WebInjection).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>
/// The payload File Transformation passes to <see cref="WebInjection.Transform"/>.
/// </summary>
public class TransformationPayload
{
    /// <summary>
    /// Gets or sets the original contents of the file being served.
    /// </summary>
    public string? Contents { get; set; }
}
