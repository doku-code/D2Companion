namespace D2CompanionMvc.Domain.Items;

/// <summary>
/// Bag-of-fields representation of a single stat as it arrives from a raw item
/// source (Styx packets today, save-files later). It's the *input* to the
/// resolver — nothing here has been mapped against the TXT tables yet.
///
/// Fields are deliberately optional: a simple `+30 to Life` only needs
/// <see cref="StatId"/> + <see cref="Value"/>, while a charged-skill triple
/// needs <see cref="SkillId"/>, <see cref="SkillLevel"/>, <see cref="Charges"/>,
/// <see cref="MaxCharges"/>. The resolver inspects the populated fields to
/// pick the right rendering path.
/// </summary>
public sealed class RawItemStat
{
    /// <summary>Row index in <c>ItemStatCost.txt</c>. The single most important field.</summary>
    public required int StatId { get; init; }

    /// <summary>Optional parameter (e.g. class id for class skills, skill id for charged skills).</summary>
    public int? Param { get; init; }

    /// <summary>Primary numeric value (e.g. +30 to Life → 30).</summary>
    public int Value { get; init; }

    /// <summary>
    /// Original numeric value before integer coercion. Per-level stats can
    /// arrive as fractional coefficients such as 0.375 per character level.
    /// </summary>
    public double? RawValue { get; init; }

    /// <summary>Character level at snapshot time, used to evaluate per-level stats when available.</summary>
    public int? CharacterLevel { get; init; }

    /// <summary>Min side of a range stat (e.g. fire damage min). Null when not a range.</summary>
    public int? Min { get; init; }

    /// <summary>Max side of a range stat. Null when not a range.</summary>
    public int? Max { get; init; }

    /// <summary>For poison damage: duration in frames. Null otherwise.</summary>
    public int? Frames { get; init; }

    /// <summary>For skill stats: the skill row id (Skills.txt).</summary>
    public int? SkillId { get; init; }

    /// <summary>For skill-on-event / charged stats: the skill level granted.</summary>
    public int? SkillLevel { get; init; }

    /// <summary>For skill-on-event: chance to trigger (1..100).</summary>
    public int? Chance { get; init; }

    /// <summary>For charged skills: current charges remaining.</summary>
    public int? Charges { get; init; }

    /// <summary>For charged skills: maximum charges.</summary>
    public int? MaxCharges { get; init; }

    /// <summary>For class skill bonuses: class id (0..6).</summary>
    public int? ClassId { get; init; }

    /// <summary>For skill-tab bonuses: tab id within the class (0..2).</summary>
    public int? TabId { get; init; }

    /// <summary>For "reanimate as <monster>" stats.</summary>
    public int? MonsterId { get; init; }

    /// <summary>Free-form source identifier ("styx:dstats", "styx:flatstat", "save:csv-pair", ...) used in debug output.</summary>
    public string Source { get; init; } = "";
}
