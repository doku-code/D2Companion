using D2CompanionMvc.Services.GameData;
using D2CompanionMvc.Services.Persistence;

namespace D2CompanionMvc.Services.Importers.MuleLogger;

public sealed class MuleLoggerImportService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt",
        ".json",
        ".jsonl",
    };

    private readonly SqliteCompanionStore _store;
    private readonly D2ItemLookupService _itemLookup;
    private readonly ILogger<MuleLoggerImportService> _logger;

    public MuleLoggerImportService(
        SqliteCompanionStore store,
        D2ItemLookupService itemLookup,
        ILogger<MuleLoggerImportService> logger)
    {
        _store = store;
        _itemLookup = itemLookup;
        _logger = logger;
    }

    public async Task<MuleLoggerImportSummary> ImportAsync(string? sourcePath, CancellationToken cancellationToken = default)
    {
        var summary = new MuleLoggerImportSummary();
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            summary.Errors.Add("Choose a MuleLogger file or folder path.");
            return summary;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(sourcePath.Trim().Trim('"'));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            summary.Errors.Add("The source path is not valid.");
            return summary;
        }

        summary.SourcePath = fullPath;
        var isFile = File.Exists(fullPath);
        var isDirectory = Directory.Exists(fullPath);
        if (!isFile && !isDirectory)
        {
            summary.Errors.Add("The source path does not exist.");
            return summary;
        }

        List<string> files;
        try
        {
            files = DiscoverFiles(fullPath, isFile).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            summary.Errors.Add("The source path could not be read.");
            _logger.LogWarning(ex, "Could not enumerate MuleLogger source path {Path}", fullPath);
            return summary;
        }

        if (files.Count == 0)
        {
            summary.Errors.Add("No supported MuleLogger files were found. Supported extensions are .txt, .json, and .jsonl.");
            return summary;
        }

        var characters = new List<ImportedCharacterSnapshot>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var parsed = await MuleLoggerParser.ParseFileAsync(file, isDirectory ? fullPath : Path.GetDirectoryName(fullPath)!, _itemLookup, cancellationToken);
                if (parsed.Characters.Count == 0)
                {
                    summary.SkippedFiles++;
                    AddMessage(summary.Warnings, $"{Path.GetFileName(file)}: no importable item rows found.");
                    continue;
                }

                characters.AddRange(parsed.Characters);
                foreach (var warning in parsed.Warnings)
                    AddMessage(summary.Warnings, warning);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                summary.SkippedFiles++;
                AddMessage(summary.Warnings, $"{Path.GetFileName(file)}: {ex.Message}");
                _logger.LogWarning(ex, "Could not import MuleLogger file {File}", file);
            }
        }

        if (characters.Count == 0)
        {
            summary.Errors.Add("No characters or items could be imported from the selected path.");
            return summary;
        }

        var merged = MergeCharacters(characters);
        await _store.SaveImportedCharactersAsync(merged, cancellationToken);

        summary.ImportedAccounts = merged.Select(c => c.Account).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        summary.ImportedCharacters = merged.Count;
        summary.ImportedItems = merged.Sum(c => c.Items.Count);
        summary.Success = summary.ImportedCharacters > 0 && summary.ImportedItems > 0;
        summary.RefreshRecommended = summary.Success;
        return summary;
    }

    private static IEnumerable<string> DiscoverFiles(string path, bool isFile)
    {
        if (isFile)
        {
            if (SupportedExtensions.Contains(Path.GetExtension(path)))
                yield return path;
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories))
        {
            if (SupportedExtensions.Contains(Path.GetExtension(file)))
                yield return file;
        }
    }

    private static List<ImportedCharacterSnapshot> MergeCharacters(IEnumerable<ImportedCharacterSnapshot> snapshots)
    {
        var byCharacter = new Dictionary<string, List<ImportedCharacterSnapshot>>(StringComparer.OrdinalIgnoreCase);
        foreach (var snapshot in snapshots)
        {
            var key = $"{snapshot.Account}\u001f{snapshot.Character}";
            if (!byCharacter.TryGetValue(key, out var list))
            {
                list = [];
                byCharacter[key] = list;
            }

            list.Add(snapshot);
        }

        return byCharacter.Values.Select(list =>
        {
            var latest = list.OrderByDescending(c => c.SeenAt).First();
            var items = list.SelectMany(c => c.Items)
                .GroupBy(item => string.IsNullOrWhiteSpace(item.Gid)
                    ? $"{item.SourceFile}:{item.ClassId}:{item.Location}:{item.X}:{item.Y}:{item.Title}"
                    : $"{item.SourceFile}:{item.Gid}:{item.ClassId}:{item.Location}:{item.X}:{item.Y}",
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToList();

            return new ImportedCharacterSnapshot
            {
                Account = latest.Account,
                Character = latest.Character,
                Realm = latest.Realm,
                Mode = latest.Mode,
                Hardcore = latest.Hardcore,
                Expansion = latest.Expansion,
                Ladder = latest.Ladder,
                Level = latest.Level,
                ClassId = latest.ClassId,
                ClassName = latest.ClassName,
                SeenAt = latest.SeenAt,
                Source = "mulelogger",
                Items = items,
            };
        }).ToList();
    }

    private static void AddMessage(List<string> messages, string message)
    {
        if (messages.Count < 25)
            messages.Add(message);
    }
}
