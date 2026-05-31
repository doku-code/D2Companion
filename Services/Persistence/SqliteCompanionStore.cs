using System.Text;
using D2CompanionMvc.Models.Catalog;
using D2CompanionMvc.Options;
using D2CompanionMvc.Extensions.Styx.Ingestion;
using D2CompanionMvc.Extensions.Styx.Models;
using D2CompanionMvc.Services.GameData;
using D2CompanionMvc.Services.Mapping;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace D2CompanionMvc.Services.Persistence;

public sealed class SqliteCompanionStore
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<SqliteCompanionStore> _logger;
    private readonly IOptionsMonitor<CompanionAppOptions> _options;
    private readonly D2ItemLookupService _itemLookup;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SqliteCompanionStore(
        IWebHostEnvironment environment,
        ILogger<SqliteCompanionStore> logger,
        IOptionsMonitor<CompanionAppOptions> options,
        D2ItemLookupService itemLookup)
    {
        _environment = environment;
        _logger = logger;
        _options = options;
        _itemLookup = itemLookup;
    }

    public string ResolveDatabasePath()
    {
        var catalogOptions = _options.CurrentValue.Catalog;
        var dataDirectory = Path.IsPathRooted(catalogOptions.DataDirectory)
            ? catalogOptions.DataDirectory
            : Path.Combine(_environment.ContentRootPath, catalogOptions.DataDirectory);
        return Path.GetFullPath(Path.Combine(dataDirectory, catalogOptions.DatabaseFileName));
    }

    public string ResolveDatabaseDisplayPath()
    {
        var path = ResolveDatabasePath();
        var root = Path.GetFullPath(_environment.ContentRootPath);
        var relative = Path.GetRelativePath(root, path);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? path
            : relative;
    }

    public async Task<bool> HasItemsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseAsync(cancellationToken);

        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM Items LIMIT 1);";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    public async Task SaveSnapshotAsync(StyxCharacterSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureDatabaseAsync(cancellationToken);

            await using var connection = OpenConnection();
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var accountId = await SqliteCharacterPersistence.UpsertAccountAsync(connection, snapshot, cancellationToken);
            var characterId = await SqliteCharacterPersistence.UpsertCharacterAsync(connection, accountId, snapshot, cancellationToken);
            await SqliteCharacterPersistence.DeleteLocalItemsAsync(connection, characterId, cancellationToken);
            await SqliteCharacterPersistence.InsertSessionAsync(connection, characterId, snapshot, cancellationToken);

            foreach (var item in snapshot.Items)
            {
                var itemId = await SqliteItemPersistence.InsertItemAsync(connection, characterId, snapshot, item, cancellationToken);
                await SqliteItemPersistence.InsertSocketsAsync(connection, itemId, item, cancellationToken);
            }

            foreach (var observed in snapshot.ObservedPlayers)
            {
                var observedId = await SqliteObservedPersistence.UpsertObservedPlayerAsync(connection, characterId, snapshot, observed, cancellationToken);
                await SqliteObservedPersistence.DeleteObservedPlayerItemsAsync(connection, observedId, cancellationToken);
                foreach (var item in SqliteObservedPersistence.SelectObservedDisplayItems(observed.Items))
                {
                    var itemId = await SqliteObservedPersistence.InsertObservedPlayerItemAsync(connection, observedId, item, cancellationToken);
                    await SqliteObservedPersistence.InsertObservedPlayerSocketsAsync(connection, itemId, item, cancellationToken);
                }
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Persist a fully-canonicalized character snapshot (Styx route after the
    /// adapter+renderer). Uses the canonical fields for sprite/title/description
    /// and stores the structured tooltip JSON + raw wire snapshot for the
    /// debug-comparison endpoint.
    ///
    /// Observed players still use the legacy <see cref="StyxObservedPlayerSnapshot"/>
    /// path; they'll get canonicalized in a follow-up pass since they're
    /// purely cosmetic ("you saw player X with items Y").
    /// </summary>
    public async Task SaveCanonicalSnapshotAsync(CanonicalCharacterPayload payload, CancellationToken cancellationToken = default)
    {
        await SaveCanonicalSnapshotWithChangeDetectionAsync(payload, cancellationToken);
    }

    public async Task<SnapshotSaveResult> SaveCanonicalSnapshotWithChangeDetectionAsync(CanonicalCharacterPayload payload, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureDatabaseAsync(cancellationToken);

            await using var connection = OpenConnection();
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var existingCharacterId = await FindCharacterIdAsync(connection, payload.Account, payload.Character, cancellationToken);
            var catalogChanged = existingCharacterId is null
                || !string.Equals(
                    await BuildExistingCanonicalSignatureAsync(connection, existingCharacterId.Value, payload, cancellationToken),
                    BuildIncomingCanonicalSignature(payload),
                    StringComparison.Ordinal);

            var accountId   = await SqliteCharacterPersistence.UpsertAccountAsync(connection, payload.Account, payload.SeenAt, cancellationToken);
            var characterId = await SqliteCharacterPersistence.UpsertCharacterAsync(
                connection,
                accountId,
                payload.Character,
                payload.Realm,
                payload.CharacterLevel,
                payload.CharacterClassId,
                payload.CharacterClassName,
                payload.MercenaryKind,
                payload.MercenaryType,
                payload.MercenaryAct,
                payload.MercenaryClassId,
                payload.MercenaryTypeSource,
                payload.SeenAt,
                cancellationToken);
            await SqliteCharacterPersistence.DeleteLocalItemsAsync(connection, characterId, cancellationToken);
            await SqliteCharacterPersistence.InsertSessionAsync(connection, characterId, payload.GameName, payload.SeenAt, cancellationToken);

            foreach (var entry in payload.Items)
            {
                var itemId = await SqliteItemPersistence.InsertCanonicalItemAsync(connection, characterId, payload, entry, cancellationToken);
                await SqliteItemPersistence.InsertCanonicalSocketsAsync(connection, itemId, entry.Item.SocketFillers, cancellationToken);
            }

            // Observed players: re-use the legacy snapshot insert path for now.
            // We synthesize a thin StyxCharacterSnapshot just to hand the
            // existing helpers the realm/game name/seenAt context they need.
            var observedCtx = new StyxCharacterSnapshot
            {
                Account = payload.Account,
                Character = payload.Character,
                Realm = payload.Realm,
                GameName = payload.GameName,
                CharacterLevel = payload.CharacterLevel,
                CharacterClassId = payload.CharacterClassId,
                CharacterClassName = payload.CharacterClassName,
                MercenaryKind = payload.MercenaryKind,
                MercenaryType = payload.MercenaryType,
                MercenaryAct = payload.MercenaryAct,
                MercenaryClassId = payload.MercenaryClassId,
                MercenaryTypeSource = payload.MercenaryTypeSource,
                SeenAt = payload.SeenAt,
            };
            foreach (var observed in payload.ObservedPlayers)
            {
                var observedId = await SqliteObservedPersistence.UpsertObservedPlayerAsync(connection, characterId, observedCtx, observed, cancellationToken);
                await SqliteObservedPersistence.DeleteObservedPlayerItemsAsync(connection, observedId, cancellationToken);
                foreach (var entry in SqliteObservedPersistence.SelectObservedDisplayItems(observed.Items))
                {
                    var itemId = await SqliteObservedPersistence.InsertObservedPlayerItemAsync(connection, observedId, entry, cancellationToken);
                    await SqliteObservedPersistence.InsertObservedPlayerSocketsAsync(connection, itemId, entry.Item.SocketFillers, cancellationToken);
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return new SnapshotSaveResult(catalogChanged);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveImportedCharactersAsync(IReadOnlyList<ImportedCharacterSnapshot> snapshots, CancellationToken cancellationToken = default)
    {
        if (snapshots.Count == 0)
            return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureDatabaseAsync(cancellationToken);

            await using var connection = OpenConnection();
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            foreach (var snapshot in snapshots)
            {
                var accountId = await SqliteCharacterPersistence.UpsertAccountAsync(connection, snapshot.Account, snapshot.SeenAt, cancellationToken);
                var characterId = await SqliteCharacterPersistence.UpsertImportedCharacterAsync(connection, accountId, snapshot, cancellationToken);
                await SqliteCharacterPersistence.DeleteLocalItemsAsync(connection, characterId, cancellationToken);
                await SqliteCharacterPersistence.InsertSessionWithSourceAsync(connection, characterId, snapshot.Source, snapshot.SeenAt, cancellationToken);

                foreach (var item in snapshot.Items)
                {
                    var itemId = await SqliteItemPersistence.InsertImportedItemAsync(connection, characterId, item, cancellationToken);
                    await SqliteItemPersistence.InsertImportedSocketsAsync(connection, itemId, item.Sockets, cancellationToken);
                }
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CompanionCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseAsync(cancellationToken);

        await using var connection = OpenConnection();
        return await SqliteCatalogReader.ReadCatalogAsync(connection, ResolveDatabaseDisplayPath(), _itemLookup, cancellationToken);
    }

    public async Task<bool> DeleteCharacterAsync(string accountName, string characterName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(characterName))
            return false;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureDatabaseAsync(cancellationToken);
            await using var connection = OpenConnection();
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var deleted = await SqliteCharacterPersistence.SoftDeleteCharacterAsync(connection, accountName, characterName, cancellationToken);
            if (!deleted)
                return false;

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> ArchiveCharacterAsync(string accountName, string characterName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(characterName))
            return false;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureDatabaseAsync(cancellationToken);
            await using var connection = OpenConnection();
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var archived = await SqliteCharacterPersistence.ArchiveCharacterAsync(connection, accountName, characterName, cancellationToken);
            if (!archived)
                return false;

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> ArchiveAccountAsync(string accountName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountName))
            return 0;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureDatabaseAsync(cancellationToken);
            await using var connection = OpenConnection();
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var archived = await SqliteCharacterPersistence.ArchiveAccountAsync(connection, accountName, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return archived;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> RestoreCharacterAsync(string accountName, string characterName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(characterName))
            return false;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureDatabaseAsync(cancellationToken);
            await using var connection = OpenConnection();
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var restored = await SqliteCharacterPersistence.RestoreCharacterAsync(connection, accountName, characterName, cancellationToken);
            if (!restored)
                return false;

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> RestoreAccountAsync(string accountName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountName))
            return 0;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureDatabaseAsync(cancellationToken);
            await using var connection = OpenConnection();
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var restored = await SqliteCharacterPersistence.RestoreAccountAsync(connection, accountName, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return restored;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> PermanentlyDeleteCharacterAsync(string accountName, string characterName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(characterName))
            return false;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureDatabaseAsync(cancellationToken);
            await using var connection = OpenConnection();
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var deleted = await SqliteCharacterPersistence.PermanentlyDeleteCharacterAsync(connection, accountName, characterName, cancellationToken);
            if (!deleted)
                return false;

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> PermanentlyDeleteArchivedAccountAsync(string accountName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountName))
            return 0;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureDatabaseAsync(cancellationToken);
            await using var connection = OpenConnection();
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var deleted = await SqliteCharacterPersistence.PermanentlyDeleteArchivedAccountAsync(connection, accountName, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return deleted;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> SetAccountFavoriteAsync(string accountName, bool isFavorite, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountName))
            return false;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureDatabaseAsync(cancellationToken);
            await using var connection = OpenConnection();
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var updated = await SqliteCharacterPersistence.SetAccountFavoriteAsync(connection, accountName, isFavorite, cancellationToken);
            if (!updated)
                return false;

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> DeleteObservedPlayerAsync(string observedKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(observedKey))
            return 0;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureDatabaseAsync(cancellationToken);
            await using var connection = OpenConnection();
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var deleted = await SqliteObservedPersistence.DeleteObservedPlayerAsync(connection, observedKey, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return deleted;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> ArchiveObservedPlayerAsync(string observedKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(observedKey))
            return 0;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureDatabaseAsync(cancellationToken);
            await using var connection = OpenConnection();
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var archived = await SqliteObservedPersistence.ArchiveObservedPlayerAsync(connection, observedKey, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return archived;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> RestoreObservedPlayerAsync(string observedKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(observedKey))
            return 0;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureDatabaseAsync(cancellationToken);
            await using var connection = OpenConnection();
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var restored = await SqliteObservedPersistence.RestoreObservedPlayerAsync(connection, observedKey, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return restored;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureDatabaseAsync(CancellationToken cancellationToken)
    {
        var path = ResolveDatabasePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var connection = OpenConnection();
        await SqliteSchemaMigrator.EnsureDatabaseAsync(connection, cancellationToken);
        await SqliteCharacterPersistence.NormalizeFavoriteRanksAsync(connection, cancellationToken);
    }

    private SqliteConnection OpenConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = ResolveDatabasePath(),
            ForeignKeys = true
        };

        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }

    private static async Task<long?> FindCharacterIdAsync(SqliteConnection connection, string accountName, string characterName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.Id
            FROM Characters c
            JOIN Accounts a ON a.Id = c.AccountId
            WHERE a.Name = $account AND c.Name = $character
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$account", accountName);
        command.Parameters.AddWithValue("$character", characterName);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null || result is DBNull ? null : Convert.ToInt64(result);
    }

    private static async Task<string> BuildExistingCanonicalSignatureAsync(SqliteConnection connection, long characterId, CanonicalCharacterPayload payload, CancellationToken cancellationToken)
    {
        var lines = new List<string>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT Realm, Level, ClassId, ClassName, MercenaryKind, MercenaryType, MercenaryAct, MercenaryClassId, MercenaryTypeSource
                FROM Characters
                WHERE Id = $characterId;
                """;
            command.Parameters.AddWithValue("$characterId", characterId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return "missing-character";
            }

            lines.Add(SignatureLine(
                "character",
                ReadNullableString(reader, "Realm"),
                ReadNullableInt(reader, "Level"),
                ReadNullableInt(reader, "ClassId"),
                ReadNullableString(reader, "ClassName"),
                ReadNullableInt(reader, "MercenaryKind"),
                ReadNullableString(reader, "MercenaryType"),
                ReadNullableInt(reader, "MercenaryAct"),
                ReadNullableInt(reader, "MercenaryClassId"),
                ReadNullableString(reader, "MercenaryTypeSource")));
        }

        lines.AddRange(await ReadStoredItemSignaturesAsync(connection, "Items", "ItemSockets", "ItemId", "CharacterId", characterId, "own", cancellationToken));

        foreach (var observed in payload.ObservedPlayers.OrderBy(p => p.PlayerUid, StringComparer.OrdinalIgnoreCase))
        {
            var observedId = await FindObservedPlayerIdAsync(connection, characterId, observed.PlayerUid, cancellationToken);
            if (observedId is null)
            {
                lines.Add(SignatureLine("observed-missing", observed.PlayerUid));
                continue;
            }

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT PlayerUid, PlayerName, AccountName, ClassId, ClassName, Level, GameName
                    FROM ObservedPlayers
                    WHERE Id = $observedId;
                    """;
                command.Parameters.AddWithValue("$observedId", observedId.Value);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    lines.Add(SignatureLine(
                        "observed",
                        ReadNullableString(reader, "PlayerUid"),
                        ReadNullableString(reader, "PlayerName"),
                        ReadNullableString(reader, "AccountName"),
                        ReadNullableInt(reader, "ClassId"),
                        ReadNullableString(reader, "ClassName"),
                        ReadNullableInt(reader, "Level"),
                        ReadNullableString(reader, "GameName")));
                }
            }

            lines.AddRange(await ReadStoredItemSignaturesAsync(connection, "ObservedPlayerItems", "ObservedPlayerItemSockets", "ObservedPlayerItemId", "ObservedPlayerId", observedId.Value, $"observed:{observed.PlayerUid}", cancellationToken));
        }

        lines.Sort(StringComparer.Ordinal);
        return string.Join('\n', lines);
    }

    private static string BuildIncomingCanonicalSignature(CanonicalCharacterPayload payload)
    {
        var lines = new List<string>
        {
            SignatureLine(
                "character",
                payload.Realm,
                payload.CharacterLevel,
                payload.CharacterClassId,
                payload.CharacterClassName,
                payload.MercenaryKind,
                payload.MercenaryType,
                payload.MercenaryAct,
                payload.MercenaryClassId,
                payload.MercenaryTypeSource),
        };

        lines.AddRange(payload.Items.Select(entry => IncomingItemSignature("own", entry, observed: false, payload.Account, payload.Character, payload.Realm)));

        foreach (var observed in payload.ObservedPlayers)
        {
            lines.Add(SignatureLine(
                "observed",
                observed.PlayerUid,
                observed.PlayerName,
                observed.AccountName,
                observed.ClassId,
                observed.ClassName,
                observed.Level,
                payload.GameName));

            lines.AddRange(SqliteObservedPersistence
                .SelectObservedDisplayItems(observed.Items)
                .Select(entry => IncomingItemSignature($"observed:{observed.PlayerUid}", entry, observed: true, string.Empty, string.Empty, null)));
        }

        lines.Sort(StringComparer.Ordinal);
        return string.Join('\n', lines);
    }

    private static string IncomingItemSignature(string scope, CanonicalItemPayload entry, bool observed, string account, string character, string? realm)
    {
        var record = observed
            ? entry.Item.ToItemRecord(entry.Tooltip, string.Empty, string.Empty, null)
            : entry.Item.ToItemRecord(entry.Tooltip, account, character, realm);

        var gid = observed
            ? (string.IsNullOrWhiteSpace(record.Gid) ? $"{record.Classid}:{record.Location}:{record.X}" : record.Gid)
            : (string.IsNullOrWhiteSpace(record.Gid) ? $"{record.Classid}:{record.Location}:{record.X}:{record.Y}:{record.Title}" : record.Gid);

        return SignatureLine(
            scope,
            gid,
            record.Classid,
            string.IsNullOrWhiteSpace(record.Title) ? entry.Item.Code : record.Title,
            record.Description,
            string.IsNullOrWhiteSpace(record.Image) ? "box" : record.Image,
            record.ItemColor,
            observed && string.IsNullOrWhiteSpace(record.Storage) ? "equipped" : record.Storage,
            record.Location,
            record.X,
            record.Y,
            observed ? Math.Max(1, record.Width) : Math.Max(1, record.GridWidth) * 28,
            observed ? Math.Max(1, record.Height) : Math.Max(1, record.GridHeight) * 28,
            Math.Max(1, record.GridWidth),
            Math.Max(1, record.GridHeight),
            record.Ethereal ? 1 : 0,
            string.Join(",", entry.Item.SocketFillers));
    }

    private static async Task<long?> FindObservedPlayerIdAsync(SqliteConnection connection, long characterId, string playerUid, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id
            FROM ObservedPlayers
            WHERE ObservedByCharacterId = $characterId AND PlayerUid = $playerUid
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$characterId", characterId);
        command.Parameters.AddWithValue("$playerUid", playerUid);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null || result is DBNull ? null : Convert.ToInt64(result);
    }

    private static async Task<IReadOnlyList<string>> ReadStoredItemSignaturesAsync(
        SqliteConnection connection,
        string itemTable,
        string socketTable,
        string socketItemColumn,
        string ownerColumn,
        long ownerId,
        string scope,
        CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT i.Gid, i.ClassId, i.Title, i.Description, i.Image, i.ItemColor, i.Storage, i.Location,
                   i.X, i.Y, i.PixelWidth, i.PixelHeight, i.GridWidth, i.GridHeight, i.Ethereal,
                   (
                       SELECT group_concat(Image, ',')
                       FROM (
                           SELECT Image
                           FROM {socketTable}
                           WHERE {socketItemColumn} = i.Id
                           ORDER BY Position
                       )
                   ) AS Sockets
            FROM {itemTable} i
            WHERE i.{ownerColumn} = $ownerId;
            """;
        command.Parameters.AddWithValue("$ownerId", ownerId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            lines.Add(SignatureLine(
                scope,
                ReadNullableString(reader, "Gid"),
                ReadNullableInt(reader, "ClassId"),
                ReadNullableString(reader, "Title"),
                ReadNullableString(reader, "Description"),
                ReadNullableString(reader, "Image"),
                ReadNullableInt(reader, "ItemColor"),
                ReadNullableString(reader, "Storage"),
                ReadNullableInt(reader, "Location"),
                ReadNullableInt(reader, "X"),
                ReadNullableInt(reader, "Y"),
                ReadNullableInt(reader, "PixelWidth"),
                ReadNullableInt(reader, "PixelHeight"),
                ReadNullableInt(reader, "GridWidth"),
                ReadNullableInt(reader, "GridHeight"),
                ReadNullableInt(reader, "Ethereal"),
                ReadNullableString(reader, "Sockets")));
        }

        return lines;
    }

    private static string SignatureLine(params object?[] values)
    {
        var builder = new StringBuilder();
        foreach (var value in values)
        {
            var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            builder.Append(text.Length).Append(':').Append(text).Append('|');
        }

        return builder.ToString();
    }

    private static string? ReadNullableString(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int? ReadNullableInt(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

}

public sealed record SnapshotSaveResult(bool CatalogChanged);
