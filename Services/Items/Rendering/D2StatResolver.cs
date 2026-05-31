using D2CompanionMvc.Domain.Items;
using D2CompanionMvc.Services.GameData;

namespace D2CompanionMvc.Services.Items.Rendering;

/// <summary>
/// Converts a <see cref="RawItemStat"/> (raw stat id + value) into a
/// <see cref="ResolvedItemStat"/> with ItemStatCost metadata attached and,
/// when possible, a fully-rendered <see cref="TooltipLine"/>.
///
/// The renderer that comes next (<c>D2TooltipRenderer</c>) ultimately decides
/// the order and the colour, but if a stat has a clear, deterministic D2
/// rendering — e.g. "+%d to Strength" — we emit the line right here so the
/// renderer can append it as-is and so it shows up in the structured
/// <see cref="ItemTooltip"/> with the right metadata.
///
/// Unknown stats are not silently dropped. They land in
/// <see cref="ItemTooltip.UnresolvedStatIds"/> via the caller so the debug
/// endpoint can surface them.
/// </summary>
public sealed class D2StatResolver
{
    private readonly D2GameData _data;
    private readonly D2StatLookupService _stats;
    private readonly ID2StringResolver _strings;

    // Class id → display name. Index by `RawItemStat.ClassId`.
    // Matches CharStats.txt row order, which is also Styx's classid encoding.
    private static readonly string[] ClassNames = new[]
    {
        "Amazon", "Sorceress", "Necromancer", "Paladin",
        "Barbarian", "Druid", "Assassin",
    };

    // (classId, tabId) → tab display name. Both indices come from Styx's
    // SkillTabBonusStat (`charClass * 3 + tab`).
    private static readonly string[] SkillTabNames = new[]
    {
        // Amazon
        "Bow and Crossbow", "Passive and Magic", "Javelin and Spear",
        // Sorceress
        "Fire", "Lightning", "Cold",
        // Necromancer
        "Curses", "Poison and Bone", "Necromancer Summoning",
        // Paladin
        "Combat", "Offensive Auras", "Defensive Auras",
        // Barbarian
        "Combat", "Combat Masteries", "Warcries",
        // Druid
        "Druid Summoning", "Shape Shifting", "Elemental",
        // Assassin
        "Traps", "Shadow Disciplines", "Martial Arts",
    };

    public D2StatResolver(D2GameData data, D2StatLookupService stats, ID2StringResolver strings)
    {
        _data = data;
        _stats = stats;
        _strings = strings;
    }

