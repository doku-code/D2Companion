/**
 * @param {string} endpoint
 * @returns {Promise<import("./state.js").CompanionCatalog>}
 */
export async function fetchCatalog(endpoint) {
  const response = await fetch(endpoint, {
    cache: "no-store",
    headers: { Accept: "application/json" }
  });

  if (!response.ok) {
    throw new Error(`Catalog request failed (${response.status})`);
  }

  return response.json();
}
