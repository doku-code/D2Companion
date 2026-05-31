namespace D2CompanionMvc.Domain.Items;

/// <summary>
/// The internal canonical shape an ingestion adapter (Styx, save-files, …)
/// produces and the rest of the pipeline consumes. It carries enough resolved
/// data to populate <see cref="Models.Catalog.ItemRecord"/> for storage and
/// the structured <see cref="ItemTooltip"/> for rendering — without the caller
/// caring whether the source was MuleLogger (which already provides
/// pre-resolved fields) or Styx (which needs full TXT lookup).
/// </summary>
public sealed class CanonicalD2Item
{
    // ── Identity ──────────────────────────────────────────────────────────
    /// <summary>Unique-per-character item id (Styx <c>uid</c>, save-file gid).</summary>
    public string Gid { get; init; } = string.Empty;

    /// <summary>Merged Weapons+Armor+Misc row index — the classic D2 classid.</summary>
    public int ClassId { get; init; }

    /// <summary>D2 item code (e.g. "7m7" = Ogre Maul).</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Quality (1 lowq..8 crafted) per <c>QualityItems.txt</c>.</summary>
    public int Quality { get; init; }

    public int ItemLevel { get; init; }
    public int? CharacterLevel { get; init; }

    // ── Display ───────────────────────────────────────────────────────────
    /// <summary>Inventory sprite key (Unique invfile / Set invfile / base normcode).</summary>
    public string ImageKey { get; init; } = "box";

    /// <summary>D2 color tint index (0..20). -1 means no tint (use flat sprite).</summary>
    public int ColorIndex { get; init; } = -1;

    /// <summary>Display title — the first line of the tooltip ("Stone of Jordan", "Bramble Tomb Skull Cap", …).</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Base item display name ("Ring", "Skull Cap", …).</summary>
    public string BaseName { get; init; } = string.Empty;

    // ── Grid / location ───────────────────────────────────────────────────
    public ItemStorageBucket Storage { get; init; }

    /// <summary>Numeric D2 location code (kept for parity with MuleLogger).</summary>
    public int Location { get; init; }

    /// <summary>X position OR body-slot id (for equipped/mercenary).</summary>
    public int X { get; init; }
    public int Y { get; init; }
    public int GridWidth { get; init; } = 1;
    public int GridHeight { get; init; } = 1;
    public int PixelWidth => GridWidth * 28;
    public int PixelHeight => GridHeight * 28;

    // ── Flags ─────────────────────────────────────────────────────────────
    public bool Identified { get; init; } = true;
    public bool Ethereal { get; init; }
    public bool Runeword { get; init; }
    public bool Personalized { get; init; }
    public string? PersonalizedName { get; init; }

    // ── Affixes / flavour rows ────────────────────────────────────────────
    public int? UniqueId { get; init; }
    public int? SetId { get; init; }
    public int? RunewordId { get; init; }
    public int? MagicPrefixId { get; init; }
    public int? MagicSuffixId { get; init; }
    public int? RarePrefixId { get; init; }
    public int? RareSuffixId { get; init; }

    // ── Combat values (D2 pre-rolls these; we just store the result) ──────
    public int? Defense { get; init; }
    public int? OneHandMin { get; init; }
    public int? OneHandMax { get; init; }
    public int? TwoHandMin { get; init; }
    public int? TwoHandMax { get; init; }
    public int? ThrowMin { get; init; }
    public int? ThrowMax { get; init; }
    public int? Quantity { get; init; }
    public int? Durability { get; init; }
    public int? MaxDurability { get; init; }
    public int? RequiredStrength { get; init; }
    public int? RequiredDexterity { get; init; }
    public int? RequiredLevel { get; init; }
    /// <summary>Class restriction display name ("Necromancer" for wands etc.), null when none.</summary>
    public string? ClassRestriction { get; init; }

    // ── Sockets ───────────────────────────────────────────────────────────
    public int SocketCount { get; init; }
    /// <summary>One entry per socket: sprite key for the filler, or "gemsocket" when empty.</summary>
    public IReadOnlyList<string> SocketFillers { get; init; } = Array.Empty<string>();

    // ── Stats ─────────────────────────────────────────────────────────────
    /// <summary>Raw stats as they arrived from the source — the resolver's input.</summary>
    public IReadOnlyList<RawItemStat> RawStats { get; init; } = Array.Empty<RawItemStat>();

    // ── Origin / debug ────────────────────────────────────────────────────
    /// <summary>"mulelogger:sample/<file>.txt" / "styx:<account>/<char>" — used by the UI's sourceFile column.</summary>
    public string SourceFile { get; init; } = string.Empty;

    /// <summary>Snapshot of the raw incoming payload (JSON blob) for the debug endpoint. Optional.</summary>
    public string? RawSnapshotJson { get; init; }
}

/// <summary>Storage buckets that mirror the front-end's <c>storage</c> field exactly.</summary>
public enum ItemStorageBucket
{
    Unknown,
    Equipped,
    Inventory,
    Stash,
    Cube,
    Mercenary,
    Other,
}
