using D2CompanionMvc.Domain.Items;
using D2CompanionMvc.Extensions.Styx.Models;
using D2CompanionMvc.Services.GameData;
using D2CompanionMvc.Services.Items.Rendering;
using Microsoft.Extensions.Logging;

namespace D2CompanionMvc.Extensions.Styx.Adapters;

/// <summary>
/// Translates a <see cref="StyxItemSnapshot"/> into a <see cref="CanonicalD2Item"/>
/// using the 1.13c TXT tables.
///
/// This is the only place where Styx-specific knowledge lives in the C# code.
/// Everything downstream (persistence, tooltip rendering, the catalog API) sees
/// the canonical model and never has to ask "is this a Styx item?".
///
/// The adapter degrades gracefully:
/// - When the snapshot carries the new raw fields → full canonicalization,
///   image / base name / damage / defense all resolved from TXT.
/// - When the snapshot is from an old sidecar (no raw fields) → falls back to
///   whatever the sidecar pre-rendered for backward-compatibility.
///
/// Identity rules (enforced here):
/// - itemColor is NEVER used as a UniqueItems.txt or SetItems.txt row id.
/// - unique/set names come exclusively from snap.UniqueId / snap.SetId.
/// - When a unique/set row is found but its base code (UniqueItems.txt "code"
///   / SetItems.txt "item") does not match the item's own code, the row is
///   rejected and the title falls back to "Unique {baseName}" / "Set {baseName}".
/// </summary>
public sealed class StyxToCanonicalItemAdapter
{
    private readonly D2GameData _data;
    private readonly D2ItemLookupService _items;
    private readonly ILogger<StyxToCanonicalItemAdapter> _logger;

    public StyxToCanonicalItemAdapter(D2GameData data, D2ItemLookupService items, ILogger<StyxToCanonicalItemAdapter> logger)
    {
        _data = data;
        _items = items;
        _logger = logger;
    }

