# Canonical item pipeline

This note pins down the **item shape the front-end actually consumes** so any
new ingestion path (currently Styx, later D2S save files, etc.) can be measured
against it. The MuleLogger import path is the **golden reference** — it
already feeds items into the renderer in a shape that works, so the goal is to
make every other source converge toward that same shape.

```
   ┌─────────────────────────────┐         ┌─────────────────────────────┐
   │ MuleLogger (Kolbot, in-D2)  │         │ Styx (external SOCKS proxy) │
   │ uses unit.description from  │         │ packet-level item parser    │
   │ D2's native renderer        │         │ no D2 memory access         │
   └─────────────┬───────────────┘         └─────────────┬───────────────┘
                 │                                       │
       1) writes JSON line-files          2) POSTs JSON snapshot
       to mules/<realm>/<acct>/<char>     to /api/ingest/styx/snapshot
                 │                                       │
                 ▼                                       ▼
        ┌──────────────────┐                  ┌─────────────────────────┐
        │ JsonCatalogService│                  │ StyxIngestionService    │
        │ (sample-catalog/  │                  │ (current: hand-rolled   │
        │  catalog.json)    │                  │  shape; broken stats)   │
        └──────────┬────────┘                  └─────────────┬───────────┘
                   │                                         │
                   └─────────┬───────────────────────────────┘
                             ▼
                  ┌──────────────────────────┐
                  │ SqliteCompanionStore     │
                  │ stores ItemRecord rows   │
                  └──────────────┬───────────┘
                                 ▼
                 ┌──────────────────────────┐
                 │ /api/catalog              │
                 │ returns ItemRecord[]      │
                 └──────────────┬────────────┘
                                ▼
                 ┌──────────────────────────┐
                 │ wwwroot/js/app/*          │
                 │ d2SceneRenderer + tooltip │
                 └───────────────────────────┘
```

The DB column shape and `ItemRecord` C# DTO are intentionally aligned with the
JSON MuleLogger produces — that is the contract.

## The canonical fields

`Models/Catalog/ItemRecord.cs` is the single shape the renderer reads from
`/api/catalog`. Every field below has a renderer or DB consumer; nothing here
is decorative.

| Field          | Type        | Where it's used                                                                                  |
| -------------- | ----------- | ------------------------------------------------------------------------------------------------ |
| `gid`          | string      | `itemKey(item)` identity in the DOM (stable across re-renders, used for hover binding)           |
| `classid`      | int         | `itemKey(item)`; also helpful for grouping                                                       |
| `title`        | string      | Search index (sidebar), list view, debug                                                         |
| `description`  | string      | Bitmap tooltip body — must contain `\xffc<N>` color codes (see *tooltip format* below)           |
| `image`        | string      | Asset key. Looked up as `/assets/gfx/<image>/<itemColor>.png`, fallback `/assets/items/<image>.png` |
| `itemColor`    | int         | Color variant index, `-1` = no tint (then renderer uses the flat sprite)                          |
| `sockets`      | string[]    | One entry per socket; the value is the **filler sprite key** (e.g. `r19` for Ral rune) or `"gemsocket"` for an empty socket |
| `storage`      | string      | Lowercase storage bucket: `equipped` / `inventory` / `stash` / `cube` / `mercenary` / `other`     |
| `location`     | int         | Numeric storage code — kept for parity with MuleLogger but the renderer routes off `storage`     |
| `x`, `y`       | int         | Coordinates **in the grid for inventory/stash/cube**, OR the **body slot id** for equipped/merc |
| `gridWidth`, `gridHeight` | int | Grid footprint (1×1, 2×2, …). Used by `fitsGrid()` and `scenePosition()`                         |
| `width`, `height` | int      | Pixel size used inside equipped/cube slot layout (`renderSceneItem` calc)                        |
| `ethereal`     | bool        | Adds the `.ethereal` CSS class for the green tint                                                 |
| `account`, `character`, `header`, `realm`, `mode`, `hardcore`, `expansion`, `ladder` | various | Metadata; flows into list cards and the sidebar grouping                                          |
| `sourceFile`   | string      | Identity (with gid) for `itemKey`; also drives "where did this come from" UI                      |

