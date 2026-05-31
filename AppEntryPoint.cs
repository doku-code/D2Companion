using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;

[assembly: SupportedOSPlatform("windows")]

/// <summary>
/// Explicit entry point so we can declare [STAThread], which is required for
/// WinForms / WebView2 to initialise COM on the main thread.
/// </summary>
internal static class AppEntryPoint
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (IsWebOnly(args))
        {
            WebAppFactory.Create(args).Run();
            return;
        }

        // ── Start ASP.NET Core host on a dedicated background (MTA) thread ─────
        var hostReady = new System.Threading.ManualResetEventSlim(initialState: false);
        Exception? hostStartError = null;
        WebApplication? app = null;

        var hostThread = new System.Threading.Thread(() =>
        {
            try
            {
                app = WebAppFactory.Create(args);
                app.Lifetime.ApplicationStarted.Register(() => hostReady.Set());
                app.Run(); // blocks until shutdown
            }
            catch (Exception ex)
            {
                hostStartError = ex;
                hostReady.Set(); // unblock the main thread even on failure
            }
        });
        hostThread.Name = "ASP.NET Core host";
        hostThread.IsBackground = true;
        hostThread.SetApartmentState(System.Threading.ApartmentState.MTA);
        hostThread.Start();

        // Wait up to 30 s for the web server to be ready before opening the window
        hostReady.Wait(System.TimeSpan.FromSeconds(30));

        if (hostStartError is not null)
        {
            System.Windows.Forms.MessageBox.Show(
                $"D2 Companion failed to start:\n\n{hostStartError.Message}",
                "D2 Companion — Startup Error",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Error);
            return;
        }

        // ── Show the native window on the STA main thread ─────────────────────
        void StopManagedStyxBestEffort()
        {
            try
            {
                app?.Services.GetService<D2CompanionMvc.Extensions.Styx.Launcher.StyxProcessService>()
                    ?.StopOwnedProxyBestEffort(System.TimeSpan.FromSeconds(3));
            }
            catch
            {
                // Process-exit cleanup is best effort only.
            }
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => StopManagedStyxBestEffort();

        var styxProcess = app?.Services.GetRequiredService<D2CompanionMvc.Extensions.Styx.Launcher.StyxProcessService>();
        if (styxProcess is null)
        {
            System.Windows.Forms.MessageBox.Show(
                "D2 Companion failed to start the Styx process manager.",
                "D2 Companion â€” Startup Error",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Error);
            return;
        }

        System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.SystemAware);
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        System.Windows.Forms.Application.Run(new D2CompanionMvc.AppWindow(styxProcess));

        // ── Graceful shutdown when the window is closed ───────────────────────
        app?.StopAsync(System.TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
    }
    private static bool IsWebOnly(string[] args)
    {
        return args.Any(arg => string.Equals(arg, "--web-only", StringComparison.OrdinalIgnoreCase)) ||
               string.Equals(Environment.GetEnvironmentVariable("D2COMPANION_WEB_ONLY"), "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Environment.GetEnvironmentVariable("D2COMPANION_WEB_ONLY"), "true", StringComparison.OrdinalIgnoreCase);
    }
}