    public CanonicalD2Item Adapt(StyxItemSnapshot snap, string sourceFile, string? rawSnapshotJson = null)
    {
        var baseItem = _items.BaseItemByCode(snap.Code) ?? _items.BaseItemByClassId(snap.ClassId);
        var quality  = snap.Quality ?? GuessQualityFromColor(snap.ItemColor);

        // ── Unique / set identity resolution ────────────────────────────────
        // itemColor is NEVER used here. Unique/set row ids come only from
        // snap.UniqueId / snap.SetId (explicit raw fields). When a row is found
        // but its TXT base code does not match this item's code, it is rejected
        // and the fallback title "Unique {baseName}" / "Set {baseName}" is used.
        D2TxtRow? unique  = null;
        D2TxtRow? setItem = null;
        bool uniqueRejected = false;
        bool setRejected    = false;

        if (snap.UniqueId is int u && u >= 0)
        {
            unique = _items.UniqueById(u);
            if (unique is not null && !_items.BaseCodesAreCompatible(unique.Raw("code"), snap.Code))
            {
                _logger.LogWarning(
                    "UniqueId {UniqueId} → '{UniqueName}' (code='{UniqueCode}') does not match item code '{ItemCode}' — rejecting unique name.",
                    u, unique.Raw("index"), unique.Raw("code"), snap.Code);
                unique = null;
            }
            uniqueRejected = unique is null; // null = either out-of-range or code mismatch
        }

        if (snap.SetId is int s && s >= 0)
        {
            setItem = _items.SetItemById(s);
            if (setItem is not null && !_items.BaseCodesAreCompatible(setItem.Raw("item"), snap.Code))
            {
                _logger.LogWarning(
                    "SetId {SetId} → '{SetName}' (item='{SetCode}') does not match item code '{ItemCode}' — rejecting set name.",
                    s, setItem.Raw("index"), setItem.Raw("item"), snap.Code);
                setItem = null;
            }
            setRejected = setItem is null;
        }

        var runeword = snap.RunewordId is int r && r >= 0 ? _items.RuneWordById(r) : null;
        var magicPrefixId = snap.MagicPrefixId ?? FirstNonNegative(snap.MagicPrefixes);
        var magicSuffixId = snap.MagicSuffixId ?? FirstNonNegative(snap.MagicSuffixes);

        var imageKey   = _items.ResolveInventorySpriteKey(baseItem, quality, snap.UniqueId ?? -1, snap.SetId ?? -1, snap.Code, snap.Gfx);
        var transformColor = _items.ResolveInventoryTransformColor(
            quality,
            snap.UniqueId ?? -1,
            snap.SetId ?? -1,
            magicPrefixId ?? -1,
            magicSuffixId ?? -1);
        // Crafted items (quality == 8) must NEVER pick up an inventory tint from
        // prefix/suffix/automagic transform colors. AffixCalc's getItemColor()
        // short-circuits with "return true" for quality===8 before checking any
        // affix transformcolor; we mirror that rule here. Styx still sends a
        // non-21 ItemColor on the wire for some crafted items (we have ~30 in
        // the active publish DB with ItemColor 12..17 — Shadow Scarab, Hailstone
        // Track, Blood Hold, etc.), so the guard belongs in the adapter, not
        // upstream. See docs/reference/AffixCalc_Kolbot_Reference_Audit.md §2.3.
        // Styx uses 21 as the "no tint" sentinel.
        int colorIndex;
        if (quality == 8)
        {
            colorIndex = -1;
        }
        else
        {
            colorIndex = transformColor ?? (snap.ItemColor == 21 ? -1 : snap.ItemColor);
        }
        var baseName   = ResolveBaseName(baseItem, snap.Code);
        var title      = BuildTitle(snap, baseName, quality, unique, setItem, runeword, uniqueRejected, setRejected);
        var storage    = BucketFor(snap.Storage);
        var bodyX      = (storage == ItemStorageBucket.Equipped || storage == ItemStorageBucket.Mercenary)
                             ? (snap.BodyLocation ?? snap.X)
                             : snap.X;

        // Width / height: prefer the canonical baseItem invwidth/invheight when
        // we have a base row; fall back to whatever the snapshot said.
        var gridW = baseItem?.IntOrNull("invwidth")  ?? snap.GridWidth;
        var gridH = baseItem?.IntOrNull("invheight") ?? snap.GridHeight;

        // Class restriction comes from ItemTypes.txt#StaffMods or `usage` columns.
        // We use the cached typecode lookup: e.g. "wand" → Necromancer-only.
        var classRestriction = ResolveClassRestriction(baseItem);
        var socketFillers    = ResolveSocketFillers(snap);

        return new CanonicalD2Item
        {
            Gid = snap.Gid,
            ClassId = snap.ClassId,
            Code = snap.Code,
            Quality = quality,
            ItemLevel = snap.ItemLevel ?? 0,
            CharacterLevel = snap.CharacterLevel,

            ImageKey = imageKey,
            ColorIndex = colorIndex,
            Title = title,
            BaseName = baseName,

            Storage = storage,
            Location = snap.Location,
            X = bodyX,
            Y = snap.Y,
            GridWidth = Math.Max(1, gridW),
            GridHeight = Math.Max(1, gridH),

            Identified = snap.Identified ?? true,
            Ethereal = snap.Ethereal,
            Runeword = snap.IsRuneword ?? false,

            UniqueId = snap.UniqueId,
            SetId = snap.SetId,
            RunewordId = snap.RunewordId,
            MagicPrefixId = magicPrefixId,
            MagicSuffixId = magicSuffixId,
            RarePrefixId = snap.RarePrefixId,
            RareSuffixId = snap.RareSuffixId,

            Defense = snap.Defense,
            OneHandMin = snap.OneHandMinDamage,
            OneHandMax = snap.OneHandMaxDamage,
            TwoHandMin = snap.TwoHandMinDamage,
            TwoHandMax = snap.TwoHandMaxDamage,
            ThrowMin = snap.ThrowMinDamage,
            ThrowMax = snap.ThrowMaxDamage,
            Quantity = snap.Quantity,
            Durability = snap.Durability,
            MaxDurability = snap.MaxDurability,
            RequiredStrength = snap.RequiredStrength ?? baseItem?.IntOrNull("reqstr"),
            RequiredDexterity = snap.RequiredDexterity ?? baseItem?.IntOrNull("reqdex"),
            RequiredLevel = snap.RequiredLevel ?? baseItem?.IntOrNull("levelreq"),
            ClassRestriction = classRestriction,

            SocketCount = socketFillers.Count,
            SocketFillers = socketFillers,

            RawStats = AdaptStats(snap.RawStats, snap.CharacterLevel),
            SourceFile = sourceFile,
            RawSnapshotJson = rawSnapshotJson,
        };
    }