### Storage / body-slot encoding

The renderer's `scenePosition()` in `wwwroot/js/app/d2SceneRenderer.js`
dispatches based on `storage`:

- `inventory` → `x`/`y` are grid coords in a 10×4 grid (renders into a backdrop region)
- `stash` → grid coords in the stash region
- `cube` → grid coords in a 3×4 grid (only shown in the cube popover)
- `equipped` → `x` is a **body slot id** (1=helm, 2=amulet, 3=torso, 4=weapon1, 5=weapon2,
  6=ring1, 7=ring2, 8=belt, 9=boots, 10=gloves, 11=weapon-swap-1, 12=weapon-swap-2). `y` is ignored.
- `mercenary` → `x` is the merc body slot id (1=helm, 3=torso, 4=weapon, 5=shield)

The body-slot ids match D2's `BodyLocs.txt`. **The Styx adapter MUST translate
its equipment / mercenary items to the same `x` ids** — currently it stores
`bodylocation` in `x` for equipped items, which is correct only when Styx's
`bodylocation` equals D2's `BodyLocs.txt` row id (it does).

### Asset lookup

`itemAssets.js#itemImage` builds:

```
/assets/gfx/<image>/<itemColor>.png       (when itemColor >= 0)
/assets/items/<image>.png                  (fallback)
/assets/items/box.png                      (last-resort placeholder)
```

So `image` is the **D2 inventory sprite key**, NOT the item code. For most
items the D2 sprite key is the `normcode` field from `Weapons.txt`/`Armor.txt`
(shared across normal/exceptional/elite tiers). For items with a unique sprite
(Annihilus, Hellfire Torch, Stone of Jordan unique sprites etc.) the key is the
`invfile` field from `UniqueItems.txt`/`SetItems.txt` if present.

MuleLogger writes the value D2 actually used for that item (read via d2bs).
The Styx adapter has to **resolve it from the TXT tables** since Styx only sees
the raw item code.

### Tooltip format

`description` is plain text with embedded D2 colour codes. The renderer
(`wwwroot/js/app/tooltip.js#parseDescription`) accepts both:

- Literal `\xffc<digit>` (6 chars: backslash + `xffc` + digit) — what MuleLogger writes
- Unicode `ÿc<digit>` (the actual 0xFF byte) — what D2 stores internally

Color codes (digits): `0` white · `1` red · `2` set-green · `3` magic-blue ·
`4` unique-gold · `5` grey · `7` ocher · `8` craft-orange · `9` rare-yellow.

Lines are joined with `\n`. The first line is the item's display name (with
`(ilvl)` in parens when MuleLogger is configured to include it), the second
line is the base item type, then defense/damage/durability/requirements, then
the stat list, then the ethereal/socketed line.

The trailing `$<hash>:<classid>:<location>:<x>:<y>[:eth]` marker is **stripped
by the renderer** before parsing; both pipelines may keep it for round-trip
parity, but it does not contribute to the tooltip.

## Where the MuleLogger path gets each field

`MuleLogger.js#logItem` writes:

```js
return {
    itemColor:   unit.getColor(),                  // 0–20 / -1 (no tint)
    image:       Item.getItemCode(unit),           // resolved sprite key (handles set overrides)
    title:       unit.itemType + "_" + unit.fname.split("\n").reverse().join(" "),
    description: Item.getItemDesc(unit, logIlvl) + "$<hash>:<classid>:<location>:<x>:<y>[:eth]",
    sockets:     Item.getItemSockets(unit),        // ["r19", "gemsocket", ...]
};
```

The character file is then a stream of these objects, one per line, with the
character header (`<account> / <character>`), the storage label appended to
`title` (` (stash)`, ` (cube)`, ` (merc)`, ...), and the realm/mode in the
filename extension (`<char>.sen.txt` → softcore expansion non-ladder).

