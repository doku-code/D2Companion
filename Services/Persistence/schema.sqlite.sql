CREATE TABLE IF NOT EXISTS Accounts (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    Realm TEXT NOT NULL DEFAULT '',
    LastSeenUtc TEXT NULL,
    IsFavorite INTEGER NOT NULL DEFAULT 0,
    FavoriteRank INTEGER NULL,
    UNIQUE(Realm, Name)
);

CREATE TABLE IF NOT EXISTS Characters (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    AccountId INTEGER NOT NULL,
    Name TEXT NOT NULL,
    Realm TEXT NULL,
    Level INTEGER NULL,
    ClassId INTEGER NULL,
    ClassName TEXT NULL,
    MercenaryKind INTEGER NULL,
    MercenaryType TEXT NULL,
    MercenaryAct INTEGER NULL,
    MercenaryClassId INTEGER NULL,
    MercenaryTypeSource TEXT NULL,
    Mode TEXT NULL,
    Hardcore INTEGER NOT NULL DEFAULT 0,
    Expansion INTEGER NOT NULL DEFAULT 1,
    Ladder INTEGER NOT NULL DEFAULT 0,
    LastSeenUtc TEXT NULL,
    ArchivedAtUtc TEXT NULL,
    DeletedAtUtc TEXT NULL,
    UNIQUE(AccountId, Name),
    FOREIGN KEY(AccountId) REFERENCES Accounts(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS Items (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CharacterId INTEGER NOT NULL,
    Gid TEXT NOT NULL,
    ClassId INTEGER NOT NULL,
    Title TEXT NOT NULL,
    Description TEXT NULL,
    Image TEXT NOT NULL,
    ItemColor INTEGER NOT NULL DEFAULT -1,
    Storage TEXT NOT NULL,
    Location INTEGER NOT NULL,
    X INTEGER NOT NULL,
    Y INTEGER NOT NULL,
    PixelWidth INTEGER NOT NULL,
    PixelHeight INTEGER NOT NULL,
    GridWidth INTEGER NOT NULL,
    GridHeight INTEGER NOT NULL,
    Ethereal INTEGER NOT NULL DEFAULT 0,
    SourceFile TEXT NOT NULL,
    UNIQUE(CharacterId, Gid, ClassId, Location, X, Y),
    FOREIGN KEY(CharacterId) REFERENCES Characters(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS ItemSockets (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemId INTEGER NOT NULL,
    Position INTEGER NOT NULL,
    Image TEXT NOT NULL,
    FOREIGN KEY(ItemId) REFERENCES Items(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS CharacterSessions (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CharacterId INTEGER NOT NULL,
    SeenAtUtc TEXT NOT NULL,
    Source TEXT NOT NULL,
    FOREIGN KEY(CharacterId) REFERENCES Characters(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS ObservedPlayers (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ObservedByCharacterId INTEGER NOT NULL,
    PlayerUid TEXT NOT NULL,
    PlayerName TEXT NULL,
    AccountName TEXT NULL,
    ClassId INTEGER NULL,
    ClassName TEXT NULL,
    Level INTEGER NULL,
    GameName TEXT NULL,
    FirstSeenAtUtc TEXT NULL,
    ArchivedAtUtc TEXT NULL,
    SeenAtUtc TEXT NOT NULL,
    SnapshotCount INTEGER NOT NULL DEFAULT 1,
    UNIQUE(ObservedByCharacterId, PlayerUid),
    FOREIGN KEY(ObservedByCharacterId) REFERENCES Characters(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS ObservedPlayerItems (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ObservedPlayerId INTEGER NOT NULL,
    Gid TEXT NOT NULL,
    ClassId INTEGER NOT NULL,
    Title TEXT NOT NULL,
    Description TEXT NULL,
    Image TEXT NOT NULL,
    ItemColor INTEGER NOT NULL DEFAULT -1,
    Storage TEXT NOT NULL DEFAULT 'equipped',
    Location INTEGER NOT NULL,
    X INTEGER NOT NULL,
    Y INTEGER NOT NULL,
    PixelWidth INTEGER NOT NULL,
    PixelHeight INTEGER NOT NULL,
    GridWidth INTEGER NOT NULL,
    GridHeight INTEGER NOT NULL,
    Ethereal INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY(ObservedPlayerId) REFERENCES ObservedPlayers(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS ObservedPlayerItemSockets (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ObservedPlayerItemId INTEGER NOT NULL,
    Position INTEGER NOT NULL,
    Image TEXT NOT NULL,
    FOREIGN KEY(ObservedPlayerItemId) REFERENCES ObservedPlayerItems(Id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS IX_Items_Title ON Items(Title);
CREATE INDEX IF NOT EXISTS IX_Items_Storage ON Items(Storage);
CREATE INDEX IF NOT EXISTS IX_Characters_Name ON Characters(Name);
CREATE INDEX IF NOT EXISTS IX_ObservedPlayers_SeenAt ON ObservedPlayers(SeenAtUtc DESC);
CREATE INDEX IF NOT EXISTS IX_ObservedPlayers_CharacterId ON ObservedPlayers(ObservedByCharacterId);
