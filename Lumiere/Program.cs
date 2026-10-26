using Lumiere.Services;

var builder = WebApplication.CreateBuilder(args);
// No database, sessions, credentials or payment services exist in this demo.
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var exporting = builder.Configuration.GetValue<bool>("StaticExport");
if (exporting) builder.WebHost.UseUrls("http://127.0.0.1:0");
else if (Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } port)
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Services.AddControllersWithViews();
builder.Services.AddSingleton<MenuRepository>();
var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Lumiere-Mode"] = "static-demo";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] = DemoSecurity.ContentSecurityPolicy;
    if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
    {
        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        context.Response.Headers.Allow = "GET, HEAD";
        return; // Never bind, read or log the request body.
    }
    if (DemoSecurity.IsRemovedPath(context.Request.Path.Value ?? "/"))
    {
        context.Response.StatusCode = StatusCodes.Status410Gone;
        context.Response.Headers.CacheControl = "no-store";
        return;
    }
    await next();
});
app.UseStaticFiles();
app.UseRouting();
app.MapGet("/healthz", () => Results.Json(new { ready = true, mode = "static-demo" }));
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
if (exporting)
{
    await app.StartAsync();
    await PublicExporter.ExportAsync(app);
    await app.StopAsync();
}
else await app.RunAsync();
