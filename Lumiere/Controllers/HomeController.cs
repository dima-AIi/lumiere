using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Lumiere.Models;
using Lumiere.Services;

namespace Lumiere.Controllers;

public class HomeController : Controller
{
    private readonly MenuRepository _menu;
    private readonly CartService _cart;
    private readonly YooKassaService _payment;
    private readonly OrderStore _orderStore;
    private readonly ILogger<HomeController> _logger;

    public HomeController(
        MenuRepository menu,
        CartService cart,
        YooKassaService payment,
        OrderStore orderStore,
        ILogger<HomeController> logger)
    {
        _menu = menu;
        _cart = cart;
        _payment = payment;
        _orderStore = orderStore;
        _logger = logger;
    }

    /// <summary>Главная страница витрины.</summary>
    public IActionResult Index()
    {
        var data = _menu.Get();
        ViewBag.Menu = data;
        ViewBag.Highlights = _menu.Highlights(3);
        return View();
    }

    /// <summary>Меню с поиском по названию и составу.</summary>
    public IActionResult Menu(string? q)
    {
        ViewBag.Menu = _menu.Get();
        var categories = _menu.Search(q);
        ViewBag.Query = q ?? "";
        ViewBag.ItemCount = _menu.Get().AllItems.Count();

        // AJAX-поиск (заголовок X-Requested-With): отдаём только блок результатов,
        // чтобы не перезагружать страницу и не терять фокус в строке поиска.
        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            return PartialView("_MenuItems", categories);

        ViewBag.Categories = categories;
        return View();
    }

    public IActionResult Contacts()
    {
        ViewBag.Menu = _menu.Get();
        return View();
    }

    public IActionResult Privacy()
    {
        ViewBag.Menu = _menu.Get();
        return View();
    }

