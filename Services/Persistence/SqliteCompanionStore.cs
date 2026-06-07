using System.Text;
using D2CompanionMvc.Models.Catalog;
using D2CompanionMvc.Options;
using D2CompanionMvc.Extensions.Styx.Ingestion;
using D2CompanionMvc.Extensions.Styx.Models;
using D2CompanionMvc.Services.Characters;
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

    public async Task<bool> HasCatalogRowsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseAsync(cancellationToken);

        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1
                FROM Characters
                WHERE DeletedAtUtc IS NULL
                LIMIT 1
            );
            """;
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

            var existingCharacterId = await FindCharacterIdAsync(connection, payload.Account, payload.Realm, payload.Character, cancellationToken);
            var gameJoinExpiration = TrustedExpiration.FromGameJoinReset(payload);
            var gameJoinExpirationChanged = gameJoinExpiration.ExpiresAtUtc.HasValue
                && (existingCharacterId is null
                    || await ShouldApplyGameJoinExpirationAsync(connection, existingCharacterId.Value, gameJoinExpiration, cancellationToken));
            var ownChanged = existingCharacterId is null
                || !string.Equals(
                    await BuildExistingOwnSignatureAsync(connection, existingCharacterId.Value, cancellationToken),
                    BuildIncomingOwnSignature(payload),
                    StringComparison.Ordinal);
            var observedChanged = payload.ObservedPlayers.Count > 0 && (existingCharacterId is null
                || !string.Equals(
                    await BuildExistingObservedSignatureAsync(connection, existingCharacterId.Value, payload, cancellationToken),
                    BuildIncomingObservedSignature(payload),
                    StringComparison.Ordinal));
            var catalogChanged = ownChanged || observedChanged || gameJoinExpirationChanged;

            var accountId   = await SqliteCharacterPersistence.UpsertAccountAsync(connection, payload.Account, payload.Realm, payload.SeenAt, cancellationToken);
            var characterId = await SqliteCharacterPersistence.UpsertCharacterAsync(
                connection,
                accountId,
                payload.Character,
                payload.Realm,
                payload.CharacterLevel,
                payload.CharacterClassId,
                payload.CharacterClassName,
                payload.Mode,
                payload.Hardcore,
                payload.Expansion,
                payload.Ladder,
                payload.MercenaryKind,
                payload.MercenaryType,
                payload.MercenaryAct,
                payload.MercenaryClassId,
                payload.MercenaryTypeSource,
                payload.SeenAt,
                cancellationToken);
            if (gameJoinExpirationChanged && gameJoinExpiration.ExpiresAtUtc.HasValue && gameJoinExpiration.TrustedAtUtc.HasValue && !string.IsNullOrWhiteSpace(gameJoinExpiration.Source))
            {
                await SqliteCharacterPersistence.UpdateTrustedExpirationAsync(
                    connection,
                    characterId,
                    gameJoinExpiration.ExpiresAtUtc.Value,
                    gameJoinExpiration.ServerHours,
                    gameJoinExpiration.TrustedAtUtc.Value,
                    gameJoinExpiration.Source,
                    cancellationToken);
            }

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
                Mode = payload.Mode,
                Hardcore = payload.Hardcore,
                Expansion = payload.Expansion,
                Ladder = payload.Ladder,
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
            return new SnapshotSaveResult(catalogChanged, ownChanged, observedChanged);
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
                var accountId = await SqliteCharacterPersistence.UpsertAccountAsync(connection, snapshot.Account, snapshot.Realm, snapshot.SeenAt, cancellationToken);
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

    public async Task<SnapshotSaveResult> SaveAccountRosterWithChangeDetectionAsync(StyxAccountRosterSnapshot roster, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureDatabaseAsync(cancellationToken);

            await using var connection = OpenConnection();
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var realm = SqliteCharacterPersistence.NormalizeRealm(roster.Realm);
            var existingAccountId = await FindAccountIdAsync(connection, roster.Account, realm, cancellationToken);
            var catalogChanged = existingAccountId is null;

            var accountId = await SqliteCharacterPersistence.UpsertAccountAsync(connection, roster.Account, realm, roster.SeenAt, cancellationToken);
            foreach (var character in roster.Characters.Where(character => !string.IsNullOrWhiteSpace(character.Character)))
            {
                var expiration = TrustedExpiration.FromRosterHours(character.ExpirationHours, roster.SeenAt);
                var existing = await ReadRosterCharacterStateAsync(connection, accountId, character.Character, cancellationToken);
                if (RosterStateChanged(existing, character, realm, roster.SeenAt, expiration))
                {
                    catalogChanged = true;
                }

                var characterId = await SqliteCharacterPersistence.UpsertRosterCharacterAsync(
                    connection,
                    accountId,
                    character.Character,
                    realm,
                    character.CharacterLevel,
                    character.CharacterClassId,
                    character.CharacterClassName,
                    character.Mode,
                    character.Hardcore,
                    character.Expansion,
                    character.Ladder,
                    expiration.ExpiresAtUtc,
                    expiration.ServerHours,
                    expiration.TrustedAtUtc,
                    expiration.Source,
                    roster.SeenAt,
                    cancellationToken);
                await SqliteCharacterPersistence.InsertSessionWithSourceAsync(connection, characterId, "styx:roster", roster.SeenAt, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new SnapshotSaveResult(catalogChanged, catalogChanged, false);
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
        => await DeleteCharacterAsync(accountName, null, characterName, cancellationToken);

    public async Task<bool> DeleteCharacterAsync(string accountName, string? realm, string characterName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(characterName))
            return false;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureDatabaseAsync(cancellationToken);
            await using var connection = OpenConnection();
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var deleted = await SqliteCharacterPersistence.SoftDeleteCharacterAsync(connection, accountName, realm, characterName, cancellationToken);
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
        => await ArchiveCharacterAsync(accountName, null, characterName, cancellationToken);

    public async Task<bool> ArchiveCharacterAsync(string accountName, string? realm, string characterName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(characterName))
            return false;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureDatabaseAsync(cancellationToken);
            await using var connection = OpenConnection();
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var archived = await SqliteCharacterPersistence.ArchiveCharacterAsync(connection, accountName, realm, characterName, cancellationToken);
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
        => await ArchiveAccountAsync(accountName, null, cancellationToken);

    public async Task<int> ArchiveAccountAsync(string accountName, string? realm, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountName))
            return 0;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureDatabaseAsync(cancellationToken);
            await using var connection = OpenConnection();
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var archived = await SqliteCharacterPersistence.ArchiveAccountAsync(connection, accountName, realm, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return archived;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> RestoreCharacterAsync(string accountName, string characterName, CancellationToken cancellationToken = default)
        => await RestoreCharacterAsync(accountName, null, characterName, cancellationToken);

    public async Task<bool> RestoreCharacterAsync(string accountName, string? realm, string characterName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(characterName))
            return false;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureDatabaseAsync(cancellationToken);
            await using var connection = OpenConnection();
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var restored = await SqliteCharacterPersistence.RestoreCharacterAsync(connection, accountName, realm, characterName, cancellationToken);
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
        => await RestoreAccountAsync(accountName, null, cancellationToken);

    public async Task<int> RestoreAccountAsync(string accountName, string? realm, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountName))
            return 0;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureDatabaseAsync(cancellationToken);
            await using var connection = OpenConnection();
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var restored = await SqliteCharacterPersistence.RestoreAccountAsync(connection, accountName, realm, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return restored;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> PermanentlyDeleteCharacterAsync(string accountName, string characterName, CancellationToken cancellationToken = default)
        => await PermanentlyDeleteCharacterAsync(accountName, null, characterName, cancellationToken);

    public async Task<bool> PermanentlyDeleteCharacterAsync(string accountName, string? realm, string characterName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(characterName))
            return false;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureDatabaseAsync(cancellationToken);
            await using var connection = OpenConnection();
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var deleted = await SqliteCharacterPersistence.PermanentlyDeleteCharacterAsync(connection, accountName, realm, characterName, cancellationToken);
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
        => await PermanentlyDeleteArchivedAccountAsync(accountName, null, cancellationToken);

    public async Task<int> PermanentlyDeleteArchivedAccountAsync(string accountName, string? realm, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountName))
            return 0;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureDatabaseAsync(cancellationToken);
            await using var connection = OpenConnection();
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var deleted = await SqliteCharacterPersistence.PermanentlyDeleteArchivedAccountAsync(connection, accountName, realm, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return deleted;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> SetAccountFavoriteAsync(string accountName, bool isFavorite, CancellationToken cancellationToken = default)
        => await SetAccountFavoriteAsync(accountName, null, isFavorite, cancellationToken);

    public async Task<bool> SetAccountFavoriteAsync(string accountName, string? realm, bool isFavorite, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountName))
            return false;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureDatabaseAsync(cancellationToken);
            await using var connection = OpenConnection();
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var updated = await SqliteCharacterPersistence.SetAccountFavoriteAsync(connection, accountName, realm, isFavorite, cancellationToken);
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

    private static async Task<long?> FindAccountIdAsync(SqliteConnection connection, string accountName, string? realm, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id
            FROM Accounts
            WHERE Name = $account AND Realm = $realm
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$account", accountName);
        command.Parameters.AddWithValue("$realm", SqliteCharacterPersistence.NormalizeRealm(realm));
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null || result is DBNull ? null : Convert.ToInt64(result);
    }

    private static async Task<long?> FindCharacterIdAsync(SqliteConnection connection, string accountName, string? realm, string characterName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.Id
            FROM Characters c
            JOIN Accounts a ON a.Id = c.AccountId
            WHERE a.Name = $account AND a.Realm = $realm AND c.Name = $character
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$account", accountName);
        command.Parameters.AddWithValue("$realm", SqliteCharacterPersistence.NormalizeRealm(realm));
        command.Parameters.AddWithValue("$character", characterName);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null || result is DBNull ? null : Convert.ToInt64(result);
    }

    private static async Task<RosterCharacterState?> ReadRosterCharacterStateAsync(SqliteConnection connection, long accountId, string characterName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Realm, Level, ClassId, ClassName, Mode, Hardcore, Expansion, Ladder, LastSeenUtc, ExpirationExpiresAtUtc, ExpirationLastServerHours
            FROM Characters
            WHERE AccountId = $accountId AND Name = $character
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$accountId", accountId);
        command.Parameters.AddWithValue("$character", characterName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new RosterCharacterState(
            ReadNullableString(reader, "Realm"),
            ReadNullableInt(reader, "Level"),
            ReadNullableInt(reader, "ClassId"),
            ReadNullableString(reader, "ClassName"),
            ReadNullableString(reader, "Mode"),
            ReadNullableInt(reader, "Hardcore") == 1,
            ReadNullableInt(reader, "Expansion") != 0,
            ReadNullableInt(reader, "Ladder") == 1,
            ReadNullableDateTimeOffset(reader, "LastSeenUtc"),
            ReadNullableDateTimeOffset(reader, "ExpirationExpiresAtUtc"),
            ReadNullableInt(reader, "ExpirationLastServerHours"));
    }

    private static bool RosterStateChanged(RosterCharacterState? existing, StyxRosterCharacterSnapshot incoming, string? realm, DateTimeOffset seenAt, TrustedExpiration expiration)
    {
        if (existing is null)
            return true;

        return KnownStringChanged(existing.Realm, realm)
            || KnownIntChanged(existing.Level, incoming.CharacterLevel)
            || KnownIntChanged(existing.ClassId, incoming.CharacterClassId)
            || KnownStringChanged(existing.ClassName, incoming.CharacterClassName)
            || KnownStringChanged(existing.Mode, incoming.Mode)
            || KnownBoolChanged(existing.Hardcore, incoming.Hardcore)
            || KnownBoolChanged(existing.Expansion, incoming.Expansion)
            || KnownBoolChanged(existing.Ladder, incoming.Ladder)
            || (expiration.ExpiresAtUtc.HasValue && existing.ExpirationExpiresAtUtc != expiration.ExpiresAtUtc.Value.ToUniversalTime())
            || (expiration.ServerHours.HasValue && existing.ExpirationLastServerHours != expiration.ServerHours)
            || existing.LastSeenUtc != seenAt.ToUniversalTime();
    }

    private static async Task<bool> ShouldApplyGameJoinExpirationAsync(
        SqliteConnection connection,
        long characterId,
        TrustedExpiration expiration,
        CancellationToken cancellationToken)
    {
        if (!expiration.ExpiresAtUtc.HasValue)
            return false;

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ExpirationExpiresAtUtc
            FROM Characters
            WHERE Id = $characterId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$characterId", characterId);
        var existing = await command.ExecuteScalarAsync(cancellationToken);
        var existingDeadline = existing is null || existing is DBNull
            ? null
            : DateTimeOffset.TryParse(Convert.ToString(existing), out var parsed)
                ? parsed.ToUniversalTime()
                : (DateTimeOffset?)null;

        if (!existingDeadline.HasValue)
            return true;

        var refreshThreshold = expiration.ExpiresAtUtc.Value.ToUniversalTime().AddDays(-1);
        return existingDeadline.Value < refreshThreshold;
    }

    private static bool KnownStringChanged(string? existing, string? incoming)
        => !string.IsNullOrWhiteSpace(incoming)
            && !string.Equals(existing, incoming, StringComparison.OrdinalIgnoreCase);

    private static bool KnownIntChanged(int? existing, int? incoming)
        => incoming.HasValue && existing != incoming;

    private static bool KnownBoolChanged(bool existing, bool? incoming)
        => incoming.HasValue && existing != incoming.Value;

    private static async Task<string> BuildExistingOwnSignatureAsync(SqliteConnection connection, long characterId, CancellationToken cancellationToken)
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
        lines.Sort(StringComparer.Ordinal);
        return string.Join('\n', lines);
    }

    private static async Task<string> BuildExistingObservedSignatureAsync(SqliteConnection connection, long characterId, CanonicalCharacterPayload payload, CancellationToken cancellationToken)
    {
        var lines = new List<string>();

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

    private static string BuildIncomingOwnSignature(CanonicalCharacterPayload payload)
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
        lines.Sort(StringComparer.Ordinal);
        return string.Join('\n', lines);
    }

    private static string BuildIncomingObservedSignature(CanonicalCharacterPayload payload)
    {
        var lines = new List<string>();

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

    private static DateTimeOffset? ReadNullableDateTimeOffset(SqliteDataReader reader, string name)
    {
        var value = ReadNullableString(reader, name);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed.ToUniversalTime() : null;
    }
}

public sealed record SnapshotSaveResult(bool CatalogChanged, bool MyChanged = false, bool ObservedChanged = false);

internal sealed record RosterCharacterState(
    string? Realm,
    int? Level,
    int? ClassId,
    string? ClassName,
    string? Mode,
    bool Hardcore,
    bool Expansion,
    bool Ladder,
    DateTimeOffset? LastSeenUtc,
    DateTimeOffset? ExpirationExpiresAtUtc,
    int? ExpirationLastServerHours);

internal sealed record TrustedExpiration(
    DateTimeOffset? ExpiresAtUtc,
    int? ServerHours,
    DateTimeOffset? TrustedAtUtc,
    string? Source)
{
    internal static TrustedExpiration FromRosterHours(int? hoursLeft, DateTimeOffset trustedAt)
    {
        var expiresAt = CharacterExpirationCalculator.ComputeExpiresAtFromServerHours(hoursLeft, trustedAt);
        return expiresAt.HasValue
            ? new TrustedExpiration(expiresAt.Value, hoursLeft, trustedAt.ToUniversalTime(), "ServerRoster")
            : new TrustedExpiration(null, null, null, null);
    }

    internal static TrustedExpiration FromGameJoinReset(CanonicalCharacterPayload payload)
    {
        if (!IsTrustedOwnGameJoin(payload))
            return new TrustedExpiration(null, null, null, null);

        var trustedAt = payload.SeenAt.ToUniversalTime();
        return new TrustedExpiration(
            CharacterExpirationCalculator.ComputeExpiresAtFromGameJoinReset(trustedAt),
            null,
            trustedAt,
            "GameJoinReset");
    }

    private static bool IsTrustedOwnGameJoin(CanonicalCharacterPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.Account)
            || string.IsNullOrWhiteSpace(payload.Character)
            || string.IsNullOrWhiteSpace(SqliteCharacterPersistence.NormalizeRealm(payload.Realm))
            || string.IsNullOrWhiteSpace(payload.GameName))
        {
            return false;
        }

        return string.Equals(payload.SnapshotPhase, "live", StringComparison.OrdinalIgnoreCase)
            || string.Equals(payload.SnapshotPhase, "settled", StringComparison.OrdinalIgnoreCase);
    }
}
