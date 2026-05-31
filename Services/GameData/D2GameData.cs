namespace D2CompanionMvc.Services.GameData;

/// <summary>
/// In-memory snapshot of every Diablo II Excel TXT table the canonicalization
/// pipeline needs. Loaded once at startup from a configurable folder
/// (default: <c>data/d2/1.13c/txt</c>) and shared as a singleton.
///
/// All tables are loaded eagerly so the per-item adapter never blocks on disk.
/// Files that aren't present cause an error during startup — better to fail
/// loud than to silently degrade tooltip rendering at runtime.
/// </summary>
public sealed class D2GameData
{
    // Item base tables — the "BaseItem" array used everywhere else is the
    // concatenation of Weapons + Armor + Misc, in that order, which is the row
    // order D2 uses to map `classid` → base item.
    public D2TxtTable Weapons { get; }
    public D2TxtTable Armor { get; }
    public D2TxtTable Misc { get; }

    // Affixes and rarities
    public D2TxtTable MagicPrefix { get; }
    public D2TxtTable MagicSuffix { get; }
    public D2TxtTable RarePrefix { get; }
    public D2TxtTable RareSuffix { get; }
    public D2TxtTable AutoMagic { get; }
    public D2TxtTable LowQualityItems { get; }
    public D2TxtTable QualityItems { get; }

    // Item flavour tables
    public D2TxtTable UniqueItems { get; }
    public D2TxtTable SetItems { get; }
    public D2TxtTable Sets { get; }
    public D2TxtTable Runes { get; } // runewords
    public D2TxtTable Gems { get; }

    // Item types / inventory slots
    public D2TxtTable ItemTypes { get; }
    public D2TxtTable BodyLocs { get; }

    // Stats / properties (the centrepieces for tooltip rendering)
    public D2TxtTable ItemStatCost { get; }
    public D2TxtTable Properties { get; }

    // Skills (for "X% Chance to cast level Y <skill>" etc.)
    public D2TxtTable Skills { get; }
    public D2TxtTable SkillDesc { get; }

    // Class data (for "+X to <Class> Skill Levels")
    public D2TxtTable CharStats { get; }

    // Colors (transform colors referenced by some affixes / uniques)
    public D2TxtTable Colors { get; }

    /// <summary>The merged BaseItem table: Weapons + Armor + Misc in that order.</summary>
    public IReadOnlyList<D2TxtRow> BaseItems { get; }

    /// <summary>
    /// BaseItem rows indexed the way Styx/D2 class ids are indexed. This excludes
    /// only literal "Expansion" sentinel rows from Weapons/Armor/Misc.
    /// </summary>
    public IReadOnlyList<D2TxtRow> BaseItemsByInternalClassId { get; }

    /// <summary>
    /// UniqueItems rows indexed the way D2/Styx internal unique ids are indexed.
    /// This excludes only the literal "Expansion" sentinel row; all real rows,
    /// including empty/category rows, remain counted.
    /// </summary>
    public IReadOnlyList<D2TxtRow> UniqueItemsByInternalId { get; }

    /// <summary>
    /// SetItems rows indexed the way D2/Styx internal set item ids are indexed.
    /// This excludes only the literal "Expansion" sentinel row; all real rows,
    /// including empty/category rows, remain counted.
    /// </summary>
    public IReadOnlyList<D2TxtRow> SetItemsByInternalId { get; }

    /// <summary>
    /// MagicPrefix rows indexed the way D2/Styx internal magic prefix ids are indexed.
    /// This excludes only the literal "Expansion" sentinel row; all real affix rows
    /// remain counted.
    /// </summary>
    public IReadOnlyList<D2TxtRow> MagicPrefixByInternalId { get; }

    /// <summary>
    /// MagicSuffix rows indexed the way D2/Styx internal magic suffix ids are indexed.
    /// This excludes only the literal "Expansion" sentinel row; all real affix rows
    /// remain counted.
    /// </summary>
    public IReadOnlyList<D2TxtRow> MagicSuffixByInternalId { get; }

    /// <summary>Fast lookup: item code (e.g. "7m7") → BaseItem row.</summary>
    public IReadOnlyDictionary<string, D2TxtRow> BaseItemsByCode { get; }

    /// <summary>Fast lookup: ItemType code (e.g. "shld") → row.</summary>
    public IReadOnlyDictionary<string, D2TxtRow> ItemTypesByCode { get; }

    /// <summary>Fast lookup: stat name (e.g. "item_allskills") → ItemStatCost row.</summary>
    public IReadOnlyDictionary<string, D2TxtRow> StatsByName { get; }

    /// <summary>Fast lookup: skill row id → Skills row.</summary>
    public IReadOnlyDictionary<int, D2TxtRow> SkillsById { get; }

    /// <summary>Fast lookup: gem/rune code → Gems row.</summary>
    public IReadOnlyDictionary<string, D2TxtRow> GemsByCode { get; }

    /// <summary>Fast lookup: transform color code (e.g. "dgld") to Colors.txt row.</summary>
    public IReadOnlyDictionary<string, D2TxtRow> ColorsByCode { get; }

