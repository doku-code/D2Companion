export const STORAGE_KEYS = Object.freeze({
  selectedAccount: "d2-companion-selected-account",
  selectedCharacter: "d2-companion-selected-character",
  lastAccount: "d2-companion-last-account",
  lastCharacter: "d2-companion-last-character",
  sessionStatus: "d2-companion-session-status",
  observedPlayer: "d2-companion-observed-player",
  tooltipFont: "d2-companion-tooltip-font",
  styxStartupPreference: "d2-companion-styx-startup-preference",
});

export const NAV_TRACE_STORAGE_KEYS = Object.freeze([
  STORAGE_KEYS.selectedAccount,
  STORAGE_KEYS.selectedCharacter,
  STORAGE_KEYS.lastAccount,
  STORAGE_KEYS.lastCharacter,
  STORAGE_KEYS.sessionStatus,
  STORAGE_KEYS.observedPlayer,
]);
