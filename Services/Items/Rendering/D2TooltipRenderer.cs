using D2CompanionMvc.Domain.Items;
using D2CompanionMvc.Services.GameData;

namespace D2CompanionMvc.Services.Items.Rendering;

/// <summary>
/// Produces a structured <see cref="ItemTooltip"/> from a
/// <see cref="CanonicalD2Item"/>. The output is deterministic, ordered the way
/// D2 itself would order the lines, and contains every stat the resolver
/// could turn into text. Unresolved stats and missing string-table keys are
/// captured in the tooltip's debug fields so the debug endpoint surfaces them.
///
/// What this renderer is and isn't:
/// - It IS the canonical tooltip-builder; both Styx and MuleLogger items go
///   through it when they need a tooltip rebuilt.
/// - It is NOT a JS-side concern; the front-end keeps consuming the legacy
///   `description` string (built via <see cref="ItemTooltip.ToDescriptionString"/>).
/// - It does NOT mutate the canonical item — the canonical model is the
///   contract; only the tooltip is a derived artefact.
/// </summary>
public sealed class D2TooltipRenderer
{
    private readonly D2StatResolver _stats;
    private readonly D2ItemLookupService _items;

    public D2TooltipRenderer(D2StatResolver stats, D2ItemLookupService items)
    {
        _stats = stats;
        _items = items;
    }

