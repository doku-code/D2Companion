using System.Text.Json;

namespace D2CompanionMvc.Extensions.Styx.Models;

/// <summary>
/// Wire-format raw stat as Styx sends it: ItemStatCost row id (<see cref="Id"/>),
/// the primary numeric <see cref="Value"/>, and a handful of optional parameters
/// for stats that need them (skill/class/tab id, min/max for damage ranges,
/// charges for charged skills, etc.).
///
/// The adapter wraps this in a <see cref="D2CompanionMvc.Domain.Items.RawItemStat"/>
/// so the resolver doesn't have to know anything about the Styx wire format.
/// </summary>
public sealed class StyxRawStat
{
    public int Id { get; init; }

    /// <summary>
    /// Primary stat value.  Typed as <c>double</c> rather than <c>int</c> because
    /// Styx may send fractional intermediate values for per-level stats (e.g. 0.5
    /// defence per character level).  The adapter casts to <c>int</c> via
    /// <c>(int)Math.Round(Value)</c> when wiring into <c>RawItemStat</c>.
    /// Node also rounds before sending, but the double type is a belt-and-suspenders
    /// guard against future floats slipping through and causing HTTP 400.
    /// </summary>
    public double Value { get; init; }

    public int? Param { get; init; }
    public int? Min { get; init; }
    public int? Max { get; init; }
    public int? Frames { get; init; }
    public int? Skill { get; init; }
    public int? SkillId { get; init; }
    public int? SkillLevel { get; init; }
    public int? Level { get; init; }
    public int? Chance { get; init; }
    public int? Charges { get; init; }
    public int? MaxCharges { get; init; }
    public int? ClassId { get; init; }
    public int? TabId { get; init; }
    public int? MonsterId { get; init; }
    public int? Element { get; init; }

    /// <summary>Styx-side stat constructor/type name, forwarded only for diagnostics.</summary>
    public string? Type { get; init; }

    /// <summary>Same as <see cref="Type"/> when the bridge can read constructor.name.</summary>
    public string? ConstructorName { get; init; }

    /// <summary>Object.keys(stat) from the original Styx stat object.</summary>
    public IReadOnlyList<string>? NodeKeys { get; init; }

    /// <summary>Compact copy of the original Node stat object, kept for /api/debug/items/trace.</summary>
    public JsonElement? NodeRaw { get; init; }

    /// <summary>Exact outgoing raw stat object produced by CompanionBridge before POST.</summary>
    public JsonElement? OutgoingRaw { get; init; }
}
