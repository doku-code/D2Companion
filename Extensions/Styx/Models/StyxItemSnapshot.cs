namespace D2CompanionMvc.Extensions.Styx.Models;

/// <summary>
/// One item as Styx posts it. The original (minimal) fields are required so
/// legacy sidecar builds keep working; the new <c>Raw*</c> fields are
/// optional and let the C# canonicalization adapter do its work without
/// trusting the Node side to have rendered anything.
///
/// Compatibility:
/// - Old sidecar (only the original fields): the adapter falls back to
///   whatever the sidecar produced (best effort; stats stay as-is).
/// - New sidecar (full raw): the adapter ignores the pre-rendered fields and
///   builds the canonical item from scratch using the 1.13c TXT tables.
/// </summary>
public sealed class StyxItemSnapshot
{
    // ── Original fields (still required for backward-compat) ──────────────
    public string Gid { get; init; } = string.Empty;
    public int ClassId { get; init; }
    public string Code { get; init; } = string.Empty;
    public int ItemColor { get; init; } = -1;
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Storage { get; init; } = string.Empty;
    public int Location { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int GridWidth { get; init; } = 1;
    public int GridHeight { get; init; } = 1;
    public bool Ethereal { get; init; }
    public IReadOnlyList<string> Sockets { get; init; } = [];

    // ── New raw fields populated by an updated sidecar ────────────────────
    /// <summary>D2 quality: 1 lowq, 2 normal, 3 hi-quality, 4 magic, 5 set, 6 rare, 7 unique, 8 crafted.</summary>
    public int? Quality { get; init; }
    public int? UniqueId { get; init; }
    public int? SetId { get; init; }
    public int? RunewordId { get; init; }

    // Singular ids (magic items: 1 prefix + 1 suffix max; rare/crafted: name prefix+suffix).
    public int? MagicPrefixId { get; init; }
    public int? MagicSuffixId { get; init; }
    public int? RarePrefixId { get; init; }
    public int? RareSuffixId { get; init; }

    // Arrays (same indices as above but as lists for forward-compat with
    // multi-affix handling; currently single-element at most in vanilla D2).
    public IReadOnlyList<int> MagicPrefixes { get; init; } = [];
    public IReadOnlyList<int> MagicSuffixes { get; init; } = [];
    public IReadOnlyList<int> RarePrefixes  { get; init; } = [];
    public IReadOnlyList<int> RareSuffixes  { get; init; } = [];

    public int? ItemLevel { get; init; }
    public int? CharacterLevel { get; init; }
    public int? Gfx { get; init; }
    public bool? Identified { get; init; }
    public bool? IsRuneword { get; init; }
    public bool? IsPersonalized { get; init; }
    public bool? IsSocketed { get; init; }
    public bool? IsEquipped { get; init; }

    /// <summary>D2 body slot id for equipped/mercenary items (BodyLocs.txt row id).</summary>
    public int? BodyLocation { get; init; }

    /// <summary>Stash page index (0-based) for items in a multi-page shared stash.</summary>
    public int? Page { get; init; }

    /// <summary>Richer socket-filler objects (classid + gfx key). Null entries = empty sockets.</summary>
    public IReadOnlyList<StyxSocketFiller?>? SocketFillersRaw { get; init; }

    /// <summary>Pre-computed defense / damage / requirements coming straight from Styx's getDerivedStats().</summary>
    public int? Defense { get; init; }
    public int? OneHandMinDamage { get; init; }
    public int? OneHandMaxDamage { get; init; }
    public int? TwoHandMinDamage { get; init; }
    public int? TwoHandMaxDamage { get; init; }
    public int? ThrowMinDamage { get; init; }
    public int? ThrowMaxDamage { get; init; }
    public int? Durability { get; init; }
    public int? MaxDurability { get; init; }
    public int? RequiredStrength { get; init; }
    public int? RequiredDexterity { get; init; }
    public int? RequiredLevel { get; init; }
    public int? Quantity { get; init; }

    /// <summary>Raw stat array — the adapter passes these through the D2StatResolver.</summary>
    public IReadOnlyList<StyxRawStat> RawStats { get; init; } = Array.Empty<StyxRawStat>();

    /// <summary>Bridge snapshot phase: live, settled, or final.</summary>
    public string? SnapshotPhase { get; init; }

    /// <summary>Raw stat count before any bridge-side settled clone enrichment.</summary>
    public int? RawStatsCount { get; init; }

    /// <summary>How CompanionBridge selected the stat source for this item.</summary>
    public string? SocketStatSnapshot { get; init; }

    /// <summary>Number of missing socket stat signatures added by a settled clone snapshot.</summary>
    public int? SocketStatAdditions { get; init; }
}

/// <summary>
/// One socket filler as Styx encodes it: classid + gfx sprite key from the
/// item packet.  <c>Code</c> is the 3-char item code; Styx does not include it
/// in the filler payload so C# must resolve it from classid via BaseItem tables.
/// </summary>
public sealed class StyxSocketFiller
{
    /// <summary>
    /// First segment of Styx's "key:extra" filler string.
    /// In practice this is the sprite/code key (e.g. "r19" for Ral rune) — a string,
    /// NOT a numeric classid — so this field is typed as string? to avoid JSON
    /// deserialization failure when the value is non-numeric.
    /// </summary>
    public string? ClassId { get; init; }

    /// <summary>Second segment of the filler string if present (extra gfx/classid data).</summary>
    public string? Gfx { get; init; }

    /// <summary>3-char item code if available; currently null from Styx's filler format.</summary>
    public string? Code { get; init; }
}
