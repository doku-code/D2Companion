namespace D2CompanionMvc.Services.Items.Rendering;

/// <summary>
/// Resolves Diablo II string-table keys (e.g. <c>ModStr1a</c>,
/// <c>StrSklTabItem1</c>, item name keys like <c>cap</c>, etc.) to the
/// English display string D2 itself would have rendered.
///
/// Implementations:
/// - <see cref="FallbackD2StringResolver"/> (used now) — ships a hand-curated
///   map of the most common ModStr / stat tokens and falls back to the key
///   itself when unknown. This is good enough for the canonical pipeline to
///   produce useful tooltips, and unknown keys surface in the debug endpoint.
/// - A future <c>TblD2StringResolver</c> can read the real <c>.tbl</c> files
///   from a D2 MPQ when they're available, and will swap in transparently
///   wherever this interface is injected.
///
/// Callers MUST handle null/missing keys gracefully — we never throw for an
/// unknown key, we return <see langword="null"/> so the caller knows to fall
/// back (typically to the descstrpos / stat-name column text).
/// </summary>
public interface ID2StringResolver
{
    /// <summary>
    /// Look up a single string-table key. Returns null when the key is not
    /// known. Implementations should be cheap (dictionary lookup); callers
    /// may invoke this once per stat line.
    /// </summary>
    string? Resolve(string key);

    /// <summary>
    /// Convenience: returns <paramref name="key"/> itself when the resolver
    /// doesn't know the key. Use this when you just want SOME text and don't
    /// care whether it's localized.
    /// </summary>
    string ResolveOrKey(string key) => Resolve(key) ?? key;
}