    // ---------- Корзина ----------

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AddToCart(string id, int qty = 1)
    {
        var item = _menu.FindItem(id);
        if (item is not null)
        {
            _cart.Add(item, Math.Clamp(qty, 1, 99));
        }

        // AJAX из карточек меню/витрины: отвечаем новым счётчиком — страница не перезагружается
        // и не прыгает наверх. Иначе — обычный редирект на меню.
        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            return Json(new { ok = true, count = _cart.Get().Count });

        return RedirectToAction(nameof(Menu));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult CartSetQty(string id, int qty)
    {
        _cart.SetQty(id, qty);
        return RedirectToAction(nameof(CartPage));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult CartRemove(string id)
    {
        _cart.Remove(id);
        return RedirectToAction(nameof(CartPage));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SetDelivery(string type)
    {
        // Тип и стоимость доставки сохраняются в сессии — после редиректа
        // корзина перечитается уже с актуальной ценой доставки.
        _cart.SetDeliveryType(type);
        return RedirectToAction(nameof(CartPage));
    }

    public IActionResult CartPage()
    {
        var cart = _cart.Get();
        ViewBag.Menu = _menu.Get();
        return View(cart);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ClearCart()
    {
        _cart.Clear();
        return RedirectToAction(nameof(Menu));
    }

    // ---------- Оформление и оплата ----------

    [HttpGet]
    public IActionResult Checkout()
    {
        var cart = _cart.Get();
        if (cart.Count == 0)
        {
            return RedirectToAction(nameof(CartPage));
        }

        ViewBag.Menu = _menu.Get();
        var submissionId = HttpContext.Session.GetString("lumiere.checkout-submission");
        if (submissionId is null)
        {
            submissionId = Guid.NewGuid().ToString();
            HttpContext.Session.SetString("lumiere.checkout-submission", submissionId);
        }
        return View(new CheckoutForm { DeliveryType = cart.DeliveryType, SubmissionId = submissionId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(CheckoutForm form)
    {
        if (string.IsNullOrEmpty(form.SubmissionId) || form.SubmissionId != HttpContext.Session.GetString("lumiere.checkout-submission"))
            return BadRequest("Форма устарела. Откройте оформление заказа заново.");
        var cart = _cart.Get();
        if (cart.Count == 0)
        {
            return RedirectToAction(nameof(CartPage));
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Menu = _menu.Get();
            return View(form);
        }
        if (string.IsNullOrWhiteSpace(form.Name))
        {
            ModelState.AddModelError(string.Empty, "Укажите, как к вам обращаться.");
            ViewBag.Menu = _menu.Get();
            return View(form);
        }

        // Телефон: оставляем только цифры и проверяем длину (10–11 цифр). Буквы не принимаем.
        var phoneDigits = new string((form.Phone ?? "").Where(char.IsDigit).ToArray());
        if (phoneDigits.Length is < 10 or > 11)
        {
            ModelState.AddModelError(string.Empty, "Введите номер телефона: 10–11 цифр, без букв.");
            ViewBag.Menu = _menu.Get();
            return View(form);
        }
        form.Phone = phoneDigits;

        // Адрес: убираем лишние символы (запятые, точки и прочие знаки) —
        // оставляем только буквы (в т.ч. кириллицу), цифры, пробел, дефис и слэш.
        // Если от адреса ничего не осталось, а выбран способ «доставка» —
        // попросим ввести адрес заново.
        form.Address = System.Text.RegularExpressions.Regex
            .Replace(form.Address ?? "", @"[^A-Za-zА-Яа-яЁё0-9 \-/]", string.Empty).Trim();

        if (form.DeliveryType == "delivery" && string.IsNullOrWhiteSpace(form.Address))
        {
            ModelState.AddModelError(string.Empty, "Для доставки нужен адрес.");
            ViewBag.Menu = _menu.Get();
            return View(form);
        }

        // Тип доставки фиксируем до сохранения заказа, чтобы в заказ и платёж
        // попала одна и та же сумма.
        _cart.SetDeliveryType(form.DeliveryType);
        cart = _cart.Get();

        // Сохраняем заказ: сюда же подключается запись в базу/1С/уведомление в Telegram.
        var order = _orderStore.Save(form, cart);
        _logger.LogInformation(
            "Заказ {Id} на {Total} руб. ({Delivery})",
            order.Id, cart.Total / 100.0, form.DeliveryType);

        var result = await _payment.CreatePaymentAsync(cart, form, order.Id, HttpContext.RequestAborted);
        if (!result.Success
            || string.IsNullOrEmpty(result.PaymentId)
            || (result.DemoMode is false && string.IsNullOrEmpty(result.ConfirmUrl)))
        {
            _orderStore.SetPayment(order.Id, result.PaymentId, "error", paid: false);
            ModelState.AddModelError(string.Empty,
                result.Error ?? "Платёжная система не вердела ссылку на оплату. Попробуйте ещё раз.");
            ViewBag.Menu = _menu.Get();
            return View(form);
        }

        if (result.DemoMode)
        {
            HttpContext.Session.Remove("lumiere.checkout-submission");
            _cart.Clear();
            _cart.ClearPendingPayment();
            _orderStore.SetPayment(order.Id, result.PaymentId, "demo", paid: true);
            ViewBag.Menu = _menu.Get();
            ViewBag.OrderId = order.Id;
            ViewBag.PaymentId = result.PaymentId;
            // Перечитываем заказ из хранилища: SetPayment поменял статус на «demo» уже
            // в файле, а наша локальная копия этого не видит. Иначе чек не покажет «ДЕМО-ЧЕК».
            ViewBag.Order = _orderStore.Find(order.Id) ?? order;   // полный заказ — для чека покупателя
            // Учебный демо-режим (ключей ЮKassa нет): показываем ту же страницу успеха,
            // что и при реальной оплате, — путь заказа выглядит завершённым.
            return View("PaymentSuccess");
        }

        // Запоминаем заказ до редиректа: по возврату проверим платёж в ЮKassa.
        _cart.SetPendingPayment(new CartService.PendingPayment
        {
            OrderId = order.Id,
            PaymentId = result.PaymentId ?? "",
            Total = cart.Total / 100.0
        });
        _orderStore.SetPayment(order.Id, result.PaymentId, "pending", paid: false);

        // Настоящий платёж: уходим на страницу ЮKassa, возврат — на /Home/PaymentSuccess
        return Redirect(result.ConfirmUrl!);
    }

    /// <summary>
    /// Возврат с сайта ЮKassa. Страница успеха показывается только после того, как
    /// платёж реально проверен у ЮKassa и совпал с нашим заказом. Любая нестыковка —
    /// на страницу ошибки, корзина при этом сохраняется.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> PaymentSuccess(string? payment_id)
    {
        ViewBag.Menu = _menu.Get();

        var pending = _cart.GetPendingPayment();
        if (pending is null || string.IsNullOrEmpty(pending.OrderId) || string.IsNullOrEmpty(pending.PaymentId))
        {
            // Прямой заход на /Home/PaymentSuccess без оплаты — это не успех.
            _logger.LogWarning("Возврат с оплаты без ожидающего платежа в сессии (payment_id={Id})", payment_id);
            ViewBag.PaymentId = payment_id;
            ViewBag.Reason = "Ссылка на оплату устарела или открыта в другом браузере. "
                + "Мы не нашли незавершённый заказ — деньги не списаны, корзина осталась у вас.";
            return View("PaymentError");
        }

        // payment_id из адресной строки должен совпадать с тем, который мы создали.
        if (!string.IsNullOrEmpty(payment_id)
            && !string.Equals(payment_id, pending.PaymentId, StringComparison.Ordinal))
        {
            _logger.LogWarning("Возврат с чужим payment_id: {Incoming} вместо {Expected}",
                payment_id, pending.PaymentId);
            ViewBag.PaymentId = payment_id;
            ViewBag.Reason = "Идентификатор платежа не совпадает с вашим заказом. "
                + "Платёж не подтверждён, деньги не списаны.";
            return View("PaymentError");
        }

        var order = _orderStore.Find(pending.OrderId);
        if (order is null)
        {
            _logger.LogWarning("Заказ {Id} не найден в хранилище при возврате с оплаты", pending.OrderId);
            ViewBag.PaymentId = pending.PaymentId;
            ViewBag.Reason = "Заказ не найден в системе. Позвоните нам по телефону на сайте — разберёмся.";
            return View("PaymentError");
        }

        var check = await _payment.GetPaymentStatusAsync(pending.PaymentId, HttpContext.RequestAborted);
        if (!check.Verified)
        {
            _logger.LogWarning("Платёж {Id} не подтверждён ЮKassa: {Error}", pending.PaymentId, check.Error);
            ViewBag.PaymentId = pending.PaymentId;
            ViewBag.Status = check.Status;
            ViewBag.Reason = check.Error
                ?? "Не удалось получить подтверждение от платёжной системы. Попробуйте обновить страницу или позвоните нам.";
            return View("PaymentError");
        }

        // Сверяем платёж с заказом: сумма, валюта и номер заказа в metadata.
        var mismatch = CheckConsistency(order, check);
        if (mismatch is not null)
        {
            _logger.LogError(
                "Платёж {PaymentId} не соответствует заказу {OrderId}: {Mismatch} (amount={Amount}, order={MetaOrderId})",
                pending.PaymentId, order.Id, mismatch, check.Amount, check.MetadataOrderId);
            _orderStore.SetPayment(order.Id, pending.PaymentId, "mismatch", paid: false);
            ViewBag.PaymentId = pending.PaymentId;
            ViewBag.Status = check.Status;
            ViewBag.Reason = mismatch;
            return View("PaymentError");
        }

        _cart.ClearPendingPayment();

        if (check.Paid)
        {
            _orderStore.SetPayment(order.Id, pending.PaymentId, "succeeded", paid: true);
            _cart.Clear();

            ViewBag.OrderId = order.Id;
            ViewBag.PaymentId = pending.PaymentId;
            ViewBag.Order = order;   // полный заказ — для чека покупателя
            return View();
        }

        if (check.AwaitingCapture)
        {
            // Деньги заморожены, но магазин ещё не подтвердил списание. Показываем «в обработке».
            _orderStore.SetPayment(order.Id, pending.PaymentId, "waiting_for_capture", paid: false);
            _cart.Clear();

            ViewBag.OrderId = order.Id;
            ViewBag.PaymentId = pending.PaymentId;
            ViewBag.Total = order.Total / 100.0; // храним копейки, показываем рубли
            return View("PaymentPending");
        }

        // created / pending / canceled — деньги не пришли, корзину сохраняем.
        _orderStore.SetPayment(order.Id, pending.PaymentId, check.Status, paid: false);

        ViewBag.PaymentId = pending.PaymentId;
        ViewBag.Status = check.Status;
        ViewBag.Reason = check.Status == "canceled"
            ? "Платёж отменён на стороне банка. Деньги не списаны — корзина осталась у вас."
            : "Платёж ещё не подтверждён платёжной системой. Деньги не списаны — корзина осталась у вас.";
        return View("PaymentError");
    }

    /// <summary>Сверяет платёж из ЮKassa с нашим заказом. null — всё совпало.</summary>
    private string? CheckConsistency(Order order, PaymentCheck check)
    {
        if (!string.IsNullOrEmpty(check.MetadataOrderId)
            && !string.Equals(check.MetadataOrderId, order.Id, StringComparison.Ordinal))
        {
            return "Платёж в системе ЮKassa создан для другого заказа. Обратитесь к нам по телефону.";
        }

        if (!string.IsNullOrEmpty(check.Currency)
            && !string.Equals(check.Currency, "RUB", StringComparison.OrdinalIgnoreCase))
        {
            return "Валюта платежа не совпадает с рублём — заказ не подтверждён, обратитесь к нам.";
        }

        if (check.Amount > 0m && Math.Abs((double)check.Amount - order.Total / 100.0) > 0.01)
        {
            return $"Сумма платежа ({check.Amount:0.00} ₽) не совпадает с суммой заказа ({order.Total / 100.0:0.00} ₽). "
                + "Обратитесь к нам по телефону — деньги вернём.";
        }

        return null;
    }

    /// <summary>
    /// Вебхук ЮKassa. Телу запроса не доверяем: по payment_id снова спрашиваем API
    /// и только потом обновляем заказ. Повторные уведомления безопасны.
    /// </summary>
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> YooKassaWebhook()
    {
        using var reader = new StreamReader(Request.Body, System.Text.Encoding.UTF8);
        var body = await reader.ReadToEndAsync(HttpContext.RequestAborted);

        var notification = YooKassaNotification.Parse(body);
        var paymentId = notification?.ObjectId;

        if (notification is null || notification.ObjectType != "payment" || string.IsNullOrEmpty(paymentId))
        {
            // Не наше событие (например, refund) — подтверждаем, чтобы ЮKassa не слала его снова.
            return Ok();
        }

        var order = _orderStore.FindByPaymentId(paymentId);
        if (order is null)
        {
            _logger.LogWarning("Вебхук {Event} для неизвестного платежа {PaymentId}", notification.EventType, paymentId);
            return Ok();
        }

        var check = await _payment.GetPaymentStatusAsync(paymentId, HttpContext.RequestAborted);
        if (!check.Verified)
        {
            _logger.LogWarning("Вебхук {Event}: не удалось перепроверить платёж {PaymentId} ({Error})",
                notification.EventType, paymentId, check.Error);
            return Ok();
        }

        var mismatch = CheckConsistency(order, check);
        if (mismatch is not null)
        {
            _logger.LogError("Вебхук: платёж {PaymentId} не соответствует заказу {OrderId}: {Mismatch}",
                paymentId, order.Id, mismatch);
            _orderStore.SetPayment(order.Id, paymentId, "mismatch", paid: false);
            return Ok();
        }

        _orderStore.SetPayment(order.Id, paymentId, check.Status, paid: check.Paid);
        return Ok();
    }

    /// <summary>Прямая ссылка на страницу «платёж в обработке» (для проверки вёрстки).
    /// Данные подставляются через ViewBag в PaymentSuccess.</summary>
    [HttpGet]
    public IActionResult PaymentPending()
    {
        ViewBag.Menu = _menu.Get();
        return View();
    }

    [HttpGet]
    public IActionResult PaymentError(string? payment_id)
    {
        ViewBag.Menu = _menu.Get();
        ViewBag.PaymentId = payment_id;
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
            StatusCode = 500
        });
    }
}