    /// <summary>Resolve a single stat. Returns null when the stat id is out of range.</summary>
    public ResolvedItemStat? Resolve(RawItemStat raw)
    {
        var row = _stats.StatById(raw.StatId);
        if (row is null) return null;

        var name = row.Raw("Stat");
        var descPrio = row.Int("descpriority");
        var descFunc = row.Int("descfunc");
        var descVal  = row.Int("descval");
        var descStrPos = row.Raw("descstrpos");
        var descStrNeg = row.Raw("descstrneg");
        var descStr2 = row.Raw("descstr2");

        // ── Param-encoded class / skill / tab decoding ───────────────────────
        // Styx does not send separate ClassId / TabId / SkillId fields for most
        // skill stats; instead it folds them into Param using D2's own conventions:
        //   item_addskill_tab    → param = classId * 3 + tabId
        //   item_addclassskills  → param = classId
        //   all other skill stats → param = skillId
        // Explicit RawItemStat fields take precedence when present (future ingest
        // sources that fill them directly will continue to work unchanged).
        int? effectiveClassId = raw.ClassId;
        int? effectiveTabId   = raw.TabId;
        int? effectiveSkillId = raw.SkillId;

        if (raw.Param is int p)
        {
            switch (name)
            {
                case "item_addskill_tab":
                    if (effectiveClassId is null) effectiveClassId = p / 3;
                    if (effectiveTabId   is null) effectiveTabId   = p % 3;
                    break;
                case "item_addclassskills":
                    if (effectiveClassId is null) effectiveClassId = p;
                    break;
                case "item_singleskill":
                case "item_nonclassskill":
                case "item_aura":
                case "item_charged_skill":
                case "item_skillonattack":
                case "item_skillonhit":
                case "item_skillondeath":
                case "item_skillongethit":
                case "item_skillonkill":
                case "item_skillonlevelup":
                    if (effectiveSkillId is null) effectiveSkillId = p;
                    break;
            }
        }

        // Class / skill / tab name resolution using the effective (decoded) ids.
        string? className = effectiveClassId is int cid && cid >= 0 && cid < ClassNames.Length ? ClassNames[cid] : null;
        string? tabName = effectiveClassId is int ccid && effectiveTabId is int tid && tid >= 0 && tid <= 2 && ccid >= 0 && ccid < ClassNames.Length
            ? SkillTabNames[ccid * 3 + tid]
            : null;
        string? skillName = effectiveSkillId is int sid ? _stats.SkillNameById(sid) : null;

        var computedValue = ComputedValue(raw, name);
        var characterLevelFallbackUsed = IsPerLevelStat(name) && raw.CharacterLevel is null;
        var hiddenReason = HiddenReason(raw, name);

        // After-kill ordering tie-break: ItemStatCost gives both
        // item_manaafterkill and item_healafterkill descpriority=16, so a
        // pure descpriority-DESC sort renders them in arbitrary insertion
        // order. D2 (and the user-supplied golden Grief sample) shows mana
        // before life. Bump the mana priority by 1 so the tooltip sort
        // (Section asc, Priority desc) places it first.
        if (string.Equals(name, "item_manaafterkill", StringComparison.OrdinalIgnoreCase))
            descPrio += 1;
        descPrio += StatOrderPriorityBoost(name, effectiveSkillId);
        var rendered = hiddenReason is null
            ? RenderLine(raw, name, descPrio, descFunc, descVal, descStrPos, descStrNeg, descStr2, className, tabName, skillName, effectiveSkillId, computedValue)
            : null;

        var resolved = new ResolvedItemStat
        {
            Raw = raw,
            StatName = name,
            DescPriority = descPrio,
            DescFunc = descFunc,
            DescVal = descVal,
            DescStrPos = descStrPos,
            DescStrNeg = descStrNeg,
            DescStr2 = descStr2,
            DescGroup = row.Int("dgrp"),
            DescGroupFunc = row.Int("dgrpfunc"),
            DescGroupVal = row.Int("dgrpval"),
            SkillName = skillName,
            ClassName = className,
            SkillTabName = tabName,
            Rendered = rendered,
            Hidden = hiddenReason is not null,
            HiddenReason = hiddenReason,
            ComputedValue = computedValue,
            CharacterLevelFallbackUsed = characterLevelFallbackUsed,
        };
        return resolved;
    }

    private static string? HiddenReason(RawItemStat raw, string? statName)
    {
        return statName switch
        {
            "quantity" => "quantity is rendered from the canonical item Quantity field",
            "item_levelreq" => "item_levelreq is folded into the canonical Required Level line",
            "maxdurability" => "maxdurability is represented by the canonical Durability line",
            "item_extrablood" => "item_extrablood is an internal open-wounds display helper",
            _ => null,
        };
        // Per-level stats are NEVER hidden purely on the basis of CharacterLevel
        // missing or the fallback computing to 0. The fallback rule is:
        // CharacterLevel ?? 1 — see ComputedValue. The previous attempt to
        // suppress "0% Deadly Strike (Based on Character Level)" was wrong;
        // the user wants the line to render even if uninformative, because
        // hiding stats based on missing data masks data freshness problems.
    }

    private static int? ComputedValue(RawItemStat raw, string? statName)
    {
        if (!IsPerLevelStat(statName)) return null;
        var clvl = raw.CharacterLevel ?? 1;
        var coefficient = raw.RawValue ?? raw.Value;
        return (int)Math.Floor(coefficient * clvl);
    }