    /// <summary>Where the TXT files were loaded from.</summary>
    public string SourceDirectory { get; }

    public D2GameData(string sourceDirectory)
    {
        SourceDirectory = sourceDirectory;
        D2TxtTable Load(string file) => D2TxtDataLoader.Load(Path.Combine(sourceDirectory, file), Path.GetFileNameWithoutExtension(file));

        Weapons         = Load("Weapons.txt");
        Armor           = Load("Armor.txt");
        Misc            = Load("Misc.txt");

        MagicPrefix     = Load("MagicPrefix.txt");
        MagicSuffix     = Load("MagicSuffix.txt");
        RarePrefix      = Load("RarePrefix.txt");
        RareSuffix      = Load("RareSuffix.txt");
        AutoMagic       = Load("AutoMagic.txt");
        LowQualityItems = Load("LowQualityItems.txt");
        QualityItems    = Load("QualityItems.txt");

        UniqueItems     = Load("UniqueItems.txt");
        SetItems        = Load("SetItems.txt");
        Sets            = Load("Sets.txt");
        Runes           = Load("Runes.txt");
        Gems            = Load("Gems.txt");

        ItemTypes       = Load("ItemTypes.txt");
        BodyLocs        = Load("BodyLocs.txt");

        ItemStatCost    = Load("ItemStatCost.txt");
        Properties      = Load("Properties.txt");

        Skills          = Load("Skills.txt");
        SkillDesc       = Load("SkillDesc.txt");
        CharStats       = Load("CharStats.txt");
        Colors          = Load("Colors.txt");

        // Build the merged BaseItem array. Weapons rows occupy classid 0..N-1,
        // Armor rows occupy N..N+M-1, Misc rows occupy N+M..N+M+K-1. This matches
        // Styx's `BaseItem` array and any client/server using the same merge.
        var merged = new List<D2TxtRow>(Weapons.Rows.Count + Armor.Rows.Count + Misc.Rows.Count);
        merged.AddRange(Weapons.Rows);
        merged.AddRange(Armor.Rows);
        merged.AddRange(Misc.Rows);
        BaseItems = merged;

        var internalBaseItems = new List<D2TxtRow>(merged.Count);
        internalBaseItems.AddRange(BuildInternalIdentityRows(Weapons, "name"));
        internalBaseItems.AddRange(BuildInternalIdentityRows(Armor, "name"));
        internalBaseItems.AddRange(BuildInternalIdentityRows(Misc, "name"));
        BaseItemsByInternalClassId = internalBaseItems;

        UniqueItemsByInternalId = BuildInternalIdentityRows(UniqueItems, "index");
        SetItemsByInternalId = BuildInternalIdentityRows(SetItems, "index");
        MagicPrefixByInternalId = BuildInternalIdentityRows(MagicPrefix, "Name");
        MagicSuffixByInternalId = BuildInternalIdentityRows(MagicSuffix, "Name");

        // Lookup dictionaries
        var byCode = new Dictionary<string, D2TxtRow>(merged.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var row in merged)
        {
            var code = row.Raw("code");
            if (code is null) continue;
            // First entry wins for collisions — matches D2 behavior (it picks the
            // first base item with a given code, weapons before armor before misc).
            byCode.TryAdd(code, row);
        }
        BaseItemsByCode = byCode;

        var itemTypes = new Dictionary<string, D2TxtRow>(ItemTypes.Rows.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var row in ItemTypes.Rows)
        {
            var code = row.Raw("Code");
            if (code is null) continue;
            itemTypes.TryAdd(code, row);
        }
        ItemTypesByCode = itemTypes;

        var stats = new Dictionary<string, D2TxtRow>(ItemStatCost.Rows.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var row in ItemStatCost.Rows)
        {
            var name = row.Raw("Stat");
            if (name is null) continue;
            stats.TryAdd(name, row);
        }
        StatsByName = stats;

        var skills = new Dictionary<int, D2TxtRow>(Skills.Rows.Count);
        foreach (var row in Skills.Rows)
        {
            var id = row.IntOrNull("Id");
            if (id is null) continue;
            skills.TryAdd(id.Value, row);
        }
        SkillsById = skills;

        var gems = new Dictionary<string, D2TxtRow>(Gems.Rows.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var row in Gems.Rows)
        {
            var code = row.Raw("code");
            if (code is null) continue;
            gems.TryAdd(code, row);
        }
        GemsByCode = gems;

        var colors = new Dictionary<string, D2TxtRow>(Colors.Rows.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var row in Colors.Rows)
        {
            var code = row.Raw("Code");
            if (code is null) continue;
            colors.TryAdd(code, row);
        }
        ColorsByCode = colors;
    }

    private static IReadOnlyList<D2TxtRow> BuildInternalIdentityRows(D2TxtTable table, string nameColumn)
    {
        var rows = new List<D2TxtRow>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            if (IsExpansionSentinel(row, nameColumn))
            {
                continue;
            }

            rows.Add(row);
        }

        return rows;
    }

    private static bool IsExpansionSentinel(D2TxtRow row, string nameColumn)
    {
        return string.Equals(row.Raw(nameColumn), "Expansion", StringComparison.Ordinal);
    }
}
