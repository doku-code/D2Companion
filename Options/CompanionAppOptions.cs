using D2CompanionMvc.Extensions.Styx.Options;

namespace D2CompanionMvc.Options;

public sealed class CompanionAppOptions
{
    public string AppName { get; set; } = "D2 Companion";

    public string AssetVersion { get; set; } = "20260521-15";

    public string CatalogEndpoint { get; set; } = "/api/catalog";

    public CatalogStorageOptions Catalog { get; set; } = new();

    public GameDataOptions GameData { get; set; } = new();

    public StyxOptions Styx { get; set; } = new();
}
