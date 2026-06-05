using D2CompanionMvc.Domain.Items;
using D2CompanionMvc.Extensions.Styx.Ingestion;
using D2CompanionMvc.Extensions.Styx.Models;
using D2CompanionMvc.Services.Mapping;
using Microsoft.Data.Sqlite;

namespace D2CompanionMvc.Services.Persistence;

internal static class SqliteObservedPersistence
{
    internal static IReadOnlyList<StyxItemSnapshot> SelectObservedDisplayItems(IReadOnlyList<StyxItemSnapshot> items)
        => SelectObservedDisplayItems(items, ObservedDisplayKey, ObservedDisplayScore);

    internal static IReadOnlyList<CanonicalItemPayload> SelectObservedDisplayItems(IReadOnlyList<CanonicalItemPayload> items)
        => SelectObservedDisplayItems(items, ObservedDisplayKey, ObservedDisplayScore);

    private static IReadOnlyList<T> SelectObservedDisplayItems<T>(
        IReadOnlyList<T> items,
        Func<T, string> keySelector,
        Func<T, int> scoreSelector)
    {
        var selected = new List<T>();
        var indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var key = keySelector(item);
            if (!indexes.TryGetValue(key, out var index))
            {
                indexes[key] = selected.Count;
                selected.Add(item);
                continue;
            }

            if (scoreSelector(item) > scoreSelector(selected[index]))
            {
                selected[index] = item;
            }
        }

