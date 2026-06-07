using System.Text.Json;
using D2CompanionMvc.Domain.Items;
using D2CompanionMvc.Models.Catalog;
using D2CompanionMvc.Services.GameData;
using Microsoft.Data.Sqlite;

namespace D2CompanionMvc.Services.Persistence;

internal static class SqliteCatalogReader
{
    internal static async Task<CompanionCatalog> ReadCatalogAsync(
        SqliteConnection connection,
        string databaseDisplayPath,
        D2ItemLookupService itemLookup,
        CancellationToken cancellationToken)
    {
        var catalog = new CompanionCatalog
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            SourceRoot = "sqlite-live-catalog",
            DatabasePath = databaseDisplayPath
        };

        var accountMap = new Dictionary<long, AccountSummary>();
        var archivedAccountMap = new Dictionary<long, AccountSummary>();
        var characterMap = new Dictionary<long, CharacterSummary>();
        var itemSockets = await LoadItemSocketsAsync(connection, cancellationToken);
        var observedItemSockets = await LoadObservedPlayerSocketsAsync(connection, cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT
                    a.Id AS AccountId,
                    a.Name AS AccountName,
                    a.Realm AS AccountRealm,
                    a.IsFavorite,
                    a.FavoriteRank,
                    a.LastSeenUtc AS AccountLastSeenUtc,
                    c.Id AS CharacterId,
                    c.Name AS CharacterName,
                    c.Realm,
                    c.Level,
                    c.ClassId,
                    c.ClassName,
                    c.MercenaryKind,
                    c.MercenaryType,
                    c.MercenaryAct,
                    c.MercenaryClassId,
                    c.MercenaryTypeSource,
                    c.Mode,
                    c.Hardcore,
                    c.Expansion,
                    c.Ladder,
                    c.LastSeenUtc AS CharacterLastSeenUtc,
                    c.ExpirationExpiresAtUtc,
                    c.ArchivedAtUtc,
                    c.DeletedAtUtc,
                    COUNT(i.Id) AS ItemCount
                FROM Accounts a
                JOIN Characters c ON c.AccountId = a.Id
                LEFT JOIN Items i ON i.CharacterId = c.Id
                WHERE c.DeletedAtUtc IS NULL
                  AND c.ArchivedAtUtc IS NULL
                GROUP BY a.Id, c.Id, c.Level, c.ClassId, c.ClassName, c.MercenaryKind, c.MercenaryType, c.MercenaryAct, c.MercenaryClassId, c.MercenaryTypeSource
                ORDER BY CASE WHEN a.FavoriteRank IS NULL THEN 1 ELSE 0 END, a.FavoriteRank, a.Name, c.Name;
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var accountId = reader.GetInt64(reader.GetOrdinal("AccountId"));
                var account = GetOrAddAccount(accountMap, accountId, reader);
                var characterId = reader.GetInt64(reader.GetOrdinal("CharacterId"));
                var character = new CharacterSummary
                {
                    Name = reader.GetString(reader.GetOrdinal("CharacterName")),
                    Account = account.Name,
                    Realm = ReadString(reader, "Realm"),
                    Level = ReadNullableInt(reader, "Level"),
                    ClassId = ReadNullableInt(reader, "ClassId"),
                    ClassName = ReadString(reader, "ClassName") ?? ClassNameFromId(ReadNullableInt(reader, "ClassId")),
                    MercenaryKind = ReadNullableInt(reader, "MercenaryKind"),
                    MercenaryType = ReadString(reader, "MercenaryType"),
                    MercenaryAct = ReadNullableInt(reader, "MercenaryAct"),
                    MercenaryClassId = ReadNullableInt(reader, "MercenaryClassId"),
                    MercenaryTypeSource = ReadString(reader, "MercenaryTypeSource"),
                    Mode = ReadString(reader, "Mode"),
                    Hardcore = ReadBool(reader, "Hardcore"),
                    Expansion = ReadBool(reader, "Expansion"),
                    Ladder = ReadBool(reader, "Ladder"),
                    ItemCount = reader.GetInt32(reader.GetOrdinal("ItemCount")),
                    StorageCounts = [],
                    LastSeenAt = ReadDateTimeOffset(reader, "CharacterLastSeenUtc"),
                    ExpiresAt = ReadDateTimeOffset(reader, "ExpirationExpiresAtUtc"),
                    ArchivedAt = ReadDateTimeOffset(reader, "ArchivedAtUtc"),
                    DeletedAt = ReadDateTimeOffset(reader, "DeletedAtUtc"),
                };

                account.Characters.Add(character);
                account.ItemCount += character.ItemCount;
                characterMap[characterId] = character;
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT
                    a.Id AS AccountId,
                    a.Name AS AccountName,
                    a.Realm AS AccountRealm,
                    a.IsFavorite,
                    a.FavoriteRank,
                    a.LastSeenUtc AS AccountLastSeenUtc,
                    c.Id AS CharacterId,
                    c.Name AS CharacterName,
                    c.Realm,
                    c.Level,
                    c.ClassId,
                    c.ClassName,
                    c.MercenaryKind,
                    c.MercenaryType,
                    c.MercenaryAct,
                    c.MercenaryClassId,
                    c.MercenaryTypeSource,
                    c.Mode,
                    c.Hardcore,
                    c.Expansion,
                    c.Ladder,
                    c.LastSeenUtc AS CharacterLastSeenUtc,
                    c.ExpirationExpiresAtUtc,
                    c.ArchivedAtUtc,
                    c.DeletedAtUtc,
                    COUNT(i.Id) AS ItemCount
                FROM Accounts a
                JOIN Characters c ON c.AccountId = a.Id
                LEFT JOIN Items i ON i.CharacterId = c.Id
                WHERE c.ArchivedAtUtc IS NOT NULL
                  AND c.DeletedAtUtc IS NULL
                GROUP BY a.Id, c.Id, c.Level, c.ClassId, c.ClassName, c.MercenaryKind, c.MercenaryType, c.MercenaryAct, c.MercenaryClassId, c.MercenaryTypeSource
                ORDER BY a.Name, c.Name;
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var accountId = reader.GetInt64(reader.GetOrdinal("AccountId"));
                var account = GetOrAddAccount(archivedAccountMap, accountId, reader);
                var character = new CharacterSummary
                {
                    Name = reader.GetString(reader.GetOrdinal("CharacterName")),
                    Account = account.Name,
                    Realm = ReadString(reader, "Realm"),
                    Level = ReadNullableInt(reader, "Level"),
                    ClassId = ReadNullableInt(reader, "ClassId"),
                    ClassName = ReadString(reader, "ClassName") ?? ClassNameFromId(ReadNullableInt(reader, "ClassId")),
                    MercenaryKind = ReadNullableInt(reader, "MercenaryKind"),
                    MercenaryType = ReadString(reader, "MercenaryType"),
                    MercenaryAct = ReadNullableInt(reader, "MercenaryAct"),
                    MercenaryClassId = ReadNullableInt(reader, "MercenaryClassId"),
                    MercenaryTypeSource = ReadString(reader, "MercenaryTypeSource"),
                    Mode = ReadString(reader, "Mode"),
                    Hardcore = ReadBool(reader, "Hardcore"),
                    Expansion = ReadBool(reader, "Expansion"),
                    Ladder = ReadBool(reader, "Ladder"),
                    ItemCount = reader.GetInt32(reader.GetOrdinal("ItemCount")),
                    StorageCounts = [],
                    LastSeenAt = ReadDateTimeOffset(reader, "CharacterLastSeenUtc"),
                    ExpiresAt = ReadDateTimeOffset(reader, "ExpirationExpiresAtUtc"),
                    ArchivedAt = ReadDateTimeOffset(reader, "ArchivedAtUtc"),
                    DeletedAt = ReadDateTimeOffset(reader, "DeletedAtUtc"),
                };

                account.Characters.Add(character);
                account.ItemCount += character.ItemCount;
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT
                    a.Name AS AccountName,
                    c.Name AS CharacterName,
                    i.CharacterId,
                    c.Realm,
                    c.Mode,
                    c.Hardcore,
                    c.Expansion,
                    c.Ladder,
                    i.Id,
                    i.Gid,
                    i.ClassId,
                    i.Title,
                    i.Description,
                    i.Image,
                    i.ItemColor,
                    i.Storage,
                    i.Location,
                    i.X,
                    i.Y,
                    i.PixelWidth,
                    i.PixelHeight,
                    i.GridWidth,
                    i.GridHeight,
                    i.Ethereal,
                    i.SourceFile,
                    i.RawSnapshotJson
                FROM Items i
                JOIN Characters c ON c.Id = i.CharacterId
                JOIN Accounts a ON a.Id = c.AccountId
                WHERE c.DeletedAtUtc IS NULL
                  AND c.ArchivedAtUtc IS NULL
                ORDER BY a.Name, c.Name, i.Storage, i.Y, i.X, i.Title;
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var itemId = reader.GetInt64(reader.GetOrdinal("Id"));
                var characterId = reader.GetInt64(reader.GetOrdinal("CharacterId"));
                itemSockets.TryGetValue(itemId, out var sockets);
                if (characterMap.TryGetValue(characterId, out var character)
                    && character.Level is null
                    && TryReadCharacterLevel(ReadString(reader, "RawSnapshotJson"), out var characterLevel))
                {
                    character.Level = characterLevel;
                }

                var rawSnapshotJson = ReadString(reader, "RawSnapshotJson");
                var item = new ItemRecord
                {
                    Account = reader.GetString(reader.GetOrdinal("AccountName")),
                    Character = reader.GetString(reader.GetOrdinal("CharacterName")),
                    Realm = ReadString(reader, "Realm"),
                    Mode = ReadString(reader, "Mode"),
                    Hardcore = ReadBool(reader, "Hardcore"),
                    Expansion = ReadBool(reader, "Expansion"),
                    Ladder = ReadBool(reader, "Ladder"),
                    Header = $"{reader.GetString(reader.GetOrdinal("AccountName"))} / {reader.GetString(reader.GetOrdinal("CharacterName"))}",
                    Gid = reader.GetString(reader.GetOrdinal("Gid")),
                    Classid = reader.GetInt32(reader.GetOrdinal("ClassId")),
                    Title = reader.GetString(reader.GetOrdinal("Title")),
                    Description = ReadString(reader, "Description"),
                    Image = reader.GetString(reader.GetOrdinal("Image")),
                    ItemColor = ResolveProjectedItemColor(reader.GetInt32(reader.GetOrdinal("ItemColor")), rawSnapshotJson, itemLookup),
                    Storage = reader.GetString(reader.GetOrdinal("Storage")),
                    Location = reader.GetInt32(reader.GetOrdinal("Location")),
                    X = reader.GetInt32(reader.GetOrdinal("X")),
                    Y = reader.GetInt32(reader.GetOrdinal("Y")),
                    Width = reader.GetInt32(reader.GetOrdinal("PixelWidth")),
                    Height = reader.GetInt32(reader.GetOrdinal("PixelHeight")),
                    GridWidth = reader.GetInt32(reader.GetOrdinal("GridWidth")),
                    GridHeight = reader.GetInt32(reader.GetOrdinal("GridHeight")),
                    Ethereal = ReadBool(reader, "Ethereal"),
                    SourceFile = reader.GetString(reader.GetOrdinal("SourceFile")),
                    Sockets = sockets ?? []
                };

                catalog.Items.Add(item);
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT i.CharacterId, i.Storage, COUNT(*) AS ItemCount
                FROM Items i
                JOIN Characters c ON c.Id = i.CharacterId
                WHERE c.DeletedAtUtc IS NULL
                  AND c.ArchivedAtUtc IS NULL
                GROUP BY i.CharacterId, i.Storage;
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var characterId = reader.GetInt64(reader.GetOrdinal("CharacterId"));
                if (!characterMap.TryGetValue(characterId, out var character))
                {
                    continue;
                }

                character.StorageCounts[reader.GetString(reader.GetOrdinal("Storage"))] = reader.GetInt32(reader.GetOrdinal("ItemCount"));
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT
                    op.Id AS ObservedPlayerId,
                    op.PlayerUid,
                    op.PlayerName,
                    op.AccountName AS ObservedAccountName,
                    op.ClassName AS ObservedClassName,
                    op.Level AS ObservedLevel,
                    op.GameName,
                    op.FirstSeenAtUtc,
                    op.ArchivedAtUtc,
                    op.SeenAtUtc,
                    op.SnapshotCount,
                    a.Name AS ObservedByAccount,
                    c.Name AS ObservedByCharacter,
                    c.Realm AS ObservedRealm,
                    opi.Id AS ItemId,
                    opi.Gid,
                    opi.ClassId,
                    opi.Title,
                    opi.Description,
                    opi.Image,
                    opi.ItemColor,
                    opi.Storage,
                    opi.Location,
                    opi.X,
                    opi.Y,
                    opi.PixelWidth,
                    opi.PixelHeight,
                    opi.GridWidth,
                    opi.GridHeight,
                    opi.Ethereal
                FROM ObservedPlayers op
                JOIN Characters c ON c.Id = op.ObservedByCharacterId
                JOIN Accounts a ON a.Id = c.AccountId
                LEFT JOIN ObservedPlayerItems opi ON opi.ObservedPlayerId = op.Id
                ORDER BY op.SeenAtUtc DESC, op.Id, opi.Location, opi.X;
                """;

            var observedMap = new Dictionary<string, ObservedPlayerRecord>(StringComparer.OrdinalIgnoreCase);
            var observedItemIndexes = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
            var observedRecordIds = new Dictionary<string, HashSet<long>>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var observedId = reader.GetInt64(reader.GetOrdinal("ObservedPlayerId"));
                var playerUid = reader.GetString(reader.GetOrdinal("PlayerUid"));
                var playerName = ReadString(reader, "PlayerName");
                var observedKey = SqliteObservedPersistence.ObservedPlayerMergeKey(playerUid, playerName);
                var archivedAt = ReadDateTimeOffset(reader, "ArchivedAtUtc");
                var isArchived = archivedAt is not null;
                var mapKey = (isArchived ? "archived:" : "active:") + observedKey;
                if (!observedMap.TryGetValue(mapKey, out var observedRecord))
                {
                    var seenAt = DateTimeOffset.TryParse(ReadString(reader, "SeenAtUtc"), out var parsedSeenAt) ? parsedSeenAt : DateTimeOffset.UtcNow;
                    var firstSeenAt = DateTimeOffset.TryParse(ReadString(reader, "FirstSeenAtUtc"), out var parsedFirstSeenAt) ? parsedFirstSeenAt : seenAt;
                    observedRecord = new ObservedPlayerRecord
                    {
                        ObservedKey = observedKey,
                        PlayerUid = playerUid,
                        PlayerName = playerName,
                        Realm = ReadString(reader, "ObservedRealm"),
                        ClassName = ReadString(reader, "ObservedClassName"),
                        Level = NormalizeLevel(ReadNullableInt(reader, "ObservedLevel")),
                        GameName = ReadString(reader, "GameName"),
                        FirstSeenAt = firstSeenAt,
                        SeenAt = seenAt,
                        ArchivedAt = archivedAt,
                        SnapshotCount = reader.GetInt32(reader.GetOrdinal("SnapshotCount")),
                        ObservedByAccount = reader.GetString(reader.GetOrdinal("ObservedByAccount")),
                        ObservedByCharacter = reader.GetString(reader.GetOrdinal("ObservedByCharacter")),
                    };
                    observedMap[mapKey] = observedRecord;
                    observedItemIndexes[mapKey] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    observedRecordIds[mapKey] = [observedId];
                    (isArchived ? catalog.ArchivedObservedPlayers : catalog.ObservedPlayers).Add(observedRecord);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(observedRecord.PlayerName))
                        observedRecord.PlayerName = playerName;
                    if (string.IsNullOrWhiteSpace(observedRecord.ClassName))
                        observedRecord.ClassName = ReadString(reader, "ObservedClassName");
                    if (observedRecord.Level is null)
                        observedRecord.Level = NormalizeLevel(ReadNullableInt(reader, "ObservedLevel"));
                    if (string.IsNullOrWhiteSpace(observedRecord.Realm))
                        observedRecord.Realm = ReadString(reader, "ObservedRealm");
                    if (string.IsNullOrWhiteSpace(observedRecord.GameName))
                        observedRecord.GameName = ReadString(reader, "GameName");

                    if (observedRecordIds[mapKey].Add(observedId))
                    {
                        if (DateTimeOffset.TryParse(ReadString(reader, "FirstSeenAtUtc"), out var firstSeenAt) &&
                            (observedRecord.FirstSeenAt is null || firstSeenAt < observedRecord.FirstSeenAt))
                            observedRecord.FirstSeenAt = firstSeenAt;

                        observedRecord.SnapshotCount += reader.GetInt32(reader.GetOrdinal("SnapshotCount"));
                    }
                }

                var ordinal = reader.GetOrdinal("ItemId");
                if (!reader.IsDBNull(ordinal))
                {
                    var itemId = reader.GetInt64(ordinal);
                    observedItemSockets.TryGetValue(itemId, out var sockets);
                    var item = new ItemRecord
                    {
                        Account = observedRecord.ObservedByAccount,
                        Character = observedRecord.PlayerName ?? observedRecord.PlayerUid,
                        Gid = reader.GetString(reader.GetOrdinal("Gid")),
                        Classid = reader.GetInt32(reader.GetOrdinal("ClassId")),
                        Title = reader.GetString(reader.GetOrdinal("Title")),
                        Description = ReadString(reader, "Description"),
                        Image = reader.GetString(reader.GetOrdinal("Image")),
                        ItemColor = reader.GetInt32(reader.GetOrdinal("ItemColor")),
                        Storage = reader.GetString(reader.GetOrdinal("Storage")),
                        Location = reader.GetInt32(reader.GetOrdinal("Location")),
                        X = reader.GetInt32(reader.GetOrdinal("X")),
                        Y = reader.GetInt32(reader.GetOrdinal("Y")),
                        Width = reader.GetInt32(reader.GetOrdinal("PixelWidth")),
                        Height = reader.GetInt32(reader.GetOrdinal("PixelHeight")),
                        GridWidth = reader.GetInt32(reader.GetOrdinal("GridWidth")),
                        GridHeight = reader.GetInt32(reader.GetOrdinal("GridHeight")),
                        Ethereal = ReadBool(reader, "Ethereal"),
                        Sockets = sockets ?? [],
                    };
                    var itemKey = ObservedItemMergeKey(item);
                    if (!observedItemIndexes[mapKey].TryGetValue(itemKey, out var existingIndex))
                    {
                        observedItemIndexes[mapKey][itemKey] = observedRecord.Items.Count;
                        observedRecord.Items.Add(item);
                    }
                    else if (ObservedItemDisplayScore(item) > ObservedItemDisplayScore(observedRecord.Items[existingIndex]))
                    {
                        observedRecord.Items[existingIndex] = item;
                    }
                }
            }
        }

        catalog.Accounts = accountMap.Values.ToList();
        catalog.ArchivedAccounts = archivedAccountMap.Values.ToList();
        catalog.Totals = new CatalogTotals
        {
            SourceFiles = 0,
            ImportedFiles = catalog.Accounts.Sum(account => account.Characters.Count),
            Items = catalog.Items.Count,
            Accounts = catalog.Accounts.Count,
            Characters = catalog.Accounts.Sum(account => account.Characters.Count)
        };

        return catalog;
    }

    private static async Task<Dictionary<long, List<string>>> LoadItemSocketsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var socketsByItem = new Dictionary<long, List<string>>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ItemId, Image FROM ItemSockets ORDER BY ItemId, Position;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var itemId = reader.GetInt64(0);
            if (!socketsByItem.TryGetValue(itemId, out var sockets))
            {
                sockets = [];
                socketsByItem[itemId] = sockets;
            }

            sockets.Add(reader.GetString(1));
        }

        return socketsByItem;
    }

    private static int ResolveProjectedItemColor(int storedItemColor, string? rawSnapshotJson, D2ItemLookupService itemLookup)
    {
        if (storedItemColor >= 0)
            return storedItemColor;
        if (!TryResolveRawMagicTransformColor(rawSnapshotJson, itemLookup, out var resolved))
            return storedItemColor;

        return resolved;
    }

    private static bool TryResolveRawMagicTransformColor(string? rawSnapshotJson, D2ItemLookupService itemLookup, out int itemColor)
    {
        itemColor = -1;
        if (string.IsNullOrWhiteSpace(rawSnapshotJson))
            return false;

        try
        {
            using var document = JsonDocument.Parse(rawSnapshotJson);
            var root = document.RootElement;
            var quality = ReadJsonInt(root, "Quality") ?? ReadJsonInt(root, "quality");
            if (quality != 4)
                return false;

            var magicPrefixId =
                ReadJsonInt(root, "MagicPrefixId") ??
                ReadJsonInt(root, "magicPrefixId") ??
                FirstNonNegativeJsonInt(root, "MagicPrefixes") ??
                FirstNonNegativeJsonInt(root, "magicPrefixes") ??
                -1;
            var magicSuffixId =
                ReadJsonInt(root, "MagicSuffixId") ??
                ReadJsonInt(root, "magicSuffixId") ??
                FirstNonNegativeJsonInt(root, "MagicSuffixes") ??
                FirstNonNegativeJsonInt(root, "magicSuffixes") ??
                -1;

            var resolved = itemLookup.ResolveInventoryTransformColor(quality.Value, -1, -1, magicPrefixId, magicSuffixId);
            if (resolved is not int color)
                return false;

            itemColor = color;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static int? ReadJsonInt(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Number)
            return null;

        return value.TryGetInt32(out var parsed) ? parsed : null;
    }

    private static int? FirstNonNegativeJsonInt(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var parsed) && parsed >= 0)
                return parsed;
        }

        return null;
    }

    private static AccountSummary GetOrAddAccount(Dictionary<long, AccountSummary> accountMap, long accountId, SqliteDataReader reader)
    {
        if (accountMap.TryGetValue(accountId, out var account))
        {
            return account;
        }

        account = new AccountSummary
        {
            Name = reader.GetString(reader.GetOrdinal("AccountName")),
            Realm = ReadString(reader, "AccountRealm"),
            IsFavorite = ReadBool(reader, "IsFavorite"),
            FavoriteRank = ReadNullableInt(reader, "FavoriteRank"),
            LastSeen = ReadDateTimeOffset(reader, "AccountLastSeenUtc")
        };
        accountMap[accountId] = account;
        return account;
    }

    private static string? ClassNameFromId(int? classId)
    {
        string[] names = ["Amazon", "Sorceress", "Necromancer", "Paladin", "Barbarian", "Druid", "Assassin"];
        return classId is int id && id >= 0 && id < names.Length ? names[id] : null;
    }

    private static async Task<Dictionary<long, List<string>>> LoadObservedPlayerSocketsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var socketsByItem = new Dictionary<long, List<string>>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ObservedPlayerItemId, Image FROM ObservedPlayerItemSockets ORDER BY ObservedPlayerItemId, Position;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var itemId = reader.GetInt64(0);
            if (!socketsByItem.TryGetValue(itemId, out var sockets))
            {
                sockets = [];
                socketsByItem[itemId] = sockets;
            }

            sockets.Add(reader.GetString(1));
        }

        return socketsByItem;
    }

    private static string ObservedItemMergeKey(ItemRecord item)
    {
        if (string.Equals(item.Storage, "equipped", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Storage, "mercenary", StringComparison.OrdinalIgnoreCase))
            return $"{item.Storage}:{item.Location}:{item.X}:{item.Y}";

        return string.IsNullOrWhiteSpace(item.Gid)
            ? $"{item.Storage}:{item.Classid}:{item.X}:{item.Y}:{item.Title}"
            : $"gid:{item.Gid}";
    }

    private static int ObservedItemDisplayScore(ItemRecord item)
    {
        var lineCount = string.IsNullOrWhiteSpace(item.Description)
            ? 0
            : item.Description.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        return lineCount * 100
               + (item.Description?.Length ?? 0)
               + (item.ItemColor >= 0 ? 20 : 0)
               + item.Sockets.Count * 10;
    }

    private static bool TryReadCharacterLevel(string? rawSnapshotJson, out int level)
    {
        level = 0;
        if (string.IsNullOrWhiteSpace(rawSnapshotJson))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(rawSnapshotJson);
            if (TryGetIntProperty(doc.RootElement, "CharacterLevel", out level)
                || TryGetIntProperty(doc.RootElement, "characterLevel", out level))
            {
                return level > 0;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private static bool TryGetIntProperty(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var prop))
            return false;

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out value))
            return true;

        return prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out value);
    }

    private static string? ReadString(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int? ReadNullableInt(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static int? NormalizeLevel(int? level)
        => level is >= 1 and <= 99 ? level : null;

    private static bool ReadBool(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return !reader.IsDBNull(ordinal) && reader.GetInt32(ordinal) == 1;
    }

    private static DateTimeOffset? ReadDateTimeOffset(SqliteDataReader reader, string name)
    {
        var value = ReadString(reader, name);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }
}
