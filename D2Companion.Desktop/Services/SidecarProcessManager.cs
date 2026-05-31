using System.Diagnostics;
using D2Companion.Desktop.Configuration;

namespace D2Companion.Desktop.Services;

public sealed class SidecarProcessManager : IDisposable
{
    private readonly LauncherOptions _options;
    private Process? _mvcProcess;
    private Process? _styxProcess;

    public SidecarProcessManager(LauncherOptions options)
    {
        _options = options;
    }

    public async Task StartAsync()
    {
        _mvcProcess = StartProcess("dotnet", $"run --project \"{ResolvePath(_options.MvcProjectPath)}\" --urls {_options.AspNetUrl}", AppContext.BaseDirectory);

        if (_options.StartStyxCollector)
        {
            _styxProcess = StartProcess(
                _options.StyxCommand,
                _options.StyxArguments,
                ResolvePath(_options.StyxWorkingDirectory));
        }

        await WaitForAppAsync();
    }

    private async Task WaitForAppAsync()
    {
        using var client = new HttpClient();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync(_options.AppUrl);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
                // The MVC sidecar is still booting.
            }

            await Task.Delay(500);
        }

        throw new TimeoutException("D2 Companion MVC did not start within 30 seconds.");
    }

    private static Process StartProcess(string fileName, string arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start process: {fileName}");
    }

    private static string ResolvePath(string path)
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }

    public void Dispose()
    {
        StopProcess(_styxProcess);
        StopProcess(_mvcProcess);
    }

    private static void StopProcess(Process? process)
    {
        if (process is null || process.HasExited) return;

        try
        {
            process.Kill(entireProcessTree: true);
            process.Dispose();
        }
        catch
        {
            // Best effort shutdown for child sidecars.
        }
    }
}
