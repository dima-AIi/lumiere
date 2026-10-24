using Lumiere.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Caching.Distributed;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
// Render secret files are an alternative to Dashboard environment variables.
builder.Configuration.AddJsonFile("/etc/secrets/lumiere.json", optional: true, reloadOnChange: false).AddEnvironmentVariables();
var exporting = builder.Configuration.GetValue<bool>("StaticExport");
var importing = builder.Configuration["ImportOrders"];
var database = new PostgresState(builder.Configuration);
if (!exporting && importing is null && builder.Environment.IsProduction())
{
    if (!database.Enabled) throw new InvalidOperationException("Set DATABASE_URL in the hosting environment. Production cannot use temporary files.");
    if (string.IsNullOrWhiteSpace(builder.Configuration["Admin:Password"]))
        throw new InvalidOperationException("Set Admin__Password in the hosting environment.");
}
builder.Services.AddSingleton(database);
await database.InitializeAsync(builder.Environment.ContentRootPath);
if (importing is not null)
{
    if (!database.Enabled) throw new InvalidOperationException("DATABASE_URL is required for import.");
    var original = System.Text.Json.JsonSerializer.Deserialize<List<Order>>(await File.ReadAllTextAsync(importing))
        ?? throw new InvalidDataException("Invalid import file.");
    database.MutateOrders(all => { foreach (var order in original) if (all.All(o => o.Id != order.Id)) all.Add(order); });
    Console.WriteLine($"Import complete: {original.Count} source orders checked. Existing IDs were preserved.");
    return;
}

// На хостинге (Render, Fly и т.п.) порт приходит в переменной окружения PORT —
// слушаем его на всех сетевых интерфейсах. Локально PORT не задан → ничего не меняем.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}
if (exporting) builder.WebHost.UseUrls("http://127.0.0.1:0");

builder.Services.AddControllersWithViews()
    .AddSessionStateTempDataProvider(); // TempData (всплышки «Позиция добавлена/удалена») хранит данные в сессии
builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddDataProtection().SetApplicationName("Lumiere");
if (database.Enabled)
{
    builder.Services.AddSingleton<IDistributedCache, PostgresCache>();
    builder.Services.AddOptions<KeyManagementOptions>().Configure<PostgresState>((options, db) => options.XmlRepository = db);
}
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-CSRF-TOKEN";
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = builder.Environment.IsProduction() && !exporting ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
});
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("admin-login", context => RateLimitPartition.GetFixedWindowLimiter("admin-login", _ => new FixedWindowRateLimiterOptions
    { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddSession(o =>
{
    o.IdleTimeout = TimeSpan.FromHours(3);
    o.Cookie.HttpOnly = true;
    o.Cookie.IsEssential = true;
    o.Cookie.Name = "lumiere.session";
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = builder.Environment.IsProduction() && !exporting ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddSingleton<MenuRepository>();
builder.Services.AddSingleton<YooKassaService>();
builder.Services.AddSingleton<OrderStore>();
builder.Services.AddScoped<CartService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment() && !exporting)
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UsePathBase("/backend");
app.Use(async (context, next) =>
{
    // Render terminates public HTTPS at its ingress and talks HTTP to this container.
    // Use its fixed platform flag, rather than trusting a visitor-supplied forwarding header.
    if (app.Environment.IsProduction() && app.Configuration.GetValue<bool>("RENDER")) context.Request.Scheme = "https";
    // All dynamic responses, errors and redirects are private, including login and CSRF tokens.
    context.Response.Headers.CacheControl = "private, no-store, max-age=0";
    context.Response.Headers["CDN-Cache-Control"] = "no-store";
    context.Response.Headers["Vercel-CDN-Cache-Control"] = "no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Lumiere-Backend"] = "1";
    await next();
});
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseSession();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

if (exporting)
{
    await app.StartAsync();
    await PublicExporter.ExportAsync(app);
    await app.StopAsync();
}
else await app.RunAsync();
