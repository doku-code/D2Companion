export function getAppElements() {
  return {
    // Legacy summary fallback for old shells.
    summary: document.querySelector("#appSummary") || document.querySelector("#summary"),
    accountList: document.querySelector("#accountList"),
    search: document.querySelector("#search"),
    characterTitle: document.querySelector("#characterTitle"),
    characterMeta: document.querySelector("#characterMeta"),
    contextLabel: document.querySelector("#contextLabel"),
    stats: document.querySelector("#stats"),
    tabs: document.querySelector("#tabs"),
    panelTitle: document.querySelector("#panelTitle"),
    panelHint: document.querySelector("#panelHint"),
    storagePanel: document.querySelector("#storagePanel"),
    storageView: document.querySelector("#storageView"),
    tooltip: document.querySelector("#tooltip"),
    storageFilter: document.querySelector("#storageFilter"),
    compactToggle: document.querySelector("#compactToggle"),
    cubeAction: document.querySelector("#d2GearCubeAction"),
    mercenaryAction: document.querySelector("#d2GearMercenaryAction"),
    tooltipFontToggle: document.querySelector("#tooltipFontToggle"),
    observedSection: document.querySelector("#observedSection"),
    observedList: document.querySelector("#observedList")
  };
}
