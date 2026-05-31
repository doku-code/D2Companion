using System.IO.Compression;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;

namespace D2CompanionMvc.Services.Assets;

public sealed class AssetPackService : IDisposable
{
    public const string PackFileName = "d2companion-assets.d2pack";
    public const string PackRelativePath = "wwwroot/assets/" + PackFileName;

    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<AssetPackService> _logger;
    private readonly FileExtensionContentTypeProvider _contentTypes;
    private readonly object _gate = new();
    private ZipArchive? _archive;
    private Dictionary<string, ZipArchiveEntry>? _entries;
    private bool _loadAttempted;

    public AssetPackService(IWebHostEnvironment environment, ILogger<AssetPackService> logger)
    {
        _environment = environment;
        _logger = logger;
        _contentTypes = new FileExtensionContentTypeProvider();
        _contentTypes.Mappings[".ico"] = "image/x-icon";
        _contentTypes.Mappings[".webp"] = "image/webp";
        _contentTypes.Mappings[".woff"] = "font/woff";
        _contentTypes.Mappings[".woff2"] = "font/woff2";
    }

    public async Task<bool> TryServeAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            return false;
        }

        var requestedPath = context.Request.Path.Value;
        var assetPath = NormalizeAssetRequestPath(requestedPath, out var isAssetRequest, out var isSafe);
        if (!isAssetRequest)
        {
            return false;
        }

        if (!isSafe || assetPath is null || assetPath.Equals("assets/" + PackFileName, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return true;
        }

        byte[]? bytes;
        long length;
        string entryName;
        lock (_gate)
        {
            var entries = EnsureIndex();
            if (entries is null || !entries.TryGetValue(assetPath, out var entry))
            {
                return false;
            }

            entryName = entry.FullName;
            length = entry.Length;
            using var entryStream = entry.Open();
            using var buffer = new MemoryStream();
            entryStream.CopyTo(buffer);
            bytes = buffer.ToArray();
        }

        if (!_contentTypes.TryGetContentType(entryName, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = contentType;
        context.Response.ContentLength = length;

        if (HttpMethods.IsHead(context.Request.Method))
        {
            return true;
        }

        await context.Response.Body.WriteAsync(bytes, cancellationToken);
        return true;
    }

    private Dictionary<string, ZipArchiveEntry>? EnsureIndex()
    {
        if (_loadAttempted)
        {
            return _entries;
        }

        _loadAttempted = true;
        var packPath = Path.Combine(_environment.ContentRootPath, "wwwroot", "assets", PackFileName);
        if (!File.Exists(packPath))
        {
            return null;
        }

        try
        {
            _archive = ZipFile.OpenRead(packPath);
            _entries = _archive.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .ToDictionary(
                    entry => entry.FullName.Replace('\\', '/'),
                    entry => entry,
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (InvalidDataException ex)
        {
            _logger.LogWarning(ex, "Asset pack could not be opened: {PackPath}", packPath);
            _archive?.Dispose();
            _archive = null;
            _entries = null;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Asset pack could not be read: {PackPath}", packPath);
            _archive?.Dispose();
            _archive = null;
            _entries = null;
        }

        return _entries;
    }

    private static string? NormalizeAssetRequestPath(string? requestedPath, out bool isAssetRequest, out bool isSafe)
    {
        isAssetRequest = false;
        isSafe = false;

        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            return null;
        }

        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(requestedPath).Replace('\\', '/');
        }
        catch (UriFormatException)
        {
            isAssetRequest = requestedPath.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase);
            return null;
        }
        if (!decoded.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        isAssetRequest = true;
        var normalized = decoded.TrimStart('/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[0].Equals("assets", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (parts.Any(part => part.Equals(".", StringComparison.Ordinal) || part.Equals("..", StringComparison.Ordinal)))
        {
            return null;
        }

        isSafe = true;
        return string.Join('/', parts);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _archive?.Dispose();
            _archive = null;
            _entries = null;
        }
    }
}
