namespace ProjectTime.Api.Modules;

/// <summary>Give every authorization middleware the same route spelling accepted by routing.</summary>
public static class CanonicalApiPaths
{
    public static string Normalize(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (!path.StartsWith('/') || segments.Length == 0
            || !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)) return path;
        for (var i = 0; i < segments.Length; i++)
            if (Guid.TryParse(segments[i], out var id)) segments[i] = id.ToString("D");
        return "/" + string.Join('/', segments);
    }

    public static WebApplication UseCanonicalApiPaths(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            context.Request.Path = Normalize(context.Request.Path.Value ?? string.Empty);
            await next();
        });
        return app;
    }
}
