using System.Text.Json.Serialization;

namespace Lumiere.Models;

/// <summary>Позиция меню: одно блюдо или напиток.</summary>
public class MenuItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Desc { get; set; } = "";

    /// <summary>Цена в копейках, как в исходном API.</summary>
    public double Price { get; set; }

    /// <summary>Имя файла фотографии в /img.</summary>
    public string Img { get; set; } = "";
    public int? Weight { get; set; }

    [JsonIgnore]
    public double PriceRubles => Price / 100.0;

    [JsonIgnore]
    public string ImgUrl => string.IsNullOrEmpty(Img) ? "/img/placeholder.svg" : "/img/" + Img;

    [JsonIgnore]
    public string WeightText => Weight.HasValue ? $"{Weight} г" : "";
}

/// <summary>Категория меню (Кофе, Десерты, Пицца ...).</summary>
public class MenuCategory
{
    public string Title { get; set; } = "";
    public List<MenuItem> Items { get; set; } = new();
}

/// <summary>Корень файла data/menu.json.</summary>
public class MenuData
{
    public string Tagline { get; set; } = "";
    public string City { get; set; } = "";
    public string Address { get; set; } = "";
    public string Phone { get; set; } = "";
    public string WorkStart { get; set; } = "";
    public string WorkEnd { get; set; } = "";

    /// <summary>Стоимость доставки в копейках.</summary>
    public double DeliveryPrice { get; set; }
    public List<MenuCategory> Categories { get; set; } = new();

    [JsonIgnore]
    public IEnumerable<MenuItem> AllItems => Categories.SelectMany(c => c.Items);
}

/// <summary>Строка корзины.</summary>
public class CartLine
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public double Price { get; set; }
    public string Img { get; set; } = "";
    public int Qty { get; set; }

    public double Sum => Price * Qty;
    public string ImgUrl => string.IsNullOrEmpty(Img) ? "/img/placeholder.svg" : "/img/" + Img;
}

/// <summary>Корзина целиком.</summary>
public class Cart
{
    public List<CartLine> Lines { get; set; } = new();

    /// <summary>Стоимость доставки в копейках.</summary>
    public double DeliveryPrice { get; set; }
    public string DeliveryType { get; set; } = "pickup";

    public int Count => Lines.Sum(l => l.Qty);
    public double Subtotal => Lines.Sum(l => l.Sum);
    public bool IsDelivery => DeliveryType == "delivery";
    public double Delivery => IsDelivery ? DeliveryPrice : 0;
    public double Total => Subtotal + Delivery;

    public CartLine? Find(string id) => Lines.FirstOrDefault(l => l.Id == id);
}