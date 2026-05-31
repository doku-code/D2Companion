using System.Text.Json;
using D2CompanionMvc.Domain.Items;
using D2CompanionMvc.Services.GameData;
using D2CompanionMvc.Extensions.Styx.Adapters;
using D2CompanionMvc.Extensions.Styx.Models;
using D2CompanionMvc.Services.Items.Rendering;
using D2CompanionMvc.Services.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using D2CompanionMvc.Services.Mapping;

namespace D2CompanionMvc.Controllers;

/// <summary>
/// Diagnostic endpoints for tracing a single item through the full Styx pipeline.
///
/// These endpoints are available in all environments — the app only listens on
/// localhost so there is no external exposure risk.
///
/// Routes
/// ──────
///   GET /api/debug/items?gid=…
///       Returns the raw DB row (TooltipJson, RawSnapshotJson) plus base-item
///       metadata and the stored tooltip model.  Also splits results by origin
///       (styx / mulelogger) for side-by-side comparison.
///
///   GET /api/debug/items/trace?gid=…
///       Re-runs the full stat-resolution pipeline live against the stored
///       RawSnapshotJson and returns a per-stat trace:
///         • every StyxRawStat field from the wire payload
///         • the adapted RawItemStat (after AdaptStats conversion)
///         • the ItemStatCost.txt row metadata (descfunc / descstrpos / dgrp …)
///         • effective class/tab/skill ids after Param decoding
///         • the resolved tooltip line (or a failure reason if null)
///       Use this to pinpoint exactly where a stat is lost or misrendered.
/// </summary>
[ApiController]
[Route("api/debug/items")]
public sealed class DebugItemsController : ControllerBase
{
    private readonly SqliteCompanionStore _store;
    private readonly D2ItemLookupService _items;
    private readonly D2StatLookupService _statLookup;
    private readonly StyxToCanonicalItemAdapter _adapter;
    private readonly D2StatResolver _resolver;
    private readonly D2TooltipRenderer _renderer;

    public DebugItemsController(
        SqliteCompanionStore store,
        D2ItemLookupService items,
        D2StatLookupService statLookup,
        StyxToCanonicalItemAdapter adapter,
        D2StatResolver resolver,
        D2TooltipRenderer renderer)
    {
        _store = store;
        _items = items;
        _statLookup = statLookup;
        _adapter = adapter;
        _resolver = resolver;
        _renderer = renderer;
    }

