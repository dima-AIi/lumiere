
namespace Lumiere.Services;

/// <summary>Build public Razor pages without connecting to any production database.</summary>
public static class PublicExporter
{
    public static async Task ExportAsync(WebApplication app)
    {
        var output = Path.GetFullPath(app.Configuration["FrontendOutput"] ?? "../dist");
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var pages = new Dictionary<string, string>
        {
            ["/"] = "index.html", ["/Home/Menu"] = "Home/Menu.html",
            ["/Home/Contacts"] = "Home/Contacts.html", ["/Home/Privacy"] = "Home/Privacy.html",
            ["/Home/CartPage"] = "Home/CartPage.html", ["/Home/Checkout"] = "Home/Checkout.html"
        };
        foreach (var (url, file) in pages)
        {
            var html = await client.GetStringAsync(url);
            var target = Path.Combine(output, file);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, html);
        }
        app.Logger.LogInformation("Exported {Count} public pages to {Output}", pages.Count, output);
    }
}
