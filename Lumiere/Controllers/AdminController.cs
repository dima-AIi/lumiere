using Microsoft.AspNetCore.Mvc;
using Lumiere.Filters;
using Lumiere.Models;
using Lumiere.Services;

namespace Lumiere.Controllers;

/// <summary>
/// Админка: список и карточки заказов, смена статуса готовности.
/// Весь контроллер закрыт паролем (см. <see cref="AdminOnlyAttribute"/>);
/// пароль хранится в appsettings.json → Admin:Password.
/// </summary>
[AdminOnly]
public class AdminController : Controller
{
    private readonly OrderStore _orderStore;
    private readonly MenuRepository _menu;
    private readonly IConfiguration _config;
    private readonly ILogger<AdminController> _logger;

    public AdminController(OrderStore orderStore, MenuRepository menu, IConfiguration config, ILogger<AdminController> logger)
    {
        _orderStore = orderStore;
        _menu = menu;
        _config = config;
        _logger = logger;
    }

    // ---------- Корень админки: уводим на обзор ----------

    public IActionResult Index() => RedirectToAction(nameof(Dashboard));

    // ---------- Вход / выход ----------

    [AllowAdminAnonymous]
    public IActionResult Login(string? returnUrl)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpPost]
    [AllowAdminAnonymous]
    [ValidateAntiForgeryToken]
    public IActionResult Login(string password, string? returnUrl)
    {
        var expected = _config["Admin:Password"] ?? "";

        if (!string.IsNullOrEmpty(expected) && password == expected)
        {
            HttpContext.Session.SetString(AdminOnlyAttribute.SessionKey, "1");
            _logger.LogInformation("Админ вошёл в систему");

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction(nameof(Orders));
        }

        ViewBag.Error = "Неверный пароль.";
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpPost]
    [AllowAdminAnonymous]
    [ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        HttpContext.Session.Remove(AdminOnlyAttribute.SessionKey);
        return RedirectToAction(nameof(Login));
    }

    // ---------- Обзор (главная страница админки) ----------

    public IActionResult Dashboard()
    {
        var all = _orderStore.All().OrderByDescending(o => o.CreatedAt).ToList();
        var today = DateTime.Now.Date;
        var paid = all.Where(o => o.PaymentStatus == "succeeded");

        var model = new AdminOrdersViewModel
        {
            TotalCount = all.Count,
            PaidCount = paid.Count(),
            DemoCount = all.Count(o => o.IsDemo),
            Revenue = (decimal)(paid.Sum(o => o.Total) / 100.0),
            CountToday = all.Count(o => o.CreatedAt.Date == today),
            RevenueToday = (decimal)(paid.Where(o => o.CreatedAt.Date == today).Sum(o => o.Total) / 100.0),

            NeedsDecisionCount = all.Count(o => o.NeedsDecision),
            Pending = all.Where(o => o.NeedsDecision).Take(6).ToList(),
            Recent = all.Take(8).ToList()
        };

        return View(model);
    }

    // ---------- Список заказов ----------

    public IActionResult Orders(string? status, string? period, string? q)
    {
        var all = _orderStore.All();

        // Статистика считается по всем заказам, без учёта текущих фильтров.
        var today = DateTime.Now.Date;
        var paid = all.Where(o => o.PaymentStatus == "succeeded");

        var model = new AdminOrdersViewModel
        {
            TotalCount = all.Count,
            PaidCount = paid.Count(),
            DemoCount = all.Count(o => o.IsDemo),
            Revenue = (decimal)(paid.Sum(o => o.Total) / 100.0),
            CountToday = all.Count(o => o.CreatedAt.Date == today),
            RevenueToday = (decimal)(paid.Where(o => o.CreatedAt.Date == today).Sum(o => o.Total) / 100.0),
            NeedsDecisionCount = all.Count(o => o.NeedsDecision),
            Status = string.IsNullOrWhiteSpace(status) ? "all" : status,
            Period = string.IsNullOrWhiteSpace(period) ? "all" : period,
            Q = q?.Trim()
        };

        var orders = all.AsEnumerable();

        // Фильтр по статусу оплаты.
        if (!string.IsNullOrWhiteSpace(status))
        {
            orders = orders.Where(o => o.PaymentStatus == status);
        }

        // Фильтр по периоду.
        if (model.Period == "today")
        {
            orders = orders.Where(o => o.CreatedAt.Date == today);
        }
        else if (model.Period == "week")
        {
            orders = orders.Where(o => o.CreatedAt >= today.AddDays(-7));
        }

        // Поиск по номеру, имени или телефону.
        if (!string.IsNullOrWhiteSpace(model.Q))
        {
            var needle = model.Q.ToLowerInvariant();
            orders = orders.Where(o =>
                o.Id.ToLowerInvariant().Contains(needle) ||
                (o.Form.Name ?? "").ToLowerInvariant().Contains(needle) ||
                (o.Form.Phone ?? "").Contains(model.Q));
        }

        // Новые сверху.
        model.Orders = orders.OrderByDescending(o => o.CreatedAt).ToList();
        return View(model);
    }

    // ---------- Карточка заказа ----------

    public IActionResult OrderDetails(string id)
    {
        var order = _orderStore.Find(id);
        if (order is null)
        {
            return NotFound();
        }
        return View(order);
    }

    // ---------- Принять / отклонить заказ ----------

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Decide(string id, bool accept, string? returnTo)
    {
        if (_orderStore.Decide(id, accept))
        {
            _logger.LogInformation("Заказ {Id}: {Action} из админки", id, accept ? "принят" : "отклонён");
        }

        return RedirectToAction(nameof(OrderDetails), new { id });
    }

    // ---------- Смена статуса выполнения на кухне ----------

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SetOrderStatus(string id, string status)
    {
        if (_orderStore.SetOrderStatus(id, status))
        {
            _logger.LogInformation("Заказ {Id}: статус выполнения -> {Status}", id, status);
        }
        return RedirectToAction(nameof(OrderDetails), new { id });
    }

    // ---------- Меню: управление позициями ----------

    public IActionResult Menu()
    {
        return View(_menu.Get());
    }

    // ---------- Создание позиции ----------

    public IActionResult MenuItemCreate()
    {
        var vm = new AdminMenuItemViewModel
        {
            Categories = _menu.Categories().Select(c => c.Title).ToList()
        };
        return View("MenuEdit", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MenuItemCreate(AdminMenuItemViewModel vm, IFormFile? photo)
    {
        vm.Categories = _menu.Categories().Select(c => c.Title).ToList();

        var (photoName, photoError) = await SavePhotoAsync(photo);
        if (photoError is not null)
        {
            ModelState.AddModelError(nameof(photo), photoError);
        }

        // При создании позиции фото обязательно.
        if (string.IsNullOrEmpty(photoName))
        {
            ModelState.AddModelError(nameof(photo), "Загрузите фото блюда в формате JPEG.");
        }

        var targetCat = !string.IsNullOrWhiteSpace(vm.NewCategory) ? vm.NewCategory.Trim() : vm.Category?.Trim();
        if (string.IsNullOrWhiteSpace(targetCat))
        {
            ModelState.AddModelError(nameof(vm.Category), "Выберите категорию или введите новую");
        }

        if (!ModelState.IsValid)
        {
            return View("MenuEdit", vm);
        }

        var data = _menu.Get();
        var item = new MenuItem
        {
            Id = Guid.NewGuid().ToString(),
            Name = vm.Name.Trim(),
            Desc = vm.Desc?.Trim() ?? "",
            Price = vm.PriceRubles * 100,
            Weight = vm.Weight,
            Img = photoName ?? ""
        };

        var cat = data.Categories.FirstOrDefault(c => c.Title.Equals(targetCat!, StringComparison.OrdinalIgnoreCase));
        if (cat is null)
        {
            cat = new MenuCategory { Title = targetCat! };
            data.Categories.Add(cat);
        }
        cat.Items.Add(item);

        _menu.Save(data);
        _logger.LogInformation("Добавлена позиция меню {Name} в «{Cat}»", item.Name, cat.Title);
        TempData["Flash"] = $"Позиция «{item.Name}» добавлена в категорию «{cat.Title}».";
        return RedirectToAction(nameof(Menu));
    }

    // ---------- Редактирование позиции ----------

    public IActionResult MenuItemEdit(string id)
    {
        var data = _menu.Get();
        var item = data.AllItems.FirstOrDefault(i => i.Id == id);
        if (item is null)
        {
            return NotFound();
        }
        var currentCat = data.Categories.FirstOrDefault(c => c.Items.Any(i => i.Id == id))?.Title ?? "";
        var vm = new AdminMenuItemViewModel
        {
            Id = item.Id,
            CurrentImg = item.Img,
            Name = item.Name,
            Desc = item.Desc,
            PriceRubles = (int)Math.Round(item.PriceRubles, MidpointRounding.AwayFromZero),
            Weight = item.Weight ?? 0,
            Category = currentCat,
            Categories = data.Categories.Select(c => c.Title).ToList()
        };
        return View("MenuEdit", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MenuItemEdit(AdminMenuItemViewModel vm, IFormFile? photo, bool removePhoto)
    {
        vm.Categories = _menu.Categories().Select(c => c.Title).ToList();

        var (photoName, photoError) = await SavePhotoAsync(photo);
        if (photoError is not null)
        {
            ModelState.AddModelError(nameof(photo), photoError);
        }

        var targetCat = !string.IsNullOrWhiteSpace(vm.NewCategory) ? vm.NewCategory.Trim() : vm.Category?.Trim();
        if (string.IsNullOrWhiteSpace(targetCat))
        {
            ModelState.AddModelError(nameof(vm.Category), "Выберите категорию или введите новую");
        }

        if (!ModelState.IsValid)
        {
            return View("MenuEdit", vm);
        }

        var data = _menu.Get();
        var item = data.AllItems.FirstOrDefault(i => i.Id == vm.Id);
        if (item is null)
        {
            return NotFound();
        }

        item.Name = vm.Name.Trim();
        item.Desc = vm.Desc?.Trim() ?? "";
        item.Price = vm.PriceRubles * 100;
        item.Weight = vm.Weight;

        if (photoName is not null)
        {
            item.Img = photoName;
        }
        else if (removePhoto)
        {
            item.Img = "";
        }

        // Перенос в другую категорию, если она изменилась.
        var currentCat = data.Categories.FirstOrDefault(c => c.Items.Any(i => i.Id == item.Id));
        if (currentCat is null || !currentCat.Title.Equals(targetCat!, StringComparison.OrdinalIgnoreCase))
        {
            currentCat?.Items.RemoveAll(i => i.Id == item.Id);
            var dest = data.Categories.FirstOrDefault(c => c.Title.Equals(targetCat!, StringComparison.OrdinalIgnoreCase));
            if (dest is null)
            {
                dest = new MenuCategory { Title = targetCat! };
                data.Categories.Add(dest);
            }
            dest.Items.Add(item);
        }

        _menu.Save(data);
        _logger.LogInformation("Изменена позиция меню {Id} ({Name})", item.Id, item.Name);
        TempData["Flash"] = $"Позиция «{item.Name}» сохранена.";
        return RedirectToAction(nameof(Menu));
    }


    // ---------- Удаление позиции ----------

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult MenuItemDelete(string id)
    {
        var data = _menu.Get();
        var item = data.AllItems.FirstOrDefault(i => i.Id == id);
        if (item is null)
        {
            return NotFound();
        }

        foreach (var c in data.Categories)
        {
            c.Items.RemoveAll(i => i.Id == id);
        }
        // Убираем категории, которые опустели после удаления.
        data.Categories.RemoveAll(c => c.Items.Count == 0);

        _menu.Save(data);
        _logger.LogInformation("Удалена позиция меню {Name}", item.Name);
        TempData["Flash"] = $"Позиция «{item.Name}» удалена.";
        return RedirectToAction(nameof(Menu));
    }

    // ---------- Удаление категории вместе с позициями ----------

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult CategoryDelete(string title)
    {
        var data = _menu.Get();
        var cat = data.Categories.FirstOrDefault(c => c.Title == title);
        if (cat is null)
        {
            return NotFound();
        }

        data.Categories.Remove(cat);
        _menu.Save(data);
        _logger.LogWarning("Удалена категория меню {Title} ({Count} поз.)", title, cat.Items.Count);
        TempData["Flash"] = $"Категория «{title}» удалена вместе с {cat.Items.Count} поз.";
        return RedirectToAction(nameof(Menu));
    }

    /// <summary>
    /// Сохраняет загруженное фото в wwwroot/img под уникальным именем.
    /// Проверяет формат (только JPEG) и размеры прямо из заголовка файла.
    /// Возвращает (имя файла, текст ошибки). Ошибка — null при успехе.
    /// </summary>
    private async Task<(string? name, string? error)> SavePhotoAsync(IFormFile? photo)
    {
        if (photo is null || photo.Length == 0)
        {
            return (null, null);
        }

        const long maxSize = 5 * 1024 * 1024; // 5 МБ
        if (photo.Length > maxSize)
        {
            return (null, "Файл слишком большой (максимум 5 МБ).");
        }

        var ext = Path.GetExtension(photo.FileName).ToLowerInvariant();
        var allowed = new[] { ".jpg", ".jpeg" };
        if (!allowed.Contains(ext))
        {
            return (null, "Нужен файл формата JPEG (.jpg). Другие форматы не поддерживаются.");
        }

        // Копируем в память, чтобы прочитать заголовок и при успехе записать на диск.
        using var ms = new MemoryStream();
        await photo.CopyToAsync(ms);
        ms.Position = 0;

        var size = ReadJpegSize(ms);
        if (size is null)
        {
            return (null, "Не удалось прочитать файл как изображение JPEG. Загрузите корректный .jpg.");
        }

        const int minSide = 400;   // минимальная сторона — иначе фото будет мыльным на витрине
        const int maxSide = 4000;  // максимальная сторона — иначе файл слишком тяжёлый
        if (size.Value.w < minSide || size.Value.h < minSide)
        {
            return (null, $"Фото слишком маленькое ({size.Value.w}×{size.Value.h} px). Минимум {minSide}×{minSide} px, лучше всего 800×800 px.");
        }
        if (size.Value.w > maxSide || size.Value.h > maxSide)
        {
            return (null, $"Фото слишком большое ({size.Value.w}×{size.Value.h} px). Максимум {maxSide}×{maxSide} px, лучше всего 800×800 px.");
        }

        var name = Guid.NewGuid().ToString("N")[..16] + ext;
        var dir = _menu.ImgFolder;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        await using (var fs = new FileStream(path, FileMode.Create))
        {
            ms.Position = 0;
            await ms.CopyToAsync(fs);
        }
        return (name, null);
    }

    /// <summary>
    /// Читает ширину и высоту JPEG прямо из заголовка файла (без сторонних библиотек).
    /// Возвращает null, если поток не является разбираемым JPEG.
    /// </summary>
    private static (int w, int h)? ReadJpegSize(Stream s)
    {
        // JPEG начинается с маркера SOI (FF D8).
        if (s.ReadByte() != 0xFF || s.ReadByte() != 0xD8)
        {
            return null;
        }

        while (true)
        {
            int b = s.ReadByte();
            if (b == -1)
            {
                return null; // конец файла без SOF
            }
            if (b != 0xFF)
            {
                continue; // ищем следующий маркер (FF)
            }

            // пропускаем подряд идущие 0xFF
            int marker;
            do
            {
                marker = s.ReadByte();
            } while (marker == 0xFF);
            if (marker == -1)
            {
                return null;
            }

            // Маркеры без поля длины (RSTn, SOI, EOI, TEM).
            if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD9))
            {
                continue;
            }

            // Читаем длину сегмента (2 байта, старший байт первый).
            int lenHi = s.ReadByte();
            int lenLo = s.ReadByte();
            if (lenHi == -1 || lenLo == -1)
            {
                return null;
            }
            int len = (lenHi << 8) | lenLo;
            if (len < 2)
            {
                return null;
            }

            // SOS (FF DA) — дальше сжатые пиксели, размер уже должен был найтись.
            if (marker == 0xDA)
            {
                return null;
            }

            // SOF-маркеры (C0–CF) содержат размеры, кроме DHT (C4), JPG (C8), DAC (CC).
            bool isSof = marker >= 0xC0 && marker <= 0xCF
                         && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
            if (isSof)
            {
                int precision = s.ReadByte();  // обычно 8
                int hHi = s.ReadByte();
                int hLo = s.ReadByte();
                int wHi = s.ReadByte();
                int wLo = s.ReadByte();
                if (precision == -1 || hHi == -1 || hLo == -1 || wHi == -1 || wLo == -1)
                {
                    return null;
                }
                int height = (hHi << 8) | hLo;
                int width = (wHi << 8) | wLo;
                if (width <= 0 || height <= 0)
                {
                    return null;
                }
                return (width, height);
            }

            // пропускаем остаток сегмента (len уже включает 2 байта самой длины)
            int remaining = len - 2;
            for (int i = 0; i < remaining; i++)
            {
                if (s.ReadByte() == -1)
                {
                    return null;
                }
            }
        }
    }

    // ---------- Ручное изменение статуса оплаты (для разбора ситуаций) ----------

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SetPaymentStatus(string id, string status)
    {
        var paid = status == "succeeded";
        if (_orderStore.SetPayment(id, null, status, paid))
        {
            _logger.LogWarning("Заказ {Id}: статус оплаты изменён вручную -> {Status}", id, status);
        }
        return RedirectToAction(nameof(OrderDetails), new { id });
    }
}
