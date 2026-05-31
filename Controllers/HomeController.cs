using Microsoft.AspNetCore.Mvc;
using D2CompanionMvc.Options;
using D2CompanionMvc.ViewModels;
using Microsoft.Extensions.Options;

namespace D2CompanionMvc.Controllers;

public class HomeController : Controller
{
    private readonly IOptionsMonitor<CompanionAppOptions> _options;

    public HomeController(IOptionsMonitor<CompanionAppOptions> options)
    {
        _options = options;
    }

    [HttpGet("/")]
    [HttpGet("/equipments")]
    public IActionResult Index()
    {
        var options = _options.CurrentValue;
        return View(new HomeIndexViewModel
        {
            AppName = options.AppName,
            AssetVersion = options.AssetVersion,
            CatalogEndpoint = options.CatalogEndpoint
        });
    }

    [HttpGet("/trade-preview")]
    public IActionResult TradePreview()
    {
        var options = _options.CurrentValue;
        return View(new HomeIndexViewModel
        {
            AppName = options.AppName,
            AssetVersion = options.AssetVersion,
            CatalogEndpoint = options.CatalogEndpoint
        });
    }

    [HttpGet("/chat-archive")]
    public IActionResult ChatArchive() => Redirect("/archives");
}