    /// <summary>
    /// Try to produce a fully-rendered tooltip line for the stat. Returns null
    /// when no template is known for the stat — caller is responsible for
    /// noting the unresolved stat id in the tooltip debug fields.
    /// </summary>
    private TooltipLine? RenderLine(
        RawItemStat raw,
        string? statName,
        int descPrio,
        int descFunc,
        int descVal,
        string? descStrPos,
        string? descStrNeg,
        string? descStr2,
        string? className,
        string? tabName,
        string? skillName,
        int? effectiveSkillId,
        int? computedValue)
    {
        // Skill-bonus stats (single skill, class skills, skill tab, charged, aura, on-event)
        // need bespoke templates because they thread named parameters that descfunc
        // 13/14/15/16/24 can't express by themselves.
        switch (statName)
        {
            case "item_allskills":
                return LineFromTemplate("ModStr3k", raw, descPrio, statName);
            // Throw-damage variants share the same display string as their one-hand
            // counterparts in D2's native tooltip. We map them explicitly here so they
            // always render even when the TXT row has no descstrpos.
            case "item_throw_maxdamage":
                return LineFromTemplate("ModStr1f", raw, descPrio, statName);
            case "item_throw_mindamage":
                return LineFromTemplate("ModStr1g", raw, descPrio, statName);
            case "firemindam":
                return ElementalDamageLine(raw, "Fire", descPrio, statName);
            case "lightmindam":
                return ElementalDamageLine(raw, "Lightning", descPrio, statName);
            case "coldmindam":
                return ElementalDamageLine(raw, "Cold", descPrio, statName);
            case "item_normaldamage":
                // Grief's flat physical damage stat. D2/MuleLogger wording is
                // "Damage +400" (label first, signed value second), not the
                // "Adds X-Y damage" min/max-pair phrasing. Confirmed against
                // golden Grief / Beast samples. Keep this separate from the
                // mindamage+maxdamage pair grouping below.
                return new TooltipLine
                {
                    Text = $"Damage +{raw.Value}",
                    Color = D2Color.Magic,
                    Section = TooltipSection.Stats,
                    Priority = descPrio,
                    Source = $"stat:{statName}",
                    StatIds = new[] { raw.StatId },
                };
            case "item_mindamage_percent":
                if (raw.Min is int minDamagePercent && raw.Max == minDamagePercent && raw.Value == 0)
                    return LineFromTemplate(descStrPos ?? "ModStr2k", raw, descPrio, statName, minDamagePercent);
                break;
            case "item_addclassskills":
                className ??= "Unknown";
                return new TooltipLine
                {
                    Text = $"{SignedInt(raw.Value)} to {className} Skill Levels",
                    Color = D2Color.Magic,
                    Section = TooltipSection.Stats,
                    Priority = descPrio,
                    Source = $"stat:{statName}({className})",
                    StatIds = new[] { raw.StatId },
                };
            case "item_addskill_tab":
                className ??= "Unknown";
                tabName ??= "Unknown Skill Tab";
                // D2/MuleLogger wording: "+3 to Warcries Skills (Barbarian Only)".
                // The trailing " Skills" is part of the canonical line for
                // skill-tab bonuses (the tab is a *group* of skills, not a
                // single skill). The "(<Class> Only)" tag is always present
                // because skill tabs are class-restricted. Guard against a
                // tab name that already ends in "Skills" so we don't render
                // "Skills Skills" if a future tab table changes.
                var tabWithSkills = tabName.EndsWith(" Skills", StringComparison.OrdinalIgnoreCase)
                    ? tabName
                    : $"{tabName} Skills";
                return new TooltipLine
                {
                    Text = $"{SignedInt(raw.Value)} to {tabWithSkills} ({className} Only)",
                    Color = D2Color.Magic,
                    Section = TooltipSection.Stats,
                    Priority = descPrio,
                    Source = $"stat:{statName}({className}:{tabName})",
                    StatIds = new[] { raw.StatId },
                };
            case "item_singleskill":
                // Native class skill bonus. Look up the skill's owning class
                // and append "(<Class> Only)" — matches D2 / MuleLogger.
                // E.g. "+3 to Battle Orders (Barbarian Only)".
                skillName ??= "Unknown Skill";
                var owningClass = effectiveSkillId is int eSid ? _stats.SkillClassById(eSid) : null;
                return new TooltipLine
                {
                    Text = owningClass is not null
                        ? $"{SignedInt(raw.Value)} to {skillName} ({owningClass} Only)"
                        : $"{SignedInt(raw.Value)} to {skillName}",
                    Color = D2Color.Magic,
                    Section = TooltipSection.Stats,
                    Priority = descPrio,
                    Source = $"stat:{statName}({skillName})",
                    StatIds = new[] { raw.StatId },
                };
            case "item_nonclassskill":
                // Oskill — the whole point is that anyone can cast it
                // (Enigma's "+1 to Teleport"). Do NOT add a class suffix
                // even though the underlying skill has an owning class.
                skillName ??= "Unknown Skill";
                return new TooltipLine
                {
                    Text = $"{SignedInt(raw.Value)} to {skillName}",
                    Color = D2Color.Magic,
                    Section = TooltipSection.Stats,
                    Priority = descPrio,
                    Source = $"stat:{statName}({skillName})",
                    StatIds = new[] { raw.StatId },
                };
            case "item_aura":
                skillName ??= "Unknown Skill";
                if (raw.SkillLevel is null) break;
                return new TooltipLine
                {
                    Text = $"Level {raw.SkillLevel.Value} {skillName} Aura When Equipped",
                    Color = D2Color.Magic,
                    Section = TooltipSection.Stats,
                    Priority = descPrio,
                    Source = $"stat:{statName}({skillName})",
                    StatIds = new[] { raw.StatId },
                };
            case "item_charged_skill":
                skillName ??= "Unknown Skill";
                if (raw.SkillLevel is null) break;
                return new TooltipLine
                {
                    Text = $"Level {raw.SkillLevel.Value} {skillName} ({raw.Charges ?? 0}/{raw.MaxCharges ?? 0} Charges)",
                    Color = D2Color.Magic,
                    Section = TooltipSection.Stats,
                    Priority = descPrio,
                    Source = $"stat:{statName}({skillName})",
                    StatIds = new[] { raw.StatId },
                };
            case "item_skillonattack":
                return SkillTriggerLine(raw, skillName, "on attack", descPrio, statName);
            case "item_skillonhit":
                return SkillTriggerLine(raw, skillName, "on striking", descPrio, statName);
            case "item_skillondeath":
                return SkillTriggerLine(raw, skillName, "when you Die", descPrio, statName);
            case "item_skillongethit":
                return SkillTriggerLine(raw, skillName, "when struck", descPrio, statName);
            case "item_skillonkill":
                return SkillTriggerLine(raw, skillName, "when you Kill an Enemy", descPrio, statName);
            case "item_skillonlevelup":
                return SkillTriggerLine(raw, skillName, "when you Level-Up", descPrio, statName);

            // ItemStatCost row 36 is `damageresist` — the PERCENT variant of damage
            // reduction (descfunc=2). It shares the descstrpos "ModStr2u" with the
            // FLAT variant at row 34 ("normal_damage_reduction", descfunc=3), and
            // because the shared template is just "Damage Reduced by %d" the % sign
            // is missing when the percent variant is rendered through the default
            // template path. Render it explicitly here so 8% facets stack to
            // "Damage Reduced by 24%" instead of "Damage Reduced by 24".
            case "damageresist":
                return new TooltipLine
                {
                    Text = $"Damage Reduced by {raw.Value}%",
                    Color = D2Color.Magic,
                    Section = TooltipSection.Stats,
                    Priority = descPrio,
                    Source = $"stat:{statName}",
                    StatIds = new[] { raw.StatId },
                };

            // ItemStatCost row 37 is `magicresist` — magic resistance (an elemental
            // White-style weapon tooltips place the undead damage line at the
            // tail of the stat block, directly before the flags/socket line.
            case "item_undeaddamage_percent":
                return new TooltipLine
                {
                    Text = $"+{raw.Value}% Damage to Undead",
                    Color = D2Color.Magic,
                    Section = TooltipSection.Stats,
                    Priority = -900,
                    Source = $"stat:{statName}",
                    StatIds = new[] { raw.StatId },
                };
        }

        // Stats with simple "+N to <descstrpos>" / "<descstrpos> +N%" / etc. — drive
        // straight off descfunc + the resolved string template. Picking the right
        // descstr (pos vs neg) follows D2's rule: neg only when val < 0.
        var template = (raw.Value < 0 ? descStrNeg : descStrPos) ?? descStrPos;
        if (template is null) return null;
        return LineFromTemplate(template, raw, descPrio, statName, computedValue);
    }