    // ── GET /api/debug/items?gid=… ────────────────────────────────────────
    [HttpGet]
    public IActionResult Get(
        [FromQuery] string? gid,
        [FromQuery] string? character,
        [FromQuery] string? storage,
        [FromQuery] int? x,
        [FromQuery] int? y)
    {
        using var conn = new SqliteConnection($"Data Source={_store.ResolveDatabasePath()};Foreign Keys=true;");
        conn.Open();

        using var cmd = conn.CreateCommand();
        if (!string.IsNullOrWhiteSpace(gid))
        {
            cmd.CommandText = """
                SELECT a.Name AS AccountName, c.Name AS CharacterName, c.Realm,
                       i.Gid, i.ClassId, i.Title, i.Description, i.Image, i.ItemColor,
                       i.Storage, i.Location, i.X, i.Y,
                       i.PixelWidth, i.PixelHeight, i.GridWidth, i.GridHeight,
                       i.Ethereal, i.SourceFile,
                       i.TooltipJson, i.RawSnapshotJson
                FROM Items i
                JOIN Characters c ON c.Id = i.CharacterId
                JOIN Accounts a ON a.Id = c.AccountId
                WHERE i.Gid = $gid
                LIMIT 5;
                """;
            cmd.Parameters.AddWithValue("$gid", gid);
        }
        else if (!string.IsNullOrWhiteSpace(character) && !string.IsNullOrWhiteSpace(storage) && x.HasValue && y.HasValue)
        {
            cmd.CommandText = """
                SELECT a.Name AS AccountName, c.Name AS CharacterName, c.Realm,
                       i.Gid, i.ClassId, i.Title, i.Description, i.Image, i.ItemColor,
                       i.Storage, i.Location, i.X, i.Y,
                       i.PixelWidth, i.PixelHeight, i.GridWidth, i.GridHeight,
                       i.Ethereal, i.SourceFile,
                       i.TooltipJson, i.RawSnapshotJson
                FROM Items i
                JOIN Characters c ON c.Id = i.CharacterId
                JOIN Accounts a ON a.Id = c.AccountId
                WHERE c.Name = $character AND i.Storage = $storage AND i.X = $x AND i.Y = $y
                LIMIT 5;
                """;
            cmd.Parameters.AddWithValue("$character", character);
            cmd.Parameters.AddWithValue("$storage", storage);
            cmd.Parameters.AddWithValue("$x", x.Value);
            cmd.Parameters.AddWithValue("$y", y.Value);
        }
        else
        {
            return BadRequest(new { error = "Provide either ?gid=… or ?character=…&storage=…&x=…&y=…" });
        }

        var matches = new List<object>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var source  = r.GetString(r.GetOrdinal("SourceFile"));
            var origin  = source.StartsWith("styx:", StringComparison.OrdinalIgnoreCase) ? "styx" : "mulelogger";

            ItemTooltip? tooltip = null;
            var tooltipJson = ReadString(r, "TooltipJson");
            if (!string.IsNullOrEmpty(tooltipJson))
                try { tooltip = JsonSerializer.Deserialize<ItemTooltip>(tooltipJson); } catch { }

            var baseItem = _items.BaseItemByCode(r.IsDBNull(r.GetOrdinal("Image")) ? null! : r.GetString(r.GetOrdinal("Image")));

            matches.Add(new
            {
                origin,
                account    = r.GetString(r.GetOrdinal("AccountName")),
                character  = r.GetString(r.GetOrdinal("CharacterName")),
                realm      = ReadString(r, "Realm"),
                gid        = r.GetString(r.GetOrdinal("Gid")),
                classid    = r.GetInt32(r.GetOrdinal("ClassId")),
                title      = r.GetString(r.GetOrdinal("Title")),
                description = ReadString(r, "Description"),
                image      = r.GetString(r.GetOrdinal("Image")),
                itemColor  = r.GetInt32(r.GetOrdinal("ItemColor")),
                storage    = r.GetString(r.GetOrdinal("Storage")),
                location   = r.GetInt32(r.GetOrdinal("Location")),
                x          = r.GetInt32(r.GetOrdinal("X")),
                y          = r.GetInt32(r.GetOrdinal("Y")),
                grid       = new { w = r.GetInt32(r.GetOrdinal("GridWidth")),  h = r.GetInt32(r.GetOrdinal("GridHeight")) },
                pixel      = new { w = r.GetInt32(r.GetOrdinal("PixelWidth")), h = r.GetInt32(r.GetOrdinal("PixelHeight")) },
                ethereal   = r.GetInt32(r.GetOrdinal("Ethereal")) == 1,
                sourceFile = source,
                baseItem   = baseItem is null ? null : new
                {
                    code      = baseItem.Raw("code"),
                    name      = baseItem.Raw("name"),
                    normcode  = baseItem.Raw("normcode"),
                    invfile   = baseItem.Raw("invfile"),
                    invwidth  = baseItem.IntOrNull("invwidth"),
                    invheight = baseItem.IntOrNull("invheight"),
                    type      = baseItem.Raw("type"),
                },
                tooltipModel   = tooltip,
                tooltipJsonRaw = tooltipJson,
                rawSnapshotJson = ReadString(r, "RawSnapshotJson"),
            });
        }

        if (matches.Count == 0) return NotFound(new { error = "No matching item." });

