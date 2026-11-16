using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

using CrystalCode.Server.Hosting;

namespace CrystalCode.Server.Endpoints;

/// <summary>
/// The routes and middleware that only <c>crystal web</c> adds: <c>/config</c>
/// at the root, and the operator's front end files from a directory. The
/// server holds no HTML. Files are read through a
/// <see cref="PhysicalFileProvider"/>, which refuses any path that leaves its
/// root and hides dot-prefixed, hidden, and system files.
/// </summary>
internal static class WebEndpoints
{
    public const string ConfigPath = "/config";

    public const string Product = "crystal-code";

    private const string IndexFile = "index.html";

    /// <summary>
    /// Maps <c>/config</c>, the API 404, and the front end. Returns a warning
    /// for the operator when there is nothing to serve, or null.
    /// </summary>
    public static string? Map(WebApplication app, ServerContext server, WebOptions web)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(web);

        // Public and uncached: a page reads it before it has a token, and the
        // authentication flag can change on the next start.
        app.MapGet(ConfigPath, (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Json.Ok(writer =>
            {
                writer.WriteString("product", Product);
                writer.WriteString("apiBase", WebOptions.ApiBase);
                writer.WriteBoolean("authentication", server.Options.AuthenticationEnabled);
            });
        });

        // Without this an unknown API path would fall through to the front end
        // fallback and answer 200 with HTML.
        app.Map(WebOptions.ApiBase + "/{**path}", () => Json.Problem(StatusCodes.Status404NotFound, "No such API route."));

        var root = Path.GetFullPath(web.Root);
        if (!Directory.Exists(root))
        {
            return "Warning: the web directory does not exist: " + root + ". Only " + WebOptions.ApiBase + " and " + ConfigPath + " are served.";
        }

        var files = new PhysicalFileProvider(root);
        app.Lifetime.ApplicationStopped.Register(files.Dispose);
        var options = new StaticFileOptions
        {
            FileProvider = files,
            ServeUnknownFileTypes = false,
            OnPrepareResponse = static context =>
            {
                context.Context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Context.Response.Headers.CacheControl = "no-cache";
            }
        };
        app.UseWhen(
            static context => !IsReserved(context.Request.Path),
            branch =>
            {
                branch.UseDefaultFiles(new DefaultFilesOptions
                {
                    FileProvider = files,
                    DefaultFileNames = [IndexFile]
                });
                branch.UseStaticFiles(options);
            });
        if (web.SpaFallback)
        {
            app.MapFallbackToFile(IndexFile, options);
        }

        return files.GetFileInfo(IndexFile).Exists
            ? null
            : "Warning: " + Path.Combine(root, IndexFile) + " does not exist, so the front end has no entry page.";
    }

    /// <summary>A file in the web directory never shadows the API or <c>/config</c>.</summary>
    private static bool IsReserved(PathString path) =>
        path.StartsWithSegments(WebOptions.ApiBase, StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments(ConfigPath, StringComparison.OrdinalIgnoreCase);
}
