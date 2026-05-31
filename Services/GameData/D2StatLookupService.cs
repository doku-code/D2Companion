namespace D2CompanionMvc.Services.GameData;

/// <summary>
/// Convenience accessors for stat-related lookups: ItemStatCost rows by name or id,
/// skills by id, etc. The stat resolver consumes this; nothing else should be
/// reading these tables directly.
/// </summary>
public sealed class D2StatLookupService
{
    private readonly D2GameData _data;

    public D2StatLookupService(D2GameData data) => _data = data;

    /// <summary>Stat row by its <c>Stat</c> name column (e.g. "item_allskills").</summary>
    public D2TxtRow? StatByName(string name) =>
        name is null ? null
        : _data.StatsByName.TryGetValue(name, out var row) ? row : null;

    /// <summary>
    /// Stat row by its row index in ItemStatCost.txt. This is Styx's `stat.id`,
    /// also referred to as the "stat id" / "internal id" in D2 modding docs.
    /// </summary>
    public D2TxtRow? StatById(int statId) =>
        statId >= 0 && statId < _data.ItemStatCost.Rows.Count
            ? _data.ItemStatCost.Rows[statId] : null;

    /// <summary>
    /// `descpriority` from ItemStatCost.txt — controls display order in D2's
    /// native tooltip (higher = earlier in the list). Returns 0 when the stat
    /// is unknown so callers can use it as a stable, low-priority sort key.
    /// </summary>
    public int DescPriority(string statName)
    {
        var row = StatByName(statName);
        return row?.Int("descpriority") ?? 0;
    }

    /// <summary>
    /// `descfunc` from ItemStatCost.txt — the formatter id (1..28). The renderer
    /// uses this to pick the right "+X to Y" / "X% of Y" template.
    /// </summary>
    public int DescFunc(string statName) => StatByName(statName)?.Int("descfunc") ?? 0;

    /// <summary>Skill display name (English) from Skills.txt by skill row id.</summary>
    public string? SkillNameById(int skillId)
    {
        if (!_data.SkillsById.TryGetValue(skillId, out var row)) return null;
        if (string.Equals(row.Raw("skill"), "DiabWall", StringComparison.OrdinalIgnoreCase)
            && string.Equals(row.Raw("skilldesc"), "firestorm", StringComparison.OrdinalIgnoreCase))
            return "Firestorm";

        // `skill` column is the internal English name (e.g. "Poison Nova"); that's the
        // value that survives into MuleLogger output too. `skilldesc` is a stringtbl key.
        return row.Raw("skill");
    }

    /// <summary>
    /// Owning class display name for a skill, or null if the skill is
    /// class-neutral (e.g. Attack, Throw, Unsummon — empty <c>charclass</c>
    /// column) or the skill id is unknown. Used by the renderer to add
    /// the "(Barbarian Only)" / "(Necromancer Only)" suffix on
    /// <c>item_singleskill</c> bonuses for native class skills.
    /// <c>item_nonclassskill</c> (oskills) callers must NOT use this —
    /// the whole point of an oskill is that anyone can use it.
    /// </summary>
    public string? SkillClassById(int skillId)
    {
        if (!_data.SkillsById.TryGetValue(skillId, out var row)) return null;
        var code = row.Raw("charclass");
        if (string.IsNullOrEmpty(code)) return null;
        return code.ToLowerInvariant() switch
        {
            "ama" => "Amazon",
            "sor" => "Sorceress",
            "nec" => "Necromancer",
            "pal" => "Paladin",
            "bar" => "Barbarian",
            "dru" => "Druid",
            "ass" => "Assassin",
            _ => null,
        };
    }
}
