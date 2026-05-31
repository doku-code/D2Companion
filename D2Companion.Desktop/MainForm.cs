using D2Companion.Desktop.Configuration;
using D2Companion.Desktop.Services;
using Microsoft.Web.WebView2.WinForms;

namespace D2Companion.Desktop;

public sealed class MainForm : Form
{
    private readonly LauncherOptions _options;
    private readonly SidecarProcessManager _processManager;
    private readonly WebView2 _webView = new();
    private readonly ToolStripStatusLabel _statusLabel = new("Starting...");

    public MainForm(LauncherOptions options, SidecarProcessManager processManager)
    {
        _options = options;
        _processManager = processManager;

        Text = "D2 Companion";
        Width = 1280;
        Height = 840;
        MinimumSize = new Size(1024, 700);
        StartPosition = FormStartPosition.CenterScreen;

        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_statusLabel);

        _webView.Dock = DockStyle.Fill;
        Controls.Add(_webView);
        Controls.Add(statusStrip);

        Load += OnLoad;
        FormClosing += OnFormClosing;
    }

    private async void OnLoad(object? sender, EventArgs eventArgs)
    {
        try
        {
            _statusLabel.Text = "Starting D2 Companion services...";
            await _processManager.StartAsync();

            await _webView.EnsureCoreWebView2Async();
            _webView.Source = new Uri(_options.AppUrl);
            _statusLabel.Text = $"Ready - {_options.AppUrl}";
        }
        catch (Exception exception)
        {
            _statusLabel.Text = "Startup failed";
            MessageBox.Show(this, exception.Message, "D2 Companion", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        _processManager.Dispose();
    }
}