    private static List<RawItemStat> AdaptStats(IReadOnlyList<StyxRawStat> raws, int? characterLevel)
    {
        var list = new List<RawItemStat>(raws.Count);
        foreach (var s in raws)
        {
            var rawValue = EffectiveRawValue(s);
            list.Add(new RawItemStat
            {
                StatId = s.Id,
                Value = (int)Math.Round(rawValue), // StyxRawStat.Value is double; round to int here
                RawValue = rawValue,
                CharacterLevel = characterLevel,
                Param = s.Param,
                Min = s.Min,
                Max = s.Max,
                Frames = s.Frames,
                SkillId = s.Skill ?? s.SkillId,
                SkillLevel = s.SkillLevel ?? s.Level,
                Chance = s.Chance,
                Charges = s.Charges,
                MaxCharges = s.MaxCharges,
                ClassId = s.ClassId,
                TabId = s.TabId,
                MonsterId = s.MonsterId,
                Source = "styx:rawstat",
            });
        }
        return list;
    }

    private static double EffectiveRawValue(StyxRawStat stat)
    {
        if (Math.Abs(stat.Value) > double.Epsilon) return stat.Value;
        if (!string.Equals(stat.Type, "PerLevelStat", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(stat.ConstructorName, "PerLevelStat", StringComparison.OrdinalIgnoreCase))
            return stat.Value;

        if (stat.NodeRaw is not { ValueKind: System.Text.Json.JsonValueKind.Object } nodeRaw)
            return stat.Value;

        if (nodeRaw.TryGetProperty("value", out var value) && value.TryGetDouble(out var number))
            return number;
        if (nodeRaw.TryGetProperty("raw", out var raw)
            && raw.ValueKind == System.Text.Json.JsonValueKind.Object
            && raw.TryGetProperty("val", out var rawVal)
            && rawVal.TryGetDouble(out var rawNumber))
            return rawNumber;

        return stat.Value;
    }

    private static int? FirstNonNegative(IReadOnlyList<int>? values)
    {
        if (values is null) return null;
        foreach (var value in values)
        {
            if (value >= 0) return value;
        }

        return null;
    }

    /// <summary>
    /// Map Styx's storage string to the bucket enum the renderer dispatches on.
    /// MuleLogger uses the same labels in lowercase, so a single map covers both.
    /// </summary>
    private static ItemStorageBucket BucketFor(string? storage) => storage?.ToLowerInvariant() switch
    {
        "equipped"  => ItemStorageBucket.Equipped,
        "inventory" => ItemStorageBucket.Inventory,
        "stash"     => ItemStorageBucket.Stash,
        "cube"      => ItemStorageBucket.Cube,
        "mercenary" => ItemStorageBucket.Mercenary,
        "merc"      => ItemStorageBucket.Mercenary,
        "other"     => ItemStorageBucket.Other,
        _           => ItemStorageBucket.Unknown,
    };

    /// <summary>
    /// Resolve the class-only restriction display name from the base item row.
    /// D2 stores this implicitly via ItemTypes (`Class`/`StaffMods` columns)
    /// and via the base item's `type`. We look up the type chain and stop when
    /// we hit one that's flagged as a class type.
    /// </summary>
    private string? ResolveClassRestriction(D2TxtRow? baseItem)
    {
        if (baseItem is null) return null;
        var typeCode = baseItem.Raw("type");
        if (typeCode is null) return null;

        // Hard-coded class-type → class name mapping. Matches D2's "StaffMods"
        // / "Class" columns in ItemTypes.txt; we keep it here because it's
        // tiny and stable, and avoids parsing the entire ItemTypes chain.
        var classOnly = typeCode switch
        {
            "amaz" => "Amazon",
            "wand" or "head" => "Necromancer",
            "club2" or "pelt" => "Druid",
            "cler" or "kite" or "larm" => "Paladin",
            "phlm" => "Barbarian",
            "orb"  => "Sorceress",
            "h2h" or "h2h2" => "Assassin",
            _ => null,
        };
        return classOnly;
    }

    /// <summary>
    /// Crude fallback when the snapshot didn't supply <c>quality</c>: we map
    /// the Styx-emitted item color to a quality. Magic = 14 (light yellow),
    /// Unique = -1 (no transform). Used only for legacy snapshots; new
    /// snapshots always send <c>quality</c>.
    /// </summary>
    private static int GuessQualityFromColor(int color) => color switch
    {
        // We can't reliably reverse-engineer quality from the color alone.
        // Default to 2 (Normal) so the renderer treats it as a basic item.
        _ => 2,
    };

    // ── Static helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when the TXT row's base code is the same as the item's
    /// actual code. Both must be non-empty. Case is ignored.
    /// Used to reject unique/set rows that Styx pointed at the wrong item type.
    /// </summary>
    internal static bool IsBaseCodeCompatible(string? rowCode, string? itemCode) =>
        !string.IsNullOrEmpty(rowCode) && !string.IsNullOrEmpty(itemCode) &&
        string.Equals(rowCode, itemCode, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolve the base item display name, applying in-game overrides where D2's
    /// TXT data doesn't match what the game actually shows. Charms are the main
    /// example: TXT stores "Charm Small" / "Charm Medium" / "Charm Large" but
    /// D2 displays "Small Charm" / "Large Charm" / "Grand Charm".
    /// </summary>
    internal static string ResolveBaseName(D2TxtRow? baseItem, string? code) => code switch
    {
        "cm1" => "Small Charm",
        "cm2" => "Large Charm",
        "cm3" => "Grand Charm",
        "rin" => "Ring",
        "amu" => "Amulet",
        _     => baseItem?.Raw("name") ?? code ?? "",
    };

    /// <summary>
    /// Build the title (line 1 of the tooltip) the way D2 itself does it:
    /// unique/set name, runeword name, or prefix+base+suffix for magic items.
    /// Falls back to "Unique/Set {baseName}" when the id was present but the
    /// row was rejected due to a base-code mismatch.
    /// </summary>
    private string BuildTitle(
        StyxItemSnapshot snap,
        string baseName,
        int quality,
        D2TxtRow? unique,
        D2TxtRow? setItem,
        D2TxtRow? runeword,
        bool uniqueRejected,
        bool setRejected)
    {
        // Unidentified items show only the base name.
        if (snap.Identified == false) return baseName;

        if (snap.IsRuneword == true && runeword is not null)
        {
            var rwName = runeword.Raw("Rune Name") ?? runeword.Raw("Name") ?? "";
            rwName = RunewordDisplayName(runeword, rwName);
            return string.IsNullOrEmpty(rwName) ? baseName : rwName;
        }

        return quality switch
        {
            7 when unique is not null   => UniqueDisplayName(unique) ?? baseName,
            7 when uniqueRejected        => $"Unique {baseName}",
            5 when setItem is not null  => setItem.Raw("index") ?? baseName,
            5 when setRejected           => $"Set {baseName}",
            6 or 8 => BuildRareCraftedTitle(snap, baseName),
            4 => BuildMagicTitle(snap, baseName),
            _ => baseName,
        };
    }

    /// <summary>
    /// Resolve socket filler sprite keys for this item.
    ///
    /// Styx may send socket fillers as numeric classIds (e.g. "643" for a gem)
    /// rather than the 3-char item code. This method converts them:
    /// numeric string → <see cref="D2ItemLookupService.BaseItemByClassId"/> → code.
    ///
    /// Priority: <see cref="StyxItemSnapshot.SocketFillersRaw"/> (newer, richer)
    /// → <see cref="StyxItemSnapshot.Sockets"/> (legacy string list).
    /// </summary>
    private IReadOnlyList<string> ResolveSocketFillers(StyxItemSnapshot snap)
    {
        if (snap.SocketFillersRaw is { Count: > 0 })
        {
            return snap.SocketFillersRaw.Select(filler =>
            {
                if (filler is null)
                    return "gemsocket";
                return _items.ResolveSocketFillerSpriteKey(filler.ClassId, filler.Code, filler.Gfx);
            }).ToList();
        }

        // Legacy: Sockets list may contain raw numeric classids from Node's socketImages()
        return snap.Sockets.Select(s =>
        {
            if (string.IsNullOrEmpty(s)) return "gemsocket";
            return _items.ResolveSocketFillerSpriteKey(s);
        }).ToList();
    }

    private string BuildRareCraftedTitle(StyxItemSnapshot snap, string baseName)
    {
        var pre = snap.RarePrefixId is int p && p >= 0 ? RareAffixDisplayName(_items.RarePrefixById(p)) : null;
        var suf = snap.RareSuffixId is int s && s >= 0 ? RareAffixDisplayName(_items.RareSuffixById(s)) : null;
        var compound = $"{pre ?? string.Empty} {suf ?? string.Empty}".Trim();
        return string.IsNullOrEmpty(compound) ? baseName : compound;
    }

    private string BuildMagicTitle(StyxItemSnapshot snap, string baseName)
    {
        var parts = new List<string>(3);
        if (snap.MagicPrefixId is int p && p >= 0)
        {
            var pre = MagicAffixDisplayName(_items.MagicPrefixById(p));
            if (!string.IsNullOrEmpty(pre)) parts.Add(pre);
        }
        parts.Add(baseName);
        if (snap.MagicSuffixId is int s && s >= 0)
        {
            var suf = MagicAffixDisplayName(_items.MagicSuffixById(s));
            if (!string.IsNullOrEmpty(suf)) parts.Add(suf);
        }
        return string.Join(' ', parts);
    }

    private static string RunewordDisplayName(D2TxtRow row, string name)
    {
        // The local 1.13c Runes.txt carries several pre-release/internal names
        // in the visible "Rune Name" cell. The runes and stats are correct, but
        // D2/MuleLogger display the final shipped runeword names.
        return row.Raw("Name") switch
        {
            "Runeword47" when string.Equals(name, "Widowmaker", StringComparison.OrdinalIgnoreCase) => "Grief",
            "Runeword4" when string.Equals(name, "The Beast", StringComparison.OrdinalIgnoreCase) => "Beast",
            "Runeword14" when string.Equals(name, "Bound by Duty", StringComparison.OrdinalIgnoreCase) => "Chains of Honor",
            "Runeword26" when string.Equals(name, "Doomsayer", StringComparison.OrdinalIgnoreCase) => "Doom",
            "Runeword37" when string.Equals(name, "Exile's Path", StringComparison.OrdinalIgnoreCase) => "Exile",
            _ => name,
        };
    }

    private static string? MagicAffixDisplayName(D2TxtRow? row)
    {
        var name = row?.Raw("Name");
        if (name is null) return null;

        // The 1.13c MagicPrefix row for Druid Elemental grand charms is named
        // "Nature's", but the in-game affix title is "Natural Grand Charm".
        if (string.Equals(name, "Nature's", StringComparison.Ordinal)
            && string.Equals(row?.Raw("mod1code"), "skilltab", StringComparison.OrdinalIgnoreCase)
            && string.Equals(row?.Raw("mod1param"), "17", StringComparison.Ordinal))
        {
            return "Natural";
        }

        // Local 1.13c MagicSuffix.txt has a known typo; D2 displays Atlas.
        if (string.Equals(name, "of Atlus", StringComparison.Ordinal))
            return "of Atlas";
        if (string.Equals(name, "of Decrepification", StringComparison.Ordinal)
            && string.Equals(row?.Raw("mod1code"), "charged", StringComparison.OrdinalIgnoreCase)
            && string.Equals(row?.Raw("mod1param"), "87", StringComparison.Ordinal))
            return "of Decrepify";

        // TBL renames: a handful of MagicPrefix/Suffix rows have a TXT `Name`
        // that differs from D2's actual displayed string (string-table key
        // replacement). MuleLogger samples carry the displayed strings:
        //   * Trump (row 528, slvl 50 weapon dmg/lvl) → "Fool's"
        //     Golden: "Fool's Greater Talons of Quickness".
        //   * Artificer's (row 423, slvl 33 sock 3) → "Jeweler's"
        //     Golden: "Jeweler's Vortex Shield of Deflecting".
        // Each name appears as a single unique row in MagicPrefix.txt, so a
        // plain name-keyed map is safe — no mod-code guard needed.
        if (string.Equals(name, "Trump", StringComparison.Ordinal))
            return "Fool's";
        if (string.Equals(name, "Artificer's", StringComparison.Ordinal))
            return "Jeweler's";

        return name;
    }

    internal static string? RareAffixDisplayName(D2TxtRow? row)
    {
        var name = row?.Raw("name");
        if (string.IsNullOrWhiteSpace(name)) return null;

        // Some local TXT rows carry rare-name internal markers in the visible
        // name cell (for example "GhoulRI" / "barRI"). D2 does not print those.
        if (name.EndsWith("RI", StringComparison.Ordinal))
            name = name[..^2];
        if (string.Equals(name, "Holocaust", StringComparison.Ordinal))
            return "Armageddon";

        if (name.Length == 0) return null;
        return char.ToUpperInvariant(name[0]) + name[1..];
    }

    private static string? UniqueDisplayName(D2TxtRow row)
    {
        var name = row.Raw("index");
        if (string.Equals(name, "Wisp", StringComparison.Ordinal) &&
            string.Equals(row.Raw("code"), "rin", StringComparison.OrdinalIgnoreCase))
        {
            return "Wisp Projector";
        }

        return name;
    }
}