    private TooltipLine? SkillTriggerLine(RawItemStat raw, string? skillName, string eventClause, int prio, string? statName)
    {
        skillName ??= "Unknown Skill";
        if (raw.SkillLevel is null || raw.Chance is null) return null;
        return new TooltipLine
        {
            Text = $"{raw.Chance.Value}% Chance to cast level {raw.SkillLevel.Value} {skillName} {eventClause}",
            Color = D2Color.Magic,
            Section = TooltipSection.Stats,
            // Skill-on-event lines have very high descpriority in D2 (~160) — boost ours
            // if the table doesn't carry one (some derived stats don't set it).
            Priority = prio > 0 ? prio : 160,
            Source = $"stat:{statName}({skillName})",
            StatIds = new[] { raw.StatId },
        };
    }

    private static TooltipLine? ElementalDamageLine(RawItemStat raw, string element, int prio, string? statName)
    {
        if (raw.Min is not int minVal || raw.Max is not int maxVal) return null;
        // Golden / MuleLogger uses lowercase for the element noun:
        //   "Adds 34-90 fire damage"   (not "Fire Damage")
        // The "Adds" capital and the digits are unchanged. Match this so
        // SampleAccount1 parity rows in category A go away without
        // breaking any other elemental-damage formatting elsewhere
        // (headings like "Lightning Damage: 1-5" are templated through
        // LineFromTemplate and stay unaffected).
        var elementLower = element.ToLowerInvariant();
        return new TooltipLine
        {
            Text = $"Adds {minVal}-{maxVal} {elementLower} damage",
            Color = D2Color.Magic,
            Section = TooltipSection.Stats,
            Priority = prio,
            Source = $"stat:{statName}",
            StatIds = new[] { raw.StatId },
        };
    }

