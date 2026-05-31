using System.IO.Compression;
using D2CompanionMvc.Services.Assets;

namespace D2CompanionMvc.Services.GameData;

/// <summary>
/// Convenience accessors for item-related lookups: base items, unique/set/runeword
/// flavour rows, sprite-key resolution, etc. Everything here delegates to
/// <see cref="D2GameData"/> — this service is the friendly surface adapter code
/// should call instead of poking the raw tables.
/// </summary>
public sealed class D2ItemLookupService
{
    private readonly D2GameData _data;
    private readonly Lazy<HashSet<string>> _itemAssetKeys;
    private readonly Lazy<HashSet<string>> _gfxAssetKeys;

    public D2ItemLookupService(D2GameData data)
    {
        _data = data;
        _itemAssetKeys = new Lazy<HashSet<string>>(() => LoadAssetKeys("items", "*.png"));
        _gfxAssetKeys = new Lazy<HashSet<string>>(() => LoadAssetKeys("gfx", "*"));
    }

    public sealed record InventorySpriteCandidate(
        string Source,
        string Key,
        bool AssetExists,
        bool Accepted,
        string? Reason);

    public sealed record InventorySpriteResolution(
        string ImageKey,
        IReadOnlyList<InventorySpriteCandidate> Candidates);

    /// <summary>Look up a base item by its D2 code (e.g. "7m7" for Ogre Maul).</summary>
    public D2TxtRow? BaseItemByCode(string code) =>
        code is null ? null
        : _data.BaseItemsByCode.TryGetValue(code, out var row) ? row
        : null;

    /// <summary>
    /// Look up a base item by its classid (the merged Weapons+Armor+Misc row index Styx uses).
    /// Returns null if out of range — caller is responsible for falling back.
    /// </summary>
    public D2TxtRow? BaseItemByClassId(int classId) =>
        classId >= 0 && classId < _data.BaseItemsByInternalClassId.Count ? _data.BaseItemsByInternalClassId[classId] : null;

    /// <summary>
    /// True when two base item codes identify the same item or the same
    /// normal/exceptional/elite upgrade family.
    /// </summary>
    public bool BaseCodesAreCompatible(string? rowCode, string? itemCode)
    {
        if (string.IsNullOrWhiteSpace(rowCode) || string.IsNullOrWhiteSpace(itemCode))
            return false;
        if (string.Equals(rowCode, itemCode, StringComparison.OrdinalIgnoreCase))
            return true;

        var rowFamily = UpgradeFamilyCodes(rowCode);
        if (rowFamily.Count == 0) return false;
        var itemFamily = UpgradeFamilyCodes(itemCode);
        if (itemFamily.Count == 0) return false;
        return rowFamily.Any(itemFamily.Contains);
    }

    public IReadOnlyCollection<string> UpgradeFamilyCodes(string? code)
    {
        var family = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(code)) return family;
        var row = BaseItemByCode(code);
        if (row is null)
        {
            family.Add(code);
            return family;
        }