        return selected;
    }

    internal static Task<long> UpsertObservedPlayerAsync(SqliteConnection connection, long characterId, StyxCharacterSnapshot snapshot, StyxObservedPlayerSnapshot observed, CancellationToken cancellationToken)
        => UpsertObservedPlayerAsync(connection, characterId, snapshot, observed.PlayerUid, observed.PlayerName, observed.AccountName, observed.ClassId, string.IsNullOrWhiteSpace(observed.ClassName) ? ClassNameFromId(observed.ClassId) : observed.ClassName, observed.Level, cancellationToken);

    internal static Task<long> UpsertObservedPlayerAsync(SqliteConnection connection, long characterId, StyxCharacterSnapshot snapshot, CanonicalObservedPlayerPayload observed, CancellationToken cancellationToken)
        => UpsertObservedPlayerAsync(connection, characterId, snapshot, observed.PlayerUid, observed.PlayerName, observed.AccountName, observed.ClassId, observed.ClassName, observed.Level, cancellationToken);

    internal static async Task<long> UpsertObservedPlayerAsync(SqliteConnection connection, long characterId, StyxCharacterSnapshot snapshot, string playerUid, string? playerName, string? accountName, int? classId, string? className, int? level, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ObservedPlayers (ObservedByCharacterId, PlayerUid, PlayerName, AccountName, ClassId, ClassName, Level, GameName, FirstSeenAtUtc, SeenAtUtc, SnapshotCount)
            VALUES ($characterId, $playerUid, $playerName, $accountName, $classId, $className, $level, $gameName, $seenAtUtc, $seenAtUtc, 1)
            ON CONFLICT(ObservedByCharacterId, PlayerUid) DO UPDATE SET
                PlayerName = COALESCE(excluded.PlayerName, ObservedPlayers.PlayerName),
                AccountName = COALESCE(excluded.AccountName, ObservedPlayers.AccountName),
                ClassId = COALESCE(excluded.ClassId, ObservedPlayers.ClassId),
                ClassName = COALESCE(excluded.ClassName, ObservedPlayers.ClassName),
                Level = COALESCE(excluded.Level, ObservedPlayers.Level),
                GameName = excluded.GameName,
                ArchivedAtUtc = NULL,
                FirstSeenAtUtc = COALESCE(ObservedPlayers.FirstSeenAtUtc, ObservedPlayers.SeenAtUtc, excluded.FirstSeenAtUtc),
                SeenAtUtc = excluded.SeenAtUtc,
                SnapshotCount = ObservedPlayers.SnapshotCount + 1
            RETURNING Id;
            """;
        command.Parameters.AddWithValue("$characterId", characterId);
        command.Parameters.AddWithValue("$playerUid", string.IsNullOrWhiteSpace(playerUid) ? "unknown" : playerUid);
        command.Parameters.AddWithValue("$playerName", string.IsNullOrWhiteSpace(playerName) ? DBNull.Value : (object)playerName);
        command.Parameters.AddWithValue("$accountName", string.IsNullOrWhiteSpace(accountName) ? DBNull.Value : (object)accountName);
        command.Parameters.AddWithValue("$classId", classId is null ? DBNull.Value : (object)classId.Value);
        command.Parameters.AddWithValue("$className", string.IsNullOrWhiteSpace(className) ? DBNull.Value : (object)className);
        var normalizedLevel = NormalizeLevel(level);
        command.Parameters.AddWithValue("$level", normalizedLevel is null ? DBNull.Value : (object)normalizedLevel.Value);
        command.Parameters.AddWithValue("$gameName", (object?)snapshot.GameName ?? DBNull.Value);
        command.Parameters.AddWithValue("$seenAtUtc", snapshot.SeenAt.UtcDateTime.ToString("O"));
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    internal static async Task DeleteObservedPlayerItemsAsync(SqliteConnection connection, long observedPlayerId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ObservedPlayerItems WHERE ObservedPlayerId = $observedPlayerId;";
        command.Parameters.AddWithValue("$observedPlayerId", observedPlayerId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task<long> InsertObservedPlayerItemAsync(SqliteConnection connection, long observedPlayerId, StyxItemSnapshot item, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ObservedPlayerItems (
                ObservedPlayerId, Gid, ClassId, Title, Description, Image, ItemColor, Storage,
                Location, X, Y, PixelWidth, PixelHeight, GridWidth, GridHeight, Ethereal)
            VALUES (
                $observedPlayerId, $gid, $classId, $title, $description, $image, $itemColor, $storage,
                $location, $x, $y, $pixelWidth, $pixelHeight, $gridWidth, $gridHeight, $ethereal)
            RETURNING Id;
            """;
        command.Parameters.AddWithValue("$observedPlayerId", observedPlayerId);
        command.Parameters.AddWithValue("$gid", string.IsNullOrWhiteSpace(item.Gid) ? $"{item.ClassId}:{item.Location}:{item.X}" : item.Gid);
        command.Parameters.AddWithValue("$classId", item.ClassId);
        command.Parameters.AddWithValue("$title", string.IsNullOrWhiteSpace(item.Title) ? item.Code : item.Title);
        command.Parameters.AddWithValue("$description", (object?)item.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$image", string.IsNullOrWhiteSpace(item.Code) ? "box" : item.Code);
        command.Parameters.AddWithValue("$itemColor", item.ItemColor);
        command.Parameters.AddWithValue("$storage", NormalizeStorage(item));
        command.Parameters.AddWithValue("$location", item.Location);
        command.Parameters.AddWithValue("$x", item.X);
        command.Parameters.AddWithValue("$y", item.Y);
        command.Parameters.AddWithValue("$pixelWidth", Math.Max(1, item.GridWidth) * 28);
        command.Parameters.AddWithValue("$pixelHeight", Math.Max(1, item.GridHeight) * 28);
        command.Parameters.AddWithValue("$gridWidth", Math.Max(1, item.GridWidth));
        command.Parameters.AddWithValue("$gridHeight", Math.Max(1, item.GridHeight));
        command.Parameters.AddWithValue("$ethereal", item.Ethereal ? 1 : 0);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    internal static async Task<long> InsertObservedPlayerItemAsync(SqliteConnection connection, long observedPlayerId, CanonicalItemPayload entry, CancellationToken cancellationToken)
    {
        var item = entry.Item;
        var record = item.ToItemRecord(entry.Tooltip, string.Empty, string.Empty, null);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ObservedPlayerItems (
                ObservedPlayerId, Gid, ClassId, Title, Description, Image, ItemColor, Storage,
                Location, X, Y, PixelWidth, PixelHeight, GridWidth, GridHeight, Ethereal)
            VALUES (
                $observedPlayerId, $gid, $classId, $title, $description, $image, $itemColor, $storage,
                $location, $x, $y, $pixelWidth, $pixelHeight, $gridWidth, $gridHeight, $ethereal)
            RETURNING Id;
            """;
        command.Parameters.AddWithValue("$observedPlayerId", observedPlayerId);
        command.Parameters.AddWithValue("$gid", string.IsNullOrWhiteSpace(record.Gid) ? $"{record.Classid}:{record.Location}:{record.X}" : record.Gid);
        command.Parameters.AddWithValue("$classId", record.Classid);
        command.Parameters.AddWithValue("$title", string.IsNullOrWhiteSpace(record.Title) ? item.Code : record.Title);
        command.Parameters.AddWithValue("$description", (object?)record.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$image", string.IsNullOrWhiteSpace(record.Image) ? "box" : record.Image);
        command.Parameters.AddWithValue("$itemColor", record.ItemColor);
        command.Parameters.AddWithValue("$storage", string.IsNullOrWhiteSpace(record.Storage) ? "equipped" : record.Storage);
        command.Parameters.AddWithValue("$location", record.Location);
        command.Parameters.AddWithValue("$x", record.X);
        command.Parameters.AddWithValue("$y", record.Y);
        command.Parameters.AddWithValue("$pixelWidth", Math.Max(1, record.Width));
        command.Parameters.AddWithValue("$pixelHeight", Math.Max(1, record.Height));
        command.Parameters.AddWithValue("$gridWidth", Math.Max(1, record.GridWidth));
        command.Parameters.AddWithValue("$gridHeight", Math.Max(1, record.GridHeight));
        command.Parameters.AddWithValue("$ethereal", record.Ethereal ? 1 : 0);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    internal static async Task InsertObservedPlayerSocketsAsync(SqliteConnection connection, long observedItemId, StyxItemSnapshot item, CancellationToken cancellationToken)
        => await InsertObservedPlayerSocketsAsync(connection, observedItemId, item.Sockets, cancellationToken);

    internal static async Task InsertObservedPlayerSocketsAsync(SqliteConnection connection, long observedItemId, IReadOnlyList<string> sockets, CancellationToken cancellationToken)
    {
        for (var index = 0; index < sockets.Count; index++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO ObservedPlayerItemSockets (ObservedPlayerItemId, Position, Image) VALUES ($itemId, $position, $image);";
            command.Parameters.AddWithValue("$itemId", observedItemId);
            command.Parameters.AddWithValue("$position", index);
            command.Parameters.AddWithValue("$image", string.IsNullOrWhiteSpace(sockets[index]) ? "gemsocket" : sockets[index]);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    internal static async Task<int> DeleteObservedPlayerAsync(SqliteConnection connection, string observedKey, CancellationToken cancellationToken)
        => await UpdateObservedPlayerArchiveStateAsync(connection, observedKey, deleteRows: true, cancellationToken);

    internal static async Task<int> ArchiveObservedPlayerAsync(SqliteConnection connection, string observedKey, CancellationToken cancellationToken)
        => await UpdateObservedPlayerArchiveStateAsync(connection, observedKey, deleteRows: false, cancellationToken);

    internal static async Task<int> RestoreObservedPlayerAsync(SqliteConnection connection, string observedKey, CancellationToken cancellationToken)
        => await UpdateObservedPlayerRestoreStateAsync(connection, observedKey, cancellationToken);

    private static async Task<int> UpdateObservedPlayerArchiveStateAsync(SqliteConnection connection, string observedKey, bool deleteRows, CancellationToken cancellationToken)
    {
        var observedIds = new List<long>();
        await using (var select = connection.CreateCommand())
        {
            select.CommandText = deleteRows
                ? "SELECT Id, PlayerUid, PlayerName FROM ObservedPlayers;"
                : "SELECT Id, PlayerUid, PlayerName FROM ObservedPlayers WHERE ArchivedAtUtc IS NULL;";
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var playerUid = reader.GetString(reader.GetOrdinal("PlayerUid"));
                var playerName = ReadString(reader, "PlayerName");
                if (string.Equals(ObservedPlayerMergeKey(playerUid, playerName), observedKey, StringComparison.OrdinalIgnoreCase))
                {
                    observedIds.Add(reader.GetInt64(reader.GetOrdinal("Id")));
                }
            }
        }

        foreach (var observedId in observedIds)
        {
            await using var command = connection.CreateCommand();
            if (deleteRows)
            {
                command.CommandText = "DELETE FROM ObservedPlayers WHERE Id = $observedId;";
            }
            else
            {
                command.CommandText = "UPDATE ObservedPlayers SET ArchivedAtUtc = $archivedAtUtc WHERE Id = $observedId;";
                command.Parameters.AddWithValue("$archivedAtUtc", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
            }
            command.Parameters.AddWithValue("$observedId", observedId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return observedIds.Count;
    }

    private static async Task<int> UpdateObservedPlayerRestoreStateAsync(SqliteConnection connection, string observedKey, CancellationToken cancellationToken)
    {
        var observedIds = new List<long>();
        await using (var select = connection.CreateCommand())
        {
            select.CommandText = "SELECT Id, PlayerUid, PlayerName FROM ObservedPlayers WHERE ArchivedAtUtc IS NOT NULL;";
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var playerUid = reader.GetString(reader.GetOrdinal("PlayerUid"));
                var playerName = ReadString(reader, "PlayerName");
                if (string.Equals(ObservedPlayerMergeKey(playerUid, playerName), observedKey, StringComparison.OrdinalIgnoreCase))
                {
                    observedIds.Add(reader.GetInt64(reader.GetOrdinal("Id")));
                }
            }
        }

        foreach (var observedId in observedIds)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE ObservedPlayers SET ArchivedAtUtc = NULL WHERE Id = $observedId;";
            command.Parameters.AddWithValue("$observedId", observedId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return observedIds.Count;
    }

    internal static string ObservedPlayerMergeKey(string playerUid, string? playerName)
    {
        var normalizedName = NormalizeObservedPlayerName(playerName);
        if (!string.IsNullOrWhiteSpace(normalizedName))
            return $"name:{normalizedName}";

        return $"uid:{(string.IsNullOrWhiteSpace(playerUid) ? "unknown" : playerUid.Trim())}";
    }

    private static string NormalizeObservedPlayerName(string? playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return string.Empty;

        var normalized = new string(playerName
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());

        return normalized.StartsWith("unknownplayer", StringComparison.Ordinal)
            ? string.Empty
            : normalized;
    }

    private static string? ClassNameFromId(int? classId)
    {
        string[] names = ["Amazon", "Sorceress", "Necromancer", "Paladin", "Barbarian", "Druid", "Assassin"];
        return classId is int id && id >= 0 && id < names.Length ? names[id] : null;
    }

    private static int? NormalizeLevel(int? level)
        => level is >= 1 and <= 99 ? level : null;

    private static string NormalizeStorage(StyxItemSnapshot item)
    {
        if (!string.IsNullOrWhiteSpace(item.Storage))
        {
            return item.Storage;
        }

        return item.Location switch
        {
            1 => "equipped",
            3 => "inventory",
            6 => "cube",
            7 => "stash",
            _ => "other"
        };
    }

    private static string ObservedDisplayKey(StyxItemSnapshot item)
    {
        var storage = NormalizeStorage(item);
        if (string.Equals(storage, "equipped", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(storage, "mercenary", StringComparison.OrdinalIgnoreCase))
        {
            return $"{storage}:{item.Location}:{item.X}:{item.Y}";
        }

        return string.IsNullOrWhiteSpace(item.Gid)
            ? $"{storage}:{item.ClassId}:{item.X}:{item.Y}:{item.Title}"
            : $"gid:{item.Gid}";
    }

    private static string ObservedDisplayKey(CanonicalItemPayload entry)
    {
        var item = entry.Item;
        var storage = StorageString(item.Storage);
        if (string.Equals(storage, "equipped", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(storage, "mercenary", StringComparison.OrdinalIgnoreCase))
        {
            return $"{storage}:{item.Location}:{item.X}:{item.Y}";
        }

        return string.IsNullOrWhiteSpace(item.Gid)
            ? $"{storage}:{item.ClassId}:{item.X}:{item.Y}:{item.Title}"
            : $"gid:{item.Gid}";
    }

    private static int ObservedDisplayScore(StyxItemSnapshot item)
    {
        var lineCount = string.IsNullOrWhiteSpace(item.Description)
            ? 0
            : item.Description.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        return lineCount * 100
               + (item.Description?.Length ?? 0)
               + (item.ItemColor >= 0 ? 20 : 0)
               + item.Sockets.Count * 10
               + (!string.IsNullOrWhiteSpace(item.Title) && !string.Equals(item.Title, item.Code, StringComparison.OrdinalIgnoreCase) ? 5 : 0);
    }

    private static int ObservedDisplayScore(CanonicalItemPayload entry)
    {
        var item = entry.Item;
        return entry.Tooltip.Lines.Count * 100
               + item.RawStats.Count * 10
               + item.SocketFillers.Count * 10
               + (item.ColorIndex >= 0 ? 20 : 0)
               + (!string.IsNullOrWhiteSpace(item.Title) && !string.Equals(item.Title, item.BaseName, StringComparison.OrdinalIgnoreCase) ? 5 : 0);
    }

    private static string StorageString(ItemStorageBucket bucket) => bucket switch
    {
        ItemStorageBucket.Equipped => "equipped",
        ItemStorageBucket.Inventory => "inventory",
        ItemStorageBucket.Stash => "stash",
        ItemStorageBucket.Cube => "cube",
        ItemStorageBucket.Mercenary => "mercenary",
        ItemStorageBucket.Other => "other",
        _ => "unknown",
    };

    private static string? ReadString(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}