    /// <summary>
    /// Render "+%d to X" / "%d%% Y" templates from a resolved string-table value.
    ///
    /// When the template contains two <c>%d</c> placeholders (e.g. "Lightning
    /// Damage: %d-%d") and the raw stat carries a Min/Max pair, the first
    /// placeholder is filled with Min and the second with Max. This handles all
    /// elemental damage stats (lightmindam, firemindam, coldmindam, etc.) where
    /// Styx stores the damage range in Min/Max rather than Value (which is 0).
    ///
    /// Single-placeholder templates continue to use <c>raw.Value</c>.
    /// </summary>
    private TooltipLine? LineFromTemplate(string templateKey, RawItemStat raw, int prio, string? statName, int? valueOverride = null)
    {
        var template = _strings.Resolve(templateKey);
        if (template is null) return null;

        string text;
        var first = template.IndexOf("%d", StringComparison.Ordinal);
        var second = first >= 0 ? template.IndexOf("%d", first + 2, StringComparison.Ordinal) : -1;

        if (second >= 0 && raw.Min is int minVal && raw.Max is int maxVal)
        {
            // Range template: replace first %d with Min, second with Max.
            // String-build from left to right keeps indices stable.
            text = (template.Substring(0, first)
                + minVal.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + template.Substring(first + 2, second - first - 2)
                + maxVal.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + template.Substring(second + 2))
                .Replace("%%", "%");
        }
        else
        {
            var valueStr = (valueOverride ?? raw.Value).ToString(System.Globalization.CultureInfo.InvariantCulture);
            text = template.Replace("%d", valueStr).Replace("%%", "%");
        }
        if (IsPerLevelStat(statName))
            text = $"{text} (Based on Character Level)";
        text = text.Replace("%s", "Unknown");

        return new TooltipLine
        {
            Text = text,
            Color = D2Color.Magic,
            Section = TooltipSection.Stats,
            Priority = prio,
            Source = $"stat:{statName}",
            StatIds = new[] { raw.StatId },
        };
    }

    private static string SignedInt(int v) => v >= 0 ? $"+{v}" : v.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static int StatOrderPriorityBoost(string? statName, int? effectiveSkillId)
    {
        if (statName is null) return 0;

        if (string.Equals(statName, "item_nonclassskill", StringComparison.OrdinalIgnoreCase))
        {
            // Call to Arms / D2 orders the Barbarian warcry oskills as
            // Battle Command, Battle Orders, Battle Cry. ItemStatCost gives
            // the oskill rows one shared priority, so use the skill id only
            // as a tie-breaker for this known runeword family.
            return effectiveSkillId switch
            {
                155 => 3, // Battle Command
                149 => 2, // Battle Orders
                146 => 1, // Battle Cry
                _ => 0,
            };
        }

        if (IsElementalPierceStat(statName))
            return 1;

        return 0;
    }

    private static bool IsElementalPierceStat(string statName) =>
        string.Equals(statName, "item_pierce_fire", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(statName, "item_pierce_cold", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(statName, "item_pierce_ltng", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(statName, "item_pierce_pois", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(statName, "passive_fire_pierce", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(statName, "passive_cold_pierce", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(statName, "passive_ltng_pierce", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(statName, "passive_pois_pierce", StringComparison.OrdinalIgnoreCase);

    private static bool IsPerLevelStat(string? statName) =>
        statName?.EndsWith("_perlevel", StringComparison.OrdinalIgnoreCase) == true;
}