        return Ok(new
        {
            matches,
            byOrigin = new
            {
                mulelogger = matches.Where(m => Equals(m.GetType().GetProperty("origin")!.GetValue(m), "mulelogger")),
                styx       = matches.Where(m => Equals(m.GetType().GetProperty("origin")!.GetValue(m), "styx")),
            },
        });
    }

    // ── GET /api/debug/items/trace?gid=… ─────────────────────────────────
    /// <summary>
    /// Re-runs the stat-resolution pipeline live against the stored
    /// RawSnapshotJson and emits a per-stat trace so it is immediately visible
    /// where each stat comes from, what TXT metadata drives it, whether the
    /// Param was decoded into classId/tabId/skillId, and what line was rendered
    /// (or why it wasn't).
    /// </summary>
    [HttpGet("trace")]
    public IActionResult GetTrace([FromQuery] string gid)
    {
        if (string.IsNullOrWhiteSpace(gid))
            return BadRequest(new { error = "?gid=… is required" });

        // 1. Fetch stored row.
        using var conn = new SqliteConnection($"Data Source={_store.ResolveDatabasePath()};Foreign Keys=true;");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT a.Name AS AccountName, c.Name AS CharacterName,
                   i.Gid, i.Title, i.SourceFile, i.RawSnapshotJson, i.TooltipJson
            FROM Items i
            JOIN Characters c ON c.Id = i.CharacterId
            JOIN Accounts a ON a.Id = c.AccountId
            WHERE i.Gid = $gid
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$gid", gid);

        using var r = cmd.ExecuteReader();
        if (!r.Read()) return NotFound(new { error = $"No item with gid '{gid}'." });

        var account    = r.GetString(r.GetOrdinal("AccountName"));
        var character  = r.GetString(r.GetOrdinal("CharacterName"));
        var title      = r.GetString(r.GetOrdinal("Title"));
        var sourceFile = r.GetString(r.GetOrdinal("SourceFile"));
        var rawJson    = ReadString(r, "RawSnapshotJson");
        var tooltipJson = ReadString(r, "TooltipJson");

        // 2. Parse the stored snapshot.
        StyxItemSnapshot? snap = null;
        string? parseError = null;
        if (!string.IsNullOrEmpty(rawJson))
        {
            try
            {
                snap = JsonSerializer.Deserialize<StyxItemSnapshot>(rawJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                parseError = ex.Message;
            }
        }

        if (snap is null)
        {
            return Ok(new
            {
                account, character, title, sourceFile,
                error = parseError ?? "RawSnapshotJson is null — item was ingested before the raw-snapshot feature was added.",
                rawJson,
            });
        }

        // 3. Re-adapt through the canonical adapter to get RawItemStats.
        // Compute the identity resolution trace BEFORE adapting so we can show
        // the raw row lookups and whether they were accepted or rejected.
        var identityBase = _items.BaseItemByCode(snap.Code) ?? _items.BaseItemByClassId(snap.ClassId);
        var rawUniqueRow  = snap.UniqueId   is int uid && uid >= 0 ? _items.UniqueById(uid)   : null;
        var rawSetRow     = snap.SetId      is int sid && sid >= 0 ? _items.SetItemById(sid)  : null;
        var rawRwRow      = snap.RunewordId is int rid && rid >= 0 ? _items.RuneWordById(rid) : null;
        var rawMagicPrefixRow = snap.MagicPrefixId is int mp && mp >= 0 ? _items.MagicPrefixById(mp) : null;
        var rawMagicSuffixRow = snap.MagicSuffixId is int ms && ms >= 0 ? _items.MagicSuffixById(ms) : null;

        bool uniqueCodeOk = rawUniqueRow is not null &&
            _items.BaseCodesAreCompatible(rawUniqueRow.Raw("code"), snap.Code);
        bool setCodeOk    = rawSetRow    is not null &&
            _items.BaseCodesAreCompatible(rawSetRow.Raw("item"),   snap.Code);

        var identityResolution = new
        {
            // ── Raw ids from Styx wire payload ───────────────────────────────
            rawQuality     = snap.Quality,
            rawItemColor   = snap.ItemColor,
            rawUniqueId    = snap.UniqueId,
            rawSetId       = snap.SetId,
            rawRunewordId  = snap.RunewordId,
            rawMagicPrefix = snap.MagicPrefixId,
            rawMagicSuffix = snap.MagicSuffixId,
            rawMagicPrefixes = snap.MagicPrefixes,
            rawMagicSuffixes = snap.MagicSuffixes,
            rawRarePrefix  = snap.RarePrefixId,
            rawRareSuffix  = snap.RareSuffixId,
            rawGfx         = snap.Gfx,
            code           = snap.Code,
            classId        = snap.ClassId,
            // ── Base item from TXT ──────────────────────────────────────────
            baseItemCode   = identityBase?.Raw("code"),
            baseItemName   = identityBase?.Raw("name"),
            currentUpgradeFamilyCodes = _items.UpgradeFamilyCodes(snap.Code),
            finalTitle = title,
            finalTitleSource = snap.Identified == false ? "unidentified-base"
                : snap.IsRuneword == true && rawRwRow is not null ? "runeword"
                : snap.Quality == 7 && uniqueCodeOk ? "unique"
                : snap.Quality == 7 && snap.UniqueId is not null ? "unique-fallback"
                : snap.Quality == 5 && setCodeOk ? "set"
                : snap.Quality == 5 && snap.SetId is not null ? "set-fallback"
                : snap.Quality is 6 or 8 ? "rare-crafted-affixes"
                : snap.Quality == 4 ? "magic-affixes"
                : "base",
            // ── Unique row lookup ────────────────────────────────────────────
            unique = rawUniqueRow is null ? null : new
            {
                rowIndex    = snap.UniqueId,
                rowCode     = rawUniqueRow.Raw("code"),
                rowName     = rawUniqueRow.Raw("index"),
                rowUpgradeFamilyCodes = _items.UpgradeFamilyCodes(rawUniqueRow.Raw("code")),
                codeMatches = uniqueCodeOk,
                accepted    = uniqueCodeOk,
                rejectedReason = uniqueCodeOk ? null
                    : $"row code '{rawUniqueRow.Raw("code")}' is not compatible with item code '{snap.Code}'",
            },
            noUniqueRow = rawUniqueRow is null && snap.UniqueId is not null
                ? $"UniqueId {snap.UniqueId} out of range in UniqueItems.txt ({snap.UniqueId} >= row count)"
                : (string?)null,
            // ── Set row lookup ───────────────────────────────────────────────
            set = rawSetRow is null ? null : new
            {
                rowIndex    = snap.SetId,
                rowCode     = rawSetRow.Raw("item"),
                rowName     = rawSetRow.Raw("index"),
                rowUpgradeFamilyCodes = _items.UpgradeFamilyCodes(rawSetRow.Raw("item")),
                codeMatches = setCodeOk,
                accepted    = setCodeOk,
                rejectedReason = setCodeOk ? null
                    : $"row item '{rawSetRow.Raw("item")}' is not compatible with item code '{snap.Code}'",
            },
            noSetRow = rawSetRow is null && snap.SetId is not null
                ? $"SetId {snap.SetId} out of range in SetItems.txt"
                : (string?)null,
            // ── Runeword row lookup ──────────────────────────────────────────
            runeword = rawRwRow is null ? null : new
            {
                rowIndex = snap.RunewordId,
                rowName  = rawRwRow.Raw("Rune Name") ?? rawRwRow.Raw("Name"),
            },
            magicPrefix = rawMagicPrefixRow is null ? null : new
            {
                rowIndex = rawMagicPrefixRow.RowIndex,
                rowName = rawMagicPrefixRow.Raw("Name"),
                itemType = rawMagicPrefixRow.Raw("itype1"),
                mod1Code = rawMagicPrefixRow.Raw("mod1code"),
                mod1Param = rawMagicPrefixRow.Raw("mod1param"),
                mod1Min = rawMagicPrefixRow.Raw("mod1min"),
                mod1Max = rawMagicPrefixRow.Raw("mod1max"),
            },
            magicSuffix = rawMagicSuffixRow is null ? null : new
            {
                rowIndex = rawMagicSuffixRow.RowIndex,
                rowName = rawMagicSuffixRow.Raw("Name"),
                itemType = rawMagicSuffixRow.Raw("itype1"),
                mod1Code = rawMagicSuffixRow.Raw("mod1code"),
                mod1Param = rawMagicSuffixRow.Raw("mod1param"),
                mod1Min = rawMagicSuffixRow.Raw("mod1min"),
                mod1Max = rawMagicSuffixRow.Raw("mod1max"),
            },
            rarePrefix = snap.RarePrefixId is int rp && rp >= 0 ? new
            {
                rawId = snap.RarePrefixId,
                rowName = _items.RarePrefixById(rp)?.Raw("name"),
                displayName = StyxToCanonicalItemAdapter.RareAffixDisplayName(_items.RarePrefixById(rp)),
            } : null,
            rareSuffix = snap.RareSuffixId is int rs && rs >= 0 ? new
            {
                rawId = snap.RareSuffixId,
                rowName = _items.RareSuffixById(rs)?.Raw("name"),
                displayName = StyxToCanonicalItemAdapter.RareAffixDisplayName(_items.RareSuffixById(rs)),
            } : null,
        };

        var canonical = _adapter.Adapt(snap, sourceFile ?? "debug:re-adapt");
        var imageResolution = _items.ResolveInventorySprite(
            identityBase,
            canonical.Quality,
            snap.UniqueId ?? -1,
            snap.SetId ?? -1,
            snap.Code,
            snap.Gfx);

        // 4. Build the per-stat trace.
        var statTrace = new List<object>();
        for (var statIndex = 0; statIndex < canonical.RawStats.Count; statIndex++)
        {
            var raw = canonical.RawStats[statIndex];
            var wire = statIndex < snap.RawStats.Count ? snap.RawStats[statIndex] : null;
            var txtRow  = _statLookup.StatById(raw.StatId);
            var statName = txtRow?.Raw("Stat");
            var descFunc = txtRow?.Int("descfunc") ?? 0;
            var descPrio = txtRow?.Int("descpriority") ?? 0;
            var descStrPos = txtRow?.Raw("descstrpos");
            var descStrNeg = txtRow?.Raw("descstrneg");
            var descStr2   = txtRow?.Raw("descstr2");
            var dgrp       = txtRow?.Int("dgrp") ?? 0;
            var dgrpFunc   = txtRow?.Int("dgrpfunc") ?? 0;
            var dgrpVal    = txtRow?.Int("dgrpval") ?? 0;

            // Replicate the param-decode logic from D2StatResolver so we can
            // show WHICH effective ids were used.
            int? effClass = raw.ClassId;
            int? effTab   = raw.TabId;
            int? effSkill = raw.SkillId;
            bool decodedFromParam = false;
            if (raw.Param is int p)
            {
                switch (statName)
                {
                    case "item_addskill_tab":
                        if (effClass is null) { effClass = p / 3; decodedFromParam = true; }
                        if (effTab   is null) { effTab   = p % 3; decodedFromParam = true; }
                        break;
                    case "item_addclassskills":
                        if (effClass is null) { effClass = p; decodedFromParam = true; }
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
                        if (effSkill is null) { effSkill = p; decodedFromParam = true; }
                        break;
                }
            }

            var resolved = _resolver.Resolve(raw);
            var renderedText = resolved?.Rendered?.Text;
            var renderedContainsPlaceholder = renderedText?.Contains("%s", StringComparison.Ordinal) == true;

            string? failureReason = null;
            if (resolved is null)
                failureReason = $"stat id {raw.StatId} is out of range in ItemStatCost.txt";
            else if (renderedContainsPlaceholder)
                failureReason = $"rendered line still contains %s (stat={statName}, param={raw.Param})";
            else if (resolved.Hidden)
                failureReason = $"hidden: {resolved.HiddenReason}";
            else if (resolved.Rendered is null)
            {
                // Classify why
                if (descStrPos is null && descStrNeg is null)
                    failureReason = "no descstrpos/descstrneg in TXT row";
                else if (resolved.ClassName is null && statName is "item_addclassskills")
                    failureReason = $"className null (effClass={effClass}, param={raw.Param})";
                else if ((resolved.ClassName is null || resolved.SkillTabName is null) && statName is "item_addskill_tab")
                    failureReason = $"className or tabName null (effClass={effClass}, effTab={effTab}, param={raw.Param})";
                else if (resolved.SkillName is null && statName is "item_singleskill" or "item_nonclassskill" or "item_aura" or "item_charged_skill")
                    failureReason = $"skillName null (effSkill={effSkill}, param={raw.Param})";
                else
                    failureReason = $"descstrpos '{descStrPos}' not in string table";
            }

            statTrace.Add(new
            {
                // ── Wire-level raw stat ──────────────────────────────────
                statIndex,
                nodeAndOutgoing = wire is null ? null : new
                {
                    type = wire.Type,
                    constructorName = wire.ConstructorName,
                    nodeKeys = wire.NodeKeys,
                    nodeRaw = wire.NodeRaw,
                    outgoingRaw = wire.OutgoingRaw,
                },
                rawSnapshotStat = wire is null ? null : new
                {
                    id = wire.Id,
                    value = wire.Value,
                    param = wire.Param,
                    min = wire.Min,
                    max = wire.Max,
                    frames = wire.Frames,
                    skill = wire.Skill,
                    skillId = wire.SkillId,
                    skillLevel = wire.SkillLevel,
                    level = wire.Level,
                    chance = wire.Chance,
                    charges = wire.Charges,
                    maxCharges = wire.MaxCharges,
                    classId = wire.ClassId,
                    tabId = wire.TabId,
                    monsterId = wire.MonsterId,
                    element = wire.Element,
                    type = wire.Type,
                    constructorName = wire.ConstructorName,
                },
                wireRaw = new
                {
                    statId    = raw.StatId,
                    value     = raw.Value,
                    rawValue  = raw.RawValue,
                    characterLevel = raw.CharacterLevel,
                    param     = raw.Param,
                    min       = raw.Min,
                    max       = raw.Max,
                    frames    = raw.Frames,
                    skillId   = raw.SkillId,
                    skillLevel = raw.SkillLevel,
                    chance    = raw.Chance,
                    charges   = raw.Charges,
                    maxCharges = raw.MaxCharges,
                    classId   = raw.ClassId,
                    tabId     = raw.TabId,
                    monsterId = raw.MonsterId,
                    source    = raw.Source,
                },
                statSource = "ParentRaw",

                // ── ItemStatCost.txt metadata ────────────────────────────
                txtRow = txtRow is null ? null : new
                {
                    statName,
                    descFunc,
                    descPriority = descPrio,
                    descStrPos,
                    descStrNeg,
                    descStr2,
                    dgrp,
                    dgrpFunc,
                    dgrpVal,
                },

                // ── Param-decode result ──────────────────────────────────
                paramDecode = new
                {
                    decodedFromParam,
                    rawParam = raw.Param,
                    effectiveClassId = effClass,
                    effectiveTabId   = effTab,
                    effectiveSkillId = effSkill,
                },

                // ── Resolved names ───────────────────────────────────────
                resolvedNames = resolved is null ? null : new
                {
                    resolved.StatName,
                    resolved.ClassName,
                    resolved.SkillTabName,
                    resolved.SkillName,
                    resolved.Hidden,
                    resolved.HiddenReason,
                    resolved.ComputedValue,
                    resolved.CharacterLevelFallbackUsed,
                },

                // ── Rendered line ────────────────────────────────────────
                rendered = resolved?.Rendered is null ? null : new
                {
                    text     = resolved.Rendered.Text,
                    containsPlaceholder = renderedContainsPlaceholder,
                    color    = resolved.Rendered.Color.ToString(),
                    section  = resolved.Rendered.Section.ToString(),
                    priority = resolved.Rendered.Priority,
                    statIds  = resolved.Rendered.StatIds,
                },

                // ── Failure diagnosis ────────────────────────────────────
                failureReason,
            });
        }

        // 5. Also parse the stored tooltip for comparison.
        ItemTooltip? storedTooltip = null;
        if (!string.IsNullOrEmpty(tooltipJson))
            try { storedTooltip = JsonSerializer.Deserialize<ItemTooltip>(tooltipJson); } catch { }

        return Ok(new
        {
            account,
            character,
            title    = canonical.Title,
            baseName = canonical.BaseName,
            code      = canonical.Code,
            quality   = canonical.Quality,
            uniqueId  = canonical.UniqueId,
            setId     = canonical.SetId,
            itemLevel = canonical.ItemLevel,
            image     = canonical.ImageKey,
            itemColor = canonical.ColorIndex,
            snapshotPhase = snap.SnapshotPhase,
            rawStatsCountFromBridge = snap.RawStatsCount,
            socketStatSnapshot = snap.SocketStatSnapshot,
            socketStatAdditions = snap.SocketStatAdditions,
            imageResolution = new
            {
                finalImage = imageResolution.ImageKey,
                candidates = imageResolution.Candidates.Select(c => new
                {
                    c.Source,
                    c.Key,
                    c.AssetExists,
                    c.Accepted,
                    c.Reason,
                }),
            },
            imageAsset = new
            {
                transformedPath = canonical.ColorIndex >= 0
                    ? $"/assets/gfx/{canonical.ImageKey}/{canonical.ColorIndex}.png"
                    : null,
                transformedExists = canonical.ColorIndex >= 0 &&
                    System.IO.File.Exists(Path.Combine("wwwroot", "assets", "gfx", canonical.ImageKey, $"{canonical.ColorIndex}.png")),
                fallbackPath = $"/assets/items/{canonical.ImageKey}.png",
                fallbackExists = System.IO.File.Exists(Path.Combine("wwwroot", "assets", "items", $"{canonical.ImageKey}.png")),
            },
            socketFillers = snap.SocketFillersRaw?.Select((filler, index) =>
            {
                int? parsedClassId = int.TryParse(filler?.ClassId, out var classId) ? classId : null;
                var baseRow = parsedClassId is int cid ? _items.BaseItemByClassId(cid) : null;
                return new
                {
                    index,
                    rawClassId = filler?.ClassId,
                    rawCode = filler?.Code,
                    rawGfx = filler?.Gfx,
                    resolvedImage = filler is null ? "gemsocket" : _items.ResolveSocketFillerSpriteKey(filler.ClassId, filler.Code, filler.Gfx),
                    resolvedBase = baseRow is null ? null : new
                    {
                        classId = parsedClassId,
                        code = baseRow.Raw("code"),
                        name = baseRow.Raw("name"),
                        type = baseRow.Raw("type"),
                    },
                    childStatsPresent = false,
                    childRawStats = Array.Empty<object>(),
                };
            }),
            socketStatHandling = new
            {
                currentCase = snap.SocketFillersRaw is { Count: > 0 }
                    ? "socketed item; see snapshotPhase/socketStatSnapshot/socketStatAdditions for bridge timing details"
                    : "No socket filler children in RawSnapshotJson",
                parentStatsAreEffective = snap.SocketFillersRaw is { Count: > 0 },
                separateSocketChildStatsPresent = false,
                mergedSocketChildStats = false,
                snapshotPhase = snap.SnapshotPhase,
                bridgeRawStatsCount = snap.RawStatsCount,
                bridgeSocketStatSnapshot = snap.SocketStatSnapshot,
                bridgeSocketStatAdditions = snap.SocketStatAdditions,
            },
            sourceFile,
            statCount = canonical.RawStats.Count,

            // ── Identity resolution trace ────────────────────────────────────
            identityResolution,

            // Per-stat trace
            statTrace,

            // Stored tooltip lines for comparison
            storedTooltipLines = storedTooltip?.Lines.Select(l => new
            {
                text     = l.Text,
                color    = l.Color.ToString(),
                section  = l.Section.ToString(),
                priority = l.Priority,
            }),

            // Summary of problems from the STORED tooltip (pre-rendered at ingest time).
            // Cross-reference against the statTrace array above to see each stat's
            // current live resolution result.
            storedProblems = new
            {
                missingStringKeys = storedTooltip?.MissingStringKeys ?? (IEnumerable<string>)[],
                unresolvedStatIds = storedTooltip?.UnresolvedStatIds ?? (IEnumerable<int>)[],
            },
        });
    }

    // ── POST /api/debug/recanonicalize ────────────────────────────────────
    /// <summary>
    /// Re-runs the full adapter+renderer pipeline on every Styx item that has
    /// a stored RawSnapshotJson, then updates Title / Description / TooltipJson /
    /// Image / ItemSockets in-place.
    ///
    /// Optional query params:
    ///   ?account=…   — limit to items belonging to a specific account
    ///   ?character=… — limit to items belonging to a specific character
    ///
    /// This is non-destructive for MuleLogger data: only rows where
    /// SourceFile LIKE 'styx:%' AND RawSnapshotJson IS NOT NULL are touched.
    /// </summary>
    [HttpPost("recanonicalize")]
    public async Task<IActionResult> Recanonicalize(
        [FromQuery] string? account   = null,
        [FromQuery] string? character = null,
        CancellationToken cancellationToken = default)
    {
        using var conn = new SqliteConnection($"Data Source={_store.ResolveDatabasePath()};Foreign Keys=true;");
        conn.Open();

        // ── 1. Collect candidate rows ────────────────────────────────────────
        var whereParts = new List<string>
        {
            "i.RawSnapshotJson IS NOT NULL",
            "i.SourceFile LIKE 'styx:%'",
        };
        var fetchCmd = conn.CreateCommand();
        if (!string.IsNullOrWhiteSpace(account))
        {
            whereParts.Add("a.Name = $account");
            fetchCmd.Parameters.AddWithValue("$account", account);
        }
        if (!string.IsNullOrWhiteSpace(character))
        {
            whereParts.Add("c.Name = $character");
            fetchCmd.Parameters.AddWithValue("$character", character);
        }
        fetchCmd.CommandText = $"""
            SELECT i.Id, i.Gid, i.SourceFile, i.RawSnapshotJson
            FROM Items i
            JOIN Characters c ON c.Id = i.CharacterId
            JOIN Accounts a   ON a.Id = c.AccountId
            WHERE {string.Join(" AND ", whereParts)};
            """;

        var rows = new List<(long Id, string Gid, string SourceFile, string RawJson)>();
        using (var r = fetchCmd.ExecuteReader())
        {
            while (r.Read())
                rows.Add((r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3)));
        }

        // ── 2. Re-canonicalize each item ─────────────────────────────────────
        var opts        = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        int updated     = 0;
        int errors      = 0;
        var errorList   = new List<object>();

        foreach (var (id, gid, sf, rawJson) in rows)
        {
            try
            {
                var snap = JsonSerializer.Deserialize<StyxItemSnapshot>(rawJson, opts);
                if (snap is null)
                {
                    errors++;
                    errorList.Add(new { gid, error = "Snapshot deserialization returned null." });
                    continue;
                }

                var canonical   = _adapter.Adapt(snap, sf);
                var tooltip     = _renderer.Render(canonical);
                var tooltipJson = JsonSerializer.Serialize(tooltip);
                var description = tooltip.ToDescriptionString();

                // Update the item row
                using (var upd = conn.CreateCommand())
                {
                    upd.CommandText = """
                        UPDATE Items
                        SET Title = $title, Description = $description,
                            TooltipJson = $tooltipJson, Image = $image
                        WHERE Id = $id;
                        """;
                    upd.Parameters.AddWithValue("$id",          id);
                    upd.Parameters.AddWithValue("$title",       canonical.Title);
                    upd.Parameters.AddWithValue("$description", (object?)description ?? DBNull.Value);
                    upd.Parameters.AddWithValue("$tooltipJson", tooltipJson);
                    upd.Parameters.AddWithValue("$image",       canonical.ImageKey);
                    upd.ExecuteNonQuery();
                }

                // Rebuild sockets
                using (var del = conn.CreateCommand())
                {
                    del.CommandText = "DELETE FROM ItemSockets WHERE ItemId = $id;";
                    del.Parameters.AddWithValue("$id", id);
                    del.ExecuteNonQuery();
                }
                for (var pos = 0; pos < canonical.SocketFillers.Count; pos++)
                {
                    using var ins = conn.CreateCommand();
                    ins.CommandText = "INSERT INTO ItemSockets (ItemId, Position, Image) VALUES ($id, $pos, $img);";
                    ins.Parameters.AddWithValue("$id",  id);
                    ins.Parameters.AddWithValue("$pos", pos);
                    ins.Parameters.AddWithValue("$img", string.IsNullOrEmpty(canonical.SocketFillers[pos])
                        ? "gemsocket" : canonical.SocketFillers[pos]);
                    ins.ExecuteNonQuery();
                }

                updated++;
            }
            catch (Exception ex)
            {
                errors++;
                errorList.Add(new { gid, error = ex.Message });
            }
        }

        await Task.CompletedTask; // keep the async signature for future async DB upgrades
        return Ok(new
        {
            processed = rows.Count,
            updated,
            errors,
            scope = new { account, character },
            errorList,
        });
    }

    private static string? ReadString(SqliteDataReader r, string column)
    {
        var i = r.GetOrdinal(column);
        return r.IsDBNull(i) ? null : r.GetString(i);
    }
}
