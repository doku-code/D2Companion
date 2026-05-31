using System.Diagnostics;
using D2CompanionMvc.Extensions.Styx.Adapters;
using D2CompanionMvc.Extensions.Styx.Ingestion;
using D2CompanionMvc.Extensions.Styx.Launcher;
using D2CompanionMvc.Options;
using D2CompanionMvc.Services.Assets;
using D2CompanionMvc.Services.Catalog;
using D2CompanionMvc.Services.GameData;
using D2CompanionMvc.Services.Importers.MuleLogger;
using D2CompanionMvc.Services.Items.Rendering;
using D2CompanionMvc.Services.LiveUpdate;
using D2CompanionMvc.Services.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

// Top-level statements cannot have [STAThread], so we use an explicit entry point.
// See AppEntryPoint.cs — the actual [STAThread] Main lives there.
// This file sets up the WebApplication factory used by both dev (hosted) and prod (WinForms).

internal static class WebAppFactory
{
    internal static WebApplication Create(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddDebug();
        builder.WebHost.UseUrls("http://127.0.0.1:5178");

        builder.Services.AddControllersWithViews();

        // Log every model-binding / JSON-deserialization failure before returning 400.
        // This surfaces the exact field name and error message in the server log so we
        // can quickly diagnose Styx payload mismatches (e.g. wrong JSON type for a field).
        builder.Services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("ModelBinding");

                var errors = string.Join(" | ", context.ModelState
                    .Where(e => e.Value?.Errors.Count > 0)
                    .SelectMany(e => e.Value!.Errors.Select(err =>
                        $"[{e.Key}] {(string.IsNullOrEmpty(err.ErrorMessage) ? err.Exception?.Message : err.ErrorMessage)}")));

                logger.LogError("[HTTP 400] Model binding failed — {Errors}", errors);

                return new BadRequestObjectResult(new
                {
                    error   = "Request body could not be deserialized.",
                    details = errors,
                });
            };
        });
        builder.Services.Configure<CompanionAppOptions>(builder.Configuration.GetSection("CompanionApp"));
        // D2 game data (1.13c TXT tables): loaded once at startup from a path under
        // ContentRootPath so dev (visual studio launch) and prod (publish/) both work.
        builder.Services.AddSingleton<D2GameData>(sp =>
        {
            var env = sp.GetRequiredService<IWebHostEnvironment>();
            var opts = sp.GetRequiredService<IOptions<CompanionAppOptions>>().Value.GameData;
            var dir = Path.IsPathRooted(opts.TxtDirectory)
                ? opts.TxtDirectory
                : Path.Combine(env.ContentRootPath, opts.TxtDirectory);
            return new D2GameData(dir);
        });
        builder.Services.AddSingleton<D2ItemLookupService>();
        builder.Services.AddSingleton<AssetPackService>();
        builder.Services.AddSingleton<D2StatLookupService>();
        builder.Services.AddSingleton<ID2StringResolver, FallbackD2StringResolver>();
        builder.Services.AddSingleton<D2StatResolver>();
        builder.Services.AddSingleton<D2TooltipRenderer>();
        builder.Services.AddSingleton<StyxToCanonicalItemAdapter>();

        builder.Services.AddSingleton<JsonCatalogService>();
        builder.Services.AddSingleton<SqliteCompanionStore>();
        builder.Services.AddSingleton<MuleLoggerImportService>();
        builder.Services.AddSingleton<ICatalogService, LiveCatalogService>();
        builder.Services.AddSingleton<ICompanionArchiveRepository, JsonCompanionArchiveRepository>();
        builder.Services.AddSingleton<ILiveUpdateService, LiveUpdateService>();
        builder.Services.AddSingleton<StyxStatus>();
        builder.Services.AddSingleton<IStyxIngestionService, StyxIngestionService>();
        builder.Services.AddSingleton<StyxProcessService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<StyxProcessService>());

        var app = builder.Build();

        // ── Startup diagnostics ───────────────────────────────────────────────
        // Printed once at startup so it's easy to confirm which build / paths are
        // in use — especially useful when switching between dev (dotnet run) and
        // the published exe.
        LogStartupDiagnostics(app);

        // Wire Styx status changes to the SSE channel so the UI gets push updates
        // (no polling). Fired by SetRunning() and RecordSnapshot().
        var styxStatus = app.Services.GetRequiredService<StyxStatus>();
        var liveUpdate = app.Services.GetRequiredService<ILiveUpdateService>();
        styxStatus.Changed += s => liveUpdate.NotifyStyxStatus(s.ToJson());

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler(err => err.Run(async ctx =>
            {
                ctx.Response.StatusCode = 500;
                await ctx.Response.WriteAsJsonAsync(new { error = "An unexpected error occurred." });
            }));
            app.UseHsts();
        }

        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = context =>
            {
                if (app.Environment.IsDevelopment())
                {
                    context.Context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
                    context.Context.Response.Headers.Pragma = "no-cache";
                    context.Context.Response.Headers.Expires = "0";
                }
            }
        });
        app.UseD2AssetPackFallback();

        app.UseRouting();
        app.UseAuthorization();

        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}");

        return app;
    }

    // ─────────────────────────────────────────────────────────────────────────
    private static void LogStartupDiagnostics(WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("Startup");

        var env     = app.Services.GetRequiredService<IWebHostEnvironment>();
        var opts    = app.Services.GetRequiredService<IOptions<CompanionAppOptions>>().Value;
        var dataDir = Path.IsPathRooted(opts.Catalog.DataDirectory)
            ? opts.Catalog.DataDirectory
            : Path.GetFullPath(Path.Combine(env.ContentRootPath, opts.Catalog.DataDirectory));
        var sqliteFile  = Path.Combine(dataDir, opts.Catalog.DatabaseFileName);
        var txtDir      = Path.IsPathRooted(opts.GameData.TxtDirectory)
            ? opts.GameData.TxtDirectory
            : Path.GetFullPath(Path.Combine(env.ContentRootPath, opts.GameData.TxtDirectory));
        var styxDir     = Path.Combine(env.ContentRootPath, "styx");

        // Detect rough launch mode: published single-file exe vs dotnet run vs Desktop launcher
        var exePath     = Environment.ProcessPath ?? "(unknown)";
        var launchMode  = exePath.Contains("dotnet", StringComparison.OrdinalIgnoreCase)
            ? "dotnet run / Visual Studio"
            : "published executable";

        logger.LogInformation("===========================================================");
        logger.LogInformation("[Startup] D2 Companion — {Env} mode | {LaunchMode}",
            env.EnvironmentName, launchMode);
        logger.LogInformation("[Startup] Exe           : {Exe}",   exePath);
        logger.LogInformation("[Startup] Working dir   : {Cwd}",   Environment.CurrentDirectory);
        logger.LogInformation("[Startup] Content root  : {Root}",  env.ContentRootPath);
        logger.LogInformation("[Startup] Web root      : {Web}",   env.WebRootPath);
        logger.LogInformation("[Startup] Data dir      : {Data}  [{Exists}]",
            dataDir, Directory.Exists(dataDir) ? "OK" : "MISSING");
        logger.LogInformation("[Startup] SQLite        : {Db}  [{Exists}]",
            sqliteFile, File.Exists(sqliteFile) ? "exists" : "will be created");
        logger.LogInformation("[Startup] TXT tables    : {Txt}  [{Exists}]",
            txtDir, Directory.Exists(txtDir) ? "OK" : "MISSING");
        logger.LogInformation("[Startup] Styx dir      : {Styx}  [{Exists}]",
            styxDir, Directory.Exists(styxDir) ? "OK" : "MISSING");
        logger.LogInformation("[Startup] Listening on  : http://127.0.0.1:5178");
        logger.LogInformation("[Startup] Companion API : http://127.0.0.1:5178/api/ingest/styx/snapshot");
        logger.LogInformation("===========================================================");
    }
}
