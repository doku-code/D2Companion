using D2CompanionMvc.Domain.Items;

namespace D2CompanionMvc.Services.Items.Rendering;

/// <summary>
/// A <see cref="RawItemStat"/> after it has been crossed against
/// <c>ItemStatCost.txt</c> / <c>Skills.txt</c> / class data, ready to be
/// rendered as a tooltip line.
///
/// The resolver keeps the original <see cref="Raw"/> handy for debugging and
/// for stats that re-export themselves without further translation (e.g.
/// fillers that the front-end already knows how to draw).
/// </summary>
public sealed class ResolvedItemStat
{
    public required RawItemStat Raw { get; init; }

    /// <summary>Canonical name from ItemStatCost.txt (e.g. "item_allskills"). Null when the stat id is out of range.</summary>
    public string? StatName { get; init; }

    /// <summary>Display priority pulled from <c>descpriority</c>. Higher = earlier line in the tooltip.</summary>
    public int DescPriority { get; init; }

    /// <summary>Display function id (1..28) from <c>descfunc</c>. Used by the renderer to pick a template.</summary>
    public int DescFunc { get; init; }

    /// <summary>Where the value goes in the template: 0 hidden, 1 before, 2 after.</summary>
    public int DescVal { get; init; }

    /// <summary>String-table key for the positive variant of the stat description.</summary>
    public string? DescStrPos { get; init; }

    /// <summary>String-table key for the negative variant.</summary>
    public string? DescStrNeg { get; init; }

    /// <summary>Secondary string-table key (e.g. "to Mana" suffix on `manarecoverybonus`).</summary>
    public string? DescStr2 { get; init; }

    /// <summary>Display priority within a group (D2 uses <c>dgrp</c> to merge multi-stats).</summary>
    public int DescGroup { get; init; }

    /// <summary>Group function (when <see cref="DescGroup"/> > 0).</summary>
    public int DescGroupFunc { get; init; }

    /// <summary>Group value position (analogous to <see cref="DescVal"/>).</summary>
    public int DescGroupVal { get; init; }

    /// <summary>Resolved English skill name when the stat references a skill (Skills.txt#skill).</summary>
    public string? SkillName { get; init; }

    /// <summary>Resolved class name when the stat references a class.</summary>
    public string? ClassName { get; init; }

    /// <summary>Resolved skill-tab name when the stat references a skill tab.</summary>
    public string? SkillTabName { get; init; }

    /// <summary>
    /// The rendered tooltip line. The resolver fills this in when it can produce
    /// confident text; the renderer reads it directly. Null when the resolver
    /// chose to defer (caller falls back to the descstr* templates).
    /// </summary>
    public TooltipLine? Rendered { get; init; }

    /// <summary>True when the stat is known but intentionally omitted from the visible tooltip.</summary>
    public bool Hidden { get; init; }

    /// <summary>Diagnostic reason for <see cref="Hidden"/> stats.</summary>
    public string? HiddenReason { get; init; }

    /// <summary>Computed display value for derived stats such as per-character-level bonuses.</summary>
    public int? ComputedValue { get; init; }

    /// <summary>True when a per-character-level stat used level 1 because the raw stat had no character level.</summary>
    public bool CharacterLevelFallbackUsed { get; init; }
}
