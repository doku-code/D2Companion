using D2CompanionMvc.Options;
using Microsoft.Extensions.Options;

namespace D2CompanionMvc.Services.Maintenance;

public sealed class RuntimeDataRetentionService
{
    internal const long MaxPreviousLogBytes = 5 * 1024 * 1024;
    internal const int MaxRootLogFiles = 1;
    internal static readonly string[] DisposableExtensions = [".log", ".jsonl"];

    private readonly IWebHostEnvironment _environment;
    private readonly IOptionsMonitor<CompanionAppOptions> _options;
    private readonly ILogger<RuntimeDataRetentionService> _logger;

    public RuntimeDataRetentionService(
        IWebHostEnvironment environment,
        IOptionsMonitor<CompanionAppOptions> options,
        ILogger<RuntimeDataRetentionService> logger)
    {
        _environment = environment;
        _options = options;
        _logger = logger;
    }

    public void CleanOnStartup()
    {
        try
        {
            var dataDir = ResolveDataDirectory();
            CleanDataDirectory(dataDir, _logger);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Runtime data cleanup skipped because the data folder could not be cleaned safely.");
        }
    }

    internal string ResolveDataDirectory()
    {
        var configured = _options.CurrentValue.Catalog.DataDirectory;
        return Path.GetFullPath(Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(_environment.ContentRootPath, configured));
    }

    public static void CleanDataDirectory(string dataDir, ILogger? logger = null)
    {
        var root = Path.GetFullPath(dataDir);
        Directory.CreateDirectory(root);

        RotateLog(Path.Combine(root, "styx.log"), logger);
        CleanRootLogs(root, logger);
        CleanDebugDirectory(Path.Combine(root, "debug"), logger);
    }

    private static void RotateLog(string logPath, ILogger? logger)
    {
        if (!File.Exists(logPath)) return;

        var previousPath = Path.Combine(Path.GetDirectoryName(logPath)!, "styx.previous.log");
        try
        {
            if (File.Exists(previousPath)) File.Delete(previousPath);
            File.Move(logPath, previousPath);

            if (new FileInfo(previousPath).Length > MaxPreviousLogBytes)
            {
                File.Delete(previousPath);
            }
        }
        catch (IOException ex)
        {
            logger?.LogWarning(ex, "Could not rotate previous Styx log at {LogPath}.", logPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger?.LogWarning(ex, "Could not rotate previous Styx log at {LogPath}.", logPath);
        }
    }

    private static void CleanRootLogs(string root, ILogger? logger)
    {
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(root, "styx.previous.log"),
        };

        var disposable = Directory.EnumerateFiles(root)
            .Where(path => DisposableExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Where(path => !keep.Contains(path))
            .ToArray();

        foreach (var path in disposable)
        {
            TryDeleteFile(path, logger);
        }

        var previousLogs = Directory.EnumerateFiles(root, "*.previous.log")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Skip(MaxRootLogFiles);
        foreach (var path in previousLogs)
        {
            TryDeleteFile(path, logger);
        }
    }

    private static void CleanDebugDirectory(string debugDir, ILogger? logger)
    {
        if (!Directory.Exists(debugDir)) return;

        foreach (var path in Directory.EnumerateFiles(debugDir, "*", SearchOption.AllDirectories))
        {
            if (IsDatabaseFile(path)) continue;
            TryDeleteFile(path, logger);
        }

        foreach (var dir in Directory.EnumerateDirectories(debugDir, "*", SearchOption.AllDirectories).OrderByDescending(path => path.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch (IOException ex)
            {
                logger?.LogWarning(ex, "Could not remove empty debug directory {Directory}.", dir);
            }
        }
    }

    private static bool IsDatabaseFile(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".sqlite-wal", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".sqlite-shm", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".sqlite3", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteFile(string path, ILogger? logger)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException ex)
        {
            logger?.LogWarning(ex, "Could not delete old runtime artifact {Path}.", path);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger?.LogWarning(ex, "Could not delete old runtime artifact {Path}.", path);
        }
    }
}
