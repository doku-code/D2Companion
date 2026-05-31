namespace D2CompanionMvc.Services.Items.Rendering;

/// <summary>
/// In-memory lookup of the D2 string-table keys most frequently referenced by
/// ItemStatCost / SkillDesc / etc. The English strings are reverse-engineered
/// from MuleLogger output (see <c>tools/MuleCorpus</c>): the corpus was scanned
/// for 324k items, and the resulting tooltip lines pinned down the canonical
/// "+%d to Strength" / "Fire Resist +%d%%" style strings D2 uses.
///
/// This resolver intentionally covers only the keys we know we need today.
/// The interface returns null for unknown keys so the renderer can surface
/// "missing string-table entry: <c>ModStrXY</c>" in the debug output, which
/// is how new keys get added back here.
///
/// Replacing this with a real .tbl-backed resolver in the future is a drop-in
/// swap at the DI registration site — every consumer takes
/// <see cref="ID2StringResolver"/>, never this class directly.
/// </summary>
public sealed class FallbackD2StringResolver : ID2StringResolver
{
    private static readonly Dictionary<string, string> Strings = new(StringComparer.OrdinalIgnoreCase)
    {
        // Attributes (ModStr1a..1u)
        { "ModStr1a", "+%d to Strength" },
        { "ModStr1b", "+%d to Dexterity" },
        { "ModStr1c", "+%d to Vitality" },
        { "ModStr1d", "+%d to Energy" },
        { "ModStr1e", "+%d to Mana" },
        { "ModStr1f", "+%d to Maximum Damage" },
        { "ModStr1g", "+%d to Minimum Damage" },
        { "ModStr1h", "+%d to Attack Rating" },
        { "ModStr1i", "+%d Defense" },
        { "ModStr1j", "Fire Resist +%d%%" },
        { "ModStr1k", "Cold Resist +%d%%" },
        { "ModStr1l", "Lightning Resist +%d%%" },
        { "ModStr1m", "Magic Resist +%d%%" },
        { "ModStr1n", "Poison Resist +%d%%" },
        { "ModStr1o", "Adds %d-%d fire damage" },
        { "ModStr1p", "Adds %d-%d fire damage" },
        { "ModStr1q", "Adds %d-%d lightning damage" },
        { "ModStr1r", "Adds %d-%d lightning damage" },
        { "ModStr1s", "Adds %d-%d cold damage" },
        { "ModStr1t", "Adds %d-%d cold damage" },
        { "ModStr1u", "+%d to Life" },
        { "ModStr1v", "Attacker Takes Damage of %d" },
        { "ModStr1w", "+%d%% Extra Gold from Monsters" },
        { "ModStr1x", "+%d%% Better Chance of Getting Magic Items" },
        { "ModStr1y", "Knockback" },

        // Damage / defense percent
        { "Modstr2v", "+%d%% Enhanced Defense" },
        { "ModStr2j", "+%d%% Enhanced Damage" },
        { "ModStr2k", "+%d%% Enhanced Damage" }, // min variant; D2 collapses to the same line
        { "ModStr2g", "Increase Maximum Life %d%%" },
        { "ModStr2h", "Increase Maximum Mana %d%%" },
        { "ModStr2i", "Increase Maximum Durability %d%%" },
        { "ModStr2l", "Replenish Life +%d" },
        { "ModStr2t", "Magic Damage Reduced by %d" },
        { "ModStr2u", "Damage Reduced by %d" },
        { "ModStr2w", "Drain Life -%d" },
        { "ModStr2y", "%d%% Mana stolen per hit" },
        { "ModStr2z", "%d%% Life stolen per hit" },

        // Skill bonuses (ModStr3a..3k)
        { "ModStr3a", "+%d to %s Skill Levels" },           // class skills (param = class)
        { "ModStr3f", "+%d to Light Radius" },
        { "ModStr3g", "+%d%% Increased Chance of Blocking" },
        { "ModStr3j", "Attacker Takes Lightning Damage of %d" },
        { "ModStr3i", "+%d to Fire Skills" },               // also +Cold/Ltng depending on element
        { "ModStr3k", "+%d to All Skills" },
        { "ModStr3h", "Requirements %d%%" },
        { "ModStr3l", "Freezes target +%d" },               // item_freeze (Doom: "Freezes target +3")
        { "ModStr3u", "Hit Causes Monster to Flee %d%%" },  // item_howl (White wand)
        { "ModStr3m", "%d%% Chance of Open Wounds" },
        { "ModStr3r", "Poison Length Reduced by %d%%" },
        { "ModStr3v", "Heal Stamina Plus %d%%" },
        { "ModStr3w", "%d%% Damage Taken Goes to Mana" },
        { "ModStr3y", "Ignore Target's Defense" },

        // Speed / combat
        { "ModStr4a", "Prevent Monster Heal" },
        { "ModStr4b", "Half Freeze Duration" },
        { "ModStr4c", "+%d%% Bonus to Attack Rating" },
        { "ModStr4d", "%d to Maximum Damage" },
        { "ModStr4e", "+%d%% Damage to Demons" },
        { "ModStr4f", "+%d%% Damage to Undead" },
        { "ModStr4g", "Regenerate Mana %d%%" },
        { "ModStr4h", "Poison Damage: %d-%d" },
        { "ModStr4i", "Poison Damage: %d-%d" },
        { "ModStr4j", "+%d to Attack Rating against Demons" },
        { "ModStr4k", "+%d to Attack Rating against Undead" },
        { "ModStr4m", "+%d%% Faster Attack Speed" },
        { "ModStr4p", "+%d%% Faster Hit Recovery" },
        { "ModStr4s", "+%d%% Faster Run/Walk" },
        { "ModStr4v", "+%d%% Faster Cast Rate" },
        { "ModStr4y", "+%d%% Faster Block Rate" },

        // Misc combat / stamina (ModStr5*)
        { "ModStr5b", "%d%% Damage to Demons" },
        { "ModStr5c", "%d%% Chance of Crushing Blow" },
        { "ModStr5d", "+%d Maximum Stamina" },
        { "ModStr5e", "+%d Kick Damage" },
        { "ModStr5f", "+%d to Mana after each Kill" },
        // Percent absorb keeps the "Element Absorb +N%%" wording (unchanged;
        // golden uses "Cold Absorb 20%" but we preserve our current wording
        // here per the brief — flat absorb is the only one we're flipping).
        { "ModStr5g", "Fire Absorb +%d%%" },
        // Flat absorb uses the D2/MuleLogger "+N Element Absorb" wording
        // ("+5 Magic Absorb", "+20 Lightning Absorb"). Golden samples
        // confirm: "+3..+9 Magic Absorb", "+20 Lightning Absorb".
        { "ModStr5h", "+%d Fire Absorb" },
        { "ModStr5i", "Lightning Absorb +%d%%" },
        { "ModStr5j", "+%d Lightning Absorb" },
        { "ModStr5k", "Magic Absorb +%d%%" },
        { "ModStr5l", "+%d Magic Absorb" },
        { "ModStr5m", "Cold Absorb +%d%%" },
        { "ModStr5n", "+%d Cold Absorb" },
        { "ModStr5o", "%d%% Slows Target" },
        { "ModStr5q", "%d%% Deadly Strike" },
        { "ModStr5r", "Slows Target by %d%%" },
        { "ModStr5u", "+%d%% to Maximum Fire Resist" },
        { "ModStr5v", "+%d%% to Maximum Cold Resist" },
        { "ModStr5w", "+%d%% to Maximum Lightning Resist" },
        { "ModStr5x", "+%d%% to Maximum Magic Resist" },
        { "ModStr5y", "+%d%% to Maximum Poison Resist" },
        { "ModStr5z", "Cannot Be Frozen" },

        // Resist vs misc (ModStr6*)
        { "ModStr6a", "+%d Defense vs. Missile" },
        { "ModStr6b", "+%d Defense vs. Melee" },
        { "ModStr6c", "+%d Life after each Demon Kill" },
        { "ModStr6e", "%d%% Drain Stamina" },
        { "ModStr6g", "%d%% Piercing Attack" },
        { "ModStr6h", "%d%% Chance to Become Magic Arrow" },
        { "ModStr6i", "%d%% Chance to Become Explosive Arrow" },

        // Extended (Lord of Destruction)
        { "ModStre9o", "+%d Fire Absorb" },
        { "ModStre9p", "+%d Cold Absorb" },
        { "ModStre9q", "+%d Lightning Absorb" },
        { "ModStre9s", "Indestructible" },
        { "ModStre9t", "Repairs %d durability per second" },
        { "ModStre9v", "Replenishes quantity" },
        { "ModStre9i", "Increased Stack Size" },
        { "ModStre10d", "Level %d %s (%d/%d Charges)" },     // charged skill template

        // Skill tabs / class-restricted skill bonuses
        { "StrSklTabItem1", "+%d to %s Skills (%s Only)" },

        // Resist / damage modifiers
        { "ModitemAura",         "Level %d %s Aura When Equipped" },
        { "ModitemHPaK",         "+%d Life after each Kill" },
        { "Moditem2ExpG",        "%d%% Bonus to Experience" },
        { "ModitemAttratvsM",    "%d%% Bonus to Attack Rating against %s" },
        { "Moditemdamvsm",       "%d%% Damage to %s" },
        { "Moditemreanimas",     "%d%% Reanimate as: %s" },
        { "ModitemRedVendP",     "Reduces all Vendor Prices %d%%" },
        { "Moditemenrescoldsk",  "-%d%% to Enemy Cold Resistance" },
        { "Moditemenresfiresk",  "-%d%% to Enemy Fire Resistance" },
        { "Moditemenresltngsk",  "-%d%% to Enemy Lightning Resistance" },
        { "Moditemenrespoissk",  "-%d%% to Enemy Poison Resistance" },
        { "ModitemdamFiresk",    "+%d%% to Fire Skill Damage" },
        { "ModitemdamColdsk",    "+%d%% to Cold Skill Damage" },
        { "ModitemdamLtngsk",    "+%d%% to Lightning Skill Damage" },
        { "ModitemdamPoissk",    "+%d%% to Poison Skill Damage" },
        { "ModitemSMRIP",        "Slain Monsters Rest in Peace" },
        { "ModitemskonKill",     "%d%% Chance to cast level %d %s when you Kill an Enemy" },
        { "Moditemskondeath",    "%d%% Chance to cast level %d %s when you Die" },
        { "ModitemskonLevel",    "%d%% Chance to cast level %d %s when you Level-Up" },

        // Trigger-on-event skill cast variants (used by item_skillonhit / on_attack / on_get_hit)
        { "ItemExpansiveChancX", "%d%% Chance to cast level %d %s on attack" },
        { "ItemExpansiveChanc1", "%d%% Chance to cast level %d %s on striking" },
        { "ItemExpansiveChanc2", "%d%% Chance to cast level %d %s when struck" },

        // Standalone strings the tooltip renderer assembles directly
        { "strModMagicDamage",    "Magic Damage: %d-%d" },
        { "strRequiredLevel",     "Required Level: %d" },
        { "strRequiredStrength",  "Required Strength: %d" },
        { "strRequiredDexterity", "Required Dexterity: %d" },
        { "strDefenseFormat",     "Defense: %d" },
        { "strDamageFormat",      "Damage: %d to %d" },
        { "strDurabilityFormat",  "Durability: %d of %d" },
        { "strQuantityFormat",    "Quantity: %d" },
        { "strSocketed",          "Socketed (%d)" },
        { "strEthereal",          "Ethereal (Cannot be Repaired)" },
        { "strUnidentified",      "Unidentified" },
        { "strItemLevel",         "Item Level: %d" },
    };

    public string? Resolve(string key)
    {
        if (key is null) return null;
        return Strings.TryGetValue(key, out var v) ? v : null;
    }
}
