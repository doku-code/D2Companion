namespace D2CompanionMvc.Domain.Items;

/// <summary>
/// D2 tooltip color codes — the digit that follows a <c>\xffc</c> escape
/// sequence in the rendered description string.
/// </summary>
public enum D2Color
{
    White   = 0,
    Red     = 1,
    Set     = 2, // green
    Magic   = 3, // blue
    Unique  = 4, // gold
    Gray    = 5,
    Black   = 6,
    Ocher   = 7,
    Craft   = 8, // orange
    Rare    = 9, // yellow
}
