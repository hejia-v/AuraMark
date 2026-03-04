using System.IO;
using System.Text.Json;
using AuraMark.Core;
using Microsoft.Web.WebView2.Core;

namespace AuraMark.App;

public partial class MainWindow
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private string _currentFilePath = "";

    public MainWindow()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            await MainWebView.EnsureCoreWebView2Async();

            MainWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            // Serve frontend from output\EditorView via virtual host mapping.
            var editorDir = Path.Combine(AppContext.BaseDirectory, "EditorView");
            Directory.CreateDirectory(editorDir);

            MainWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "app.auramark.local",
                editorDir,
                CoreWebView2HostResourceAccessKind.Allow);

            MainWebView.Source = new Uri("https://app.auramark.local/index.html");

            // Default save path
            var docDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AuraMark");
            Directory.CreateDirectory(docDir);
            _currentFilePath = Path.Combine(docDir, "Untitled.md");

            // Send Init message with existing file content (if any)
            var md = File.Exists(_currentFilePath) ? File.ReadAllText(_currentFilePath) : "# AuraMark\n\nStart typing...";
            PostToWeb(new WebMessagePayload { Type = "Init", Content = md });
        };
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var json = e.TryGetWebMessageAsString();
        if (string.IsNullOrWhiteSpace(json)) return;

        WebMessagePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<WebMessagePayload>(json, _jsonOptions);
        }
        catch
        {
            return;
        }
        if (payload is null) return;

        if (payload.Type.Equals("Update", StringComparison.OrdinalIgnoreCase))
        {
            SavingDot.Visibility = System.Windows.Visibility.Visible;
            try
            {
                File.WriteAllText(_currentFilePath, payload.Content);
                PostToWeb(new WebMessagePayload { Type = "Ack", Content = "Saved" });
            }
            finally
            {
                SavingDot.Visibility = System.Windows.Visibility.Collapsed;
            }
        }
    }

    private void PostToWeb(WebMessagePayload payload)
    {
        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        MainWebView.CoreWebView2.PostWebMessageAsString(json);
    }
}
