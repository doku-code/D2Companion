namespace D2Companion.Desktop.Configuration;

public sealed class LauncherOptions
{
    public string AppUrl { get; set; } = "http://127.0.0.1:5178";

    public string MvcProjectPath { get; set; } = "../D2CompanionMvc.csproj";

    public string BindMode { get; set; } = "LocalOnly";

    public int Port { get; set; } = 5178;

    public bool StartStyxCollector { get; set; }

    public string StyxWorkingDirectory { get; set; } = "../Styx-Styx";

    public string StyxCommand { get; set; } = "node";

    public string StyxArguments { get; set; } = "index.js";

    public string AspNetUrl => BindMode.Equals("Lan", StringComparison.OrdinalIgnoreCase)
        ? $"http://0.0.0.0:{Port}"
        : $"http://127.0.0.1:{Port}";
}
