using System.Text.Json;
using Lumiere.Models;

namespace Lumiere.Services;

/// <summary>
/// Загружает меню один раз из wwwroot/data/menu.json и отдаёт его в представления.
/// </summary>
public class MenuRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IWebHostEnvironment _env;
    private readonly ILogger<MenuRepository> _log;
    private readonly PostgresState _database;
    private MenuData? _cache;

    public MenuRepository(IWebHostEnvironment env, ILogger<MenuRepository> log, PostgresState database)
    {
        _env = env;
        _log = log;
        _database = database;
    }

    public MenuData Get()
    {
        if (!_database.Enabled && _cache is not null)
        {
            return _cache;
        }

        var path = Path.Combine(_env.ContentRootPath, "wwwroot", "data", "menu.json");
        var json = _database.Enabled ? _database.GetDocument("menu") ?? throw new InvalidDataException("Menu has not been initialized.") : File.ReadAllText(path);
        var data = JsonSerializer.Deserialize<MenuData>(json, JsonOptions) ?? new MenuData();

        // В исходном API цены указаны в рублях, а модель и платёжный шлюз работают
        // в копейках — приводим к копейкам один раз при загрузке.
        foreach (var item in data.AllItems)
        {
            item.Price = Math.Round(item.Price * 100, MidpointRounding.AwayFromZero);
        }

        data.DeliveryPrice = Math.Round(data.DeliveryPrice * 100, MidpointRounding.AwayFromZero);

        _cache = data;
        _log.LogInformation("Меню загружено: {Count} позиций", data.AllItems.Count());
        return _cache;
    }

    public MenuItem? FindItem(string id) => Get().AllItems.FirstOrDefault(i => i.Id == id);

    public List<MenuCategory> Categories() => Get().Categories;

    public List<MenuCategory> Search(string? query)
    {
        var categories = Get().Categories;

        if (string.IsNullOrWhiteSpace(query))
        {
            return categories;
        }

        var q = query.Trim().ToLowerInvariant();
        return categories
            .Select(c =>
            {
                // Совпадение с названием категории (например, «Кофе») показывает её целиком,
                // иначе ищем по названию блюда и его составу.
                var wholeCategory = c.Title.ToLowerInvariant().Contains(q);
                var items = wholeCategory
                    ? c.Items
                    : c.Items
                        .Where(i => i.Name.ToLowerInvariant().Contains(q) || (i.Desc ?? "").ToLowerInvariant().Contains(q))
                        .ToList();

                return new MenuCategory { Title = c.Title, Items = items.ToList() };
            })
            .Where(c => c.Items.Count > 0)
            .ToList();
    }

    /// <summary>Подборка для главной страницы: по позиции из каждой категории.</summary>
    public List<MenuItem> Highlights(int perCategory = 3)
    {
        return Get().Categories
            .Where(c => c.Items.Count > 0)
            .SelectMany(c => c.Items.Take(perCategory))
            .ToList();
    }

    // ---------- Сохранение меню (используется админкой) ----------

    private static readonly object SaveGate = new();

    /// <summary>Путь к папке с фотографиями блюд (wwwroot/img).</summary>
    public string ImgFolder => Path.Combine(_env.ContentRootPath, "wwwroot", "img");

    /// <summary>Путь к файлу menu.json.</summary>
    public string MenuFilePath => Path.Combine(_env.ContentRootPath, "wwwroot", "data", "menu.json");

    /// <summary>
    /// Сохраняет меню обратно в menu.json. В кэше цены хранятся в копейках, а в файле — в рублях
    /// (так их загружает <see cref="Get"/>), поэтому при записи конвертируем копейки → рубли.
    /// </summary>
    public void Save(MenuData data)
    {
        lock (SaveGate)
        {
            var clone = new MenuData
            {
                Tagline = data.Tagline,
                City = data.City,
                Address = data.Address,
                Phone = data.Phone,
                WorkStart = data.WorkStart,
                WorkEnd = data.WorkEnd,
                DeliveryPrice = data.DeliveryPrice / 100.0,
                Categories = data.Categories.Select(c => new MenuCategory
                {
                    Title = c.Title,
                    Items = c.Items.Select(i => new MenuItem
                    {
                        Id = i.Id,
                        Name = i.Name,
                        Desc = i.Desc,
                        Price = i.Price / 100.0,
                        Img = i.Img,
                        Weight = i.Weight
                    }).ToList()
                }).ToList()
            };

            var json = JsonSerializer.Serialize(clone, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            if (_database.Enabled) _database.SaveDocument("menu", json);
            else File.WriteAllText(MenuFilePath, json);

            // Кэш теперь содержит актуальные данные (в копейках) — обновляем,
            // чтобы изменения были видны сразу, без перезапуска сервера.
            _cache = data;
            _log.LogInformation("Меню сохранено в файл: {Count} позиций", data.AllItems.Count());
        }
    }
}
