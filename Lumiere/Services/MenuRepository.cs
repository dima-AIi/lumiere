using System.Text.Json;
using Lumiere.Models;
namespace Lumiere.Services;
/// <summary>Read-only deployment menu. No database and no save method.</summary>
public sealed class MenuRepository(IWebHostEnvironment environment)
{
    private readonly Lazy<MenuData> _menu = new(() =>
    {
        var file = Path.Combine(environment.ContentRootPath, "wwwroot", "data", "menu.json");
        var data = JsonSerializer.Deserialize<MenuData>(File.ReadAllText(file), new JsonSerializerOptions
        { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Menu is missing.");
        foreach (var item in data.AllItems) item.Price = Math.Round(item.Price * 100, MidpointRounding.AwayFromZero);
        data.DeliveryPrice = Math.Round(data.DeliveryPrice * 100, MidpointRounding.AwayFromZero);
        return data;
    });
    public MenuData Get() => _menu.Value;
    public List<MenuItem> Highlights(int perCategory = 3) => Get().Categories
        .SelectMany(category => category.Items.Take(perCategory)).ToList();
}