    public ItemTooltip Render(CanonicalD2Item item)
    {
        var tip = new ItemTooltip();

        // ── Title ─────────────────────────────────────────────────────────
        var titleColor = QualityColor(item);
        var titleText = item.Identified ? item.Title : item.BaseName; // unid items only show the base name
        if (item.ItemLevel > 0 && item.Identified)
            titleText = $"{titleText} ({item.ItemLevel})";
        tip.Lines.Add(new TooltipLine
        {
            Text = titleText,
            Color = titleColor,
            Section = TooltipSection.Title,
            Priority = 1000,
            Source = "title",
        });

        // ── Base name ────────────────────────────────────────────────────
        // Skip when the base name is already the title (e.g. unidentified items),
        // and also skip for magic items whose title already embeds the base type
        // — D2 itself does not repeat "Tower Shield" below "Artificer's Tower
        // Shield of Deflecting". Uniques/sets/rares/crafted/runewords keep the
        // base type as a separate line because D2 shows it for those qualities.
        if (!TitleAlreadyRepresentsBaseName(item, titleText)
            && !IsMagicTitleEmbeddingBase(item, titleText))
        {
            tip.Lines.Add(new TooltipLine
            {
                Text = item.BaseName,
                Color = item.Runeword ? D2Color.Gray : titleColor,
                Section = TooltipSection.BaseName,
                Priority = 1000,
                Source = "baseName",
            });
        }

        // ── Socket name line (e.g. "BerBerBer" for 3 inserted Ber runes) ─
        // Rendered in D2's gold (Unique) color, between the title/base name and
        // the stat lines. Only for socketed non-runeword identified items — the
        // runeword title naming already conveys the rune sequence.
        AppendSocketNameLine(item, tip);

        // ── Defense / Damage / Quantity ──────────────────────────────────
        if (item.Defense is int def && def > 0)
        {
            tip.Lines.Add(new TooltipLine
            {
                Text = $"Defense: {def}",
                Color = D2Color.White,
                Section = TooltipSection.DamageOrDefense,
                Priority = 100,
                Source = "defense",
            });
        }
        if (item.OneHandMax is int oneMax && oneMax > 0)
            tip.Lines.Add(DamageLine("One-Hand Damage", item.OneHandMin ?? 0, oneMax, "damage:one"));
        if (item.TwoHandMax is int twoMax && twoMax > 0)
            tip.Lines.Add(DamageLine("Two-Hand Damage", item.TwoHandMin ?? 0, twoMax, "damage:two"));
        if (item.ThrowMax is int thrMax && thrMax > 0)
            tip.Lines.Add(DamageLine("Throw Damage", item.ThrowMin ?? 0, thrMax, "damage:throw"));
        if (item.Quantity is int qty && qty > 0)
        {
            tip.Lines.Add(new TooltipLine
            {
                Text = $"Quantity: {qty}",
                Color = D2Color.White,
                Section = TooltipSection.DamageOrDefense,
                Priority = 90,
                Source = "quantity",
            });
        }

        // ── Durability ────────────────────────────────────────────────────
        if (item.Durability is int d && item.MaxDurability is int md && md > 0)
        {
            tip.Lines.Add(new TooltipLine
            {
                Text = $"Durability: {d} of {md}",
                Color = D2Color.White,
                Section = TooltipSection.Durability,
                Priority = 100,
                Source = "durability",
            });
        }

        // ── Class restriction ────────────────────────────────────────────
        if (!string.IsNullOrEmpty(item.ClassRestriction))
        {
            tip.Lines.Add(new TooltipLine
            {
                Text = $"({item.ClassRestriction} Only)",
                Color = D2Color.White,
                Section = TooltipSection.Requirements,
                Priority = 110,
                Source = "classRestriction",
            });
        }

        // ── Required str / dex / level ───────────────────────────────────
        if (item.RequiredDexterity is int rd && rd > 0)
            tip.Lines.Add(ReqLine("Required Dexterity", rd, 80, "req:dex"));
        if (item.RequiredStrength is int rs && rs > 0)
            tip.Lines.Add(ReqLine("Required Strength", rs, 79, "req:str"));
        if (item.RequiredLevel is int rl && rl > 1)
        {
            tip.Lines.Add(ReqLine("Required Level", rl, 78, "req:lvl"));
        }

        // ── Unidentified bail-out ────────────────────────────────────────
        // D2 hides every stat line on unidentified items and shows just
        // "Unidentified" in red. Don't run the resolver in that case.
        if (!item.Identified)
        {
            tip.Lines.Add(new TooltipLine
            {
                Text = "Unidentified",
                Color = D2Color.Red,
                Section = TooltipSection.Stats,
                Priority = 50,
                Source = "unidentified",
            });
            AppendFlagsLine(item, tip);
            return tip;
        }

        // ── Stats ─────────────────────────────────────────────────────────
        // Resolve each raw stat. Lines come out pre-rendered when the resolver
        // could fill them in, or null-Rendered when a template is missing.
        // We keep the order stable by sorting after collection.
        var resolvedStats = new List<ResolvedItemStat?>(item.RawStats.Count);
        foreach (var raw in item.RawStats)
            resolvedStats.Add(_stats.Resolve(raw));

        var consumed = new HashSet<int>();
        // Multiple identical "X% Chance to cast level N <skill> <event>" stats
        // (e.g. one entry per Rainbow Facet in a 3-socketed shield) are stacked
        // into a single line whose chance is the sum. Only entries that match
        // exactly on (StatName, SkillId, SkillLevel) are stacked so unrelated
        // triggers (e.g. Nova-on-levelup vs Chain-Lightning-on-death) stay
        // independent. This runs BEFORE the dedup-by-text pass so the survivor
        // already carries the summed chance and isn't subsequently collapsed
        // against a stale 100% line.
        StackIdenticalSkillTriggers(item.RawStats, resolvedStats, consumed);

        AddGroupedStatLines(item, resolvedStats, consumed, tip);

        for (var i = 0; i < item.RawStats.Count; i++)
        {
            if (consumed.Contains(i)) continue;
            var raw = item.RawStats[i];
            var resolved = resolvedStats[i];
            if (resolved is null)
            {
                tip.UnresolvedStatIds.Add(raw.StatId);
                continue;
            }

            if (resolved.Hidden)
                continue;

            if (resolved.Rendered is not null)
            {
                tip.Lines.Add(resolved.Rendered);
                continue;
            }

            // No rendered line: either a missing string-table key or a stat we
            // explicitly chose not to render via the simple template path.
            // Emit a TODO line so the user/dev sees it instead of losing the stat.
            var label = resolved.StatName ?? $"stat#{raw.StatId}";
            tip.MissingStringKeys.Add(resolved.DescStrPos ?? resolved.DescStrNeg ?? label);
            tip.Lines.Add(new TooltipLine
            {
                Text = $"[TODO {label}: {raw.Value}]",
                Color = D2Color.Red,
                Section = TooltipSection.Stats,
                Priority = resolved.DescPriority,
                Source = $"fallback:unknown-stat:{raw.StatId}",
                StatIds = new[] { raw.StatId },
            });
        }

        // ── Dedup identical stat lines ───────────────────────────────────
        // Some stat variants share the same descstrpos in D2's data (e.g.
        // maxdamage and item_throw_maxdamage both map to "+%d to Maximum Damage").
        // D2 itself uses the dgrp mechanism to collapse them; we implement the
        // simpler guarantee that identical text in the Stats section only appears
        // once — keeping the first occurrence (highest priority wins via the
        // sort order applied in ToDescriptionString).
        {
            var seenStatLines = new HashSet<string>(StringComparer.Ordinal);
            tip.Lines.RemoveAll(l =>
                l.Section == TooltipSection.Stats && !seenStatLines.Add(l.Text));
        }

        AppendCharmInstructionLine(item, tip);

        // ── Flags (Ethereal, Socketed) ───────────────────────────────────
        AppendFlagsLine(item, tip);
        return tip;
    }

