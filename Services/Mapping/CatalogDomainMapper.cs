using D2CompanionMvc.Domain;
using D2CompanionMvc.Models.Catalog;

namespace D2CompanionMvc.Services.Mapping;

public static class CatalogDomainMapper
{
    public static CompanionArchive ToDomain(this CompanionCatalog catalog)
    {
        return new CompanionArchive
        {
            GeneratedAt = catalog.GeneratedAt,
            Totals = new ArchiveTotals
            {
                SourceFiles = catalog.Totals.SourceFiles,
                ImportedFiles = catalog.Totals.ImportedFiles,
                Items = catalog.Totals.Items,
                Accounts = catalog.Totals.Accounts,
                Characters = catalog.Totals.Characters
            },
            Accounts = catalog.Accounts.Select(ToDomain).ToList(),
            Items = catalog.Items.Select(ToDomain).ToList()
        };
    }

    private static GameAccount ToDomain(AccountSummary account)
    {
        return new GameAccount
        {
            Name = account.Name,
            Characters = account.Characters.Select(ToDomain).ToList(),
            ItemCount = account.ItemCount,
            LastSeen = account.LastSeen
        };
    }

    private static GameCharacter ToDomain(CharacterSummary character)
    {
        return new GameCharacter
        {
            Name = character.Name,
            Account = character.Account,
            Mode = character.Mode,
            Hardcore = character.Hardcore,
            Expansion = character.Expansion,
            Ladder = character.Ladder,
            ItemCount = character.ItemCount,
            StorageCounts = new Dictionary<string, int>(character.StorageCounts)
        };
    }

    private static CharacterItem ToDomain(ItemRecord item)
    {
        return new CharacterItem
        {
            ItemColor = item.ItemColor,
            Image = item.Image,
            Title = item.Title,
            Description = item.Description,
            Sockets = item.Sockets,
            Account = item.Account,
            Character = item.Character,
            SourceFile = item.SourceFile,
            Realm = item.Realm,
            Mode = item.Mode,
            Hardcore = item.Hardcore,
            Expansion = item.Expansion,
            Ladder = item.Ladder,
            Gid = item.Gid,
            ClassId = item.Classid,
            Location = item.Location,
            Storage = ParseStorage(item.Storage),
            Position = new ItemGridPosition { X = item.X, Y = item.Y },
            Size = new ItemGridSize
            {
                PixelWidth = item.Width,
                PixelHeight = item.Height,
                GridWidth = item.GridWidth,
                GridHeight = item.GridHeight
            },
            Ethereal = item.Ethereal
        };
    }

    private static ItemStorageLocation ParseStorage(string? storage)
    {
        return storage?.ToLowerInvariant() switch
        {
            "equipped" => ItemStorageLocation.Equipped,
            "inventory" => ItemStorageLocation.Inventory,
            "stash" => ItemStorageLocation.Stash,
            "cube" => ItemStorageLocation.Cube,
            "other" => ItemStorageLocation.Other,
            _ => ItemStorageLocation.Unknown
        };
    }
}
