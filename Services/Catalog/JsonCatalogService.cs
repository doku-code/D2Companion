using System.Text.Json;
using D2CompanionMvc.Models.Catalog;
using D2CompanionMvc.Options;
using Microsoft.Extensions.Options;

namespace D2CompanionMvc.Services.Catalog;

public sealed class JsonCatalogService : ICatalogService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<JsonCatalogService> _logger;
    private readonly IOptionsMonitor<CompanionAppOptions> _options;

    public JsonCatalogService(
        IWebHostEnvironment environment,
        ILogger<JsonCatalogService> logger,
        IOptionsMonitor<CompanionAppOptions> options)
    {
        _environment = environment;
        _logger = logger;
        _options = options;
    }

    public string ResolveCatalogPath()
    {
        var catalogOptions = _options.CurrentValue.Catalog;
        var dataRoot = Path.Combine(_environment.ContentRootPath, catalogOptions.DataDirectory);
        var realCatalog = Path.Combine(dataRoot, catalogOptions.PrimaryFileName);
        if (File.Exists(realCatalog))
        {
            return realCatalog;
        }

        return Path.Combine(dataRoot, catalogOptions.SampleFileName);
    }

    public async Task<CompanionCatalog?> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        var path = ResolveCatalogPath();
        if (!File.Exists(path))
        {
            _logger.LogWarning("Catalog file was not found at {Path}", path);
            return null;
        }

        await using var stream = File.OpenRead(path);
        var catalog = await JsonSerializer.DeserializeAsync<CompanionCatalog>(stream, SerializerOptions, cancellationToken);
        if (catalog is null) return null;

        // If we loaded the sanitised sample file (rather than a real
        // catalog.json), mark the response so callers can tell. Even when
        // called directly (not via LiveCatalogService), the JSON loader
        // should never silently serve demo data as if it were live.
        var sampleFileName = _options.CurrentValue.Catalog.SampleFileName;
        var loadedSample = !string.IsNullOrEmpty(sampleFileName)
            && string.Equals(Path.GetFileName(path), sampleFileName, StringComparison.OrdinalIgnoreCase);
        if (loadedSample)
        {
            catalog.IsSampleData = true;
            catalog.SampleDataReason ??= $"Loaded from sanitised sample catalog '{sampleFileName}'.";
        }
        return catalog;
    }
}
