using System.Text.Json;
using Lumiere.Models;

namespace Lumiere.Services;

/// <summary>
/// Корзина хранится в сессии пользователя (cookie), чтобы работать без базы данных.
/// </summary>
public class CartService
{
    public const string SessionKey = "lumiere.cart";
    private const int MaxQty = 99;

    private readonly IServiceProvider _provider;
    private readonly MenuRepository _menu;

    public CartService(IServiceProvider provider, MenuRepository menu)
    {
        _provider = provider;
        _menu = menu;
    }

    private ISession Session => _provider.GetRequiredService<IHttpContextAccessor>().HttpContext!.Session;

    /// <summary>Ключ незавершённого платежа — нужен для проверки возврата с ЮKassa.</summary>
    public const string PendingPaymentKey = "lumiere.pendingPayment";

    /// <summary>Данные о платеже, по которому клиент вернулся с сайта ЮKassa.</summary>
    public class PendingPayment
    {
        public string OrderId { get; set; } = "";
        public string PaymentId { get; set; } = "";
        public double Total { get; set; }
    }

    public Cart Get()
    {
        Cart cart;
        var raw = Session.GetString(SessionKey);
        if (string.IsNullOrEmpty(raw))
        {
            cart = new Cart();
        }
        else
        {
            try
            {
                cart = JsonSerializer.Deserialize<Cart>(raw) ?? new Cart();
            }
            catch (JsonException)
            {
                cart = new Cart();
            }
        }

        // Стоимость доставки всегда актуальна из меню — иначе после перезагрузки
        // страницы корзина «забывает» про доставку.
        cart.DeliveryPrice = _menu.Get().DeliveryPrice;
        return cart;
    }

    public void SetPendingPayment(PendingPayment pending) =>
        Session.SetString(PendingPaymentKey, JsonSerializer.Serialize(pending));

    public PendingPayment? GetPendingPayment()
    {
        var raw = Session.GetString(PendingPaymentKey);
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PendingPayment>(raw);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void ClearPendingPayment() => Session.Remove(PendingPaymentKey);

    private void Save(Cart cart)
    {
        Session.SetString(SessionKey, JsonSerializer.Serialize(cart));
    }

    public void SetDeliveryType(string type)
    {
        var cart = Get();
        cart.DeliveryType = type == "delivery" ? "delivery" : "pickup";
        cart.DeliveryPrice = _menu.Get().DeliveryPrice;
        Save(cart);
    }

    public void Add(MenuItem item, int qty = 1)
    {
        var cart = Get();
        var line = cart.Find(item.Id);

        if (line is null)
        {
            cart.Lines.Add(new CartLine
            {
                Id = item.Id,
                Name = item.Name,
                Price = item.Price,
                Img = item.Img,
                Qty = Math.Clamp(qty, 1, MaxQty)
            });
        }
        else
        {
            line.Qty = Math.Clamp(line.Qty + qty, 1, MaxQty);
        }

        Save(cart);
    }

    public void SetQty(string id, int qty)
    {
        var cart = Get();
        var line = cart.Find(id);
        if (line is null)
        {
            return;
        }

        if (qty <= 0)
        {
            cart.Lines.Remove(line);
        }
        else
        {
            line.Qty = Math.Clamp(qty, 1, MaxQty);
        }

        Save(cart);
    }

    public void Remove(string id)
    {
        var cart = Get();
        cart.Lines.RemoveAll(l => l.Id == id);
        Save(cart);
    }

    public void Clear()
    {
        Session.Remove(SessionKey);
    }
}