using Lumiere.Services;

var builder = WebApplication.CreateBuilder(args);

// На хостинге (Render, Fly и т.п.) порт приходит в переменной окружения PORT —
// слушаем его на всех сетевых интерфейсах. Локально PORT не задан → ничего не меняем.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

builder.Services.AddControllersWithViews()
    .AddSessionStateTempDataProvider(); // TempData (всплышки «Позиция добавлена/удалена») хранит данные в сессии
builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o =>
{
    o.IdleTimeout = TimeSpan.FromHours(3);
    o.Cookie.HttpOnly = true;
    o.Cookie.IsEssential = true;
    o.Cookie.Name = "lumiere.session";
});

builder.Services.AddSingleton<MenuRepository>();
builder.Services.AddSingleton<YooKassaService>();
builder.Services.AddSingleton<OrderStore>();
builder.Services.AddScoped<CartService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
