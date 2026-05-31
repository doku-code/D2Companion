namespace D2CompanionMvc.Options;

public sealed class CatalogStorageOptions
{
    public string DataDirectory { get; set; } = "data";

    public string PrimaryFileName { get; set; } = "catalog.json";

    public string SampleFileName { get; set; } = "sample-catalog.json";

    public string DatabaseFileName { get; set; } = "companion.sqlite";
}
