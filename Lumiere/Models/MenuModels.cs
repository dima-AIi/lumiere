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
