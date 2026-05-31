using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using D2CompanionMvc.Extensions.Styx.Options;
using D2CompanionMvc.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace D2CompanionMvc.Extensions.Styx.Launcher;

public sealed class StyxProcessService : BackgroundService
{
    internal const string BundledNodeRelativePath = "runtimes/node/node.exe";
    internal const string BundledNpmRelativePath = "runtimes/node/npm.cmd";
    internal const string SnapshotEndpointPath = "/api/ingest/styx/snapshot";
    internal const string SessionEndpointPath = "/api/ingest/styx/session";
    internal static readonly string[] RequiredNodeModules = ["js-sha256", "node-persist", "ws"];

    private readonly ILogger<StyxProcessService> _logger;
    private readonly IWebHostEnvironment _env;
    private readonly StyxStatus _status;
    private readonly StyxOptions _options;
    private readonly SemaphoreSlim _processGate = new(1, 1);
    private Process? _process;
    private WindowsProcessJob? _processJob;
    private string? _logFile;

    public StyxProcessService(
        ILogger<StyxProcessService> logger,
        IWebHostEnvironment env,
        StyxStatus status,
        IOptions<CompanionAppOptions> options)
    {
        _logger = logger;
        _env = env;
        _status = status;
        _options = options.Value.Styx;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.ManageProcess)
        {
            if (IsPortInUse(_options.Port))
            {
                _logger.LogInformation("[Styx] External process mode: detected existing proxy on port {Port}; leaving it running.", _options.Port);
                _status.SetRunning(true);
            }
            else
            {
                _logger.LogInformation("[Styx] External process mode: process management disabled. Start Styx manually when needed.");
                _status.SetRunning(false);
            }

            return;
        }

        _logger.LogInformation("[Styx] Managed process mode: waiting for user start request.");
        _status.SetRunning(false);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public bool OwnsRunningProcess => _process is { HasExited: false };

    public bool ShouldWarnBeforeClose => OwnsRunningProcess && IsActiveCaptureState(_status.SessionState);

