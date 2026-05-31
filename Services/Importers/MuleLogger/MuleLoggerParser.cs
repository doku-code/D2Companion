using System.Text.Json;
using System.Text.RegularExpressions;
using D2CompanionMvc.Services.GameData;
using D2CompanionMvc.Services.Persistence;

namespace D2CompanionMvc.Services.Importers.MuleLogger;

internal static partial class MuleLoggerParser
{
    internal sealed class ParseResult
    {
        public List<ImportedCharacterSnapshot> Characters { get; } = [];

        public List<string> Warnings { get; } = [];
    }

    public static async Task<ParseResult> ParseFileAsync(
        string filePath,
        string sourceRoot,
        D2ItemLookupService itemLookup,
        CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(filePath, cancellationToken);
        var result = new ParseResult();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        var context = InferPathContext(filePath, sourceRoot);
        if (LooksLikeJsonDocument(text))
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                ParseJsonDocument(doc.RootElement, filePath, context, itemLookup, result);
            }
            catch (JsonException) when (text.Contains('\n') || text.Contains('\r'))
            {
                ParseJsonLines(text, filePath, context, itemLookup, result);
            }
        }
        else
        {
            ParseJsonLines(text, filePath, context, itemLookup, result);
        }

        return result;
    }

    private static void ParseJsonDocument(
        JsonElement root,
        string filePath,
        PathContext context,
        D2ItemLookupService itemLookup,
        ParseResult result)
    {
        if (root.ValueKind == JsonValueKind.Object && TryGetProperty(root, "items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            var characterMetadata = ReadCharacterMetadata(root);
            ParseItemArray(items.EnumerateArray(), filePath, context, characterMetadata, itemLookup, result);
            return;
        }

        if (root.ValueKind == JsonValueKind.Array)
        {
            ParseItemArray(root.EnumerateArray(), filePath, context, new Dictionary<string, CharacterMetadata>(), itemLookup, result);
            return;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            AddItemToResult(root, filePath, context, null, itemLookup, result);
            return;
        }

        result.Warnings.Add($"{Path.GetFileName(filePath)}: unsupported JSON root.");
    }

    private static void ParseJsonLines(
        string text,
        string filePath,
        PathContext context,
        D2ItemLookupService itemLookup,
        ParseResult result)
    {
        var lineNumber = 0;
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            lineNumber++;
            if (!rawLine.StartsWith("{", StringComparison.Ordinal))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(rawLine);
                AddItemToResult(doc.RootElement, filePath, context, null, itemLookup, result);
            }
            catch (JsonException ex)
            {
                result.Warnings.Add($"{Path.GetFileName(filePath)} line {lineNumber}: {ex.Message}");
            }
        }
    }

    private static void ParseItemArray(
        IEnumerable<JsonElement> items,
        string filePath,
        PathContext context,
        IReadOnlyDictionary<string, CharacterMetadata> characterMetadata,
        D2ItemLookupService itemLookup,
        ParseResult result)
    {
        foreach (var item in items)
        {
            CharacterMetadata? metadata = null;
            var account = ReadString(item, "account") ?? ReadString(item, "accountName") ?? context.Account;
            var character = ReadString(item, "character") ?? ReadString(item, "characterName") ?? context.Character;
            if (!string.IsNullOrWhiteSpace(account) && !string.IsNullOrWhiteSpace(character))
                characterMetadata.TryGetValue(CharacterKey(account, character), out metadata);

            AddItemToResult(item, filePath, context, metadata, itemLookup, result);
        }
    }

    private static void AddItemToResult(
        JsonElement item,
        string filePath,
        PathContext context,
        CharacterMetadata? metadata,
        D2ItemLookupService itemLookup,
        ParseResult result)
    {
        var description = ReadString(item, "description");
        if (string.IsNullOrWhiteSpace(description))
            return;

        var sourceFile = ReadString(item, "sourceFile") ?? PortableRelativePath(context.SourceRoot, filePath);
        var account = ReadString(item, "account") ?? ReadString(item, "accountName") ?? context.Account;
        var character = ReadString(item, "character") ?? ReadString(item, "characterName") ?? context.Character;
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(character))
        {
            result.Warnings.Add($"{Path.GetFileName(filePath)}: item skipped because account/character could not be inferred.");
            return;
        }

        var tail = ReadString(item, "tail") ?? ExtractTail(description);
        var tailParts = SplitTail(tail);
        var classId = ReadInt(item, "classid") ?? ReadInt(item, "classId") ?? TailInt(tailParts, 1) ?? 0;
        var location = ReadInt(item, "location") ?? TailInt(tailParts, 2) ?? 0;
        var x = ReadInt(item, "x") ?? TailInt(tailParts, 3) ?? 0;
        var y = ReadInt(item, "y") ?? TailInt(tailParts, 4) ?? 0;
        var rawCode = ReadString(item, "code") ?? ReadString(item, "baseCode");
        var baseItem = ResolveBaseItem(itemLookup, classId, rawCode);
        rawCode ??= baseItem?.Raw("code");
        var quality = ReadInt(item, "quality") ?? ReadInt(item, "itemQuality") ?? ReadInt(item, "qualityId") ?? 2;
        var uniqueId = ReadInt(item, "uniqueid") ?? ReadInt(item, "uniqueId") ?? -1;
        var setId = ReadInt(item, "setid") ?? ReadInt(item, "setId") ?? -1;
        var magicPrefixId = ReadInt(item, "magicPrefixId") ?? ReadInt(item, "magicprefixid") ?? -1;
        var magicSuffixId = ReadInt(item, "magicSuffixId") ?? ReadInt(item, "magicsuffixid") ?? -1;
        var gfx = ReadInt(item, "gfx") ?? ReadInt(item, "Gfx");
        var layout = ResolveLayout(item, baseItem);
        if (layout.DimensionFallbackUsed)
        {
            result.Warnings.Add($"{Path.GetFileName(filePath)}: {DeriveTitle(description, rawCode ?? "item")} could not resolve item dimensions; using 1x1 fallback.");
        }

        var image = ReadString(item, "image")
            ?? itemLookup.ResolveInventorySpriteKey(baseItem, quality, uniqueId, setId, rawCode, gfx);
        if (string.Equals(image, "xyz", StringComparison.OrdinalIgnoreCase) && baseItem is null && string.IsNullOrWhiteSpace(rawCode))
        {
            result.Warnings.Add($"{Path.GetFileName(filePath)}: {DeriveTitle(description, "item")} could not resolve an inventory sprite; using placeholder.");
        }
        var resolvedTint = itemLookup.ResolveInventoryTransformColor(quality, uniqueId, setId, magicPrefixId, magicSuffixId);
        var title = ReadString(item, "title") ?? ReadString(item, "name") ?? DeriveTitle(description, rawCode ?? image);
        var storage = ReadString(item, "storage") ?? StorageFromLocation(location);
        var sockets = ReadSockets(item, description);
        var seenAt = ReadDateTimeOffset(item, "seenAt")
            ?? ReadDateTimeOffset(item, "lastSeenAt")
            ?? File.GetLastWriteTimeUtc(filePath);

        var importedItem = new ImportedItemSnapshot
        {
            Gid = ReadStringOrNumber(item, "gid") ?? TailString(tailParts, 0) ?? $"{classId}:{location}:{x}:{y}:{title}",
            ClassId = classId,
            Title = title,
            Description = description,
            Image = string.IsNullOrWhiteSpace(image) ? "xyz" : image,
            ItemColor = resolvedTint ?? ReadInt(item, "itemColor") ?? ColorFromDescription(description),
            Storage = storage,
            Location = location,
            X = x,
            Y = y,
            PixelWidth = layout.PixelWidth,
            PixelHeight = layout.PixelHeight,
            GridWidth = layout.GridWidth,
            GridHeight = layout.GridHeight,
            Ethereal = ReadBool(item, "ethereal") ?? description.Contains("Ethereal", StringComparison.OrdinalIgnoreCase),
            SourceFile = sourceFile,
            RawSnapshotJson = item.GetRawText(),
            Sockets = sockets,
        };

        var snapshot = new ImportedCharacterSnapshot
        {
            Account = account,
            Character = character,
            Realm = ReadString(item, "realm") ?? metadata?.Realm ?? context.Realm,
            Mode = ReadString(item, "mode") ?? metadata?.Mode,
            Hardcore = ReadBool(item, "hardcore") ?? metadata?.Hardcore ?? false,
            Expansion = ReadBool(item, "expansion") ?? metadata?.Expansion ?? true,
            Ladder = ReadBool(item, "ladder") ?? metadata?.Ladder ?? false,
            Level = ReadInt(item, "level") ?? ReadInt(item, "characterLevel") ?? metadata?.Level,
            ClassId = ReadInt(item, "classId") ?? ReadInt(item, "characterClassId") ?? metadata?.ClassId,
            ClassName = ReadString(item, "className") ?? ReadString(item, "characterClassName") ?? metadata?.ClassName,
            SeenAt = seenAt,
            Source = "mulelogger",
            Items = [importedItem],
        };

        result.Characters.Add(snapshot);
    }

    private static Dictionary<string, CharacterMetadata> ReadCharacterMetadata(JsonElement root)
    {
        var metadata = new Dictionary<string, CharacterMetadata>(StringComparer.OrdinalIgnoreCase);
        if (!TryGetProperty(root, "accounts", out var accounts) || accounts.ValueKind != JsonValueKind.Array)
            return metadata;

        foreach (var accountElement in accounts.EnumerateArray())
        {
            var accountName = ReadString(accountElement, "name");
            if (string.IsNullOrWhiteSpace(accountName)
                || !TryGetProperty(accountElement, "characters", out var characters)
                || characters.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var characterElement in characters.EnumerateArray())
            {
                var characterName = ReadString(characterElement, "name");
                if (string.IsNullOrWhiteSpace(characterName))
                    continue;

                metadata[CharacterKey(accountName, characterName)] = new CharacterMetadata(
                    ReadString(characterElement, "realm"),
                    ReadString(characterElement, "mode"),
                    ReadBool(characterElement, "hardcore") ?? false,
                    ReadBool(characterElement, "expansion") ?? true,
                    ReadBool(characterElement, "ladder") ?? false,
                    ReadInt(characterElement, "level"),
                    ReadInt(characterElement, "classId"),
                    ReadString(characterElement, "className"));
            }
        }

        return metadata;
    }

    private static bool LooksLikeJsonDocument(string text)
    {
        var trimmed = text.TrimStart();
        return trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal);
    }

    private static PathContext InferPathContext(string filePath, string sourceRoot)
    {
        var file = new FileInfo(filePath);
        var character = Path.GetFileNameWithoutExtension(file.Name);
        var account = file.Directory?.Name ?? "ImportedMules";
        var realm = InferRealm(file.Directory);
        return new PathContext(sourceRoot, account, character, realm);
    }

    private static string? InferRealm(DirectoryInfo? directory)
    {
        for (var current = directory; current is not null; current = current.Parent)
        {
            if (IsKnownRealm(current.Name))
                return current.Name;
        }

        return null;
    }

    private static bool IsKnownRealm(string value) =>
        value.Equals("USEast", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("USWest", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Europe", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Asia", StringComparison.OrdinalIgnoreCase);

    private static string PortableRelativePath(string sourceRoot, string filePath)
    {
        try
        {
            return Path.GetRelativePath(sourceRoot, filePath).Replace('\\', '/');
        }
        catch (ArgumentException)
        {
            return Path.GetFileName(filePath);
        }
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            _ => null,
        };
    }

    private static string? ReadStringOrNumber(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static int? ReadInt(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;
        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)
            ? number
            : null;
    }

    private static bool? ReadBool(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.True) return true;
        if (value.ValueKind == JsonValueKind.False) return false;
        return value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;
    }

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement element, string name)
    {
        var value = ReadString(element, name);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? ExtractTail(string description)
    {
        var match = TailRegex().Match(description);
        return match.Success ? match.Groups["tail"].Value : null;
    }

    private static string[] SplitTail(string? tail)
        => string.IsNullOrWhiteSpace(tail) ? [] : tail.Split(':', StringSplitOptions.TrimEntries);

    private static string? TailString(string[] parts, int index)
        => index >= 0 && index < parts.Length && !string.IsNullOrWhiteSpace(parts[index]) ? parts[index] : null;

    private static int? TailInt(string[] parts, int index)
        => TailString(parts, index) is { } value && int.TryParse(value, out var parsed) ? parsed : null;

    private static int? GridFromPixels(int? pixels)
        => pixels.HasValue && pixels.Value > 0 ? Math.Max(1, (int)Math.Ceiling(pixels.Value / 28d)) : null;

    private static string StorageFromLocation(int location) => location switch
    {
        1 => "equipped",
        3 => "inventory",
        6 => "cube",
        7 => "stash",
        _ => "other",
    };

    private static D2TxtRow? ResolveBaseItem(D2ItemLookupService itemLookup, int classId, string? rawCode)
    {
        var byCode = string.IsNullOrWhiteSpace(rawCode) ? null : itemLookup.BaseItemByCode(rawCode);
        return byCode ?? itemLookup.BaseItemByClassId(classId);
    }

    private static ItemLayout ResolveLayout(JsonElement item, D2TxtRow? baseItem)
    {
        var explicitPixelWidth = ReadInt(item, "width") ?? ReadInt(item, "pixelWidth");
        var explicitPixelHeight = ReadInt(item, "height") ?? ReadInt(item, "pixelHeight");
        var txtGridWidth = baseItem?.IntOrNull("invwidth");
        var txtGridHeight = baseItem?.IntOrNull("invheight");

        var gridWidth = ReadInt(item, "gridWidth")
            ?? ReadInt(item, "invWidth")
            ?? GridFromPixels(explicitPixelWidth)
            ?? txtGridWidth
            ?? 1;
        var gridHeight = ReadInt(item, "gridHeight")
            ?? ReadInt(item, "invHeight")
            ?? GridFromPixels(explicitPixelHeight)
            ?? txtGridHeight
            ?? 1;

        gridWidth = Math.Max(1, gridWidth);
        gridHeight = Math.Max(1, gridHeight);

        var pixelWidth = Math.Max(1, explicitPixelWidth ?? gridWidth * 28);
        var pixelHeight = Math.Max(1, explicitPixelHeight ?? gridHeight * 28);
        var usedFallback = explicitPixelWidth is null
            && explicitPixelHeight is null
            && !HasPositiveInt(item, "gridWidth")
            && !HasPositiveInt(item, "gridHeight")
            && !HasPositiveInt(item, "invWidth")
            && !HasPositiveInt(item, "invHeight")
            && txtGridWidth is null
            && txtGridHeight is null;

        return new ItemLayout(pixelWidth, pixelHeight, gridWidth, gridHeight, usedFallback);
    }

    private static bool HasPositiveInt(JsonElement item, string name)
        => ReadInt(item, name) is int value && value > 0;

    private sealed record ItemLayout(
        int PixelWidth,
        int PixelHeight,
        int GridWidth,
        int GridHeight,
        bool DimensionFallbackUsed);

    private static string DeriveTitle(string description, string fallback)
    {
        var firstLine = description.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstLine))
            return fallback;

        var clean = ColorCodeRegex().Replace(firstLine, "").Trim();
        clean = ItemLevelSuffixRegex().Replace(clean, "").Trim();
        return string.IsNullOrWhiteSpace(clean) ? fallback : clean;
    }

    private static int ColorFromDescription(string description)
    {
        var match = ColorCodeRegex().Match(description);
        if (!match.Success)
            return -1;
        var value = match.Groups["color"].Value;
        return int.TryParse(value, out var parsed) ? parsed : -1;
    }

    private static IReadOnlyList<string> ReadSockets(JsonElement item, string description)
    {
        if (TryGetProperty(item, "sockets", out var sockets) && sockets.ValueKind == JsonValueKind.Array)
        {
            return sockets.EnumerateArray()
                .Select(socket => socket.ValueKind == JsonValueKind.String ? socket.GetString() : socket.GetRawText())
                .Where(socket => !string.IsNullOrWhiteSpace(socket))
                .Select(socket => socket!)
                .ToList();
        }

        var match = SocketedRegex().Match(description);
        if (!match.Success || !int.TryParse(match.Groups["count"].Value, out var count) || count <= 0)
            return [];

        return Enumerable.Repeat("gemsocket", Math.Min(count, 6)).ToList();
    }

    private static string CharacterKey(string account, string character)
        => $"{account}\u001f{character}";

    private sealed record CharacterMetadata(
        string? Realm,
        string? Mode,
        bool Hardcore,
        bool Expansion,
        bool Ladder,
        int? Level,
        int? ClassId,
        string? ClassName);

    private sealed record PathContext(string SourceRoot, string Account, string Character, string? Realm);

    [GeneratedRegex(@"\\xffc(?<color>[0-9a-zA-Z])")]
    private static partial Regex ColorCodeRegex();

    [GeneratedRegex(@"\$(?<tail>[0-9:,-]+)")]
    private static partial Regex TailRegex();

    [GeneratedRegex(@"\s+\([0-9]{1,3}\)$")]
    private static partial Regex ItemLevelSuffixRegex();

    [GeneratedRegex(@"Socketed\s*\((?<count>[0-9]+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex SocketedRegex();
}