        AddCode(family, row.Raw("code"));
        AddCode(family, row.Raw("normcode"));
        AddCode(family, row.Raw("ubercode"));
        AddCode(family, row.Raw("ultracode"));
        return family;
    }

    /// <summary>Look up an ItemTypes row by its 4-char code (e.g. "shld" for shields).</summary>
    public D2TxtRow? ItemTypeByCode(string code) =>
        code is null ? null
        : _data.ItemTypesByCode.TryGetValue(code, out var row) ? row : null;

    /// <summary>
    /// Walks the ItemTypes equivalence chain to test "is this item a member of typeCode?".
    /// Used by tooltip code to ask "is this a weapon?" / "is this a shield?" / etc.
    /// </summary>
    public bool BaseItemIsType(D2TxtRow baseItem, string typeCode)
    {
        if (baseItem is null) return false;
        var rootType = baseItem.Raw("type");
        if (rootType is null) return false;

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        stack.Push(rootType);
        while (stack.Count > 0)
        {
            var t = stack.Pop();
            if (!visited.Add(t)) continue;
            if (string.Equals(t, typeCode, StringComparison.OrdinalIgnoreCase)) return true;
            var row = ItemTypeByCode(t);
            if (row is null) continue;
            var e1 = row.Raw("Equiv1"); if (e1 is not null) stack.Push(e1);
            var e2 = row.Raw("Equiv2"); if (e2 is not null) stack.Push(e2);
        }
        return false;
    }

    /// <summary>Look up a unique item by its row index (Styx's `uniqueid`).</summary>
    public D2TxtRow? UniqueById(int id) =>
        id >= 0 && id < _data.UniqueItemsByInternalId.Count ? _data.UniqueItemsByInternalId[id] : null;

    /// <summary>Look up a set item by its row index (Styx's `setid`).</summary>
    public D2TxtRow? SetItemById(int id) =>
        id >= 0 && id < _data.SetItemsByInternalId.Count ? _data.SetItemsByInternalId[id] : null;

    /// <summary>Look up a runeword by its row index (Styx's `runewordid`).</summary>
    public D2TxtRow? RuneWordById(int id) =>
        id >= 0 && id < _data.Runes.Rows.Count ? _data.Runes.Rows[id] : null;

    /// <summary>Look up a magic prefix by its Styx/D2 internal affix id.</summary>
    public D2TxtRow? MagicPrefixById(int id) =>
        id >= 0 && id < _data.MagicPrefixByInternalId.Count ? _data.MagicPrefixByInternalId[id] : null;

    /// <summary>Look up a magic suffix by its Styx/D2 internal affix id.</summary>
    public D2TxtRow? MagicSuffixById(int id) =>
        id >= 0 && id < _data.MagicSuffixByInternalId.Count ? _data.MagicSuffixByInternalId[id] : null;

    public D2TxtRow? RarePrefixById(int id) =>
        id >= 0 && id < _data.RarePrefix.Rows.Count ? _data.RarePrefix.Rows[id] : null;

    public D2TxtRow? RareSuffixById(int id) =>
        id >= 0 && id < _data.RareSuffix.Rows.Count ? _data.RareSuffix.Rows[id] : null;

    /// <summary>Look up a gem/rune by its code (e.g. "r19" for Sol rune).</summary>
    public D2TxtRow? GemByCode(string code) =>
        code is null ? null
        : _data.GemsByCode.TryGetValue(code, out var row) ? row : null;

    /// <summary>
    /// Resolve the sprite key used by the front-end renderer for this item.
    ///
    /// Priority: unique-specific invfile -> set-specific invfile -> base normcode ->
    /// base code -> raw code -> neutral placeholder.
    /// </summary>
    public string ResolveInventorySpriteKey(D2TxtRow? baseItem, int quality, int uniqueId, int setId, string? rawCode, int? gfx = null)
    {
        return ResolveInventorySprite(baseItem, quality, uniqueId, setId, rawCode, gfx).ImageKey;
    }

    public InventorySpriteResolution ResolveInventorySprite(D2TxtRow? baseItem, int quality, int uniqueId, int setId, string? rawCode, int? gfx = null)
    {
        var candidates = new List<InventorySpriteCandidate>();
        var uniqueRow = quality == 7 /* unique */ ? UniqueById(uniqueId) : null;
        var setRow = quality == 5 /* set */ ? SetItemById(setId) : null;
        var uniqueFlavorBase = !string.IsNullOrWhiteSpace(uniqueRow?.Raw("code"))
            ? BaseItemByCode(uniqueRow!.Raw("code")!)
            : null;
        var setFlavorBase = !string.IsNullOrWhiteSpace(setRow?.Raw("item"))
            ? BaseItemByCode(setRow!.Raw("item")!)
            : null;
        var uniqueIsUpgraded = IsUpgradedFlavorBase(uniqueRow?.Raw("code"), baseItem?.Raw("code"));
        var preferUniqueFamilyBeforeBaseInvfile = uniqueIsUpgraded;
        var preferSetFamilyBeforeBaseInvfile = IsUpgradedFlavorBase(setRow?.Raw("item"), baseItem?.Raw("code"));

        bool Try(string source, string? key, out string accepted, bool normalize = false, D2TxtRow? contextBaseItem = null)
        {
            accepted = "";
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            var candidate = normalize ? NormalizeInventoryFileKey(key, contextBaseItem) : key;
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            var exists = ImageAssetExists(candidate);
            candidates.Add(new InventorySpriteCandidate(
                source,
                candidate,
                exists,
                exists,
                exists ? null : "asset key not found"));

            if (!exists)
            {
                return false;
            }

            accepted = candidate;
            return true;
        }

        if (quality == 7 /* unique */)
        {
            if (TryInventoryFile("UniqueItems.invfile", uniqueRow?.Raw("invfile"), out var selected, uniqueFlavorBase ?? baseItem)) return new(selected, candidates);
            if (TryInventoryFile("BaseItem.uniqueinvfile", baseItem?.Raw("uniqueinvfile"), out selected, baseItem)) return new(selected, candidates);
            if (uniqueIsUpgraded)
            {
                if (TryInventoryFile("UniqueFlavor.uniqueinvfile", uniqueFlavorBase?.Raw("uniqueinvfile"), out selected, uniqueFlavorBase)) return new(selected, candidates);
                if (TryInventoryFile("UniqueFlavor.invfile", uniqueFlavorBase?.Raw("invfile"), out selected, uniqueFlavorBase)) return new(selected, candidates);
                if (TryInventoryFile("UpgradeFamily.uniqueinvfile", ResolveFamilyInventoryFile(baseItem, "uniqueinvfile"), out selected, baseItem)) return new(selected, candidates);
            }
        }
        if (quality == 5 /* set */)
        {
            if (TryInventoryFile("SetItems.invfile", setRow?.Raw("invfile"), out var selected, setFlavorBase ?? baseItem)) return new(selected, candidates);
            if (TryInventoryFile("BaseItem.setinvfile", baseItem?.Raw("setinvfile"), out selected, baseItem)) return new(selected, candidates);
            if (IsUpgradedFlavorBase(setRow?.Raw("item"), baseItem?.Raw("code")) &&
                TryInventoryFile("UpgradeFamily.setinvfile", ResolveFamilyInventoryFile(baseItem, "setinvfile"), out selected, baseItem)) return new(selected, candidates);
        }

        if (Try("ItemTypes.InvGfx", ResolveVariableInventorySpriteKey(baseItem, gfx), out var variant)) return new(variant, candidates);
        if (Try("BaseItem.code", baseItem?.Raw("code"), out var codeSelected)) return new(codeSelected, candidates);
        if (Try("Raw code", rawCode, out var rawSelected)) return new(rawSelected, candidates);
        if (quality == 7 /* unique */ && preferUniqueFamilyBeforeBaseInvfile &&
            TryInventoryFile("UpgradeFamily.uniqueinvfile", ResolveFamilyInventoryFile(baseItem, "uniqueinvfile"), out var familyUniqueSelected, baseItem)) return new(familyUniqueSelected, candidates);
        if (quality == 5 /* set */ && preferSetFamilyBeforeBaseInvfile &&
            TryInventoryFile("UpgradeFamily.setinvfile", ResolveFamilyInventoryFile(baseItem, "setinvfile"), out var familySetSelected, baseItem)) return new(familySetSelected, candidates);
        if (Try("BaseItem.invfile", baseItem?.Raw("invfile"), out var invSelected, normalize: true, contextBaseItem: baseItem)) return new(invSelected, candidates);
        if (quality == 7 /* unique */ && !preferUniqueFamilyBeforeBaseInvfile &&
            TryInventoryFile("UpgradeFamily.uniqueinvfile", ResolveFamilyInventoryFile(baseItem, "uniqueinvfile"), out familyUniqueSelected, baseItem)) return new(familyUniqueSelected, candidates);
        if (quality == 5 /* set */ && !preferSetFamilyBeforeBaseInvfile &&
            TryInventoryFile("UpgradeFamily.setinvfile", ResolveFamilyInventoryFile(baseItem, "setinvfile"), out familySetSelected, baseItem)) return new(familySetSelected, candidates);
        if (Try("BaseItem.normcode", baseItem?.Raw("normcode"), out var normSelected)) return new(normSelected, candidates);
        if (Try("Placeholder", "xyz", out var placeholder)) return new(placeholder, candidates);

        return new("xyz", candidates);

        bool TryInventoryFile(string source, string? key, out string accepted, D2TxtRow? contextBaseItem = null)
        {
            if (Try(source + ".normalized", key, out accepted, normalize: true, contextBaseItem)) return true;
            return Try(source, key, out accepted);
        }
    }

    public int? ResolveInventoryTransformColor(
        int quality,
        int uniqueId,
        int setId,
        int magicPrefixId = -1,
        int magicSuffixId = -1)
    {
        if (quality == 7 /* unique */)
            return TransformColorIndex(UniqueById(uniqueId)?.Raw("invtransform"));
        if (quality == 5 /* set */)
            return TransformColorIndex(SetItemById(setId)?.Raw("invtransform"));
        if (quality == 4 /* magic */)
            return TransformColorIndex(MagicPrefixById(magicPrefixId))
                ?? TransformColorIndex(MagicSuffixById(magicSuffixId));

        return null;
    }

    public bool ImageAssetExists(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        return _itemAssetKeys.Value.Contains(key) || _gfxAssetKeys.Value.Contains(key);
    }

    public string ResolveSocketFillerSpriteKey(string? classIdOrCode, string? code = null, string? gfx = null)
    {
        if (!string.IsNullOrWhiteSpace(code) && ImageAssetExists(code))
        {
            return code;
        }

        if (!string.IsNullOrWhiteSpace(classIdOrCode))
        {
            if (int.TryParse(classIdOrCode, out var classId))
            {
                var baseRow = BaseItemByClassId(classId);
                var baseCode = baseRow?.Raw("code");

                // Variable-gfx bases (jewels, rings, small/large/grand charms)
                // have N display variants. CompanionBridge already serialises
                // the variant index in SocketFillersRaw.Gfx (e.g. live jewel
                // captures showed Gfx=3 / Gfx=1).
                // For those, prefer the variant key (jew1..jew6, rin1..rin5,
                // cm11..cm33) over the bare base code.
                if (baseRow is not null && !string.IsNullOrWhiteSpace(gfx)
                    && int.TryParse(gfx, out var gfxIndex))
                {
                    var variantKey = ResolveVariableInventorySpriteKey(baseRow, gfxIndex);
                    if (!string.IsNullOrEmpty(variantKey) && ImageAssetExists(variantKey))
                    {
                        return variantKey;
                    }
                }

                return ImageAssetExists(baseCode) ? baseCode! : "gemsocket";
            }

            if (ImageAssetExists(classIdOrCode))
            {
                return classIdOrCode;
            }
        }

        if (!string.IsNullOrWhiteSpace(gfx) && ImageAssetExists(gfx))
        {
            return gfx;
        }

        return "gemsocket";
    }

    private string? ResolveVariableInventorySpriteKey(D2TxtRow? baseItem, int? gfx)
    {
        if (baseItem is null || gfx is null || gfx.Value < 0) return null;

        var type = baseItem.Raw("type");
        if (string.IsNullOrEmpty(type)) return null;
        if (!_data.ItemTypesByCode.TryGetValue(type, out var itemType)) return null;
        if (!CanUseVariableInventoryGfx(type)) return null;
        if (itemType.Int("VarInvGfx") <= 0) return null;

        var key = itemType.Raw($"InvGfx{gfx.Value + 1}");
        if (string.IsNullOrEmpty(key)) return null;
        return NormalizeVariableInventoryKey(type, key);
    }

    private string? ResolveFamilyInventoryFile(D2TxtRow? baseItem, string column)
    {
        if (baseItem is null) return null;

        var currentCode = baseItem.Raw("code");
        foreach (var code in OrderedUpgradeFamilyCodes(baseItem))
        {
            if (string.Equals(code, currentCode, StringComparison.OrdinalIgnoreCase))
                continue;

            var row = BaseItemByCode(code);
            var key = row?.Raw(column);
            if (!string.IsNullOrWhiteSpace(key))
                return key;
        }

        return null;
    }

    private bool IsUpgradedFlavorBase(string? flavorBaseCode, string? currentBaseCode)
    {
        return !string.IsNullOrWhiteSpace(flavorBaseCode) &&
               !string.IsNullOrWhiteSpace(currentBaseCode) &&
               !string.Equals(flavorBaseCode, currentBaseCode, StringComparison.OrdinalIgnoreCase) &&
               BaseCodesAreCompatible(flavorBaseCode, currentBaseCode);
    }

    private static IEnumerable<string> OrderedUpgradeFamilyCodes(D2TxtRow baseItem)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var code in new[]
        {
            baseItem.Raw("normcode"),
            baseItem.Raw("ubercode"),
            baseItem.Raw("ultracode"),
            baseItem.Raw("code"),
        })
        {
            if (string.IsNullOrWhiteSpace(code) ||
                string.Equals(code, "xxx", StringComparison.OrdinalIgnoreCase) ||
                !seen.Add(code))
            {
                continue;
            }

            yield return code;
        }
    }

    private static bool CanUseVariableInventoryGfx(string type)
    {
        return type.Equals("ring", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("scha", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("mcha", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("lcha", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("jewl", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVariableInventoryKey(string type, string key)
    {
        if (type.Equals("ring", StringComparison.OrdinalIgnoreCase) &&
            key.StartsWith("invrin", StringComparison.OrdinalIgnoreCase))
        {
            return "rin" + key["invrin".Length..];
        }

        if (type.Equals("jewl", StringComparison.OrdinalIgnoreCase) &&
            key.StartsWith("invjw", StringComparison.OrdinalIgnoreCase))
        {
            return "jew" + key["invjw".Length..];
        }

        return key.ToLowerInvariant() switch
        {
            "invch1" => "cm11",
            "invch4" => "cm12",
            "invch7" => "cm13",
            "invch2" => "cm21",
            "invch5" => "cm22",
            "invch8" => "cm23",
            "invch3" => "cm31",
            "invch6" => "cm32",
            "invch9" => "cm33",
            _ => NormalizeInventoryFileKey(key),
        };
    }

    private static string NormalizeInventoryFileKey(string key)
    {
        return key.ToLowerInvariant() switch
        {
            "invchm" => "cm1",
            "invwnd" => "cm2",
            "invsst" => "cm3",
            "invgswe" => "jew",
            _ when key.StartsWith("inv", StringComparison.OrdinalIgnoreCase) && key.Length > 3 => key[3..],
            _ => key,
        };
    }

    private static string NormalizeInventoryFileKey(string key, D2TxtRow? contextBaseItem)
    {
        var lower = key.ToLowerInvariant();
        var baseCode = contextBaseItem?.Raw("code");
        if (lower == "invwnd" && BaseCodeIs(baseCode, "wnd", "9wn", "7wn"))
            return "wnd";
        if (lower == "invsst" && BaseCodeIs(baseCode, "sst", "8ss", "6ss"))
            return "sst";

        return NormalizeInventoryFileKey(key);
    }

    private static bool BaseCodeIs(string? baseCode, params string[] codes)
    {
        return !string.IsNullOrWhiteSpace(baseCode) &&
            codes.Any(code => string.Equals(baseCode, code, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddCode(HashSet<string> family, string? code)
    {
        if (!string.IsNullOrWhiteSpace(code) && !string.Equals(code, "xxx", StringComparison.OrdinalIgnoreCase))
            family.Add(code);
    }

    private int? TransformColorIndex(D2TxtRow? row)
    {
        if (row is null || row.Int("transform") <= 0) return null;
        return TransformColorIndex(row.Raw("transformcolor"));
    }

    private int? TransformColorIndex(string? transform)
    {
        if (string.IsNullOrWhiteSpace(transform)) return null;
        return _data.ColorsByCode.TryGetValue(transform, out var row) ? row.RowIndex : null;
    }

    private HashSet<string> LoadAssetKeys(string assetKind, string pattern)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var root = FindAssetDirectory(assetKind);
        if (root is not null)
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(root, pattern, SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(path);
                if (string.IsNullOrEmpty(name)) continue;
                keys.Add(Path.GetFileNameWithoutExtension(name));
            }
        }

        if (keys.Count == 0)
        {
            LoadAssetKeysFromPack(keys, assetKind);
        }

        return keys;
    }

    private void LoadAssetKeysFromPack(HashSet<string> keys, string assetKind)
    {
        var packPath = FindAssetPack();
        if (packPath is null) return;

        var prefix = $"assets/{assetKind}/";
        try
        {
            using var archive = ZipFile.OpenRead(packPath);
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(entry.Name))
                {
                    continue;
                }

                var rest = name[prefix.Length..];
                if (assetKind.Equals("gfx", StringComparison.OrdinalIgnoreCase))
                {
                    var slash = rest.IndexOf('/');
                    if (slash > 0)
                    {
                        keys.Add(rest[..slash]);
                    }

                    continue;
                }

                if (!rest.Contains('/'))
                {
                    keys.Add(Path.GetFileNameWithoutExtension(rest));
                }
            }
        }
        catch (InvalidDataException)
        {
        }
        catch (IOException)
        {
        }
    }

    private string? FindAssetDirectory(string assetKind)
    {
        var dir = new DirectoryInfo(_data.SourceDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "wwwroot", "assets", assetKind);
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "wwwroot", "assets", assetKind);
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    private string? FindAssetPack()
    {
        var dir = new DirectoryInfo(_data.SourceDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "wwwroot", "assets", AssetPackService.PackFileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "wwwroot", "assets", AssetPackService.PackFileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }
}
