using D2Companion.Desktop.Configuration;
using D2Companion.Desktop.Services;
using Microsoft.Extensions.Configuration;

namespace D2Companion.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.desktop.json", optional: false, reloadOnChange: true)
            .Build();

        var options = configuration.GetSection("Launcher").Get<LauncherOptions>() ?? new LauncherOptions();
        using var processManager = new SidecarProcessManager(options);

        Application.Run(new MainForm(options, processManager));
    }
}
