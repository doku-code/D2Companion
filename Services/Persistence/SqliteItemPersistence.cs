using System.Text.Json;
using D2CompanionMvc.Extensions.Styx.Ingestion;
using D2CompanionMvc.Extensions.Styx.Models;
using D2CompanionMvc.Services.Mapping;
using Microsoft.Data.Sqlite;

namespace D2CompanionMvc.Services.Persistence;

internal static class SqliteItemPersistence
{
    internal static async Task<long> InsertCanonicalItemAsync(SqliteConnection connection, long characterId, CanonicalCharacterPayload payload, CanonicalItemPayload entry, CancellationToken cancellationToken)
    {
        var record = entry.Item.ToItemRecord(entry.Tooltip, payload.Account, payload.Character, payload.Realm);
        // Build the unique gid the same way the legacy insert does because the
        // renderer's itemKey() relies on (sourceFile, gid, classid, location,
        // x, y, title) being stable across saves.
        var gid = string.IsNullOrWhiteSpace(record.Gid)
            ? $"{record.Classid}:{record.Location}:{record.X}:{record.Y}:{record.Title}"
            : record.Gid;

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Items (
                CharacterId, Gid, ClassId, Title, Description, Image, ItemColor, Storage, Location,
                X, Y, PixelWidth, PixelHeight, GridWidth, GridHeight, Ethereal, SourceFile,
                TooltipJson, RawSnapshotJson)
            VALUES (
                $characterId, $gid, $classId, $title, $description, $image, $itemColor, $storage, $location,
                $x, $y, $pixelWidth, $pixelHeight, $gridWidth, $gridHeight, $ethereal, $sourceFile,
                $tooltipJson, $rawSnapshotJson)
            RETURNING Id;
            """;
        command.Parameters.AddWithValue("$characterId", characterId);
        command.Parameters.AddWithValue("$gid", gid);
        command.Parameters.AddWithValue("$classId", record.Classid);
        command.Parameters.AddWithValue("$title", string.IsNullOrEmpty(record.Title) ? entry.Item.Code : record.Title);
        command.Parameters.AddWithValue("$description", (object?)record.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$image", string.IsNullOrEmpty(record.Image) ? "box" : record.Image);
        command.Parameters.AddWithValue("$itemColor", record.ItemColor);
        command.Parameters.AddWithValue("$storage", record.Storage);
        command.Parameters.AddWithValue("$location", record.Location);
        command.Parameters.AddWithValue("$x", record.X);
        command.Parameters.AddWithValue("$y", record.Y);
        command.Parameters.AddWithValue("$pixelWidth", Math.Max(1, record.GridWidth) * 28);
        command.Parameters.AddWithValue("$pixelHeight", Math.Max(1, record.GridHeight) * 28);
        command.Parameters.AddWithValue("$gridWidth", Math.Max(1, record.GridWidth));
        command.Parameters.AddWithValue("$gridHeight", Math.Max(1, record.GridHeight));
        command.Parameters.AddWithValue("$ethereal", record.Ethereal ? 1 : 0);
        command.Parameters.AddWithValue("$sourceFile", $"styx:{payload.SeenAt.UtcDateTime:O}");
        command.Parameters.AddWithValue("$tooltipJson", (object?)JsonSerializer.Serialize(entry.Tooltip) ?? DBNull.Value);
        command.Parameters.AddWithValue("$rawSnapshotJson", (object?)entry.RawSnapshotJson ?? DBNull.Value);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    internal static async Task InsertCanonicalSocketsAsync(SqliteConnection connection, long itemId, IReadOnlyList<string> sockets, CancellationToken cancellationToken)
    {
        for (var index = 0; index < sockets.Count; index++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO ItemSockets (ItemId, Position, Image) VALUES ($itemId, $position, $image);";
            command.Parameters.AddWithValue("$itemId", itemId);
            command.Parameters.AddWithValue("$position", index);
            command.Parameters.AddWithValue("$image", string.IsNullOrWhiteSpace(sockets[index]) ? "gemsocket" : sockets[index]);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    internal static async Task<long> InsertImportedItemAsync(SqliteConnection connection, long characterId, ImportedItemSnapshot item, CancellationToken cancellationToken)
    {
        var gid = string.IsNullOrWhiteSpace(item.Gid)
            ? $"{item.ClassId}:{item.Location}:{item.X}:{item.Y}:{item.Title}"
            : item.Gid;

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Items (
                CharacterId, Gid, ClassId, Title, Description, Image, ItemColor, Storage, Location,
                X, Y, PixelWidth, PixelHeight, GridWidth, GridHeight, Ethereal, SourceFile,
                RawSnapshotJson)
            VALUES (
                $characterId, $gid, $classId, $title, $description, $image, $itemColor, $storage, $location,
                $x, $y, $pixelWidth, $pixelHeight, $gridWidth, $gridHeight, $ethereal, $sourceFile,
                $rawSnapshotJson)
            RETURNING Id;
            """;
        command.Parameters.AddWithValue("$characterId", characterId);
        command.Parameters.AddWithValue("$gid", gid);
        command.Parameters.AddWithValue("$classId", item.ClassId);
        command.Parameters.AddWithValue("$title", string.IsNullOrWhiteSpace(item.Title) ? item.Image : item.Title);
        command.Parameters.AddWithValue("$description", string.IsNullOrWhiteSpace(item.Description) ? DBNull.Value : (object)item.Description);
        command.Parameters.AddWithValue("$image", string.IsNullOrWhiteSpace(item.Image) ? "box" : item.Image);
        command.Parameters.AddWithValue("$itemColor", item.ItemColor);
        command.Parameters.AddWithValue("$storage", string.IsNullOrWhiteSpace(item.Storage) ? "other" : item.Storage);
        command.Parameters.AddWithValue("$location", item.Location);
        command.Parameters.AddWithValue("$x", item.X);
        command.Parameters.AddWithValue("$y", item.Y);
        command.Parameters.AddWithValue("$pixelWidth", Math.Max(1, item.PixelWidth));
        command.Parameters.AddWithValue("$pixelHeight", Math.Max(1, item.PixelHeight));
        command.Parameters.AddWithValue("$gridWidth", Math.Max(1, item.GridWidth));
        command.Parameters.AddWithValue("$gridHeight", Math.Max(1, item.GridHeight));
        command.Parameters.AddWithValue("$ethereal", item.Ethereal ? 1 : 0);
        command.Parameters.AddWithValue("$sourceFile", string.IsNullOrWhiteSpace(item.SourceFile) ? "mulelogger" : item.SourceFile);
        command.Parameters.AddWithValue("$rawSnapshotJson", string.IsNullOrWhiteSpace(item.RawSnapshotJson) ? DBNull.Value : (object)item.RawSnapshotJson);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    internal static async Task InsertImportedSocketsAsync(SqliteConnection connection, long itemId, IReadOnlyList<string> sockets, CancellationToken cancellationToken)
    {
        for (var index = 0; index < sockets.Count; index++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO ItemSockets (ItemId, Position, Image) VALUES ($itemId, $position, $image);";
            command.Parameters.AddWithValue("$itemId", itemId);
            command.Parameters.AddWithValue("$position", index);
            command.Parameters.AddWithValue("$image", string.IsNullOrWhiteSpace(sockets[index]) ? "gemsocket" : sockets[index]);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    internal static async Task<long> InsertItemAsync(SqliteConnection connection, long characterId, StyxCharacterSnapshot snapshot, StyxItemSnapshot item, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Items (
                CharacterId, Gid, ClassId, Title, Description, Image, ItemColor, Storage, Location,
                X, Y, PixelWidth, PixelHeight, GridWidth, GridHeight, Ethereal, SourceFile)
            VALUES (
                $characterId, $gid, $classId, $title, $description, $image, $itemColor, $storage, $location,
                $x, $y, $pixelWidth, $pixelHeight, $gridWidth, $gridHeight, $ethereal, $sourceFile)
            RETURNING Id;
            """;
        command.Parameters.AddWithValue("$characterId", characterId);
        command.Parameters.AddWithValue("$gid", string.IsNullOrWhiteSpace(item.Gid) ? $"{item.ClassId}:{item.Location}:{item.X}:{item.Y}:{item.Title}" : item.Gid);
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
        command.Parameters.AddWithValue("$sourceFile", $"styx:{snapshot.SeenAt.UtcDateTime:O}");
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    internal static async Task InsertSocketsAsync(SqliteConnection connection, long itemId, StyxItemSnapshot item, CancellationToken cancellationToken)
    {
        for (var index = 0; index < item.Sockets.Count; index++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO ItemSockets (ItemId, Position, Image) VALUES ($itemId, $position, $image);";
            command.Parameters.AddWithValue("$itemId", itemId);
            command.Parameters.AddWithValue("$position", index);
            command.Parameters.AddWithValue("$image", string.IsNullOrWhiteSpace(item.Sockets[index]) ? "gemsocket" : item.Sockets[index]);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

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
}