Crucially, MuleLogger does **zero stat formatting** itself: `unit.description`
is already the fully-rendered tooltip string that D2 produces internally, with
correct order, correct strings, correct stat aggregation. d2bs reads it out of
D2's memory. We don't have that luxury from Styx — D2 isn't running in our
process.

## What Styx provides vs needs

Today `Extensions/Styx/Models/StyxItemSnapshot.cs` carries:

- `gid`, `classId`, `code`, `itemColor`, `title`, `description`, `storage`,
  `location`, `x`, `y`, `gridWidth`, `gridHeight`, `ethereal`, `sockets[]`

…but it relies on the Node sidecar (`styx/bin/plugins/CompanionBridge.js`) to
**already resolve everything** before posting. That's the bug — the sidecar
doesn't have the TXT tables loaded in a comprehensive way, doesn't know
unique-invfile overrides, doesn't render full D2 tooltips. So most stat lines
end up as raw `<statName>: <value>` and many sprites fall back to the
`box.png` placeholder.

What Styx **does** have at the packet level (and what the new C# adapter
should leverage instead of trusting the sidecar):

- `classid` → row index into the merged BaseItem table (Weapons + Armor + Misc)
- `code` → 3-char item code (e.g. `7m7` = Ogre Maul)
- `quality` → 1=lowq, 2=normal, 3=hi-quality, 4=magic, 5=set, 6=rare, 7=unique, 8=crafted
- `uniqueid` / `setid` / `runewordid` → row index into UniqueItems/SetItems/Runes
- `prefix`, `suffix`, `magicPrefixes[]`, `magicSuffixes[]` → row indices into MagicPrefix/MagicSuffix/RarePrefix/RareSuffix
- `flags.{Identified, Ethereal, Runeword, Personalized, …}`
- `sockets` (count), `fillers[]` (`<classid>:<gfx>` pairs)
- `ilvl`
- `location`, `bodylocation`, `x`, `y`, `page` (stash page, multi-stash)
- `stats[]` — raw stat objects: `{id, val, ...}` plus typed subclasses
  (`SkillBonusStat`, `ChargedSkillStat`, `ClassSkillsBonusStat`,
  `MinMaxStat`, ...). After `getDerivedStats()` they're also flattened into
  `dstats` with computed-helper keys (`def`, `min1hdam`, `max1hdam`,
  `min2hdam`, `max2hdam`, `dura`, `maxdura`, `strreq`, `dexreq`, `lvlreq`,
  `maxquant`).

The adapter's job is to take this raw bag, look everything up in the 1.13c
TXT tables, and emit an `ItemRecord` (or a richer canonical model we then
project down to `ItemRecord`) that's indistinguishable from one MuleLogger
wrote.

## Field-by-field gap list (Styx → MuleLogger)

