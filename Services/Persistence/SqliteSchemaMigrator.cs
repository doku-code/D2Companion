using Microsoft.Data.Sqlite;

namespace D2CompanionMvc.Services.Persistence;

internal static class SqliteSchemaMigrator
{
    internal static async Task EnsureDatabaseAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Schema.Sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await EnsureRealmScopedAccountsAsync(connection, cancellationToken);
        await TryAddColumnAsync(connection, "Characters", "Realm", "TEXT NULL", cancellationToken);

        // Apply the back-compat column upgrades declared in Schema. Each call
        // is idempotent (duplicate-column errors are swallowed).
        foreach (var (table, column, type) in Schema.BackCompatColumns)
            await TryAddColumnAsync(connection, table, column, type, cancellationToken);

        await NormalizeKnownRealmValuesAsync(connection, cancellationToken);
    }

    private static async Task EnsureRealmScopedAccountsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var tableSql = await ReadTableSqlAsync(connection, "Accounts", cancellationToken);
        if (tableSql is null ||
            !tableSql.Contains("Name TEXT NOT NULL UNIQUE", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var accounts = new List<AccountMigrationRow>();
        await using (var readAccounts = connection.CreateCommand())
        {
            readAccounts.CommandText = "SELECT Id, Name, LastSeenUtc, IsFavorite, FavoriteRank FROM Accounts ORDER BY Id;";
            await using var reader = await readAccounts.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                accounts.Add(new AccountMigrationRow(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    !reader.IsDBNull(3) && reader.GetInt32(3) == 1,
                    reader.IsDBNull(4) ? null : reader.GetInt32(4)));
            }
        }

        var characterRealmsByAccount = new Dictionary<long, HashSet<string>>();
        var characterUpdates = new List<CharacterRealmMigrationRow>();
        await using (var readCharacters = connection.CreateCommand())
        {
            readCharacters.CommandText = "SELECT Id, AccountId, Realm FROM Characters ORDER BY Id;";
            await using var reader = await readCharacters.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var characterId = reader.GetInt64(0);
                var accountId = reader.GetInt64(1);
                var realm = NormalizeRealm(reader.IsDBNull(2) ? null : reader.GetString(2));
                characterUpdates.Add(new CharacterRealmMigrationRow(characterId, accountId, realm));
                if (!characterRealmsByAccount.TryGetValue(accountId, out var realms))
                {
                    realms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    characterRealmsByAccount[accountId] = realms;
                }
                realms.Add(realm);
            }
        }

        var nextAccountId = accounts.Count == 0 ? 1 : accounts.Max(account => account.Id) + 1;
        var accountMappings = new Dictionary<(long OldAccountId, string Realm), long>();
        var newAccounts = new List<AccountMigrationRow>();
        foreach (var account in accounts)
        {
            var realms = characterRealmsByAccount.TryGetValue(account.Id, out var accountRealms)
                ? accountRealms.OrderBy(realm => realm, StringComparer.OrdinalIgnoreCase).ToArray()
                : [string.Empty];

            for (var index = 0; index < realms.Length; index++)
            {
                var realm = realms[index];
                var newId = index == 0 ? account.Id : nextAccountId++;
                accountMappings[(account.Id, realm)] = newId;
                newAccounts.Add(account with { Id = newId, Realm = realm });
            }
        }

        await using var disableFk = connection.CreateCommand();
        disableFk.CommandText = "PRAGMA foreign_keys = OFF;";
        await disableFk.ExecuteNonQueryAsync(cancellationToken);

        await using var legacyAlter = connection.CreateCommand();
        legacyAlter.CommandText = "PRAGMA legacy_alter_table = ON;";
        await legacyAlter.ExecuteNonQueryAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var rename = connection.CreateCommand())
            {
                rename.Transaction = (SqliteTransaction)transaction;
                rename.CommandText = "ALTER TABLE Accounts RENAME TO Accounts_legacy_realm_migration;";
                await rename.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var create = connection.CreateCommand())
            {
                create.Transaction = (SqliteTransaction)transaction;
                create.CommandText = """
                    CREATE TABLE Accounts (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        Realm TEXT NOT NULL DEFAULT '',
                        LastSeenUtc TEXT NULL,
                        IsFavorite INTEGER NOT NULL DEFAULT 0,
                        FavoriteRank INTEGER NULL,
                        UNIQUE(Realm, Name)
                    );
                    """;
                await create.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var account in newAccounts)
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = (SqliteTransaction)transaction;
                insert.CommandText = """
                    INSERT INTO Accounts (Id, Name, Realm, LastSeenUtc, IsFavorite, FavoriteRank)
                    VALUES ($id, $name, $realm, $lastSeenUtc, $isFavorite, $favoriteRank);
                    """;
                insert.Parameters.AddWithValue("$id", account.Id);
                insert.Parameters.AddWithValue("$name", account.Name);
                insert.Parameters.AddWithValue("$realm", account.Realm);
                insert.Parameters.AddWithValue("$lastSeenUtc", (object?)account.LastSeenUtc ?? DBNull.Value);
                insert.Parameters.AddWithValue("$isFavorite", account.IsFavorite ? 1 : 0);
                insert.Parameters.AddWithValue("$favoriteRank", account.FavoriteRank is int rank ? (object)rank : DBNull.Value);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var character in characterUpdates)
            {
                if (!accountMappings.TryGetValue((character.OldAccountId, character.Realm), out var newAccountId))
                    continue;

                await using var update = connection.CreateCommand();
                update.Transaction = (SqliteTransaction)transaction;
                update.CommandText = "UPDATE Characters SET AccountId = $newAccountId WHERE Id = $characterId;";
                update.Parameters.AddWithValue("$newAccountId", newAccountId);
                update.Parameters.AddWithValue("$characterId", character.CharacterId);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var drop = connection.CreateCommand())
            {
                drop.Transaction = (SqliteTransaction)transaction;
                drop.CommandText = "DROP TABLE Accounts_legacy_realm_migration;";
                await drop.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            await using var legacyOff = connection.CreateCommand();
            legacyOff.CommandText = "PRAGMA legacy_alter_table = OFF;";
            await legacyOff.ExecuteNonQueryAsync(cancellationToken);

            await using var enableFk = connection.CreateCommand();
            enableFk.CommandText = "PRAGMA foreign_keys = ON;";
            await enableFk.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<string?> ReadTableSqlAsync(SqliteConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = $table;";
        command.Parameters.AddWithValue("$table", table);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToString(result);
    }

    private static string NormalizeRealm(string? realm)
    {
        var key = realm?.Trim().ToLowerInvariant();
        return key switch
        {
            "1" or "useast" or "east" => "USEast",
            "0" or "uswest" or "west" => "USWest",
            "3" or "europe" or "euro" => "Europe",
            "2" or "asia" => "Asia",
            _ => string.IsNullOrWhiteSpace(realm) ? string.Empty : realm.Trim(),
        };
    }

    private static async Task NormalizeKnownRealmValuesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        foreach (var (raw, normalized) in KnownRealmMappings)
        {
            await NormalizeAccountRealmAsync(connection, raw, normalized, cancellationToken);
            await UpdateRealmColumnAsync(connection, "Characters", raw, normalized, cancellationToken);
        }
    }

    private static async Task NormalizeAccountRealmAsync(SqliteConnection connection, string rawRealm, string normalizedRealm, CancellationToken cancellationToken)
    {
        var rows = new List<(long Id, string Name, bool IsFavorite, int? FavoriteRank)>();
        await using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT Id, Name, IsFavorite, FavoriteRank FROM Accounts WHERE Realm = $rawRealm ORDER BY Id;";
            read.Parameters.AddWithValue("$rawRealm", rawRealm);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((
                    reader.GetInt64(0),
                    reader.GetString(1),
                    !reader.IsDBNull(2) && reader.GetInt32(2) == 1,
                    reader.IsDBNull(3) ? null : reader.GetInt32(3)));
            }
        }

        foreach (var row in rows)
        {
            var targetId = await FindAccountIdAsync(connection, row.Name, normalizedRealm, excludeId: row.Id, cancellationToken);
            if (targetId is long existingId)
            {
                await using (var removeDuplicateCharacters = connection.CreateCommand())
                {
                    removeDuplicateCharacters.CommandText = """
                        DELETE FROM Characters
                        WHERE AccountId = $sourceId
                          AND Name IN (SELECT Name FROM Characters WHERE AccountId = $targetId);
                        """;
                    removeDuplicateCharacters.Parameters.AddWithValue("$targetId", existingId);
                    removeDuplicateCharacters.Parameters.AddWithValue("$sourceId", row.Id);
                    await removeDuplicateCharacters.ExecuteNonQueryAsync(cancellationToken);
                }

                await using (var moveCharacters = connection.CreateCommand())
                {
                    moveCharacters.CommandText = "UPDATE Characters SET AccountId = $targetId WHERE AccountId = $sourceId;";
                    moveCharacters.Parameters.AddWithValue("$targetId", existingId);
                    moveCharacters.Parameters.AddWithValue("$sourceId", row.Id);
                    await moveCharacters.ExecuteNonQueryAsync(cancellationToken);
                }

                if (row.IsFavorite)
                {
                    await using var favorite = connection.CreateCommand();
                    favorite.CommandText = """
                        UPDATE Accounts
                        SET IsFavorite = 1,
                            FavoriteRank = COALESCE(FavoriteRank, $favoriteRank)
                        WHERE Id = $targetId;
                        """;
                    favorite.Parameters.AddWithValue("$targetId", existingId);
                    favorite.Parameters.AddWithValue("$favoriteRank", row.FavoriteRank is int rank ? (object)rank : DBNull.Value);
                    await favorite.ExecuteNonQueryAsync(cancellationToken);
                }

                await using (var delete = connection.CreateCommand())
                {
                    delete.CommandText = "DELETE FROM Accounts WHERE Id = $sourceId;";
                    delete.Parameters.AddWithValue("$sourceId", row.Id);
                    await delete.ExecuteNonQueryAsync(cancellationToken);
                }
            }
            else
            {
                await using var update = connection.CreateCommand();
                update.CommandText = "UPDATE Accounts SET Realm = $normalizedRealm WHERE Id = $accountId;";
                update.Parameters.AddWithValue("$normalizedRealm", normalizedRealm);
                update.Parameters.AddWithValue("$accountId", row.Id);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    private static async Task<long?> FindAccountIdAsync(SqliteConnection connection, string accountName, string realm, long excludeId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM Accounts WHERE Name = $name AND Realm = $realm AND Id <> $excludeId LIMIT 1;";
        command.Parameters.AddWithValue("$name", accountName);
        command.Parameters.AddWithValue("$realm", realm);
        command.Parameters.AddWithValue("$excludeId", excludeId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToInt64(result);
    }

    private static async Task UpdateRealmColumnAsync(SqliteConnection connection, string table, string rawRealm, string normalizedRealm, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE {table} SET Realm = $normalizedRealm WHERE Realm = $rawRealm;";
        command.Parameters.AddWithValue("$normalizedRealm", normalizedRealm);
        command.Parameters.AddWithValue("$rawRealm", rawRealm);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static readonly (string Raw, string Normalized)[] KnownRealmMappings =
    [
        ("1", "USEast"),
        ("0", "USWest"),
        ("3", "Europe"),
        ("2", "Asia"),
    ];

    private static async Task TryAddColumnAsync(SqliteConnection connection, string table, string column, string definition, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 1 && exception.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
        {
        }
    }

    private sealed record AccountMigrationRow(
        long Id,
        string Name,
        string? LastSeenUtc,
        bool IsFavorite,
        int? FavoriteRank)
    {
        public string Realm { get; init; } = string.Empty;
    }

    private sealed record CharacterRealmMigrationRow(long CharacterId, long OldAccountId, string Realm);
}