    public async Task<StyxProcessControlResult> StartProxyAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.ManageProcess)
        {
            if (IsPortInUse(_options.Port))
            {
                _status.SetRunning(true);
                return StyxProcessControlResult.Ok("External Styx proxy is already running.");
            }

            var externalMessage = "Styx process management is disabled in this environment. Start Styx with tools/dev/dev-styx.bat.";
            _status.SetRunning(false);
            _status.SetError(externalMessage);
            return StyxProcessControlResult.Fail(externalMessage);
        }

        await _processGate.WaitAsync(cancellationToken);
        try
        {
            if (_process is { HasExited: false })
                return StyxProcessControlResult.Ok("Styx proxy is already running.");

            var styxDir = FindStyxDirectory();
            if (styxDir is null)
            {
                var missingStyx = "styx/ directory not found next to the executable - live capture is unavailable.";
                _logger.LogWarning("[Styx] {Message}", missingStyx);
                _status.SetRunning(false);
                _status.SetError(missingStyx);
                return StyxProcessControlResult.Fail(missingStyx);
            }

            var nodeCommand = ResolveNodeCommand(_env.ContentRootPath, _options.NodeCommand);
            if (!IsCommandAvailable(nodeCommand, "--version"))
            {
                var message = NodeMissingMessage(nodeCommand);
                _logger.LogError("[Styx] {Message}", message);
                _status.SetRunning(false);
                _status.SetError(message);
                return StyxProcessControlResult.Fail(message);
            }

            var dataDir = Path.Combine(_env.ContentRootPath, "data");
            Directory.CreateDirectory(dataDir);
            _logFile = Path.Combine(dataDir, "styx.log");
            AppendLog($"=== Styx started at {DateTimeOffset.Now:O} ===");

            var snapshotEndpoint = ResolveEndpoint(_options.AppBaseUrl, SnapshotEndpointPath);
            var sessionEndpoint = ResolveEndpoint(_options.AppBaseUrl, SessionEndpointPath);
            EnsureConfig(styxDir, snapshotEndpoint, sessionEndpoint);
            if (!await EnsureNodeModules(styxDir, cancellationToken))
                return StyxProcessControlResult.Fail(_status.LastError ?? "Styx dependencies are unavailable.");

            if (IsPortInUse(_options.Port))
            {
                var message = $"Port {_options.Port} is already in use. Another Styx proxy or unrelated process is bound - close it before starting live capture.";
                _logger.LogWarning("[Styx] {Message}", message);
                AppendLog("[Styx] " + message);
                _status.SetRunning(false);
                _status.SetError(message);
                return StyxProcessControlResult.Fail(message);
            }

            _logger.LogInformation("[Styx] Starting proxy...");
            AppendLog("[Styx] Node: " + nodeCommand);
            AppendLog("[Styx] Working directory: " + styxDir);
            AppendLog("[Styx] Snapshot endpoint: " + snapshotEndpoint);
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = nodeCommand,
                    Arguments = _options.NodeArguments,
                    WorkingDirectory = styxDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
                EnableRaisingEvents = true,
            };
            process.StartInfo.Environment["D2COMPANION_STYX_ENDPOINT"] = snapshotEndpoint;
            process.StartInfo.Environment["D2COMPANION_STYX_SESSION_ENDPOINT"] = sessionEndpoint;
            process.StartInfo.Environment["D2COMPANION_STYX_PROXY_PORT"] = _options.Port.ToString(CultureInfo.InvariantCulture);

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                _logger.LogInformation("[Styx] {Line}", e.Data);
                AppendLog($"[OUT] {e.Data}");
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                _logger.LogWarning("[Styx] {Line}", e.Data);
                AppendLog($"[ERR] {e.Data}");
            };

            process.Start();
            _processJob = WindowsProcessJob.TryCreateKillOnClose(process, message => AppendLog("[Styx] " + message), _logger);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;

            _status.SetRunning(true);
            _logger.LogInformation("[Styx] Proxy running (PID {Pid}). SOCKS5 on port {Port}.", process.Id, _options.Port);
            AppendLog($"[Styx] Proxy running (PID {process.Id})");
            _ = MonitorProcessAsync(process);

            return StyxProcessControlResult.Ok("Styx proxy started.");
        }
        finally
        {
            _processGate.Release();
        }
    }

    public async Task<StyxProcessControlResult> StopProxyAsync(CancellationToken cancellationToken = default)
        => await StopOwnedProxyAsync("user request", cancellationToken);

    public async Task<StyxProcessControlResult> StopOwnedProxyOnShutdownAsync(CancellationToken cancellationToken = default)
        => await StopOwnedProxyAsync("application shutdown", cancellationToken);

    public void StopOwnedProxyBestEffort(TimeSpan timeout)
    {
        if (!OwnsRunningProcess) return;

        try
        {
            using var cts = new CancellationTokenSource(timeout);
            StopOwnedProxyOnShutdownAsync(cts.Token).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Styx] Best-effort shutdown cleanup failed.");
            AppendLog("[Styx] Best-effort shutdown cleanup failed: " + ex.Message);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_options.ManageProcess)
        {
            await StopOwnedProxyOnShutdownAsync(cancellationToken);
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task<StyxProcessControlResult> StopOwnedProxyAsync(string reason, CancellationToken cancellationToken = default)
    {
        await _processGate.WaitAsync(cancellationToken);
        try
        {
            if (_process is not { HasExited: false } process)
            {
                _status.SetRunning(false);
                return StyxProcessControlResult.Ok("Styx proxy is not running.");
            }

            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken);
                _process = null;
                _processJob?.Dispose();
                _processJob = null;
                _status.SetRunning(false);
                _logger.LogInformation("[Styx] Proxy stopped by {Reason}.", reason);
                AppendLog($"[Styx] Proxy stopped by {reason}.");
                return StyxProcessControlResult.Ok("Styx proxy stopped.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var message = $"Could not stop the managed Styx process: {ex.Message}";
                _logger.LogWarning(ex, "[Styx] {Message}", message);
                AppendLog("[Styx] " + message);
                _status.SetError(message);
                return StyxProcessControlResult.Fail(message);
            }
        }
        finally
        {
            _processGate.Release();
        }
    }

    private async Task MonitorProcessAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync();
        }
        catch
        {
        }
        finally
        {
            await _processGate.WaitAsync();
            try
            {
                if (ReferenceEquals(_process, process))
                {
                    _process = null;
                    _processJob?.Dispose();
                    _processJob = null;
                    _status.SetRunning(false);
                    AppendLog("[Styx] Proxy exited.");
                }
            }
            finally
            {
                _processGate.Release();
            }
        }
    }

    private void AppendLog(string line)
    {
        if (_logFile is null) return;
        try
        {
            File.AppendAllText(_logFile, $"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch { /* non-critical */ }
    }

    internal static string ResolveNodeCommand(string contentRootPath, string configuredNodeCommand)
    {
        var bundledNode = Path.Combine(contentRootPath, "runtimes", "node", "node.exe");
        if (File.Exists(bundledNode))
            return bundledNode;

        return string.IsNullOrWhiteSpace(configuredNodeCommand) ? "node" : configuredNodeCommand;
    }

    internal static string ResolveNpmCommand(string contentRootPath)
    {
        var bundledNpm = Path.Combine(contentRootPath, "runtimes", "node", "npm.cmd");
        return File.Exists(bundledNpm) ? bundledNpm : "npm";
    }

    internal static string NodeMissingMessage(string resolvedNodeCommand)
        => $"Node.js missing - live capture unavailable. D2Companion can still open and Import Mule Files still works. Install Node.js 18+ from https://nodejs.org/ or package a portable Node runtime at {BundledNodeRelativePath}. Tried: {resolvedNodeCommand}.";

    internal static bool StyxDependenciesPresent(string styxDir)
    {
        var nodeModules = Path.Combine(styxDir, "node_modules");
        return RequiredNodeModules.All(module => Directory.Exists(Path.Combine(nodeModules, module)));
    }

    internal static bool IsActiveCaptureState(string? sessionState)
        => string.Equals(sessionState, StyxStatus.SessionStateWaiting, StringComparison.Ordinal)
            || string.Equals(sessionState, StyxStatus.SessionStateInGame, StringComparison.Ordinal);

    internal static string StyxDependenciesMissingMessage(string npmCommand)
        => $"Styx dependencies are missing and npm is not available. Rebuild the release package with Styx dependencies or install Node.js/npm. Offline MuleLogger import still works. Tried: {npmCommand}.";

    internal static string ResolveEndpoint(string? appBaseUrl, string endpointPath)
    {
        var baseUrl = string.IsNullOrWhiteSpace(appBaseUrl)
            ? "http://127.0.0.1:5178"
            : appBaseUrl.Trim().TrimEnd('/');
        var path = endpointPath.StartsWith('/') ? endpointPath : "/" + endpointPath;
        return baseUrl + path;
    }

    internal static string BuildManagedConfig(int port, string snapshotEndpoint, string sessionEndpoint)
        => string.Join(Environment.NewLine, new[]
        {
            "module.exports = {",
            "    users: [],",
            "    options: {",
            "        allowNoAuth: true,",
            $"        listen: {port.ToString(CultureInfo.InvariantCulture)},",
            "        proxy: require('./DiabloProxy'),",
            "    },",
            "    CompanionBridge: {",
            "        enable: true,",
            $"        endpoint: process.env.D2COMPANION_STYX_ENDPOINT || {JsString(snapshotEndpoint)},",
            $"        sessionEndpoint: process.env.D2COMPANION_STYX_SESSION_ENDPOINT || {JsString(sessionEndpoint)},",
            "        debounceMs: 1500,",
            "    },",
            "};",
            string.Empty,
        });

    internal static bool RequiresManagedConfigRepair(string? configText, string snapshotEndpoint, string sessionEndpoint)
        => string.IsNullOrWhiteSpace(configText)
            || !configText.Contains("proxy: require('./DiabloProxy')", StringComparison.Ordinal)
            || !configText.Contains("CompanionBridge", StringComparison.Ordinal)
            || !configText.Contains("enable: true", StringComparison.Ordinal)
            || !configText.Contains("/api/ingest/styx/snapshot", StringComparison.Ordinal)
            || !configText.Contains("/api/ingest/styx/session", StringComparison.Ordinal)
            || !configText.Contains("D2COMPANION_STYX_ENDPOINT", StringComparison.Ordinal)
            || !configText.Contains("D2COMPANION_STYX_SESSION_ENDPOINT", StringComparison.Ordinal)
            || !configText.Contains(snapshotEndpoint, StringComparison.Ordinal)
            || !configText.Contains(sessionEndpoint, StringComparison.Ordinal);

    private static bool IsCommandAvailable(string command, string arguments)
    {
        try
        {
            using var probe = Process.Start(new ProcessStartInfo(command, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            probe?.WaitForExit(3000);
            return probe?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private string? FindStyxDirectory()
    {
        var candidate = Path.Combine(_env.ContentRootPath, "styx");
        return Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "index.js"))
            ? candidate
            : null;
    }

    private void EnsureConfig(string styxDir, string snapshotEndpoint, string sessionEndpoint)
    {
        var configPath = Path.Combine(styxDir, "bin", "config.js");
        var configText = File.Exists(configPath) ? File.ReadAllText(configPath) : null;
        var managedConfig = BuildManagedConfig(_options.Port, snapshotEndpoint, sessionEndpoint);

        if (!RequiresManagedConfigRepair(configText, snapshotEndpoint, sessionEndpoint))
        {
            return;
        }

        File.WriteAllText(configPath, managedConfig);
        var action = configText is null ? "Created" : "Repaired";
        _logger.LogInformation("[Styx] {Action} managed bin/config.js for D2Companion live capture.", action);
        AppendLog($"[Styx] {action} managed bin/config.js for D2Companion live capture.");
    }

    private async Task<bool> EnsureNodeModules(string styxDir, CancellationToken ct)
    {
        if (StyxDependenciesPresent(styxDir)) return true;

        _logger.LogInformation("[Styx] Installing Node.js dependencies (first run)...");
        var npmCommand = ResolveNpmCommand(_env.ContentRootPath);
        if (!IsNpmAvailable(npmCommand))
        {
            var message = StyxDependenciesMissingMessage(npmCommand);
            _logger.LogError("[Styx] {Message}", message);
            AppendLog("[Styx] " + message);
            _status.SetRunning(false);
            _status.SetError(message);
            return false;
        }

        using var npm = Process.Start(new ProcessStartInfo("cmd.exe", CmdArguments(npmCommand, "install"))
        {
            WorkingDirectory = styxDir,
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        if (npm is not null) await npm.WaitForExitAsync(ct);
        if (npm is null || npm.ExitCode != 0)
        {
            var message = $"npm install failed for Styx dependencies with exit code {npm?.ExitCode.ToString() ?? "unknown"}. Live capture is unavailable until dependencies install successfully.";
            _logger.LogError("[Styx] {Message}", message);
            AppendLog("[Styx] " + message);
            _status.SetRunning(false);
            _status.SetError(message);
            return false;
        }

        if (StyxDependenciesPresent(styxDir))
        {
            _logger.LogInformation("[Styx] Dependencies installed.");
            return true;
        }

        var missingAfterInstall = "npm install completed, but required Styx dependencies are still missing. Live capture is unavailable until dependencies install successfully.";
        _logger.LogError("[Styx] {Message}", missingAfterInstall);
        AppendLog("[Styx] " + missingAfterInstall);
        _status.SetRunning(false);
        _status.SetError(missingAfterInstall);
        return false;
    }

    private static bool IsNpmAvailable(string npmCommand)
    {
        try
        {
            using var probe = Process.Start(new ProcessStartInfo("cmd.exe", CmdArguments(npmCommand, "--version"))
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            probe?.WaitForExit(3000);
            return probe?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string CmdArguments(string command, string arguments)
        => $"/c \"\"{command}\" {arguments}\"";

    private static string JsString(string value)
        => "'" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "'";

    private static bool IsPortInUse(int port)
    {
        try
        {
            var listener = new TcpListener(System.Net.IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return false;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

}

public sealed record StyxProcessControlResult(bool Success, string Message)
{
    public static StyxProcessControlResult Ok(string message) => new(true, message);

    public static StyxProcessControlResult Fail(string message) => new(false, message);
}

internal sealed class WindowsProcessJob : IDisposable
{
    internal const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const int JobObjectExtendedLimitInformation = 9;
    private IntPtr _handle;

    private WindowsProcessJob(IntPtr handle) => _handle = handle;

    public static WindowsProcessJob? TryCreateKillOnClose(Process process, Action<string> appendLog, ILogger logger)
    {
        if (!OperatingSystem.IsWindows()) return null;

        var handle = CreateJobObjectW(IntPtr.Zero, null);
        if (handle == IntPtr.Zero)
        {
            LogFailure("create", Marshal.GetLastWin32Error(), appendLog, logger);
            return null;
        }

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JobObjectLimitKillOnJobClose,
            },
        };

        var length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var ptr = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformation, ptr, (uint)length))
            {
                LogFailure("configure", Marshal.GetLastWin32Error(), appendLog, logger);
                CloseHandle(handle);
                return null;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        if (!AssignProcessToJobObject(handle, process.Handle))
        {
            LogFailure("assign", Marshal.GetLastWin32Error(), appendLog, logger);
            CloseHandle(handle);
            return null;
        }

        appendLog("Managed proxy attached to Windows cleanup job.");
        return new WindowsProcessJob(handle);
    }

    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
        if (handle != IntPtr.Zero)
        {
            CloseHandle(handle);
        }
    }

    private static void LogFailure(string action, int error, Action<string> appendLog, ILogger logger)
    {
        var message = $"Could not {action} Windows cleanup job for Styx process. Error {error}.";
        logger.LogWarning("[Styx] {Message}", message);
        appendLog(message);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int jobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
