namespace D2CompanionMvc.Options;

/// <summary>
/// Where to find the Diablo II Excel TXT tables. Defaults to the in-repo copy
/// of the 1.13c data shipped under <c>data/d2/1.13c/txt</c>. Override via
/// <c>CompanionApp:GameData:TxtDirectory</c> in appsettings if you want to
/// point at a different patch revision.
/// </summary>
public sealed class GameDataOptions
{
    public string TxtDirectory { get; set; } = "data/d2/1.13c/txt";
}
