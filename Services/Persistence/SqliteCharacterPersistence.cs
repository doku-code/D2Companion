using D2CompanionMvc.Extensions.Styx.Models;
using Microsoft.Data.Sqlite;

namespace D2CompanionMvc.Services.Persistence;

internal static class SqliteCharacterPersistence
{
    internal static Task<long> UpsertAccountAsync(SqliteConnection connection, StyxCharacterSnapshot snapshot, CancellationToken cancellationToken)
        => UpsertAccountAsync(connection, snapshot.Account, snapshot.SeenAt, cancellationToken);

    internal static async Task<long> UpsertAccountAsync(SqliteConnection connection, string accountName, DateTimeOffset seenAt, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Accounts (Name, LastSeenUtc)
            VALUES ($name, $lastSeenUtc)
            ON CONFLICT(Name) DO UPDATE SET LastSeenUtc = excluded.LastSeenUtc
            RETURNING Id;
            """;
        command.Parameters.AddWithValue("$name", accountName);
        command.Parameters.AddWithValue("$lastSeenUtc", seenAt.UtcDateTime.ToString("O"));
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    internal static Task<long> UpsertCharacterAsync(SqliteConnection connection, long accountId, StyxCharacterSnapshot snapshot, CancellationToken cancellationToken)
        => UpsertCharacterAsync(
            connection,
            accountId,
            snapshot.Character,
            snapshot.Realm,
            snapshot.CharacterLevel,
            snapshot.CharacterClassId,
            string.IsNullOrWhiteSpace(snapshot.CharacterClassName) ? ClassNameFromId(snapshot.CharacterClassId) : snapshot.CharacterClassName,
            snapshot.MercenaryKind,
            snapshot.MercenaryType,
            snapshot.MercenaryAct,
            snapshot.MercenaryClassId,
            snapshot.MercenaryTypeSource,
            snapshot.SeenAt,
            cancellationToken);

    internal static async Task<long> UpsertCharacterAsync(
        SqliteConnection connection,
        long accountId,
        string characterName,
        string? realm,
        int? level,
        int? classId,
        string? className,
        int? mercenaryKind,
        string? mercenaryType,
        int? mercenaryAct,
        int? mercenaryClassId,
        string? mercenaryTypeSource,
        DateTimeOffset seenAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Characters (AccountId, Name, Realm, Level, ClassId, ClassName, MercenaryKind, MercenaryType, MercenaryAct, MercenaryClassId, MercenaryTypeSource, LastSeenUtc, DeletedAtUtc)
            VALUES ($accountId, $name, $realm, $level, $classId, $className, $mercenaryKind, $mercenaryType, $mercenaryAct, $mercenaryClassId, $mercenaryTypeSource, $lastSeenUtc, NULL)
            ON CONFLICT(AccountId, Name) DO UPDATE SET
                Realm = excluded.Realm,
                Level = COALESCE(excluded.Level, Characters.Level),
                ClassId = COALESCE(excluded.ClassId, Characters.ClassId),
                ClassName = COALESCE(excluded.ClassName, Characters.ClassName),
                MercenaryKind = COALESCE(excluded.MercenaryKind, Characters.MercenaryKind),
                MercenaryType = COALESCE(excluded.MercenaryType, Characters.MercenaryType),
                MercenaryAct = COALESCE(excluded.MercenaryAct, Characters.MercenaryAct),
                MercenaryClassId = COALESCE(excluded.MercenaryClassId, Characters.MercenaryClassId),
                MercenaryTypeSource = COALESCE(excluded.MercenaryTypeSource, Characters.MercenaryTypeSource),
                LastSeenUtc = excluded.LastSeenUtc,
                ArchivedAtUtc = NULL,
                DeletedAtUtc = NULL
            RETURNING Id;
            """;
        command.Parameters.AddWithValue("$accountId", accountId);
        command.Parameters.AddWithValue("$name", characterName);
        command.Parameters.AddWithValue("$realm", (object?)realm ?? DBNull.Value);
        command.Parameters.AddWithValue("$level", level.HasValue && level.Value > 0 ? (object)level.Value : DBNull.Value);
        command.Parameters.AddWithValue("$classId", classId.HasValue && classId.Value >= 0 ? (object)classId.Value : DBNull.Value);
        command.Parameters.AddWithValue("$className", string.IsNullOrWhiteSpace(className) ? DBNull.Value : (object)className);
        command.Parameters.AddWithValue("$mercenaryKind", mercenaryKind.HasValue && mercenaryKind.Value >= 0 ? (object)mercenaryKind.Value : DBNull.Value);
        command.Parameters.AddWithValue("$mercenaryType", string.IsNullOrWhiteSpace(mercenaryType) ? DBNull.Value : (object)mercenaryType);
        command.Parameters.AddWithValue("$mercenaryAct", mercenaryAct.HasValue && mercenaryAct.Value > 0 ? (object)mercenaryAct.Value : DBNull.Value);
        command.Parameters.AddWithValue("$mercenaryClassId", mercenaryClassId.HasValue && mercenaryClassId.Value >= 0 ? (object)mercenaryClassId.Value : DBNull.Value);
        command.Parameters.AddWithValue("$mercenaryTypeSource", string.IsNullOrWhiteSpace(mercenaryTypeSource) ? DBNull.Value : (object)mercenaryTypeSource);
        command.Parameters.AddWithValue("$lastSeenUtc", seenAt.UtcDateTime.ToString("O"));
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    internal static async Task<long> UpsertImportedCharacterAsync(
        SqliteConnection connection,
        long accountId,
        ImportedCharacterSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Characters (AccountId, Name, Realm, Level, ClassId, ClassName, Mode, Hardcore, Expansion, Ladder, LastSeenUtc, DeletedAtUtc)
            VALUES ($accountId, $name, $realm, $level, $classId, $className, $mode, $hardcore, $expansion, $ladder, $lastSeenUtc, NULL)
            ON CONFLICT(AccountId, Name) DO UPDATE SET
                Realm = excluded.Realm,
                Level = COALESCE(excluded.Level, Characters.Level),
                ClassId = COALESCE(excluded.ClassId, Characters.ClassId),
                ClassName = COALESCE(excluded.ClassName, Characters.ClassName),
                Mode = COALESCE(excluded.Mode, Characters.Mode),
                Hardcore = excluded.Hardcore,
                Expansion = excluded.Expansion,
                Ladder = excluded.Ladder,
                LastSeenUtc = excluded.LastSeenUtc,
                ArchivedAtUtc = NULL,
                DeletedAtUtc = NULL
            RETURNING Id;
            """;
        command.Parameters.AddWithValue("$accountId", accountId);
        command.Parameters.AddWithValue("$name", snapshot.Character);
        command.Parameters.AddWithValue("$realm", string.IsNullOrWhiteSpace(snapshot.Realm) ? DBNull.Value : (object)snapshot.Realm);
        command.Parameters.AddWithValue("$level", snapshot.Level.HasValue && snapshot.Level.Value > 0 ? (object)snapshot.Level.Value : DBNull.Value);
        command.Parameters.AddWithValue("$classId", snapshot.ClassId.HasValue && snapshot.ClassId.Value >= 0 ? (object)snapshot.ClassId.Value : DBNull.Value);
        command.Parameters.AddWithValue("$className", string.IsNullOrWhiteSpace(snapshot.ClassName) ? DBNull.Value : (object)snapshot.ClassName);
        command.Parameters.AddWithValue("$mode", string.IsNullOrWhiteSpace(snapshot.Mode) ? DBNull.Value : (object)snapshot.Mode);
        command.Parameters.AddWithValue("$hardcore", snapshot.Hardcore ? 1 : 0);
        command.Parameters.AddWithValue("$expansion", snapshot.Expansion ? 1 : 0);
        command.Parameters.AddWithValue("$ladder", snapshot.Ladder ? 1 : 0);
        command.Parameters.AddWithValue("$lastSeenUtc", snapshot.SeenAt.UtcDateTime.ToString("O"));
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    internal static async Task DeleteLocalItemsAsync(SqliteConnection connection, long characterId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Items WHERE CharacterId = $characterId;";
        command.Parameters.AddWithValue("$characterId", characterId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static Task InsertSessionAsync(SqliteConnection connection, long characterId, StyxCharacterSnapshot snapshot, CancellationToken cancellationToken)
        => InsertSessionAsync(connection, characterId, snapshot.GameName, snapshot.SeenAt, cancellationToken);

    internal static async Task InsertSessionAsync(SqliteConnection connection, long characterId, string? gameName, DateTimeOffset seenAt, CancellationToken cancellationToken)
        => await InsertSessionWithSourceAsync(
            connection,
            characterId,
            string.IsNullOrWhiteSpace(gameName) ? "styx" : $"styx:{gameName}",
            seenAt,
            cancellationToken);

    internal static async Task InsertSessionWithSourceAsync(SqliteConnection connection, long characterId, string source, DateTimeOffset seenAt, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO CharacterSessions (CharacterId, SeenAtUtc, Source) VALUES ($characterId, $seenAtUtc, $source);";
        command.Parameters.AddWithValue("$characterId", characterId);
        command.Parameters.AddWithValue("$seenAtUtc", seenAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$source", string.IsNullOrWhiteSpace(source) ? "unknown" : source);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task<bool> SoftDeleteCharacterAsync(SqliteConnection connection, string accountName, string characterName, CancellationToken cancellationToken)
    {
        var characterId = await FindActiveCharacterIdAsync(connection, accountName, characterName, cancellationToken);
        if (characterId is null)
            return false;

        // Do not delete the Characters row: ObservedPlayers rows reference
        // the observing character. Soft-hiding keeps observed history intact
        // while removing the character and its local gear from My Accounts.
        await DeleteLocalItemsAsync(connection, characterId.Value, cancellationToken);

        await using (var sessions = connection.CreateCommand())
        {
            sessions.CommandText = "DELETE FROM CharacterSessions WHERE CharacterId = $characterId;";
            sessions.Parameters.AddWithValue("$characterId", characterId.Value);
            await sessions.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var character = connection.CreateCommand())
        {
            character.CommandText = "UPDATE Characters SET DeletedAtUtc = $deletedAtUtc WHERE Id = $characterId;";
            character.Parameters.AddWithValue("$deletedAtUtc", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
            character.Parameters.AddWithValue("$characterId", characterId.Value);
            await character.ExecuteNonQueryAsync(cancellationToken);
        }

        return true;
    }

    internal static async Task<bool> ArchiveCharacterAsync(SqliteConnection connection, string accountName, string characterName, CancellationToken cancellationToken)
    {
        var characterId = await FindActiveCharacterIdAsync(connection, accountName, characterName, cancellationToken);
        if (characterId is null)
            return false;

        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Characters SET ArchivedAtUtc = $archivedAtUtc WHERE Id = $characterId;";
        command.Parameters.AddWithValue("$archivedAtUtc", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$characterId", characterId.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }

    internal static async Task<int> ArchiveAccountAsync(SqliteConnection connection, string accountName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Characters
            SET ArchivedAtUtc = $archivedAtUtc
            WHERE AccountId = (SELECT Id FROM Accounts WHERE Name = $accountName)
              AND DeletedAtUtc IS NULL
              AND ArchivedAtUtc IS NULL;
            """;
        command.Parameters.AddWithValue("$archivedAtUtc", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$accountName", accountName);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task<bool> RestoreCharacterAsync(SqliteConnection connection, string accountName, string characterName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Characters
            SET ArchivedAtUtc = NULL
            WHERE Id = (
                SELECT c.Id
                FROM Characters c
                JOIN Accounts a ON a.Id = c.AccountId
                WHERE a.Name = $accountName
                  AND c.Name = $characterName
                  AND c.DeletedAtUtc IS NULL
                  AND c.ArchivedAtUtc IS NOT NULL
                LIMIT 1
            );
            """;
        command.Parameters.AddWithValue("$accountName", accountName);
        command.Parameters.AddWithValue("$characterName", characterName);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    internal static async Task<int> RestoreAccountAsync(SqliteConnection connection, string accountName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Characters
            SET ArchivedAtUtc = NULL
            WHERE AccountId = (SELECT Id FROM Accounts WHERE Name = $accountName)
              AND DeletedAtUtc IS NULL
              AND ArchivedAtUtc IS NOT NULL;
            """;
        command.Parameters.AddWithValue("$accountName", accountName);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task<bool> PermanentlyDeleteCharacterAsync(SqliteConnection connection, string accountName, string characterName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM Characters
            WHERE Id = (
                SELECT c.Id
                FROM Characters c
                JOIN Accounts a ON a.Id = c.AccountId
                WHERE a.Name = $accountName
                  AND c.Name = $characterName
                  AND (c.ArchivedAtUtc IS NOT NULL OR c.DeletedAtUtc IS NOT NULL)
                LIMIT 1
            );
            """;
        command.Parameters.AddWithValue("$accountName", accountName);
        command.Parameters.AddWithValue("$characterName", characterName);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    internal static async Task<int> PermanentlyDeleteArchivedAccountAsync(SqliteConnection connection, string accountName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM Characters
            WHERE AccountId = (SELECT Id FROM Accounts WHERE Name = $accountName)
              AND (ArchivedAtUtc IS NOT NULL OR DeletedAtUtc IS NOT NULL);
            """;
        command.Parameters.AddWithValue("$accountName", accountName);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task<bool> SetAccountFavoriteAsync(SqliteConnection connection, string accountName, bool isFavorite, CancellationToken cancellationToken)
    {
        var account = await FindAccountFavoriteAsync(connection, accountName, cancellationToken);
        if (account is null)
            return false;

        var (_, currentRank) = account.Value;
        if (isFavorite)
        {
            var nextRank = currentRank is > 0 ? currentRank.Value : await NextFavoriteRankAsync(connection, cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Accounts SET IsFavorite = 1, FavoriteRank = $rank WHERE Name = $accountName;";
            command.Parameters.AddWithValue("$rank", nextRank);
            command.Parameters.AddWithValue("$accountName", accountName);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE Accounts SET IsFavorite = 0, FavoriteRank = NULL WHERE Name = $accountName;";
            command.Parameters.AddWithValue("$accountName", accountName);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (currentRank is > 0)
        {
            await using var reindex = connection.CreateCommand();
            reindex.CommandText = """
                UPDATE Accounts
                SET FavoriteRank = FavoriteRank - 1
                WHERE IsFavorite = 1
                  AND FavoriteRank IS NOT NULL
                  AND FavoriteRank > $removedRank;
                """;
            reindex.Parameters.AddWithValue("$removedRank", currentRank.Value);
            await reindex.ExecuteNonQueryAsync(cancellationToken);
        }

        return true;
    }

    internal static async Task NormalizeFavoriteRanksAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using (var clear = connection.CreateCommand())
        {
            clear.CommandText = "UPDATE Accounts SET FavoriteRank = NULL WHERE IsFavorite = 0;";
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }

        var favorites = new List<long>();
        await using (var read = connection.CreateCommand())
        {
            read.CommandText = """
                SELECT Id
                FROM Accounts
                WHERE IsFavorite = 1
                ORDER BY CASE WHEN FavoriteRank IS NULL THEN 1 ELSE 0 END, FavoriteRank, Name;
                """;
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                favorites.Add(reader.GetInt64(0));
        }

        for (var index = 0; index < favorites.Count; index++)
        {
            await using var update = connection.CreateCommand();
            update.CommandText = "UPDATE Accounts SET FavoriteRank = $rank WHERE Id = $id;";
            update.Parameters.AddWithValue("$rank", index + 1);
            update.Parameters.AddWithValue("$id", favorites[index]);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<(long AccountId, int? FavoriteRank)?> FindAccountFavoriteAsync(SqliteConnection connection, string accountName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, FavoriteRank FROM Accounts WHERE Name = $accountName LIMIT 1;";
        command.Parameters.AddWithValue("$accountName", accountName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var accountId = reader.GetInt64(0);
        var favoriteRank = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1);
        return (accountId, favoriteRank);
    }

    private static async Task<int> NextFavoriteRankAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(FavoriteRank), 0) + 1 FROM Accounts WHERE IsFavorite = 1;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<long?> FindActiveCharacterIdAsync(SqliteConnection connection, string accountName, string characterName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.Id
            FROM Characters c
            JOIN Accounts a ON a.Id = c.AccountId
            WHERE a.Name = $accountName
              AND c.Name = $characterName
              AND c.DeletedAtUtc IS NULL
              AND c.ArchivedAtUtc IS NULL
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$accountName", accountName);
        command.Parameters.AddWithValue("$characterName", characterName);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToInt64(result);
    }

    private static string? ClassNameFromId(int? classId)
    {
        string[] names = ["Amazon", "Sorceress", "Necromancer", "Paladin", "Barbarian", "Druid", "Assassin"];
        return classId is int id && id >= 0 && id < names.Length ? names[id] : null;
    }
}
