using D2CompanionMvc.Domain.Items;
using D2CompanionMvc.Models.Catalog;

namespace D2CompanionMvc.Services.Mapping;

/// <summary>
/// Projects the internal <see cref="CanonicalD2Item"/> + rendered
/// <see cref="ItemTooltip"/> down to the persistence-shape
/// <see cref="ItemRecord"/> the catalog API serves to the front-end.
///
/// This keeps the canonical model decoupled from the wire shape, which means
/// we can grow new fields on <see cref="CanonicalD2Item"/> (set bonuses,
/// charge counters, computed display strings) without breaking any existing
/// front-end code. The mapper picks just what the renderer needs today.
/// </summary>
public static class CanonicalToItemRecordMapper
{
    public static ItemRecord ToItemRecord(this CanonicalD2Item item, ItemTooltip tooltip, string account, string character, string? realm)
    {
        return new ItemRecord
        {
            Gid = item.Gid,
            Classid = item.ClassId,
            Title = item.Title,
            Description = tooltip.ToDescriptionString(),
            Image = item.ImageKey,
            ItemColor = item.ColorIndex,
            Sockets = item.SocketFillers.ToList(),
            Storage = StorageString(item.Storage),
            Location = item.Location,
            X = item.X,
            Y = item.Y,
            Width = item.PixelWidth,
            Height = item.PixelHeight,
            GridWidth = item.GridWidth,
            GridHeight = item.GridHeight,
            Ethereal = item.Ethereal,
            SourceFile = item.SourceFile,
            Account = account,
            Character = character,
            Realm = realm,
        };
    }

    private static string StorageString(ItemStorageBucket bucket) => bucket switch
    {
        ItemStorageBucket.Equipped  => "equipped",
        ItemStorageBucket.Inventory => "inventory",
        ItemStorageBucket.Stash     => "stash",
        ItemStorageBucket.Cube      => "cube",
        ItemStorageBucket.Mercenary => "mercenary",
        ItemStorageBucket.Other     => "other",
        _                            => "unknown",
    };
}
