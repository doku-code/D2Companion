namespace D2CompanionMvc.Domain.Items;

/// <summary>
/// Buckets used to group <see cref="TooltipLine"/>s when rendering. The order
/// of values mirrors D2's natural tooltip layout, so callers can sort by
/// (section, priority) and get the right vertical order without further work.
/// </summary>
public enum TooltipSection
{
    /// <summary>Item display name (unique/set/rare/magic/runeword name + ilvl).</summary>
    Title = 0,
    /// <summary>Base item type name shown directly under the title.</summary>
    BaseName = 1,
    /// <summary>Runeword "spelled" line — `'EnigmaJahIthBer'` etc.</summary>
    RunewordSpell = 2,
    /// <summary>Charm-only "Keep in Inventory to Gain Bonus", directly below title/base identity.</summary>
    CharmInstruction = 3,
    /// <summary>"Throw Damage:", "One-Hand Damage:", "Two-Hand Damage:", "Defense:", "Quantity:".</summary>
    DamageOrDefense = 4,
    /// <summary>"Chance to Block: NN%".</summary>
    Block = 5,
    /// <summary>"Durability: X of Y".</summary>
    Durability = 6,
    /// <summary>"Required Dexterity / Strength / Level"; class restriction.</summary>
    Requirements = 7,
    /// <summary>"Staff Class - Fast Attack Speed" etc.</summary>
    WeaponSpeed = 8,
    /// <summary>The big stat block. Ordering within is by <see cref="TooltipLine.Priority"/>.</summary>
    Stats = 9,
    /// <summary>Set / runeword partial / full bonuses.</summary>
    SetBonuses = 10,
    /// <summary>"Ethereal (Cannot be Repaired), Socketed (N)" — always last.</summary>
    Flags = 11,
}