| Canonical field         | MuleLogger source                          | Styx today                                          | Gap to close                                                                          |
| ----------------------- | ------------------------------------------ | --------------------------------------------------- | ------------------------------------------------------------------------------------- |
| `image`                 | `Item.getItemCode(unit)` (D2-resolved)     | Sidecar emits `baseItem.normcode` or raw code       | C# adapter resolves: Unique.invfile → SetItem.invfile → base.normcode → base.code     |
| `itemColor`             | `unit.getColor()` (D2-resolved)            | Raw `item.color` (often 21 = "no tint" sentinel)   | Translate 21 → -1 so the renderer uses the flat sprite                                |
| `title`                 | `unit.fname` (D2-resolved name w/ affixes) | Sidecar concatenates affix rows from tables         | C# adapter does the same lookup but uses the **1.13c** tables and full affix grammar  |
| `description`           | `unit.description` (D2's bitmap tooltip)   | Sidecar generates a partial / raw-name dump         | C# `D2TooltipRenderer` builds the full D2-style tooltip from resolved stats           |
| `gridWidth`/`gridHeight`| Read from `unit.itemsize` (D2-resolved)    | Sidecar reads `baseItem.invwidth/invheight`         | C# adapter uses the 1.13c BaseItem row; OK in principle                               |
| `width`/`height`        | `unit.itemsize` (pixel)                    | Sidecar computes `gridWidth*28`                     | Same calc in C#                                                                       |
| `sockets[]`             | `Item.getItemSockets(unit)`                | Sidecar maps `fillers[]` to sprite keys             | C# adapter resolves via `Gems.txt` / `Runes.txt`                                      |
| `storage` / `location`  | D2 native body-loc id                      | Same                                                | OK; just verify mapping is exact for merc items                                       |
| `x` / `y` for equipped  | Body slot id in `x`                        | Currently `bodylocation` in `x`                     | OK as long as Styx's `bodylocation` is BodyLocs.txt id (it is)                        |
| `ethereal`              | `unit.getFlag(Ethereal)`                   | `item.flags.Ethereal`                               | OK                                                                                    |
| Quality                 | Implicit in `description` color and `unit.quality` | `item.quality` int                          | Adapter should set the correct title colour code based on quality                     |
| Unique / Set / Runeword | Encoded by D2 into `description`           | `uniqueid` / `setid` / `runewordid`                 | Adapter renders the unique/set/runeword name as the first line                        |
| Identified flag         | If false, tooltip says "Unidentified"      | `item.flags.Identified`                             | Adapter must hide affix data when unidentified, just like D2                          |

## What must be stored vs what can be computed at read time

**Stored in SQLite (canonical):**

- All `ItemRecord` fields above (so the catalog endpoint is one query, no
  per-item resolution at request time).
- The structured tooltip lines (new column, see below) so they survive a
  TXT-table upgrade without re-ingesting items.
- A raw Styx debug blob (JSON) when the ingestion source is Styx, for the
  debug-comparison endpoint.

**Computed at ingest time (Styx adapter):**

- `image`, `title`, `description`, `itemColor` (translate 21 → -1), `sockets`
  filler resolution.
- Structured stat lines (priority + grouped sub-stats).

**Computed at read time:**

- Account/character grouping for the catalog endpoint (already done in
  `SqliteCompanionStore.GetCatalogAsync`).
- Tooltip RE-rendering when the user toggles "show ilvl" or similar UI prefs
  (the structured tooltip lines let us do this without re-running the
  resolver).

## Hard rules

1. The **MuleLogger import path must continue to work unchanged**. Sample
   files in `data/sample-catalog.json` and any imported `mules/<...>/<...>.txt`
   files must render identically before and after every change.
2. The renderer must keep consuming exactly **one** item shape (`ItemRecord`).
   Do not branch the renderer on "is this from Styx or MuleLogger?".
3. Raw Styx data must survive into the DB (or a sidecar JSON) so the debug
   endpoint can diff `mule` vs `styx` representations of the same logical item.
4. Unknown stats and missing string-table keys must appear in debug output,
   never silently disappear from tooltips.
5. Stat ordering follows `descpriority` from `ItemStatCost.txt` (with the
   known D2 grouping rules layered on top), so output is deterministic and
   matches D2's natural order.

## Next steps

This is the contract the rest of the work targets:

1. `Services/GameData/` loads the 1.13c TXT tables once and exposes lookups.
2. `Services/Items/Rendering/D2StatResolver` turns raw Styx stats into resolved stat
   objects (with `descpriority`, `descfunc`, …).
3. `Services/Items/Rendering/D2TooltipRenderer` produces a structured `ItemTooltip`
   from canonical item + resolved stats.
4. `Extensions/Styx/Adapters/StyxToCanonicalItemAdapter` is the only place
   that knows the Styx-specific quirks; everything downstream sees an
   `ItemRecord` that looks like one MuleLogger wrote.
5. A debug `/api/debug/items/{gid}` endpoint dumps raw + canonical +
   resolved + rendered for any item, and side-by-side compares with the
   MuleLogger equivalent when present.
