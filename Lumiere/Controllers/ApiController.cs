using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Lumiere.Services;

namespace Lumiere.Controllers;

[ApiController]
public sealed class ApiController(MenuRepository menu, CartService cart, PostgresState database) : ControllerBase
{
    [HttpGet("/healthz")]
    public IActionResult Health() => Ok(new { ready = !database.Enabled || database.GetDocument("menu") is not null });

    [HttpGet("/api/session")]
    public IActionResult SessionToken([FromServices] IAntiforgery antiforgery) =>
        Ok(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });

    public sealed record CartInput(List<CartEntry> Lines, string DeliveryType);
    public sealed record CartEntry(string Id, int Qty);

    [HttpPost("/api/cart/import")]
    [ValidateAntiForgeryToken]
    public IActionResult ImportCart(CartInput input)
    {
        if (input.Lines is null || input.Lines.Count is < 1 or > 100 || input.Lines.Any(l => l.Qty is < 1 or > 99))
            return BadRequest(new { error = "Проверьте количество позиций в корзине." });
        var items = input.Lines.Select(line => (line, item: menu.FindItem(line.Id))).ToList();
        if (items.Any(pair => pair.item is null))
            return Conflict(new { error = "Меню изменилось. Обновите меню и проверьте корзину." });
        cart.Clear();
        foreach (var (line, item) in items) cart.Add(item!, line.Qty);
        cart.SetDeliveryType(input.DeliveryType);
        return Ok(new { ok = true, total = cart.Get().Total });
    }

    [HttpGet("/media/{name}")]
    public async Task<IActionResult> Media(string name, CancellationToken ct)
    {
        if (!database.Enabled || !System.Text.RegularExpressions.Regex.IsMatch(name, @"^upload-[a-f0-9]{32}\.jpg$")) return NotFound();
        var bytes = await database.GetImageAsync(name, ct);
        return bytes is null ? NotFound() : File(bytes, "image/jpeg");
    }
}
