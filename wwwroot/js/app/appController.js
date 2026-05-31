import { fetchCatalog } from "./catalogApi.js";
import { getAppElements } from "./dom.js";
import { escapeAttr, escapeHtml } from "./html.js";
import { itemImage, itemImageOnerror, itemKey } from "./itemAssets.js";
import { createInitialState, storageLabels } from "./state.js";
import { getTooltipFontMode, hideTooltip, initTooltipFontPreference, showTooltip, toggleTooltipFontMode } from "./tooltip.js";
import { isCubeItem, renderD2Scene, renderList } from "./d2SceneRenderer.js";
import { hasCapturedMercenary } from "./d2SceneMercenary.js";
import { makeItemFilter, itemsBelongingTo } from "./filterItems.js";
import { addSseListener } from "./eventStream.js";
import { createPageTrace } from "./navTrace.js";
import { STORAGE_KEYS } from "./storageKeys.js";
import { expStatusKey as accountExpStatusKey, renderAccountsHtml } from "./accountSelectorView.js";
import { renderEquipmentHeader } from "./equipmentHeaderView.js";
import {
  clearNoSelectionUrlMarker as clearUrlNoSelectionMarker,
  clearStoredSelection,
  findCharacterSelection as findCatalogCharacterSelection,
  firstCharacterSelection as firstCatalogCharacterSelection,
  persistStoredSelection,
  readStoredSelection as readStorageSelection
} from "./selectionController.js";

