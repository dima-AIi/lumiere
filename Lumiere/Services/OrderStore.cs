using System.Text.Json;
using System.Text.Json.Serialization;
using Lumiere.Models;

namespace Lumiere.Services;

/// <summary>Сохранённый заказ.</summary>
public class Order
{
    public string Id { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public CheckoutForm Form { get; set; } = new();
    /// <summary>Сумма заказа в копейках — в тех же единицах, что и цены в строках Lines.</summary>
    public double Total { get; set; }
    public List<CartLine> Lines { get; set; } = new();

    /// <summary>Идентификатор платежа в ЮKassa (сохраняется после создания).</summary>
    public string? PaymentId { get; set; }

    /// <summary>Статус платежа: created / pending / succeeded / canceled / demo / unverified.</summary>
    public string PaymentStatus { get; set; } = "created";

    public DateTime? PaidAt { get; set; }

    /// <summary>Статус выполнения заказа на кухне: new / accepted / cooking / ready / done / declined.</summary>
    public string OrderStatus { get; set; } = "new";

    /// <summary>Когда админ принял или отклонил заказ (для контроля).</summary>
    public DateTime? DecidedAt { get; set; }

    /// <summary>Демо-заказ (ЮKassa не подключена) — чтобы будущая админка могла их отфильтровать.</summary>
    [JsonIgnore]
    public bool IsDemo => PaymentStatus == "demo" || (PaymentId?.StartsWith("demo-") ?? false);

    /// <summary>Заказ ждёт решения админа (ещё не принят и не отклонён).</summary>
    [JsonIgnore]
    public bool NeedsDecision => OrderStatus == "new";
}

/// <summary>
/// Хранит заказы в JSON-файле рядом с проектом.
/// Для школьной работы этого достаточно; в проде заменяется на БД.
/// </summary>
public class OrderStore
{
    private readonly string _file;
    private readonly ILogger<OrderStore> _log;
    private readonly PostgresState _database;
    private static readonly object Gate = new();

    public OrderStore(IWebHostEnvironment env, ILogger<OrderStore> log, PostgresState database)
    {
        _log = log;
        _database = database;
        var dir = Path.Combine(env.ContentRootPath, "App_Data");
        if (!database.Enabled) Directory.CreateDirectory(dir);
        _file = Path.Combine(dir, "orders.json");
    }

    public IReadOnlyList<Order> All()
    {
        if (_database.Enabled)
            return JsonSerializer.Deserialize<List<Order>>(_database.GetDocument("orders") ?? "[]")
                ?? throw new InvalidDataException("Invalid order document.");
        lock (Gate)
        {
            if (!File.Exists(_file))
            {
                return Array.Empty<Order>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<Order>>(File.ReadAllText(_file)) ?? new List<Order>();
            }
            catch (JsonException ex) { throw new InvalidDataException("Invalid orders file; original data preserved.", ex); }
        }
    }

    public Order Save(CheckoutForm form, Cart cart)
    {
        var order = new Order
        {
            Id = "LM-" + DateTime.Now.ToString("yyMMdd") + "-" + Random.Shared.Next(100000, 999999),
            CreatedAt = DateTime.Now,
            Form = form,
            Total = cart.Total, // копейки, как и цены в строках Lines
            Lines = cart.Lines
        };

        Mutate(all =>
        {
            var existing = string.IsNullOrEmpty(form.SubmissionId) ? null : all.FirstOrDefault(o => o.Form.SubmissionId == form.SubmissionId);
            if (existing is not null) order = existing;
            else all.Add(order);
        });

        _log.LogInformation("Сохранён заказ {Id} ({Lines} поз.)", order.Id, order.Lines.Count);
        return order;
    }

    public Order? Find(string id) => All().FirstOrDefault(o => o.Id == id);

    /// <summary>Ищет заказ по идентификатору платежа в ЮKassa.</summary>
    public Order? FindByPaymentId(string paymentId) =>
        All().FirstOrDefault(o => !string.IsNullOrEmpty(o.PaymentId)
                                   && string.Equals(o.PaymentId, paymentId, StringComparison.Ordinal));

    /// <summary>
    /// Записывает результат проверки платежа в заказ.
    /// Идемпотентно: повторный вызов (например, вебхук после возврата) не переписывает
    /// уже оплаченный заказ обратно в pending/canceled и не меняет время оплаты.
    /// </summary>
    public bool SetPayment(string orderId, string? paymentId, string status, bool paid)
    {
        var changed = false;
        Mutate(all =>
        {
            var order = all.FirstOrDefault(o => o.Id == orderId);
            if (order is null)
            {
                return;
            }

            // Успешную оплату нельзя «откатить» более поздним, менее успешным событием.
            if (order.PaymentStatus == "succeeded" && status != "succeeded")
            {
                return;
            }

            if (string.IsNullOrEmpty(order.PaymentId) && !string.IsNullOrEmpty(paymentId))
            {
                order.PaymentId = paymentId;
            }

            if (order.PaymentStatus != status)
            {
                order.PaymentStatus = status;
                changed = true;
            }

            if (paid && order.PaidAt is null)
            {
                order.PaidAt = DateTime.Now;
                changed = true;
            }
        });

        if (changed)
        {
            _log.LogInformation("Заказ {Id}: платёж {PaymentId} -> {Status} (paid={Paid})",
                orderId, paymentId, status, paid);
        }

        return changed;
    }

    private static readonly string[] KitchenStatuses =
        { "new", "accepted", "cooking", "ready", "done", "declined" };

    /// <summary>
    /// Решение админа по заказу: принять (accepted) или отклонить (declined).
    /// Фиксирует время решения. false — заказа нет.
    /// </summary>
    public bool Decide(string orderId, bool accept)
    {
        var target = accept ? "accepted" : "declined";
        var changed = false;
        Mutate(all =>
        {
            var order = all.FirstOrDefault(o => o.Id == orderId);
            if (order is null)
            {
                return;
            }

            order.OrderStatus = target;
            order.DecidedAt = DateTime.Now;
            changed = true;
        });

        if (changed)
        {
            _log.LogInformation("Заказ {Id}: админ {Action}", orderId, accept ? "принял" : "отклонил");
        }

        return changed;
    }

    /// <summary>Меняет статус выполнения заказа на кухне. false — заказа нет или статус некорректный.</summary>
    public bool SetOrderStatus(string orderId, string status)
    {
        if (!KitchenStatuses.Contains(status))
        {
            return false;
        }

        var changed = false;
        Mutate(all =>
        {
            var order = all.FirstOrDefault(o => o.Id == orderId);
            if (order is null)
            {
                return;
            }

            if (order.OrderStatus != status)
            {
                order.OrderStatus = status;
                if (status is "accepted" or "declined")
                {
                    order.DecidedAt = DateTime.Now;
                }
                changed = true;
            }
        });

        if (changed)
        {
            _log.LogInformation("Заказ {Id}: статус выполнения -> {Status}", orderId, status);
        }

        return changed;
    }

    private void Mutate(Action<List<Order>> change)
    {
        if (_database.Enabled) { _database.MutateOrders(change); return; }
        lock (Gate)
        {
            var all = new List<Order>();
            if (File.Exists(_file))
            {
                try
                {
                    all = JsonSerializer.Deserialize<List<Order>>(File.ReadAllText(_file)) ?? new List<Order>();
                }
                catch (JsonException ex) { throw new InvalidDataException("Invalid orders file; refusing to overwrite original data.", ex); }
            }

            change(all);
            File.WriteAllText(_file, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
