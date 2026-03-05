
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AuraMark.App.Models;
using AuraMark.Core;
using Markdig;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace AuraMark.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const string VirtualHostName = "app.auramark.local";
    private const long LargeFileThresholdBytes = 5 * 1024 * 1024;
    private const int AutosaveDelayMilliseconds = 500;
    private const int ImmersiveTypingThresholdMilliseconds = 3000;
    private const int UiAnimationMilliseconds = 150;
    private const double SidebarExpandedWidth = 280;
    private const double MouseWakeDistance = 100;
    private static readonly Regex HeadingRegex = new(@"^(#{1,6})\s+(.+?)\s*$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly HashSet<string> MarkdownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md",
        ".markdown",
        ".txt",
    };

    private static readonly Dictionary<string, string> ImageContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".svg"] = "image/svg+xml",
        [".webp"] = "image/webp",
        [".bmp"] = "image/bmp",
    };

    private readonly DispatcherTimer _autosaveTimer;
    private readonly DispatcherTimer _externalReloadTimer;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ObservableCollection<FileTreeNode> _fileTreeNodes = [];
    private readonly ObservableCollection<OutlineItem> _outlineItems = [];

    private CoreWebView2? _webViewCore;
    private FileSystemWatcher? _fileWatcher;
    private DateTime _ignoreWatcherUntilUtc = DateTime.MinValue;
    private DateTime _typingSessionStartUtc = DateTime.MinValue;
    private DateTime _lastTypingEventUtc = DateTime.MinValue;
    private Point? _lastMousePoint;

    private string _editorAssetsRoot = string.Empty;
    private string _workspaceRoot = string.Empty;
    private string _currentFilePath = string.Empty;
    private string _currentMarkdown = string.Empty;
    private string _pendingMarkdown = string.Empty;
    private string _pendingSaveRetryContent = string.Empty;
    private string _queuedDocumentForWeb = string.Empty;

    private bool _webReady;
    private bool _editorInitialized;
    private bool _dirty;
    private bool _isSaving;
    private bool _isSidebarVisible;
    private bool _isImmersive;
    private bool _sidebarBeforeImmersive;
    private bool _externalReloadPending;
    private bool _inputFrozen;
    private bool _e2eStartupPending;
    private string _e2eStartupMarkdown = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<FileTreeNode> FileTreeNodes => _fileTreeNodes;

    public ObservableCollection<OutlineItem> OutlineItems => _outlineItems;

    public MainWindow()
    {
        InitializeComponent();
        Title = "AuraMark";
        DataContext = this;

        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AutosaveDelayMilliseconds) };
        _autosaveTimer.Tick += OnAutosaveTimerTick;

        _externalReloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _externalReloadTimer.Tick += OnExternalReloadTimerTick;

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ConfigureE2eFromArgs();
        await InitializeWebViewAsync();

        var docsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "AuraMark");
        Directory.CreateDirectory(docsRoot);

        _currentFilePath = Path.Combine(docsRoot, "Untitled.md");
        await LoadDocumentAsync(_currentFilePath, createIfMissing: true);

        ApplySidebarVisualState(false, immediate: true);
        ApplyTopBarVisualState(true, immediate: true);
    }

    private void ConfigureE2eFromArgs()
    {
        var args = Environment.GetCommandLineArgs();
        if (args.Any(arg => arg.Equals("--e2e", StringComparison.OrdinalIgnoreCase)))
        {
            _e2eStartupMarkdown = "# AuraMark E2E\n\nAuraMark E2E typing sample\nline2\n";
            _e2eStartupPending = true;
        }
    }

    private async Task InitializeWebViewAsync()
    {
        await MainWebView.EnsureCoreWebView2Async();
        _webViewCore = MainWebView.CoreWebView2;

        _webViewCore.Settings.IsStatusBarEnabled = false;
        _webViewCore.Settings.AreDefaultContextMenusEnabled = false;

        _webViewCore.WebMessageReceived += OnWebMessageReceived;
        _webViewCore.NavigationStarting += OnNavigationStarting;
        _webViewCore.WebResourceRequested += OnWebResourceRequested;
        _webViewCore.AddWebResourceRequestedFilter($"https://{VirtualHostName}/*", CoreWebView2WebResourceContext.Image);

        _editorAssetsRoot = Path.Combine(AppContext.BaseDirectory, "EditorView");
        Directory.CreateDirectory(_editorAssetsRoot);
        _webViewCore.SetVirtualHostNameToFolderMapping(
            VirtualHostName,
            _editorAssetsRoot,
            CoreWebView2HostResourceAccessKind.DenyCors);

        MainWebView.Source = new Uri($"https://{VirtualHostName}/index.html");
    }

    private async void OnNewFileClicked(object sender, RoutedEventArgs e)
    {
        await CreateNewDocumentAsync();
    }

    private async void OnOpenFileClicked(object sender, RoutedEventArgs e)
    {
        await OpenWithDialogAsync();
    }

    private async void OnSaveNowClicked(object sender, RoutedEventArgs e)
    {
        await SavePendingChangesAsync(force: true);
    }

    private async void OnExportHtmlClicked(object sender, RoutedEventArgs e)
    {
        await ExportHtmlAsync();
    }

    private void OnToggleSidebarClicked(object sender, RoutedEventArgs e)
    {
        ToggleSidebar();
    }

    private async void OnRetrySaveClicked(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_pendingSaveRetryContent))
        {
            _pendingMarkdown = _pendingSaveRetryContent;
        }

        await SavePendingChangesAsync(force: true);
    }

    private void OnMinimizeClicked(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.L)
        {
            ToggleSidebar();
            e.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.Control && e.Key == Key.O)
        {
            await OpenWithDialogAsync();
            e.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.Control && e.Key == Key.N)
        {
            await CreateNewDocumentAsync();
            e.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.Control && e.Key == Key.S)
        {
            await SavePendingChangesAsync(force: true);
            e.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.Control && e.Key == Key.Oem2)
        {
            SendCommand(new HostCommand { Name = IpcCommands.ToggleSourceMode });
            e.Handled = true;
            return;
        }
    }
    private void OnWindowMouseMove(object sender, MouseEventArgs e)
    {
        var current = e.GetPosition(this);

        if (_isImmersive && _lastMousePoint.HasValue)
        {
            var distance = (current - _lastMousePoint.Value).Length;
            if (distance >= MouseWakeDistance)
            {
                ExitImmersiveMode();
            }
        }

        _lastMousePoint = current;
    }

    private async void OnAutosaveTimerTick(object? sender, EventArgs e)
    {
        _autosaveTimer.Stop();
        await SavePendingChangesAsync(force: false);
    }

    private async void OnExternalReloadTimerTick(object? sender, EventArgs e)
    {
        _externalReloadTimer.Stop();
        if (!_externalReloadPending)
        {
            return;
        }

        _externalReloadPending = false;
        await ReloadFromDiskAfterExternalChangeAsync();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _autosaveTimer.Stop();
        _externalReloadTimer.Stop();
        _fileWatcher?.Dispose();
        _fileWatcher = null;
    }

    private async Task OpenWithDialogAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open Markdown",
            Filter = "Markdown Files|*.md;*.markdown;*.txt|All Files|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) == true)
        {
            await LoadDocumentAsync(dialog.FileName, createIfMissing: false);
        }
    }

    private async Task CreateNewDocumentAsync()
    {
        var docsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "AuraMark");
        Directory.CreateDirectory(docsRoot);

        var path = BuildUniqueUntitledPath(docsRoot);
        await LoadDocumentAsync(path, createIfMissing: true);
    }

    private static string BuildUniqueUntitledPath(string root)
    {
        for (var i = 0; i < 500; i++)
        {
            var file = i == 0 ? "Untitled.md" : $"Untitled-{i}.md";
            var candidate = Path.Combine(root, file);
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(root, $"Untitled-{DateTime.Now:yyyyMMdd-HHmmss}.md");
    }

    private async Task LoadDocumentAsync(string path, bool createIfMissing)
    {
        SetState(EditorState.Loading);
        ShowLoading(true, "Loading document...");
        HideError();

        if (createIfMissing && !File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            await File.WriteAllTextAsync(path, "# AuraMark\n\n", Encoding.UTF8);
        }

        if (!File.Exists(path))
        {
            ShowSoftError("File does not exist.");
            ShowLoading(false);
            SetState(EditorState.Idle);
            return;
        }

        var info = new FileInfo(path);
        if (info.Length > LargeFileThresholdBytes)
        {
            ShowLoading(true, "Loading large file...");
            _webViewCore?.Stop();
        }

        string markdown;
        await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 8192, useAsync: true))
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            markdown = await reader.ReadToEndAsync();
        }

        _currentFilePath = path;
        _workspaceRoot = Path.GetDirectoryName(path) ?? string.Empty;
        _currentMarkdown = markdown;
        _pendingMarkdown = markdown;
        _dirty = false;
        _inputFrozen = false;

        FileNameText.Text = Path.GetFileName(path);
        SetSavingDot(false);
        RefreshFileTree();
        UpdateOutline(markdown);
        AttachFileWatcher(path);
        QueueDocumentToWeb(markdown);

        SetState(EditorState.Editing);
    }

    private void QueueDocumentToWeb(string markdown)
    {
        _queuedDocumentForWeb = markdown;
        TryPushQueuedDocumentToWeb();
    }

    private void TryPushQueuedDocumentToWeb()
    {
        if (!_webReady || string.IsNullOrEmpty(_queuedDocumentForWeb))
        {
            return;
        }

        ShowLoading(true, "Rendering...");

        if (!_editorInitialized)
        {
            PostToWeb(new WebMessagePayload
            {
                Type = IpcTypes.Init,
                Content = _queuedDocumentForWeb,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });
            _editorInitialized = true;
        }
        else
        {
            SendCommand(new HostCommand { Name = IpcCommands.ReplaceAll, Content = _queuedDocumentForWeb });
        }

        _queuedDocumentForWeb = string.Empty;
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string rawJson;
        try
        {
            rawJson = e.TryGetWebMessageAsString();
        }
        catch
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return;
        }

        if (Encoding.UTF8.GetByteCount(rawJson) > IpcLimits.MaxContentBytes + 4096)
        {
            PostError("E_IPC_PARSE: payload too large");
            return;
        }

        WebMessagePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<WebMessagePayload>(rawJson, _jsonOptions);
        }
        catch
        {
            PostError("E_IPC_PARSE: invalid json");
            return;
        }

        if (payload is null || !IpcTypes.Allowed.Contains(payload.Type))
        {
            PostError("E_IPC_PARSE: unknown type");
            return;
        }

        payload.Content ??= string.Empty;
        if (Encoding.UTF8.GetByteCount(payload.Content) > IpcLimits.MaxContentBytes)
        {
            PostError("E_IPC_PARSE: content too large");
            return;
        }

        switch (payload.Type)
        {
            case IpcTypes.Ack:
                HandleAck(payload.Content);
                break;
            case IpcTypes.Update:
                HandleMarkdownUpdate(payload.Content);
                break;
            case IpcTypes.Command:
                HandleWebCommand(payload.Content);
                break;
            case IpcTypes.Error:
                ShowSoftError(payload.Content);
                break;
        }
    }
    private void HandleAck(string content)
    {
        if (content.Equals("Ready", StringComparison.OrdinalIgnoreCase))
        {
            _webReady = true;
            TryPushQueuedDocumentToWeb();
            return;
        }

        if (content.Equals("Rendered", StringComparison.OrdinalIgnoreCase))
        {
            ShowLoading(false);

            if (_e2eStartupPending && !string.IsNullOrWhiteSpace(_e2eStartupMarkdown))
            {
                _e2eStartupPending = false;
                SendCommand(new HostCommand { Name = IpcCommands.E2eSetMarkdown, Content = _e2eStartupMarkdown });
            }

            return;
        }
    }

    private void HandleMarkdownUpdate(string markdown)
    {
        if (_inputFrozen)
        {
            return;
        }

        _pendingMarkdown = markdown;
        _dirty = true;
        UpdateOutline(markdown);
        SetState(EditorState.Dirty);
        SetSavingDot(true);
        HideError();

        _autosaveTimer.Stop();
        _autosaveTimer.Start();
        TrackTyping();
    }

    private void HandleWebCommand(string json)
    {
        if (!HostCommand.TryParse(json, out var command) || command is null)
        {
            return;
        }

        if (command.Name.Equals(IpcCommands.ToggleSidebar, StringComparison.Ordinal))
        {
            ToggleSidebar();
        }
    }

    private void TrackTyping()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastTypingEventUtc).TotalMilliseconds > 1200)
        {
            _typingSessionStartUtc = now;
        }

        _lastTypingEventUtc = now;

        if (!_isImmersive && _typingSessionStartUtc != DateTime.MinValue &&
            (now - _typingSessionStartUtc).TotalMilliseconds >= ImmersiveTypingThresholdMilliseconds)
        {
            EnterImmersiveMode();
        }
    }

    private void EnterImmersiveMode()
    {
        if (_isImmersive)
        {
            return;
        }

        _isImmersive = true;
        _sidebarBeforeImmersive = _isSidebarVisible;
        ApplyTopBarVisualState(false);
        ApplySidebarVisualState(false);
        SendCommand(new HostCommand { Name = IpcCommands.SetImmersive, Value = true });
        SetState(EditorState.Immersive);
    }

    private void ExitImmersiveMode()
    {
        if (!_isImmersive)
        {
            return;
        }

        _isImmersive = false;
        ApplyTopBarVisualState(true);
        ApplySidebarVisualState(_sidebarBeforeImmersive);
        SendCommand(new HostCommand { Name = IpcCommands.SetImmersive, Value = false });
        SetState(EditorState.Editing);
    }

    private async Task SavePendingChangesAsync(bool force)
    {
        if (_isSaving)
        {
            return;
        }

        if (!_dirty && !force)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_currentFilePath))
        {
            return;
        }

        _isSaving = true;
        SetState(EditorState.Saving);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_currentFilePath) ?? ".");
            await File.WriteAllTextAsync(_currentFilePath, _pendingMarkdown, Encoding.UTF8);
            _ignoreWatcherUntilUtc = DateTime.UtcNow.AddMilliseconds(1200);

            _currentMarkdown = _pendingMarkdown;
            _dirty = false;
            _pendingSaveRetryContent = string.Empty;
            HideError();
            SetSavingDot(false);
            SetState(EditorState.Editing);

            PostToWeb(new WebMessagePayload
            {
                Type = IpcTypes.Ack,
                Content = "Saved",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });
        }
        catch (UnauthorizedAccessException)
        {
            _pendingSaveRetryContent = _pendingMarkdown;
            SetState(EditorState.Dirty);
            ShowSoftError("E_SAVE_DENIED: no permission, retry or save elsewhere.");
        }
        catch (IOException)
        {
            _pendingSaveRetryContent = _pendingMarkdown;
            SetState(EditorState.Dirty);
            ShowSoftError("E_SAVE_IO: write failed, retry.");
        }
        finally
        {
            _isSaving = false;
        }
    }

    private async Task ReloadFromDiskAfterExternalChangeAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath) || !File.Exists(_currentFilePath))
        {
            return;
        }

        if (_isSaving)
        {
            return;
        }

        string diskMarkdown;
        await using (var stream = new FileStream(_currentFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 8192, useAsync: true))
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            diskMarkdown = await reader.ReadToEndAsync();
        }

        if (diskMarkdown == _pendingMarkdown)
        {
            return;
        }

        if (_dirty)
        {
            await SaveConflictSnapshotAsync(_pendingMarkdown);
        }

        SetState(EditorState.ExternalSync);
        _inputFrozen = true;
        SendCommand(new HostCommand { Name = IpcCommands.FreezeInput });

        _currentMarkdown = diskMarkdown;
        _pendingMarkdown = diskMarkdown;
        _dirty = false;
        SetSavingDot(false);
        UpdateOutline(diskMarkdown);
        QueueDocumentToWeb(diskMarkdown);

        await Task.Delay(120);
        _inputFrozen = false;
        SendCommand(new HostCommand { Name = IpcCommands.ResumeInput });
        SetState(EditorState.Editing);
    }

    private async Task SaveConflictSnapshotAsync(string markdown)
    {
        try
        {
            var snapshotRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AuraMark",
                "snapshots");
            Directory.CreateDirectory(snapshotRoot);

            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(_currentFilePath));
            var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
            var name = $"{hash}-{DateTime.UtcNow:yyyyMMddHHmmss}.md";
            var path = Path.Combine(snapshotRoot, name);
            await File.WriteAllTextAsync(path, markdown, Encoding.UTF8);
        }
        catch
        {
            // Best effort snapshot.
        }
    }
    private async Task ExportHtmlAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export HTML",
            Filter = "HTML|*.html",
            FileName = Path.GetFileNameWithoutExtension(_currentFilePath) + ".html",
            AddExtension = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var htmlBody = Markdown.ToHtml(_pendingMarkdown);
        var title = Path.GetFileNameWithoutExtension(_currentFilePath);
        var html = $@"<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""UTF-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
  <title>{title}</title>
  <style>
    body {{ margin: 2rem auto; max-width: 900px; line-height: 1.7; color: #434C5E; font-family: ""Noto Sans SC"", ""Segoe UI"", sans-serif; }}
    pre, code {{ font-family: ""Fira Code"", Consolas, monospace; }}
    pre {{ padding: 1rem; border-radius: 8px; background: #F3F4F6; overflow: auto; }}
    img {{ max-width: 100%; border-radius: 8px; }}
    table {{ border-collapse: collapse; width: 100%; }}
    th, td {{ border: 1px solid rgba(67, 76, 94, 0.2); padding: 0.45rem 0.6rem; }}
  </style>
</head>
<body>
{htmlBody}
</body>
</html>";

        await File.WriteAllTextAsync(dialog.FileName, html, Encoding.UTF8);
        SetState(EditorState.Editing, "Exported HTML");
    }

    private void AttachFileWatcher(string path)
    {
        _fileWatcher?.Dispose();
        _fileWatcher = null;

        var directory = Path.GetDirectoryName(path);
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName) || !Directory.Exists(directory))
        {
            return;
        }

        _fileWatcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };
        _fileWatcher.Changed += OnWatchedFileChanged;
        _fileWatcher.Created += OnWatchedFileChanged;
        _fileWatcher.Renamed += OnWatchedFileChanged;
    }

    private void OnWatchedFileChanged(object sender, FileSystemEventArgs e)
    {
        if (DateTime.UtcNow < _ignoreWatcherUntilUtc)
        {
            return;
        }

        _externalReloadPending = true;
        _externalReloadTimer.Stop();
        _externalReloadTimer.Start();
    }

    private void UpdateOutline(string markdown)
    {
        _outlineItems.Clear();

        var index = 0;
        foreach (Match match in HeadingRegex.Matches(markdown))
        {
            if (!match.Success)
            {
                continue;
            }

            var level = match.Groups[1].Value.Length;
            var title = match.Groups[2].Value.Trim();
            if (title.Length == 0)
            {
                continue;
            }

            _outlineItems.Add(new OutlineItem
            {
                Index = index++,
                Level = level,
                Text = title,
                DisplayText = $"{new string(' ', (level - 1) * 2)}{title}",
            });
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OutlineItems)));
    }

    private void RefreshFileTree()
    {
        _fileTreeNodes.Clear();

        if (string.IsNullOrWhiteSpace(_workspaceRoot) || !Directory.Exists(_workspaceRoot))
        {
            return;
        }

        _fileTreeNodes.Add(BuildDirectoryNode(_workspaceRoot, depth: 0));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FileTreeNodes)));
    }

    private FileTreeNode BuildDirectoryNode(string directory, int depth)
    {
        var name = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        name = Path.GetFileName(name);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = directory;
        }

        var node = new FileTreeNode
        {
            Name = name,
            FullPath = directory,
            IsDirectory = true,
        };

        if (depth >= 4)
        {
            return node;
        }

        try
        {
            foreach (var subDirectory in Directory.EnumerateDirectories(directory).OrderBy(Path.GetFileName))
            {
                node.Children.Add(BuildDirectoryNode(subDirectory, depth + 1));
            }

            foreach (var file in Directory.EnumerateFiles(directory).OrderBy(Path.GetFileName))
            {
                if (!ShouldShowInFileTree(file))
                {
                    continue;
                }

                node.Children.Add(new FileTreeNode
                {
                    Name = Path.GetFileName(file),
                    FullPath = file,
                    IsDirectory = false,
                });
            }
        }
        catch
        {
            // Ignore inaccessible folders.
        }

        return node;
    }

    private bool ShouldShowInFileTree(string filePath)
    {
        if (filePath.Equals(_currentFilePath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return MarkdownExtensions.Contains(Path.GetExtension(filePath));
    }

    private async void OnFileTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not FileTreeNode node || node.IsDirectory)
        {
            return;
        }

        if (!File.Exists(node.FullPath))
        {
            return;
        }

        await LoadDocumentAsync(node.FullPath, createIfMissing: false);
    }

    private void OnOutlineSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OutlineList.SelectedItem is not OutlineItem item)
        {
            return;
        }

        SendCommand(new HostCommand
        {
            Name = IpcCommands.ScrollToHeading,
            Index = item.Index,
        });

        OutlineList.SelectedItem = null;
    }
    private void ToggleSidebar()
    {
        _isSidebarVisible = !_isSidebarVisible;
        if (_isImmersive)
        {
            return;
        }

        ApplySidebarVisualState(_isSidebarVisible);
    }

    private void ApplySidebarVisualState(bool visible, bool immediate = false)
    {
        var targetWidth = visible ? SidebarExpandedWidth : 0d;

        if (immediate)
        {
            SidebarContainer.Width = targetWidth;
            SidebarContainer.Opacity = visible ? 1 : 0;
            SidebarContainer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        if (visible)
        {
            SidebarContainer.Visibility = Visibility.Visible;
        }

        var duration = TimeSpan.FromMilliseconds(UiAnimationMilliseconds);
        var widthAnimation = new DoubleAnimation(targetWidth, duration)
        {
            EasingFunction = new QuadraticEase(),
        };
        widthAnimation.Completed += (_, _) =>
        {
            if (!visible)
            {
                SidebarContainer.Visibility = Visibility.Collapsed;
            }
        };

        SidebarContainer.BeginAnimation(WidthProperty, widthAnimation);
        FadeElement(SidebarContainer, visible);
    }

    private void ApplyTopBarVisualState(bool visible, bool immediate = false)
    {
        if (immediate)
        {
            TopBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            TopBar.Opacity = visible ? 1 : 0;
            return;
        }

        FadeElement(TopBar, visible);
    }

    private void FadeElement(UIElement element, bool visible)
    {
        if (visible)
        {
            element.Visibility = Visibility.Visible;
        }

        if (element.RenderTransform is not TranslateTransform translate)
        {
            translate = new TranslateTransform();
            element.RenderTransform = translate;
        }

        var duration = TimeSpan.FromMilliseconds(UiAnimationMilliseconds);
        var opacityAnimation = new DoubleAnimation(visible ? 1 : 0, duration)
        {
            EasingFunction = new QuadraticEase(),
        };
        opacityAnimation.Completed += (_, _) =>
        {
            if (!visible)
            {
                element.Visibility = Visibility.Collapsed;
            }
        };

        var offsetAnimation = new DoubleAnimation(visible ? 0 : 2, duration)
        {
            EasingFunction = new QuadraticEase(),
        };

        element.BeginAnimation(OpacityProperty, opacityAnimation);
        translate.BeginAnimation(TranslateTransform.YProperty, offsetAnimation);
    }

    private void SetSavingDot(bool visible)
    {
        SavingDot.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        SavingDot.Opacity = visible ? 0.42 : 0;
    }

    private void ShowLoading(bool visible, string? text = null)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            LoadingText.Text = text;
        }

        if (visible)
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingOverlay.Opacity = 1;
            return;
        }

        LoadingOverlay.Visibility = Visibility.Collapsed;
        LoadingOverlay.Opacity = 0;
    }

    private void ShowSoftError(string message)
    {
        ErrorText.Text = message;
        FadeElement(ErrorToast, visible: true);
    }

    private void HideError()
    {
        FadeElement(ErrorToast, visible: false);
    }

    private void SetState(EditorState state, string? hint = null)
    {
        var label = state.ToString();
        if (!string.IsNullOrWhiteSpace(hint))
        {
            label = $"{label} - {hint}";
        }

        StateText.Text = label;
    }

    private void SendCommand(HostCommand command)
    {
        PostToWeb(new WebMessagePayload
        {
            Type = IpcTypes.Command,
            Content = command.ToJson(),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        });
    }

    private void PostError(string message)
    {
        PostToWeb(new WebMessagePayload
        {
            Type = IpcTypes.Error,
            Content = message,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        });
    }

    private void PostToWeb(WebMessagePayload payload)
    {
        if (_webViewCore is null)
        {
            return;
        }

        if (!_webReady && payload.Type is not IpcTypes.Init and not IpcTypes.Error)
        {
            return;
        }

        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        _webViewCore.PostWebMessageAsString(json);
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
        {
            return;
        }

        if (uri.Scheme.Equals("about", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!uri.Host.Equals(VirtualHostName, StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
        }
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (_webViewCore is null || string.IsNullOrWhiteSpace(_currentFilePath))
        {
            return;
        }

        if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri))
        {
            return;
        }

        if (!uri.Host.Equals(VirtualHostName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var decodedPath = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')).Replace('/', Path.DirectorySeparatorChar);
        var extension = Path.GetExtension(decodedPath);
        if (!ImageContentTypes.ContainsKey(extension))
        {
            return;
        }

        var staticAssetPath = Path.Combine(_editorAssetsRoot, decodedPath);
        if (File.Exists(staticAssetPath))
        {
            return;
        }

        var resolvedImagePath = ResolveImagePath(uri, decodedPath);
        if (string.IsNullOrWhiteSpace(resolvedImagePath) || !File.Exists(resolvedImagePath))
        {
            return;
        }

        if (!IsPathAllowed(resolvedImagePath))
        {
            return;
        }

        try
        {
            var stream = File.OpenRead(resolvedImagePath);
            var headers = $"Content-Type: {ImageContentTypes[Path.GetExtension(resolvedImagePath)]}\r\nCache-Control: no-cache";
            e.Response = _webViewCore.Environment.CreateWebResourceResponse(stream, 200, "OK", headers);
        }
        catch
        {
            // Ignore invalid image resources.
        }
    }

    private string? ResolveImagePath(Uri uri, string decodedPath)
    {
        var queryPath = TryGetQueryValue(uri.Query, "path");
        if (!string.IsNullOrWhiteSpace(queryPath))
        {
            decodedPath = queryPath.Replace('/', Path.DirectorySeparatorChar);
        }

        if (Path.IsPathRooted(decodedPath))
        {
            return Path.GetFullPath(decodedPath);
        }

        var currentDir = Path.GetDirectoryName(_currentFilePath) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(currentDir))
        {
            return null;
        }

        return Path.GetFullPath(Path.Combine(currentDir, decodedPath));
    }

    private static string? TryGetQueryValue(string query, string key)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var trimmed = query.TrimStart('?');
        var pairs = trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var pair in pairs)
        {
            var kv = pair.Split('=', 2);
            if (kv.Length != 2)
            {
                continue;
            }

            if (kv[0].Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(kv[1]);
            }
        }

        return null;
    }

    private bool IsPathAllowed(string path)
    {
        if (string.IsNullOrWhiteSpace(_workspaceRoot) || !Directory.Exists(_workspaceRoot))
        {
            return false;
        }

        var normalizedPath = Path.GetFullPath(path);
        var normalizedRoot = Path.GetFullPath(_workspaceRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private enum EditorState
    {
        Idle,
        Loading,
        Editing,
        Dirty,
        Saving,
        ExternalSync,
        Immersive,
    }
}