export function bootstrapCompanionApp(config) {
  const state = createInitialState();
  const els = getAppElements();
  const endpoint = config.catalogEndpoint || "/api/catalog";
  const navDebug = createNavDebug();
  const pendingAccountKey = STORAGE_KEYS.selectedAccount;
  const pendingCharacterKey = STORAGE_KEYS.selectedCharacter;
  const lastAccountKey = STORAGE_KEYS.lastAccount;
  const lastCharacterKey = STORAGE_KEYS.lastCharacter;

  function createNavDebug() {
    return createPageTrace("gear-viewer");
  }

  function currentQueryDebug() {
    try {
      const params = new URLSearchParams(window.location.search);
      return {
        url: window.location.href,
        queryAccount: params.get("account"),
        queryCharacter: params.get("character"),
        selection: params.get("selection"),
        debugNav: params.get("debugNav"),
        pending: readStoredSelection(pendingAccountKey, pendingCharacterKey),
        durableLast: readStoredSelection(lastAccountKey, lastCharacterKey)
      };
    } catch (error) {
      return { url: window.location.href, queryError: error.message };
    }
  }

  function characterItems(character = state.character) {
    return itemsBelongingTo(state.catalog.items, character);
  }

  function visibleCharacterItems(character = state.character) {
    const filter = makeItemFilter(state.query, state.storageFilter);
    return characterItems(character).filter(filter);
  }

  function renderAccounts() {
    els.accountList.innerHTML = renderAccountsHtml(state);
  }

  function renderHeader() {
    renderEquipmentHeader(els, state);
    syncMercenaryAction();
  }

  function syncMercenaryAction() {
    if (!els.mercenaryAction) return;
    const capturedMercenaryItems = state.character
      ? characterItems(state.character).filter(i => i.storage === "mercenary")
      : [];
    const hasMercenaryData = hasCapturedMercenary(state.character, capturedMercenaryItems);
    els.mercenaryAction.disabled = !hasMercenaryData;
    els.mercenaryAction.setAttribute("aria-disabled", hasMercenaryData ? "false" : "true");
    els.mercenaryAction.title = hasMercenaryData ? "Open mercenary" : "No mercenary captured";
  }

  function renderStorage() {
    const tab = state.tab;
    if (els.storagePanel) els.storagePanel.hidden = tab === "observed";
    if (tab === "observed") return;

    if (!state.character) {
      state.cubeOpen = false;
      state.mercenaryOpen = false;
      els.panelTitle.textContent = "Select a Character";
      els.panelHint.textContent = "Choose a character from the account list to view their gear.";
      els.storageView.innerHTML = `
        <div class="gear-empty-state">
          <h3>Select a Character</h3>
          <p>Choose a character from the account list to view their gear.</p>
          <a class="gear-empty-state__link" href="/characters">Open Account Dashboard</a>
        </div>`;
      return;
    }

    const allCharacterItems = characterItems();
    const capturedMercenaryItems = allCharacterItems.filter(i => i.storage === "mercenary");
    const hasMercenaryData = hasCapturedMercenary(state.character, capturedMercenaryItems);
    if (!hasMercenaryData) state.mercenaryOpen = false;

    const allItems = visibleCharacterItems();
    const storageItems = allItems.filter(i => ["equipped", "inventory", "stash"].includes(i.storage));
    const cubeItems = allItems.filter(i => i.storage === "cube");
    const mercenaryItems = allItems.filter(i => i.storage === "mercenary");
    const hasCube = cubeItems.length > 0 || storageItems.some(isCubeItem);
    const storageTab = els.tabs.querySelector("[data-tab=\"storage\"]");
    if (storageTab) storageTab.textContent = "Storage";
    if (!hasCube) state.cubeOpen = false;

    const items = tab === "all" ? allItems : state.mercenaryOpen ? mercenaryItems : storageItems;

    els.panelTitle.textContent = state.mercenaryOpen ? storageLabels.mercenary : storageLabels[tab] || "Storage";
    els.panelHint.textContent = tab === "storage" && !state.mercenaryOpen
      ? `${storageItems.length} visible item${storageItems.length === 1 ? "" : "s"}. Hover an item to inspect it.`
      : `${items.length} item${items.length === 1 ? "" : "s"} in this view. Hover an item to inspect it.`;

    if (tab === "all" || state.compact) {
      els.storageView.innerHTML = renderList(items);
      bindItemEvents();
      return;
    }

    navDebug.log("renderD2Scene.enter", {
      tab,
      character: state.character ? `${state.character.account}/${state.character.name}` : null,
      storageItems: storageItems.length,
      cubeItems: cubeItems.length,
      mercenaryItems: mercenaryItems.length,
      cubeOpen: state.cubeOpen,
      mercenaryOpen: state.mercenaryOpen,
      compact: state.compact,
      weaponSet: state.weaponSet
    });
    els.storageView.innerHTML = renderD2Scene("storage", storageItems, state, {
      cubeItems,
      cubeOpen: state.cubeOpen,
      mercenaryItems,
      mercenaryIconItems: capturedMercenaryItems,
      mercenaryOpen: state.mercenaryOpen,
      hasMercenaryData,
      mercenaryType: state.character?.mercenaryType,
      mercenaryAct: state.character?.mercenaryAct,
      mercenaryClassId: state.character?.mercenaryClassId,
      mercenaryKind: state.character?.mercenaryKind,
      mercenaryTypeSource: state.character?.mercenaryTypeSource
    });
    navDebug.log("renderD2Scene.complete", {
      scenePresent: Boolean(els.storageView.querySelector(".d2-scene")),
      itemCount: els.storageView.querySelectorAll(".d2-scene-item").length,
      sceneBgPresent: Boolean(els.storageView.querySelector(".d2-scene-bg")),
      sceneBgComplete: els.storageView.querySelector(".d2-scene-bg")?.complete ?? null,
      sceneBgNaturalWidth: els.storageView.querySelector(".d2-scene-bg")?.naturalWidth ?? null
    });
    bindItemEvents();
  }

  function findItemByKey(key) {
    const ownItem = state.catalog.items.find(i => itemKey(i) === key);
    if (ownItem) return ownItem;
    for (const player of (state.catalog.observedPlayers ?? [])) {
      const observed = player.items.find(i => itemKey(i) === key);
      if (observed) return observed;
    }
    return null;
  }

  function bindItemEvents() {
    document.querySelectorAll("[data-item-key]").forEach(node => {
      const showItemTooltip = event => {
        const item = findItemByKey(event.currentTarget.dataset.itemKey);
        showTooltip(els.tooltip, item, event.currentTarget);
      };
      node.addEventListener("mouseenter", showItemTooltip);
      node.addEventListener("mouseover", showItemTooltip);
      node.addEventListener("pointerenter", showItemTooltip);
      node.addEventListener("pointerover", showItemTooltip);
      node.addEventListener("mouseleave", () => hideTooltip(els.tooltip));
      node.addEventListener("pointerleave", () => hideTooltip(els.tooltip));
    });
  }

  function findCharacterSelection(accountName, characterName) {
    return findCatalogCharacterSelection(state.catalog, accountName, characterName);
  }

  function renderTooltipFontToggle() {
    if (!els.tooltipFontToggle) return;
    const mode = getTooltipFontMode();
    els.tooltipFontToggle.textContent = mode === "modern" ? "Tooltip Font: Exocet" : "Tooltip Font: D2";
    els.tooltipFontToggle.setAttribute("aria-pressed", mode === "modern" ? "true" : "false");
  }

  function firstCharacterSelection() {
    return firstCatalogCharacterSelection(state.catalog);
  }

  function readStoredSelection(accountKey, characterKey) {
    return readStorageSelection(accountKey, characterKey);
  }

  function persistLastSelection(accountName, characterName) {
    persistStoredSelection(lastAccountKey, lastCharacterKey, accountName, characterName);
  }

  function clearNoSelectionUrlMarker(accountName, characterName) {
    clearUrlNoSelectionMarker(accountName, characterName);
  }

  function clearPendingSelection() {
    clearStoredSelection(pendingAccountKey, pendingCharacterKey);
  }

  function selectCharacter(accountName, characterName, { persist = true, resetView = true } = {}) {
    navDebug.log("selectCharacter.enter", { accountName, characterName, persist, resetView, ...currentQueryDebug() });
    const selection = findCharacterSelection(accountName, characterName);
    if (!selection) {
      navDebug.log("selectCharacter.miss", {
        accountName,
        characterName,
        accounts: state.catalog?.accounts?.length ?? null,
        characters: state.catalog?.accounts?.reduce((n, account) => n + (account.characters?.length || 0), 0) ?? null
      });
      return false;
    }
    const sameSelection = state.account?.name === selection.account.name
      && state.character?.name === selection.character.name;
    state.account = selection.account;
    state.character = selection.character;
    // User-initiated character changes reset overlay/list state; live refreshes
    // that preserve the same selection keep Cube/Mercenary panels open.
    if (resetView || !sameSelection) {
      state.tab = "storage";
      state.cubeOpen = false;
      state.mercenaryOpen = false;
      state.compact = false;
      if (els.compactToggle) els.compactToggle.textContent = "Trade Preview";
    }
    if (persist) persistLastSelection(selection.account.name, selection.character.name);
    clearNoSelectionUrlMarker(selection.account.name, selection.character.name);
    navDebug.log("selectCharacter.resolved", {
      account: selection.account.name,
      character: selection.character.name,
      itemCount: characterItems(selection.character).length,
      tab: state.tab,
      compact: state.compact,
      cubeOpen: state.cubeOpen,
      mercenaryOpen: state.mercenaryOpen
    });
    render();
    return true;
  }

  // D2 body location slot numbers → friendly label
  const SLOT_LABEL = {
    1: "Helm", 2: "Amulet", 3: "Armor", 4: "Weapon", 5: "Off-hand",
    6: "Ring L", 7: "Ring R", 8: "Belt", 9: "Boots", 10: "Gloves",
    11: "Weapon II", 12: "Off-hand II"
  };
  // Slots 11/12 are weapon-swap
  const SWAP_SLOTS = new Set([11, 12]);

  function renderObservedItemSlot(item) {
    const key = itemKey(item);
    const slot = SLOT_LABEL[item.x] || `Slot ${item.x}`;
    return `<span class="observed-item${SWAP_SLOTS.has(item.x) ? " weapon-swap" : ""}" data-item-key="${escapeAttr(key)}" title="${escapeAttr(item.title)} (${slot})">`
      + `<img src="${itemImage(item)}" onerror="${itemImageOnerror(item)}" alt="${escapeAttr(item.title)}">`
      + `<span class="observed-slot-label">${slot}</span>`
      + `</span>`;
  }

  function renderObservedPlayers() {
    if (!els.observedSection || !els.observedList) return;
    const observed = state.catalog?.observedPlayers ?? [];
    if (state.tab !== "observed") {
      els.observedSection.hidden = true;
      return;
    }

    els.observedSection.hidden = false;
    if (!observed.length) {
      els.observedList.innerHTML = `<div class="observed-empty">No observed players yet. Join a D2 game with other players to capture gear.</div>`;
      return;
    }

    if (!observed.some(p => p.playerUid === state.observedPlayerUid)) {
      state.observedPlayerUid = observed[0].playerUid;
    }

    const selected = observed.find(p => p.playerUid === state.observedPlayerUid) || observed[0];
    const playerButtons = observed.map(player => {
      const name = escapeHtml(player.displayName || player.playerName || `Unknown Player ${player.shortPlayerUid || ""}`.trim());
      const meta = [
        player.gameName ? `Game: ${escapeHtml(player.gameName)}` : null,
        player.seenAt ? new Date(player.seenAt).toLocaleString() : null,
        `${player.itemCount || (player.items ?? []).length} items`
      ].filter(Boolean).join(" | ");

      return `<button class="observed-player-select ${player.playerUid === selected.playerUid ? "active" : ""}" data-observed-player="${escapeAttr(player.playerUid)}">
        <span class="observed-player-name">${name}</span>
        <span class="observed-player-meta">${meta}</span>
      </button>`;
    }).join("");

    const equipped = (selected.items ?? [])
      .filter(i => i.storage === "equipped")
      .sort((a, b) => (a.x || 0) - (b.x || 0));

    const mainSet  = equipped.filter(i => !SWAP_SLOTS.has(i.x));
    const swapSet  = equipped.filter(i => SWAP_SLOTS.has(i.x));
    const mainHtml = mainSet.map(renderObservedItemSlot).join("");
    const swapHtml = swapSet.length
      ? `<span class="observed-swap-divider" title="Weapon swap II">II</span>` + swapSet.map(renderObservedItemSlot).join("")
      : "";

    const selectedName = escapeHtml(selected.displayName || selected.playerName || `Unknown Player ${selected.shortPlayerUid || ""}`.trim());
    const selectedMeta = [
      selected.gameName ? `Game: ${escapeHtml(selected.gameName)}` : null,
      `Seen by ${escapeHtml(selected.observedByCharacter)}`,
      selected.snapshotCount ? `${selected.snapshotCount} snapshots` : null,
      selected.seenAt ? `Last seen ${new Date(selected.seenAt).toLocaleString()}` : null
    ].filter(Boolean).join(" | ");
    const itemsHtml = mainHtml + swapHtml;

    els.observedList.innerHTML = `<div class="observed-browser">
      <div class="observed-player-list">${playerButtons}</div>
      <article class="observed-player-detail">
        <div class="observed-player-header">
          <span class="observed-player-name">${selectedName}</span>
          <span class="observed-player-meta">${selectedMeta}</span>
        </div>
        <div class="observed-player-items">${itemsHtml || '<span class="observed-empty">No equipped gear captured</span>'}</div>
      </article>
    </div>`;
    bindItemEvents();
    return;

    if (!observed.length) {
      els.observedSection.hidden = true;
      return;
    }
    els.observedSection.hidden = false;
    els.observedList.innerHTML = observed.map(player => {
      const name = escapeHtml(player.playerName || "Unknown Player");
      const meta = [
        player.gameName ? `Game: ${escapeHtml(player.gameName)}` : null,
        `Seen by ${escapeHtml(player.observedByCharacter)}`,
        player.seenAt ? new Date(player.seenAt).toLocaleString() : null
      ].filter(Boolean).join(" · ");

      // Only equipped items — sort by slot number
      const equipped = (player.items ?? [])
        .filter(i => i.storage === "equipped")
        .sort((a, b) => (a.x || 0) - (b.x || 0));

      const mainSet  = equipped.filter(i => !SWAP_SLOTS.has(i.x));
      const swapSet  = equipped.filter(i => SWAP_SLOTS.has(i.x));

      const mainHtml = mainSet.map(renderObservedItemSlot).join("");
      const swapHtml = swapSet.length
        ? `<span class="observed-swap-divider" title="Weapon swap II">⚔</span>` + swapSet.map(renderObservedItemSlot).join("")
        : "";

      const itemsHtml = mainHtml + swapHtml;

      return `<article class="observed-player">
        <div class="observed-player-header">
          <span class="observed-player-name">${name}</span>
          <span class="observed-player-meta">${meta}</span>
        </div>
        <div class="observed-player-items">${itemsHtml || '<span class="observed-empty">No equipped gear captured</span>'}</div>
      </article>`;
    }).join("");
    bindItemEvents();
  }

  function render() {
    try {
      renderAccounts();
      renderHeader();
      renderStorage();
      renderObservedPlayers();
      activateTab(state.tab);
    } catch (error) {
      renderGearViewerError(error);
    }
  }

  function renderGearViewerError(error) {
    navDebug.error("render.error", error, {
      character: state.character ? `${state.character.account}/${state.character.name}` : null,
      tab: state.tab,
      compact: state.compact
    });
    if (els.panelTitle) els.panelTitle.textContent = "Could not render Gear Viewer";
    if (els.panelHint) els.panelHint.textContent = navDebug.enabled ? (error?.message || String(error)) : "Refresh the page or choose another character.";
    if (els.storageView) {
      els.storageView.innerHTML = `
        <div class="gear-empty-state gear-empty-state--error">
          <h3>Could not render Gear Viewer</h3>
          <p>${escapeHtml(navDebug.enabled ? (error?.message || String(error)) : "Refresh the page or choose another character.")}</p>
        </div>`;
    }
  }

  function activateTab(tabName) {
    els.tabs.querySelectorAll("button").forEach(node =>
      node.classList.toggle("active", node.dataset.tab === tabName)
    );
  }

  function bindEvents() {
    els.accountList.addEventListener("click", event => {
      // Character click → load that character.
      const charBtn = event.target.closest("[data-character]");
      if (charBtn) {
        selectCharacter(charBtn.dataset.account, charBtn.dataset.character);
        return;
      }
      // Account header click → toggle the account's collapse state.
      const acctBtn = event.target.closest("[data-toggle-account]");
      if (acctBtn) {
        const acct = acctBtn.dataset.toggleAccount;
        if (state.collapsedAccounts.has(acct)) state.collapsedAccounts.delete(acct);
        else state.collapsedAccounts.add(acct);
        renderAccounts();
      }
    });

    els.tabs.addEventListener("click", event => {
      const btn = event.target.closest("[data-tab]");
      if (!btn) return;
      if (btn.dataset.tab === "storage") {
        state.tab = "storage";
        state.cubeOpen = false;
        state.mercenaryOpen = false;
      } else {
        state.tab = btn.dataset.tab;
        state.cubeOpen = false;
        state.mercenaryOpen = false;
      }
      activateTab(state.tab);
      render();
    });

    els.observedList?.addEventListener("click", event => {
      const btn = event.target.closest("[data-observed-player]");
      if (!btn) return;
      state.observedPlayerUid = btn.dataset.observedPlayer;
      renderObservedPlayers();
    });

    els.storageView.addEventListener("click", event => {
      if (event.target.closest("[data-toggle-weapon-set]")) {
        state.weaponSet = state.weaponSet === 2 ? 1 : 2;
        renderStorage();
        return;
      }
      if (event.target.closest("[data-toggle-mercenary]")) {
        if (!hasCapturedMercenary(state.character, characterItems().filter(i => i.storage === "mercenary"))) return;
        state.tab = "storage";
        state.cubeOpen = false;
        state.mercenaryOpen = true;
        activateTab("storage");
        renderStorage();
        return;
      }
      if (event.target.closest("[data-weapon-set]")) {
        state.weaponSet = Number(event.target.closest("[data-weapon-set]").dataset.weaponSet) || 1;
        renderStorage();
        return;
      }
      // The mercenary panel owns its close button inside the D2 overlay;
      // closing returns to the normal storage scene and restores the
      // scene-local Mercenary opener.
      if (event.target.closest("[data-close-mercenary]")) {
        state.tab = "storage";
        state.mercenaryOpen = false;
        activateTab("storage");
        renderStorage();
        return;
      }
      if (event.target.closest("[data-close-cube]")) {
        state.tab = "storage";
        state.cubeOpen = false;
        state.mercenaryOpen = false;
        activateTab("storage");
        renderStorage();
      }
    });

    els.storageView.addEventListener("contextmenu", event => {
      if (!event.target.closest("[data-open-cube]")) return;
      event.preventDefault();
      state.tab = "storage";
      state.cubeOpen = true;
      state.mercenaryOpen = false;
      activateTab("storage");
      renderStorage();
    });

    els.search.addEventListener("input", event => {
      state.query = event.target.value.trim().toLowerCase();
      render();
    });

    els.storageFilter?.addEventListener("change", event => {
      state.storageFilter = event.target.value;
      render();
    });

    els.compactToggle?.addEventListener("click", () => {
      state.compact = !state.compact;
      els.compactToggle.textContent = state.compact ? "Grid View" : "Trade Preview";
      renderStorage();
    });

    els.cubeAction?.addEventListener("click", () => {
      state.tab = "storage";
      state.cubeOpen = true;
      state.mercenaryOpen = false;
      activateTab("storage");
      renderStorage();
    });

    els.mercenaryAction?.addEventListener("click", () => {
      if (!hasCapturedMercenary(state.character, characterItems().filter(i => i.storage === "mercenary"))) return;
      state.tab = "storage";
      state.cubeOpen = false;
      state.mercenaryOpen = true;
      activateTab("storage");
      renderStorage();
    });

    els.tooltipFontToggle?.addEventListener("click", () => {
      toggleTooltipFontMode();
      hideTooltip(els.tooltip);
      renderTooltipFontToggle();
    });

    // ?debugD2Controls=1 dispatches "d2:debug:overlay" with detail
    // { which: "cube" | "merc" | "none" } so its toolbar can force the
    // cube and mercenary popovers visible. Without this hook the
    // debug helper showed coordinates over a popover that was never
    // rendered (because cube/merc are normally only opened on
    // explicit user action). See wwwroot/js/app/d2SceneDebug.js.
    document.addEventListener("d2:debug:overlay", event => {
      const which = event.detail?.which;
      if (which === "merc" && !hasCapturedMercenary(state.character, characterItems().filter(i => i.storage === "mercenary"))) return;
      state.tab = "storage";
      state.cubeOpen = which === "cube";
      state.mercenaryOpen = which === "merc";
      activateTab("storage");
      renderStorage();
    });
  }

  async function loadCatalog({ preserveSelection = false, preferredSelection = null } = {}) {
    // Resolve the selected character with the least surprising rule:
    // Explicit URL selection wins; dashboard navigation storage is a
    // compatibility fallback.
    // fallback; otherwise show the first available character. The only
    // normal empty state is a genuinely empty catalog.
    const candidates = [];
    navDebug.log("loadCatalog.start", { preserveSelection, preferredSelection, ...currentQueryDebug() });
    if (preferredSelection?.account && preferredSelection?.character) {
      candidates.push(preferredSelection);
    }
    if (!preserveSelection && !preferredSelection) {
      try {
        const params = new URLSearchParams(window.location.search);
        const qAcct = params.get("account");
        const qChar = params.get("character");
        if (qAcct && qChar) candidates.push({ account: qAcct, character: qChar });
      } catch {}
      const pendingSelection = readStoredSelection(pendingAccountKey, pendingCharacterKey);
      if (pendingSelection) candidates.push(pendingSelection);
      clearPendingSelection();
    } else if (state.character) {
      candidates.push({ account: state.character.account, character: state.character.name });
    }

    const isFirstLoad = state.catalog == null;
    try {
      const fetchStartedAt = performance.now();
      navDebug.log("loadCatalog.fetch.start", { endpoint });
      const catalog = await fetchCatalog(endpoint);
      const catalogFetchMs = Math.round(performance.now() - fetchStartedAt);
      state.catalog = catalog;
      navDebug.log("loadCatalog.loaded", {
        catalogFetchMs,
        accounts: catalog.accounts?.length ?? 0,
        characters: catalog.accounts?.reduce((n, account) => n + (account.characters?.length || 0), 0) ?? 0,
        items: catalog.items?.length ?? 0,
        candidates
      });
      // Top-bar summary line is now derived in renderHeader. The legacy
      // one-line totals here is kept as a safety fallback for sidebar
      // boots where the top bar isn't present.
      if (els.summary) {
        const t = catalog.totals;
        els.summary.textContent = `${t.accounts} account${t.accounts === 1 ? "" : "s"} · ${t.characters} character${t.characters === 1 ? "" : "s"} · ${t.items} item${t.items === 1 ? "" : "s"}`;
      }
      renderSampleDataBanner(catalog);
      // First-load auto-collapse: accounts whose every character is
      // Safe or Unknown collapse by default so the sidebar focuses on
      // accounts that need attention. Same rule as the dashboard's
      // autoCollapseSafeOnly().
      if (isFirstLoad && state.collapsedAccounts.size === 0) {
        for (const account of catalog.accounts) {
          const hasUrgent = account.characters.some(c => {
            const k = accountExpStatusKey(c);
            return k === "critical" || k === "warning";
          });
          if (!hasUrgent) state.collapsedAccounts.add(account.name);
        }
      }
      for (const candidate of candidates) {
        if (selectCharacter(candidate.account, candidate.character, { resetView: !preserveSelection })) return;
      }
      const fallback = firstCharacterSelection();
      if (fallback && selectCharacter(fallback.account.name, fallback.character.name)) return;
      state.account = null;
      state.character = null;
      navDebug.log("loadCatalog.noSelection", { candidates });
      render();
    } catch (error) {
      navDebug.error("loadCatalog.error", error, { candidates });
      els.storageView.innerHTML = `<div class="empty">Could not load catalog: ${escapeHtml(error.message)}</div>`;
    }
  }

  function renderSampleDataBanner(catalog) {
    // Visible warning when /api/catalog flagged the response as the
    // sample/demo fallback (see Services/Catalog/LiveCatalogService.cs).
    // SampleAccount / SampleChar data must never be confused for real Styx
    // captures — diagnostics rely on this signal too.
    const banner = document.getElementById("sampleDataBanner");
    if (!banner) return;
    if (catalog && catalog.isSampleData === true) {
      const reasonEl = document.getElementById("sampleDataBannerReason");
      if (reasonEl && typeof catalog.sampleDataReason === "string" && catalog.sampleDataReason.length > 0) {
        reasonEl.textContent = catalog.sampleDataReason + " Not valid for Styx diagnostics.";
      }
      banner.hidden = false;
    } else {
      banner.hidden = true;
    }
  }

  function connectLiveUpdates() {
    navDebug.log("sse.items-updated.listen", {});
    addSseListener("items-updated", () => {
      navDebug.log("sse.items-updated", {});
      loadCatalog({ preserveSelection: true });
    });
    addSseListener("styx-status", event => {
      try {
        const status = JSON.parse(event.data);
        if (status?.sessionState !== "in-game" || !status.accountName || !status.characterName) return;
        const currentAccount = state.account?.name || null;
        const currentCharacter = state.character?.name || null;
        if (currentAccount === status.accountName && currentCharacter === status.characterName) return;
        navDebug.log("sse.styx-status.catalog-select", {
          account: status.accountName,
          character: status.characterName
        });
        loadCatalog({
          preferredSelection: { account: status.accountName, character: status.characterName }
        });
      } catch (error) {
        navDebug.error("sse.styx-status.catalog-select.error", error);
      }
    });
  }

  initTooltipFontPreference();
  renderTooltipFontToggle();
  bindEvents();
  loadCatalog();
  connectLiveUpdates();
}
