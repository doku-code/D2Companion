using D2CompanionMvc.Extensions.Styx.Launcher;
using Microsoft.Web.WebView2.WinForms;

namespace D2CompanionMvc;

/// <summary>
/// Native desktop window that embeds the D2 Companion web UI via WebView2.
/// </summary>
internal sealed class AppWindow : Form
{
    private const string AppUrl = "http://127.0.0.1:5178";
    private const string ActiveStyxCloseWarning =
        "Styx is still running. Closing D2Companion will stop live capture and may disconnect Diablo II from Battle.net if Game.exe is routed through the local proxy.";

    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private readonly StyxProcessService _styxProcess;
    private bool _allowClose;

    public AppWindow(StyxProcessService styxProcess)
    {
        _styxProcess = styxProcess;

        Text = "D2 Companion";
        Width = 1400;
        Height = 900;
        MinimumSize = new Size(1080, 720);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = TryLoadWindowIcon();

        Controls.Add(_webView);
        Load += OnLoad;
        FormClosing += OnFormClosing;
    }

    private static Icon? TryLoadWindowIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "wwwroot", "favicon.ico");
        return File.Exists(path) ? new Icon(path) : null;
    }

    private async void OnLoad(object? sender, EventArgs e)
    {
        try
        {
            await _webView.EnsureCoreWebView2Async();

            // Disable the default right-click context menu (looks out of place)
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            // Disable browser status bar at bottom of window
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

            _webView.Source = new Uri(AppUrl);
        }
        catch (Exception ex)
        {
            // WebView2 runtime not installed — fall back to opening in the default browser
            MessageBox.Show(
                $"Could not initialise the embedded browser (WebView2).\n" +
                $"The app will open in your default browser instead.\n\n" +
                $"To fix this, install the Microsoft Edge WebView2 Runtime:\n" +
                $"https://developer.microsoft.com/microsoft-edge/webview2/\n\n" +
                $"Error: {ex.Message}",
                "D2 Companion",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppUrl)
            {
                UseShellExecute = true
            });

            Close();
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose)
            return;

        if (!_styxProcess.OwnsRunningProcess)
            return;

        if (e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing)
        {
            _styxProcess.StopOwnedProxyBestEffort(TimeSpan.FromSeconds(3));
            return;
        }

        if (_styxProcess.ShouldWarnBeforeClose && !ConfirmStopStyxAndClose())
        {
            e.Cancel = true;
            return;
        }

        var result = _styxProcess.StopOwnedProxyOnShutdownAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (!result.Success)
        {
            MessageBox.Show(
                result.Message,
                "D2 Companion",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            e.Cancel = true;
            return;
        }

        _allowClose = true;
    }

    private bool ConfirmStopStyxAndClose()
    {
        using var dialog = new Form
        {
            Text = "D2 Companion",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(560, 170),
        };

        var message = new Label
        {
            Text = ActiveStyxCloseWarning,
            AutoSize = false,
            Location = new Point(18, 18),
            Size = new Size(524, 82),
        };
        var stopAndClose = new Button
        {
            Text = "Stop Styx and close",
            DialogResult = DialogResult.OK,
            Location = new Point(278, 116),
            Size = new Size(150, 32),
        };
        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(438, 116),
            Size = new Size(104, 32),
        };

        dialog.Controls.Add(message);
        dialog.Controls.Add(stopAndClose);
        dialog.Controls.Add(cancel);
        dialog.AcceptButton = stopAndClose;
        dialog.CancelButton = cancel;

        return dialog.ShowDialog(this) == DialogResult.OK;
    }
}
