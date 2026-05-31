namespace D2CompanionMvc.ViewModels;

public sealed class HomeIndexViewModel
{
    public string AppName { get; init; } = "D2 Companion";

    public string AssetVersion { get; init; } = "20260521-15";

    public string CatalogEndpoint { get; init; } = "/api/catalog";
}
