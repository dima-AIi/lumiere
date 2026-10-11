using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Lumiere.Models;

namespace Lumiere.Services;

/// <summary>
/// Создаёт платёж в ЮKassa (REST API v3, POST /v3/payments).
/// Деньги поступают на расчётный счёт магазина, карта привязывается в личном кабинете ЮKassa.
/// Если ключи не заданы в appsettings.json — работает демо-режим без реального списания.
/// </summary>
public class YooKassaService
{
    private readonly IConfiguration _config;
    private readonly ILogger<YooKassaService> _log;
    private readonly HttpClient _http;

    public YooKassaService(IConfiguration config, ILogger<YooKassaService> log)
    {
        _config = config;
        _log = log;
        _http = new HttpClient { BaseAddress = new Uri("https://api.yookassa.ru/v3/") };
    }

    public string ShopId => _config["YooKassa:ShopId"] ?? "";
    public string SecretKey => _config["YooKassa:SecretKey"] ?? "";
    public string ReturnUrl => _config.GetValue("YooKassa:ReturnUrl", "http://localhost:5055/Home/PaymentSuccess")!;

    /// <summary>Ключи не заданы — показываем демо-страницу вместо реального платежа.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ShopId) && !string.IsNullOrWhiteSpace(SecretKey);

    /// <param name="orderId">Номер заказа — уходит в metadata, чтобы при возврате
    /// можно было убедиться, что платёж создан именно для этого заказа.</param>
    public async Task<PaymentResult> CreatePaymentAsync(
        Cart cart,
        CheckoutForm form,
        string orderId,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _log.LogInformation("ЮKassa не настроена — демо-режим оплаты на {Total} руб.", cart.Total / 100.0);
            return new PaymentResult { Success = true, DemoMode = true, PaymentId = Guid.NewGuid().ToString() };
        }

        var idempotence = Guid.NewGuid().ToString();
        var payload = new Dictionary<string, object?>
        {
            ["amount"] = new Dictionary<string, string>
            {
                ["value"] = Money(cart.Total),
                ["currency"] = "RUB"
            },
            // Одноэтапная оплата: деньги списываются сразу, платёж приходит в статусе
            // succeeded. Поэтому ветка waiting_for_capture в HomeController при обычном
            // возврате не срабатывает — страница «в обработке» оставлена для вёрстки
            // и крайнего случая (если когда-нибудь переключитесь на capture:false).
            ["capture"] = true,
            ["confirmation"] = new Dictionary<string, string>
            {
                ["type"] = "redirect",
                ["return_url"] = ReturnUrl
            },
            ["description"] = $"Lumière: {cart.Count} поз., {Money(cart.Total)} руб.",
            ["metadata"] = new Dictionary<string, string>
            {
                ["order_id"] = orderId,
                ["customer_name"] = form.Name ?? "",
                ["phone"] = form.Phone ?? "",
                ["delivery_type"] = form.DeliveryType,
                ["comment"] = form.Comment ?? ""
            },
            ["receipt"] = BuildReceipt(cart)
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "payments")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ShopId}:{SecretKey}")));
        request.Headers.Add("Idempotence-Key", idempotence);

        try
        {
            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _log.LogError("ЮKassa вернула {Code}: {Body}", (int)response.StatusCode, body);
                return new PaymentResult { Success = false, Error = HumanizeError(body) };
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var url = root.TryGetProperty("confirmation", out var conf)
                      && conf.TryGetProperty("confirmation_url", out var u) ? u.GetString() : null;
            var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;

            return new PaymentResult { Success = true, ConfirmUrl = url, PaymentId = id };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Ошибка запроса к ЮKassa");
            return new PaymentResult { Success = false, Error = "Не удалось связаться с платёжной системой: " + ex.Message };
        }
    }

    /// <summary>
    /// Проверяет платёж по его id (GET /v3/payments/{id}).
    /// Нужна после возврата с сайта ЮKassa: сам редирект ещё не значит, что деньги пришли.
    /// </summary>
    public async Task<PaymentCheck> GetPaymentStatusAsync(string paymentId, CancellationToken ct = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(paymentId))
        {
            return new PaymentCheck { Verified = false, Status = "unknown" };
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"payments/{Uri.EscapeDataString(paymentId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ShopId}:{SecretKey}")));

        try
        {
            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning("Проверка платежа {Id}: код {Code}", paymentId, (int)response.StatusCode);
                return new PaymentCheck { Verified = false, Status = "unknown", Error = HumanizeError(body) };
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "unknown" : "unknown";

            var amount = 0m;
            var currency = "";
            if (root.TryGetProperty("amount", out var amt))
            {
                if (amt.TryGetProperty("value", out var v)
                    && decimal.TryParse(v.GetString(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                {
                    amount = parsed;
                }

                if (amt.TryGetProperty("currency", out var c))
                {
                    currency = c.GetString() ?? "";
                }
            }

            string? metadataOrderId = null;
            if (root.TryGetProperty("metadata", out var meta)
                && meta.TryGetProperty("order_id", out var oid))
            {
                metadataOrderId = oid.GetString();
            }

            return new PaymentCheck
            {
                Verified = true,
                Paid = status == "succeeded",
                AwaitingCapture = status == "waiting_for_capture",
                Status = status,
                PaymentId = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null,
                Amount = amount,
                Currency = currency,
                MetadataOrderId = metadataOrderId
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Не удалось проверить платёж {Id}", paymentId);
            return new PaymentCheck { Verified = false, Status = "unknown", Error = ex.Message };
        }
    }

    private static string Money(double kopecks) =>
        (kopecks / 100.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
/// <summary>Запрос чека (54-ФЗ). В тестовом режиме ЮKassa принимает фиктивные данные.</summary>
    private static Dictionary<string, object?> BuildReceipt(Cart cart)
    {
        var lines = cart.Lines
            .Select(l => (object)new Dictionary<string, object?>
            {
                ["description"] = l.Name.Length > 100 ? l.Name[..100] : l.Name,
                ["quantity"] = l.Qty.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                ["amount"] = new Dictionary<string, string>
                {
                    ["value"] = Money(l.Sum),
                    ["currency"] = "RUB"
                },
                ["vat_code"] = 1, // НДС 20%
                ["payment_subject"] = "commodity",
                ["payment_mode"] = "full_payment"
            })
            .ToList();

        return new Dictionary<string, object?>
        {
            ["customer"] = new Dictionary<string, string> { ["email"] = "no-reply@lumiere.local" },
            ["items"] = lines,
            ["tax_system_code"] = "usn_income"
        };
    }

    /// <summary>Превращает JSON-ошибку ЮKassa в читаемый текст для пользователя.</summary>
    private static string HumanizeError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("description", out var desc) && !string.IsNullOrWhiteSpace(desc.GetString()))
            {
                return desc.GetString()!;
            }
        }
        catch (JsonException)
        {
            // ответ не в формате JSON — вернём общий текст ниже
        }

        return "Платёжная система отклонила запрос. Проверьте настройки ЮKassa в appsettings.json.";
    }
}