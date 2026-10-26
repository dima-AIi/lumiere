namespace Lumiere.Services;

public static class DemoSecurity
{
    public const string ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; media-src 'self'; connect-src 'self'; form-action 'none'; frame-src 'none'; object-src 'none'; base-uri 'none'";
    public static bool IsRemovedPath(string path)
    {
        var removed = new[] { "admin", "api", "backend", "media", "register", "registration", "login", "auth", "account" };
        return path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => removed.Contains(segment.Split('.')[0], StringComparer.OrdinalIgnoreCase));
    }
}
