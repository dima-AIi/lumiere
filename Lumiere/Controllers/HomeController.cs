using Lumiere.Services;
using Microsoft.AspNetCore.Mvc;
namespace Lumiere.Controllers;
public class HomeController(MenuRepository menu) : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        ViewBag.Menu = menu.Get();
        ViewBag.Highlights = menu.Highlights(3);
        return View();
    }
    [HttpGet]
    public IActionResult Menu()
    {
        ViewBag.Menu = menu.Get();
        ViewBag.Categories = menu.Get().Categories;
        ViewBag.Query = "";
        ViewBag.ItemCount = menu.Get().AllItems.Count();
        return View();
    }
    [HttpGet] public IActionResult Contacts() => Page();
    [HttpGet] public IActionResult Privacy() => Page();
    [HttpGet] public IActionResult CartPage() => Page();
    [HttpGet] public IActionResult Checkout() => Page();
    private ViewResult Page()
    {
        ViewBag.Menu = menu.Get();
        return View();
    }
}