    private void AddGroupedStatLines(CanonicalD2Item item, IReadOnlyList<ResolvedItemStat?> resolvedStats, HashSet<int> consumed, ItemTooltip tip)
    {
        TryAddPhysicalDamageGroups(resolvedStats, consumed, tip);
        TryAddPoisonDamageGroup(item, resolvedStats, consumed, tip);

        TryAddEqualValueGroup(
            resolvedStats,
            consumed,
            new[] { "strength", "dexterity", "vitality", "energy" },
            value => $"{SignedInt(value)} to All Attributes",
            priority: 122,
            source: "group:all-attributes",
            tip);

        TryAddEqualValueGroup(
            resolvedStats,
            consumed,
            new[] { "fireresist", "coldresist", "lightresist", "poisonresist" },
            value => $"All Resistances {SignedInt(value)}",
            priority: 36,
            source: "group:all-resistances",
            tip);
    }

    private static void TryAddPhysicalDamageGroups(IReadOnlyList<ResolvedItemStat?> resolvedStats, HashSet<int> consumed, ItemTooltip tip)
    {
        var groups = new[]
        {
            new[] { "mindamage", "maxdamage" },
            new[] { "secondary_mindamage", "secondary_maxdamage" },
            new[] { "item_throw_mindamage", "item_throw_maxdamage" },
        };

        var added = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var min = FindStat(resolvedStats, consumed, group[0]);
            var max = FindStat(resolvedStats, consumed, group[1]);
            if (min.Stat is null || max.Stat is null) continue;

            var minValue = min.Stat.Raw.Value;
            var maxValue = max.Stat.Raw.Value;
            if (minValue > maxValue) continue;

            consumed.Add(min.Index);
            consumed.Add(max.Index);

            var text = minValue == maxValue
                ? $"Adds {minValue} damage"
                : $"Adds {minValue}-{maxValue} damage";
            if (!added.Add(text)) continue;

            tip.Lines.Add(new TooltipLine
            {
                Text = text,
                Color = D2Color.Magic,
                Section = TooltipSection.Stats,
                Priority = Math.Max(min.Stat.DescPriority, max.Stat.DescPriority),
                Source = $"group:physical-damage:{group[0]}:{group[1]}",
                StatIds = new[] { min.Stat.Raw.StatId, max.Stat.Raw.StatId },
            });
        }
    }

    private void TryAddPoisonDamageGroup(
        CanonicalD2Item item,
        IReadOnlyList<ResolvedItemStat?> resolvedStats,
        HashSet<int> consumed,
        ItemTooltip tip)
    {
        var matches = resolvedStats
            .Select((stat, index) => new { stat, index })
            .Where(x => x.stat?.StatName is not null
                && IsPoisonDamageStat(x.stat.StatName)
                && !consumed.Contains(x.index))
            .Select(x => (x.index, x.stat!))
            .ToList();
        if (matches.Count == 0) return;

        var poisonMin = matches.FirstOrDefault(m => string.Equals(m.Item2.StatName, "poisonmindam", StringComparison.OrdinalIgnoreCase));
        if (poisonMin.Item2 is null) return;

        var frames = PoisonFramesFromStats(matches.Select(m => m.Item2));
        var value = Math.Max(poisonMin.Item2.Raw.Value, Math.Max(poisonMin.Item2.Raw.Min ?? 0, poisonMin.Item2.Raw.Max ?? 0));
        if (item.Quality == 4)
        {
            var affixPoison = MagicAffixPoison(item);
            if (affixPoison is not null)
            {
                value = affixPoison.Value.Value;
                frames = affixPoison.Value.Frames;
            }
        }

        if (value <= 0 || frames <= 0) return;

        foreach (var match in matches) consumed.Add(match.index);

        var total = (int)Math.Round(value * frames / 256.0, MidpointRounding.AwayFromZero);
        var seconds = Math.Max(1, (int)Math.Round(frames / 25.0, MidpointRounding.AwayFromZero));
        tip.Lines.Add(new TooltipLine
        {
            Text = $"+{total} poison damage over {seconds} seconds",
            Color = D2Color.Magic,
            Section = TooltipSection.Stats,
            Priority = poisonMin.Item2.DescPriority,
            Source = "group:poison-damage",
            StatIds = matches.Select(m => m.Item2.Raw.StatId).Distinct().ToArray(),
        });
    }

    private (int Value, int Frames)? MagicAffixPoison(CanonicalD2Item item)
    {
        var value = 0;
        var frames = 0;
        AddMagicPoison(_items.MagicPrefixById(item.MagicPrefixId ?? -1), ref value, ref frames);
        AddMagicPoison(_items.MagicSuffixById(item.MagicSuffixId ?? -1), ref value, ref frames);
        return value > 0 && frames > 0 ? (value, frames) : null;
    }

    private static void AddMagicPoison(D2TxtRow? row, ref int value, ref int frames)
    {
        if (row is null) return;
        for (var i = 1; i <= 3; i++)
        {
            if (!string.Equals(row.Raw($"mod{i}code"), "dmg-pois", StringComparison.OrdinalIgnoreCase))
                continue;
            value += row.Int($"mod{i}max");
            frames += row.Int($"mod{i}param");
        }
    }

    private static bool IsPoisonDamageStat(string statName) =>
        string.Equals(statName, "poisonmindam", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(statName, "poisonmaxdam", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(statName, "poisonlength", StringComparison.OrdinalIgnoreCase);

    private static int PoisonFramesFromStats(IEnumerable<ResolvedItemStat> stats)
    {
        var frames = 0;
        foreach (var stat in stats)
        {
            var raw = stat.Raw;
            if (raw.Frames is int f && f > 0)
                frames += f;
            else if (string.Equals(stat.StatName, "poisonlength", StringComparison.OrdinalIgnoreCase) && raw.Value > 0)
                frames += raw.Value;
            else if (string.Equals(raw.Source, "affix:dmg-pois", StringComparison.OrdinalIgnoreCase) && raw.Param is int p && p > 0)
                frames += p;
        }
        return frames;
    }

    private static (int Index, ResolvedItemStat? Stat) FindStat(
        IReadOnlyList<ResolvedItemStat?> resolvedStats,
        HashSet<int> consumed,
        string statName)
    {
        for (var i = 0; i < resolvedStats.Count; i++)
        {
            if (consumed.Contains(i)) continue;
            var stat = resolvedStats[i];
            if (stat?.StatName is not null && string.Equals(stat.StatName, statName, StringComparison.OrdinalIgnoreCase))
                return (i, stat);
        }

        return (-1, null);
    }

    private static void TryAddEqualValueGroup(
        IReadOnlyList<ResolvedItemStat?> resolvedStats,
        HashSet<int> consumed,
        IReadOnlyList<string> statNames,
        Func<int, string> text,
        int priority,
        string source,
        ItemTooltip tip)
    {
        var matches = new List<(int Index, ResolvedItemStat Stat)>(statNames.Count);
        foreach (var name in statNames)
        {
            var found = resolvedStats
                .Select((stat, index) => new { stat, index })
                .FirstOrDefault(x => x.stat?.StatName is not null
                    && string.Equals(x.stat.StatName, name, StringComparison.OrdinalIgnoreCase)
                    && !consumed.Contains(x.index));
            if (found?.stat is null) return;
            matches.Add((found.index, found.stat));
        }

        var value = matches[0].Stat.Raw.Value;
        if (matches.Any(m => m.Stat.Raw.Value != value)) return;

        foreach (var match in matches) consumed.Add(match.Index);
        tip.Lines.Add(new TooltipLine
        {
            Text = text(value),
            Color = D2Color.Magic,
            Section = TooltipSection.Stats,
            Priority = priority,
            Source = source,
            StatIds = matches.Select(m => m.Stat.Raw.StatId).ToArray(),
        });
    }

    private static void AppendFlagsLine(CanonicalD2Item item, ItemTooltip tip)
    {
        var flags = new List<string>(2);
        if (item.Ethereal) flags.Add("Ethereal (Cannot be Repaired)");
        if (item.SocketCount > 0) flags.Add($"Socketed ({item.SocketCount})");
        if (flags.Count == 0) return;
        tip.Lines.Add(new TooltipLine
        {
            Text = string.Join(", ", flags),
            Color = D2Color.Magic,
            Section = TooltipSection.Flags,
            Priority = 0,
            Source = "flags",
        });
    }

    private static void AppendCharmInstructionLine(CanonicalD2Item item, ItemTooltip tip)
    {
        if (!IsCharm(item)) return;
        // D2 renders this white line directly under the charm title/base identity,
        // before Required Level and before the blue bonus stats.
        tip.Lines.Add(new TooltipLine
        {
            Text = "Keep in Inventory to Gain Bonus",
            Color = D2Color.White,
            Section = TooltipSection.CharmInstruction,
            Priority = 1000,
            Source = "charm:inventory-bonus-instruction",
        });
    }

    private static bool IsCharm(CanonicalD2Item item) =>
        string.Equals(item.Code, "cm1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(item.Code, "cm2", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(item.Code, "cm3", StringComparison.OrdinalIgnoreCase);

    private static bool TitleAlreadyRepresentsBaseName(CanonicalD2Item item, string titleText)
    {
        if (string.IsNullOrWhiteSpace(item.BaseName)) return false;
        if (string.Equals(item.BaseName, titleText, StringComparison.Ordinal)) return true;
        return item.Identified
            && string.Equals(item.BaseName, item.Title, StringComparison.Ordinal);
    }

    /// <summary>
    /// True when the item is a normal (non-runeword) magic item (quality 4) whose
    /// title already contains the base type — in which case D2 does not render a
    /// separate base-name line below the title. For other qualities (unique, set,
    /// rare, crafted) and for runewords, D2 always shows the base type as a
    /// separate line, so this returns false.
    /// </summary>
    private static bool IsMagicTitleEmbeddingBase(CanonicalD2Item item, string titleText)
    {
        if (item.Quality != 4 || item.Runeword) return false;
        if (string.IsNullOrEmpty(item.BaseName)) return false;
        return titleText.IndexOf(item.BaseName, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Append a gold "BerBerBer"-style line listing the inserted socket fillers
    /// by their D2 display names (with " Rune" suffix stripped for runes). The
    /// line is omitted for runewords (the runeword title already conveys the
    /// rune sequence), for unidentified items, and when any filler code cannot
    /// be resolved to a display name — we prefer no line over a misleading one.
    /// </summary>
    private void AppendSocketNameLine(CanonicalD2Item item, ItemTooltip tip)
    {
        if (!item.Identified) return;
        if (item.SocketFillers.Count == 0) return;

        var names = new List<string>(item.SocketFillers.Count);
        foreach (var code in item.SocketFillers)
        {
            var name = SocketFillerDisplayName(code);
            if (name is null) return; // skip the whole line on any unknown filler
            names.Add(name);
        }
        if (names.Count == 0) return;

        tip.Lines.Add(new TooltipLine
        {
            Text = string.Concat(names),
            Color = D2Color.Unique, // D2 gold for inserted-socket spelling
            Section = TooltipSection.RunewordSpell,
            Priority = 1000,
            Source = "socket:names",
        });
    }

    /// <summary>
    /// Look up the D2 display name for a socket filler code (e.g. "r30" → "Ber",
    /// "gpb" → "Perfect Topaz", "jew" → "Jewel"). Returns null for empty-socket
    /// placeholders or codes that don't resolve to a base item row.
    /// </summary>
    private string? SocketFillerDisplayName(string code)
    {
        if (string.IsNullOrEmpty(code)) return null;
        if (string.Equals(code, "gemsocket", StringComparison.OrdinalIgnoreCase)) return null;

        var row = _items.BaseItemByCode(code);
        var name = row?.Raw("name");
        if (string.IsNullOrEmpty(name)) return null;

        // Strip the " Rune" suffix so 3 Ber runes spell "BerBerBer", matching the
        // way D2 displays rune-sequence lines in the live tooltip.
        const string runeSuffix = " Rune";
        if (!name.EndsWith(runeSuffix, StringComparison.OrdinalIgnoreCase))
            return null;

        return name.Substring(0, name.Length - runeSuffix.Length);
    }

    private static TooltipLine DamageLine(string label, int min, int max, string source) => new()
    {
        Text = $"{label}: {min} to {max}",
        Color = D2Color.White,
        Section = TooltipSection.DamageOrDefense,
        Priority = 95,
        Source = source,
    };

    private static TooltipLine ReqLine(string label, int value, int prio, string source) => new()
    {
        Text = $"{label}: {value}",
        Color = D2Color.White,
        Section = TooltipSection.Requirements,
        Priority = prio,
        Source = source,
    };

    private static string SignedInt(int v) => v >= 0 ? $"+{v}" : v.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Stack identical socket-stacked skill-trigger stats by summing their
    /// <see cref="RawItemStat.Chance"/>. Two entries are considered the same
    /// trigger when their resolved stat name AND <see cref="RawItemStat.SkillId"/>
    /// AND <see cref="RawItemStat.SkillLevel"/> all match — covering the
    /// "3 identical Rainbow Facets" case while leaving distinct triggers
    /// (different skill or different level) alone.
    ///
    /// The earliest entry keeps its slot in <paramref name="resolvedStats"/>;
    /// later duplicates are marked consumed so the main rendering loop skips
    /// them. The survivor is re-resolved with the summed chance so the
    /// rendered line reads e.g. "300% Chance to cast level 31 Meteor when you
    /// Die" instead of the dedup-collapsed "100% Chance ..." that we used to
    /// produce.
    /// </summary>
    private void StackIdenticalSkillTriggers(
        IReadOnlyList<RawItemStat> raws,
        List<ResolvedItemStat?> resolvedStats,
        HashSet<int> consumed)
    {
        var head = new Dictionary<(string statName, int skillId, int skillLevel), int>();
        var sum  = new Dictionary<(string statName, int skillId, int skillLevel), int>();

        for (var i = 0; i < raws.Count; i++)
        {
            var r = resolvedStats[i];
            if (r?.StatName is null) continue;
            // All "X on event" stat names in 1.13c start with item_skillon
            // (item_skillondeath / onlevelup / onkill / onattack / onhit /
            // ongethit / onstrike). Keying off the prefix keeps this future-proof
            // for any additional onEvent stats added later.
            if (!r.StatName.StartsWith("item_skillon", StringComparison.Ordinal)) continue;

            var raw = raws[i];
            if (raw.SkillId is not int sk || raw.SkillLevel is not int lv || raw.Chance is not int ch)
                continue;

            var key = (r.StatName, sk, lv);
            if (head.TryGetValue(key, out var firstIdx))
            {
                sum[key] += ch;
                consumed.Add(i);
            }
            else
            {
                head[key] = i;
                sum[key] = ch;
            }
        }

        foreach (var (key, firstIdx) in head)
        {
            var summed = sum[key];
            var originalChance = raws[firstIdx].Chance ?? 0;
            if (summed == originalChance) continue; // single entry; nothing to stack
            var stacked = WithChance(raws[firstIdx], summed);
            var rerendered = _stats.Resolve(stacked);
            if (rerendered is null) continue;
            resolvedStats[firstIdx] = rerendered;
        }
    }

    /// <summary>
    /// Clone a <see cref="RawItemStat"/> with a replaced <see cref="RawItemStat.Chance"/>.
    /// Used by the skill-trigger stacker to feed the resolver a summed-chance
    /// stat without mutating the canonical item's input list.
    /// </summary>
    private static RawItemStat WithChance(RawItemStat src, int chance) => new()
    {
        StatId = src.StatId,
        Param = src.Param,
        Value = src.Value,
        RawValue = src.RawValue,
        CharacterLevel = src.CharacterLevel,
        Min = src.Min,
        Max = src.Max,
        Frames = src.Frames,
        SkillId = src.SkillId,
        SkillLevel = src.SkillLevel,
        Chance = chance,
        Charges = src.Charges,
        MaxCharges = src.MaxCharges,
        ClassId = src.ClassId,
        TabId = src.TabId,
        MonsterId = src.MonsterId,
        Source = src.Source + ";stacked",
    };

    private static D2Color QualityColor(CanonicalD2Item item)
    {
        if (IsHoradricCube(item)) return D2Color.Unique;
        if (item.Runeword) return D2Color.Unique;
        return item.Quality switch
        {
            7 => D2Color.Unique,
            5 => D2Color.Set,
            6 => D2Color.Rare,
            8 => D2Color.Craft,
            4 => D2Color.Magic,
            1 => D2Color.Gray,
            _ => D2Color.White,
        };
    }

    private static bool IsHoradricCube(CanonicalD2Item item) =>
        string.Equals(item.Code, "box", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(item.Title, "Horadric Cube", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(item.BaseName, "Horadric Cube", StringComparison.OrdinalIgnoreCase);
}
