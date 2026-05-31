/**
 * @typedef {Object} ItemRecord
 * @property {string} gid
 * @property {number} classId
 * @property {string} title
 * @property {string|null} description
 * @property {string} image
 * @property {number} itemColor
 * @property {string} storage  - "equipped" | "inventory" | "stash" | "cube" | "mercenary"
 * @property {number} location
 * @property {number} x
 * @property {number} y
 * @property {number} pixelWidth
 * @property {number} pixelHeight
 * @property {number} gridWidth
 * @property {number} gridHeight
 * @property {boolean} ethereal
 * @property {string[]} sockets
 * @property {string} account
 * @property {string} character
 * @property {string} sourceFile
 */

/**
 * @typedef {Object} CharacterSummary
 * @property {string} name
 * @property {string} account
 * @property {number|null} level
 * @property {boolean} hardcore
 * @property {boolean} expansion
 * @property {boolean} ladder
 * @property {number} itemCount
 * @property {Record<string, number>} storageCounts
 */

/**
 * @typedef {Object} AccountSummary
 * @property {string} name
 * @property {CharacterSummary[]} characters
 * @property {number} itemCount
 */

/**
 * @typedef {Object} ObservedPlayerRecord
 * @property {string} observedKey
 * @property {string} playerUid
 * @property {string|null} playerName
 * @property {string} displayName
 * @property {string} shortPlayerUid
 * @property {string|null} realm
 * @property {string|null} className
 * @property {number|null} level
 * @property {string|null} gameName
 * @property {string|null} firstSeenAt
 * @property {string} seenAt
 * @property {number} snapshotCount
 * @property {number} itemCount
 * @property {number} equippedSlotCount
 * @property {string} observedByAccount
 * @property {string} observedByCharacter
 * @property {ItemRecord[]} items
 */

/**
 * @typedef {Object} CompanionCatalog
 * @property {string} generatedAt
 * @property {{ accounts: number, characters: number, items: number }} totals
 * @property {AccountSummary[]} accounts
 * @property {ItemRecord[]} items
 * @property {ObservedPlayerRecord[]} observedPlayers
 */

/** @returns {{ catalog: CompanionCatalog|null, account: AccountSummary|null, character: CharacterSummary|null, tab: string, cubeOpen: boolean, mercenaryOpen: boolean, compact: boolean, query: string, storageFilter: string, weaponSet: number, observedPlayerUid: string|null }} */
export function createInitialState() {
  return {
    catalog: null,
    account: null,
    character: null,
    tab: "storage",
    cubeOpen: false,
    mercenaryOpen: false,
    compact: false,
    query: "",
    storageFilter: "all",
    weaponSet: 1,
    observedPlayerUid: null,
    /** Sidebar account-fold state; mirrors the Account Dashboard. */
    collapsedAccounts: new Set(),
  };
}

export const storageLabels = {
  storage: "Storage",
  mercenary: "Mercenary",
  all: "All Items"
};

export const gridConfig = {
  inventory: { cols: 10, rows: 4 },
  stash: { cols: 6, rows: 8 },
  cube: { cols: 3, rows: 4 }
};
