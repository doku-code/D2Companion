export function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>"']/g, char => ({
    "&": "&amp;",
    "<": "&lt;",
    ">": "&gt;",
    '"': "&quot;",
    "'": "&#039;"
  })[char]);
}

export function escapeAttr(value) {
  return escapeHtml(value).replace(/`/g, "&#096;");
}

export function stat(label, value) {
  return `<div class="stat"><strong>${value}</strong><span>${label}</span></div>`;
}
