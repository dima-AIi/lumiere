using System.ComponentModel.DataAnnotations;

namespace Lumiere.Models;

/// <summary>Данные формы оформления заказа.</summary>
public class CheckoutForm
{
    [StringLength(36)]
    public string? SubmissionId { get; set; }
    // Поля nullable намеренно: иначе MVC считает их обязательными по умолчанию
    // и показывает лишние ошибки вроде «The Comment field is required».
    // Обязательность и формат проверяем сами (см. HomeController.Checkout).

    [StringLength(60, ErrorMessage = "Имя до 60 символов.")]
    public string? Name { get; set; }

    // Телефон проверяется и нормализуется вручную в HomeController (только цифры, 10–11 шт.),
    // поэтому здесь без атрибутов — чтобы не дублировать сообщения об ошибке.
    public string? Phone { get; set; }

    [StringLength(500, ErrorMessage = "Комментарий до 500 символов.")]
    public string? Comment { get; set; }

    public string DeliveryType { get; set; } = "pickup";

    [StringLength(200, ErrorMessage = "Адрес до 200 символов.")]
    public string? Address { get; set; }
}

/// <summary>Результат проверки платежа в ЮKassa.</summary>
public class PaymentCheck
{
    /// <summary>Ответ ЮKassa получен и платёж существует.</summary>
    public bool Verified { get; set; }

    /// <summary>Платёж действительно проведён (только succeeded).</summary>
    public bool Paid { get; set; }

    /// <summary>Платёж создан, но ждёт подтверждения от магазина (waiting_for_capture).</summary>
    public bool AwaitingCapture { get; set; }

    /// <summary>Статус платежа так, как его вернула ЮKassa.</summary>
    public string Status { get; set; } = "unknown";

    /// <summary>Идентификатор платежа из ответа ЮKassa (не то, что прислал браузер).</summary>
    public string? PaymentId { get; set; }

    /// <summary>Сумма платежа в рублях, как её видит ЮKassa.</summary>
    public decimal Amount { get; set; }

    /// <summary>Валюта платежа из ответа ЮKassa.</summary>
    public string Currency { get; set; } = "";

    /// <summary>Номер заказа из metadata платежа.</summary>
    public string? MetadataOrderId { get; set; }

    public string? Error { get; set; }
}

/// <summary>Результат попытки создать платёж в ЮKassa.</summary>
public class PaymentResult
{
    public bool Success { get; set; }
    public string? ConfirmUrl { get; set; }
    public string? PaymentId { get; set; }
    public string? Error { get; set; }

    /// <summary>true, если магазин не настроен и показан демо-режим.</summary>
    public bool DemoMode { get; set; }
}

/// <summary>Событие вебхука ЮKassa (payment.succeeded / payment.canceled и т.п.).</summary>
public class YooKassaNotification
{
    public string? EventType { get; set; }
    public string? ObjectType { get; set; }
    public string? ObjectId { get; set; }

    /// <summary>Разбирает JSON уведомления. null, если тело не распознано.</summary>
    public static YooKassaNotification? Parse(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            var evt = root.TryGetProperty("event", out var e) ? e.GetString() : null;
            var obj = root.TryGetProperty("object", out var o) ? o : default;
            var objType = obj.ValueKind == System.Text.Json.JsonValueKind.Object
                && obj.TryGetProperty("type", out var t) ? t.GetString() : null;
            var objId = obj.ValueKind == System.Text.Json.JsonValueKind.Object
                && obj.TryGetProperty("id", out var i) ? i.GetString() : null;
            return new YooKassaNotification { EventType = evt, ObjectType = objType, ObjectId = objId };
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}

/// <summary>Модель списка заказов для админки: собранная статистика + сами заказы.</summary>
public class AdminOrdersViewModel
{
    public List<Lumiere.Services.Order> Orders { get; set; } = new();
    public int TotalCount { get; set; }
    public int PaidCount { get; set; }
    public int DemoCount { get; set; }
    public decimal Revenue { get; set; }          // выручка в рублях
    public decimal RevenueToday { get; set; }
    public int CountToday { get; set; }

    // Текущие фильтры — чтобы подсветить активные и сохранить форму.
    public string Status { get; set; } = "all";
    public string Period { get; set; } = "all";
    public string? Q { get; set; }

    // Для вкладки «Обзор»: заказы, ждущие решения, и последние поступления.
    public int NeedsDecisionCount { get; set; }
    public List<Lumiere.Services.Order> Pending { get; set; } = new();
    public List<Lumiere.Services.Order> Recent { get; set; } = new();
}

/// <summary>Настройки кафе/проекта для вкладки «Настройки» (только просмотр).</summary>
public class AdminSettingsViewModel
{
    public string City { get; set; } = "";
    public string Address { get; set; } = "";
    public string Phone { get; set; } = "";
    public string WorkStart { get; set; } = "";
    public string WorkEnd { get; set; } = "";
    public decimal DeliveryPrice { get; set; }   // в рублях

    public bool YooConfigured { get; set; }
    public string ShopIdMasked { get; set; } = "";
    public bool AdminPasswordSet { get; set; }
}

/// <summary>Форма создания/редактирования позиции меню в админке.</summary>
public class AdminMenuItemViewModel
{
    public string? Id { get; set; }              // null — создание новой позиции
    public string? CurrentImg { get; set; }      // имя текущего файла фото (при редактировании)

    [Required(ErrorMessage = "Укажите название позиции")]
    [StringLength(120)]
    [Display(Name = "Название")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Укажите цену")]
    [Microsoft.AspNetCore.Mvc.ModelBinder(BinderType = typeof(DecimalPriceBinder))]
    [Range(0.01, 1_000_000, ErrorMessage = "Цена должна быть больше нуля")]
    public decimal PriceRubles { get; set; }            // рубли для формы; при сохранении → копейки (×100)

    [Required(ErrorMessage = "Укажите вес в граммах")]
    [Range(1, 20000, ErrorMessage = "Вес должен быть целым числом больше нуля")]
    public int Weight { get; set; }             // граммы; только число > 0, буквы и 0 запрещены

    public string? Desc { get; set; }         // состав / описание (необязательно)

    public string? Category { get; set; }      // выбранная категория (не required — можно задать только новую)
    public string? NewCategory { get; set; }     // если админ создаёт новую категорию

    public List<string> Categories { get; set; } = new();  // для выпадающего списка
}
