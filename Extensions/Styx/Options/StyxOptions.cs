namespace D2CompanionMvc.Extensions.Styx.Options;

public sealed class StyxOptions
{
    public bool ManageProcess { get; set; } = true;

    public bool KillExistingOnStart { get; set; } = true;

    public int Port { get; set; } = 20676;

    public string NodeCommand { get; set; } = "node";

    public string NodeArguments { get; set; } = "index.js";

    public string AppBaseUrl { get; set; } = "http://127.0.0.1:5178";
}
