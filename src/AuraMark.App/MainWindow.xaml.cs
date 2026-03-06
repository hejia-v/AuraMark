
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
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
using Microsoft.Web.WebView2.Wpf;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Win32;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Button = System.Windows.Controls.Button;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;
using DragEventArgs = System.Windows.DragEventArgs;
using Clipboard = System.Windows.Clipboard;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;

namespace AuraMark.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const string VirtualHostName = "app.auramark.local";
    private const long LargeFileThresholdBytes = 5 * 1024 * 1024;
    private const int AutosaveDelayMilliseconds = 500;
    private const int ImmersiveTypingThresholdMilliseconds = 3000;
    private const int UiAnimationMilliseconds = 150;
    private const int E2eLargeFileDelayMilliseconds = 500;
    private double _sidebarExpandedWidth = 280;
    private double _outlineExpandedWidth = 300;
    private const double MouseWakeDistance = 100;
    private static readonly Regex HeadingRegex = new(@"^(#{1,6})\s+(.+?)\s*$", RegexOptions.Multiline | RegexOptions.Compiled);
    private const int MaxRecentEntries = 15;
    private static readonly string RecentFilesJsonPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AuraMark", "recent.json");
    private static readonly string LastWorkspaceFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AuraMark", "last_workspace.txt");
    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AuraMark", "settings.json");

    // Path data extracted from res/sprites-core-symbols/0623c1.svg (generic file/document)
    private const string FileIconPathData =
        "M15.169 6.5c0-.711-.001-1.204-.033-1.588a2.4 2.4 0 0 0-.112-.615l-.055-.13a1.84 1.84 0 0 0-.676-.731l-.126-.07c-.158-.081-.37-.138-.745-.169-.384-.031-.877-.032-1.588-.032H8.167c-.711 0-1.205 0-1.588.032-.376.031-.587.088-.745.168a1.84 1.84 0 0 0-.802.802c-.08.158-.137.37-.168.745-.031.384-.032.877-.032 1.588v7c0 .711 0 1.204.032 1.588.03.376.087.587.168.745l.07.126c.177.288.43.522.732.676l.13.056c.143.052.333.089.615.112.383.031.877.032 1.588.032h3.667c.71 0 1.204 0 1.588-.032.375-.031.587-.088.745-.168l.126-.07c.287-.177.522-.43.676-.732l.055-.13c.052-.144.09-.333.113-.615.03-.384.032-.877.032-1.588zm1.33 7c0 .69 0 1.246-.037 1.696-.033.4-.097.762-.241 1.098l-.068.142c-.265.522-.669.958-1.165 1.262l-.218.122c-.376.192-.782.271-1.24.309-.45.037-1.007.036-1.696.036H8.167c-.69 0-1.246 0-1.697-.036-.4-.033-.76-.098-1.097-.242l-.143-.067a3.17 3.17 0 0 1-1.261-1.165l-.123-.219c-.191-.376-.27-.782-.308-1.24-.037-.45-.036-1.007-.036-1.696v-7c0-.69 0-1.246.036-1.696.037-.458.117-.864.308-1.24A3.17 3.17 0 0 1 5.23 2.18c.377-.192.783-.271 1.24-.309.45-.037 1.008-.036 1.697-.036h3.667c.689 0 1.246 0 1.696.036.458.038.864.117 1.24.309l.218.122c.496.304.9.74 1.165 1.261l.068.143c.144.336.208.697.24 1.098.038.45.038 1.007.038 1.696z";

    // Path data extracted from res/sprites-core-symbols/547df2.svg (folder with tab)
    private const string FolderIconPathData =
        "M6.581 2.874a3 3 0 0 1 1.817.757c.072.064.142.135.243.237.112.113.15.15.186.183.292.26.663.415 1.053.44.049.002.103.002.262.002h2.718c.56 0 1.015 0 1.385.027.378.027.714.086 1.034.226.608.267 1.111.727 1.43 1.31.168.307.256.637.317 1.01q.043.27.077.609h.37a1.915 1.915 0 0 1 1.832 2.475l-1.645 5.367a2.33 2.33 0 0 1-2.228 1.648H4.752c-.61 0-1.152-.23-1.56-.6a3 3 0 0 1-.847-.933c-.19-.33-.287-.687-.35-1.093-.063-.398-.1-.89-.147-1.499L1.43 7.605c-.053-.683-.096-1.235-.094-1.681.002-.453.05-.858.214-1.237a3 3 0 0 1 1.365-1.475c.366-.192.767-.27 1.218-.308.445-.036.997-.036 1.683-.036h.426c.144 0 .242 0 .34.006m-.659 6.13a.59.59 0 0 0-.56.415L3.793 14.54a1 1 0 0 0 .588 1.224c.11.03.244.055.423.071h10.628c.44 0 .828-.288.957-.708l1.644-5.366a.585.585 0 0 0-.56-.756zm-.106-4.872c-.707 0-1.199 0-1.58.032-.374.03-.582.087-.734.167a1.74 1.74 0 0 0-.791.855c-.068.157-.11.369-.111.745-.002.382.035.873.09 1.578l.374 4.87 1.027-3.35a1.92 1.92 0 0 1 1.831-1.354h9.908a8 8 0 0 0-.052-.406c-.049-.304-.106-.476-.177-.606a1.75 1.75 0 0 0-.83-.76c-.135-.059-.312-.1-.618-.123a20 20 0 0 0-1.293-.023h-2.718c-.144 0-.243 0-.34-.006a3 3 0 0 1-1.816-.757c-.072-.065-.141-.135-.243-.237a4 4 0 0 0-.186-.183 1.75 1.75 0 0 0-1.053-.44c-.049-.002-.103-.002-.262-.002z";
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
    private string _pendingExternalMarkdown = string.Empty;
    private string _queuedDocumentForWeb = string.Empty;

    private bool _webReady;
    private bool _editorInitialized;
    private bool _dirty;
    private bool _isSaving;
    private bool _isSidebarVisible;
    private bool _isOutlineVisible;
    private bool _isImmersive;
    private bool _sidebarBeforeImmersive;
    private bool _outlineBeforeImmersive;
    private bool _externalReloadPending;
    private bool _hasExternalConflict;
    private bool _inputFrozen;
    private bool _isDraggingSidebar;
    private double _sidebarDragStartWindowX;
    private double _sidebarDragStartWidth;
    private bool _isDraggingOutline;
    private double _outlineDragStartWindowX;
    private double _outlineDragStartWidth;
    private bool _e2eMode;
    private bool _e2eStartupPending;
    private bool _e2eForceImmersive;
    private bool _e2eImmersiveApplied;
    private bool _suppressFileTreeSelectionLoad;
    private bool _webViewWarmed;
    private string _e2eOpenFilePath = string.Empty;
    private string _e2eStartupMarkdown = string.Empty;
    private int _documentLoadVersion;
    private bool _isDocumentRendering;
    private int? _pendingOutlineScrollIndex;

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
        LocationChanged += (_, _) => UpdateExpandPopupPositions();
        SizeChanged += (_, _) => UpdateExpandPopupPositions();
        StateChanged += (_, _) => UpdateCollapsedHandlesVisibility();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var margins = new MARGINS { Left = 1, Right = 1, Top = 1, Bottom = 1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int Left, Right, Top, Bottom; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ConfigureE2eFromArgs();
        await InitializeWebViewAsync();

        var docsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "AuraMark");
        Directory.CreateDirectory(docsRoot);

        var startupPath = Path.Combine(docsRoot, "Untitled.md");
        var createIfMissing = true;
        if (!string.IsNullOrWhiteSpace(_e2eOpenFilePath))
        {
            startupPath = _e2eOpenFilePath;
            createIfMissing = false;
        }

        _currentFilePath = startupPath;
        await LoadDocumentAsync(startupPath, createIfMissing: createIfMissing);
        await WarmUpWebViewAsync();
        await TryRestoreSnapshotOnStartupAsync();

        if (!_e2eMode)
        {
            var lastWorkspace = LoadLastWorkspace();
            if (!string.IsNullOrWhiteSpace(lastWorkspace) && Directory.Exists(lastWorkspace))
            {
                _workspaceRoot = lastWorkspace;
                RefreshFileTree();
            }
        }

        _isSidebarVisible = true;
        _isOutlineVisible = true;
        LoadSettings();
        ApplySidebarVisualState(_isSidebarVisible, immediate: true);
        ApplyOutlineVisualState(_isOutlineVisible, immediate: true);
        ApplyTopBarVisualState(true, immediate: true);
        RefreshRecentMenu();
    }

    private void ConfigureE2eFromArgs()
    {
        var args = Environment.GetCommandLineArgs();
        _e2eMode = args.Any(arg => arg.Equals("--e2e", StringComparison.OrdinalIgnoreCase));
        if (_e2eMode)
        {
            _e2eStartupMarkdown = "# AuraMark E2E\n\nAuraMark E2E typing sample\nline2\n";
            _e2eStartupPending = true;
        }

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--e2e-open", StringComparison.OrdinalIgnoreCase) &&
                i + 1 < args.Length &&
                !string.IsNullOrWhiteSpace(args[i + 1]))
            {
                _e2eOpenFilePath = Path.GetFullPath(args[i + 1]);
                _e2eMode = true;
                break;
            }
        }

        _e2eForceImmersive = args.Any(arg => arg.Equals("--e2e-force-immersive", StringComparison.OrdinalIgnoreCase));
        if (_e2eForceImmersive)
        {
            _e2eMode = true;
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

    private async Task ResetWebViewAsync()
    {
        if (_webViewCore is not null)
        {
            _webViewCore.WebMessageReceived -= OnWebMessageReceived;
            _webViewCore.NavigationStarting -= OnNavigationStarting;
            _webViewCore.WebResourceRequested -= OnWebResourceRequested;
        }

        _webViewCore = null;
        _webReady = false;
        _editorInitialized = false;
        _isDocumentRendering = false;

        MainWebView.Dispose();
        WebViewHost.Children.Clear();

        var webView = new WebView2();
        MainWebView = webView;
        WebViewHost.Children.Add(webView);

        await InitializeWebViewAsync();
    }

    private async Task WarmUpWebViewAsync()
    {
        if (_webViewWarmed || string.IsNullOrEmpty(_currentMarkdown))
        {
            return;
        }

        _webViewWarmed = true;
        await ResetWebViewAsync();
        QueueDocumentToWeb(_currentMarkdown);
    }

    private async void OnNewFileClicked(object sender, RoutedEventArgs e)
    {
        await CreateNewDocumentAsync();
    }

    private async void OnOpenFileClicked(object sender, RoutedEventArgs e)
    {
        await OpenWithDialogAsync();
    }

    private void OnOpenFolderClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select a folder to open as workspace",
            UseDescriptionForTitle = true,
            AutoUpgradeEnabled = true,
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }

        _workspaceRoot = dialog.SelectedPath;
        SaveLastWorkspace(_workspaceRoot);
        RefreshFileTree();

        if (!_isSidebarVisible)
        {
            _isSidebarVisible = true;
            ApplySidebarVisualState(true);
        }

        AddToRecent(dialog.SelectedPath, isFolder: true);
    }

    private List<RecentEntry> LoadRecentEntries()
    {
        try
        {
            if (!File.Exists(RecentFilesJsonPath)) return [];
            var json = File.ReadAllText(RecentFilesJsonPath);
            return JsonSerializer.Deserialize<List<RecentEntry>>(json, _jsonOptions) ?? [];
        }
        catch { return []; }
    }

    private void SaveRecentEntries(List<RecentEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RecentFilesJsonPath)!);
            File.WriteAllText(RecentFilesJsonPath, JsonSerializer.Serialize(entries, _jsonOptions));
        }
        catch { /* best effort */ }
    }

    private void SaveLastWorkspace(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LastWorkspaceFilePath)!);
            File.WriteAllText(LastWorkspaceFilePath, path);
        }
        catch { /* best effort */ }
    }

    private string LoadLastWorkspace()
    {
        try
        {
            if (!File.Exists(LastWorkspaceFilePath)) return string.Empty;
            return File.ReadAllText(LastWorkspaceFilePath).Trim();
        }
        catch { return string.Empty; }
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsFilePath)) return;
            var json = File.ReadAllText(SettingsFilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
            if (settings?.SidebarWidth is > 0)
                _sidebarExpandedWidth = Math.Clamp(settings.SidebarWidth, 160, 600);
            if (settings?.OutlineWidth is > 0)
                _outlineExpandedWidth = Math.Clamp(settings.OutlineWidth, 160, 600);
        }
        catch { /* best effort */ }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);
            var settings = new AppSettings { SidebarWidth = _sidebarExpandedWidth, OutlineWidth = _outlineExpandedWidth };
            File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(settings, _jsonOptions));
        }
        catch { /* best effort */ }
    }

    private void AddToRecent(string path, bool isFolder)
    {
        try
        {
            var entries = LoadRecentEntries();
            entries.RemoveAll(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            entries.Insert(0, new RecentEntry { Path = path, IsFolder = isFolder, LastOpenedUtc = DateTime.UtcNow });
            if (entries.Count > MaxRecentEntries)
                entries = entries.Take(MaxRecentEntries).ToList();
            SaveRecentEntries(entries);
            RefreshRecentMenu();
        }
        catch { /* best effort */ }
    }

    private void RefreshRecentMenu()
    {
        RecentMenuItem.Items.Clear();
        var entries = LoadRecentEntries();

        if (entries.Count == 0)
        {
            RecentMenuItem.Items.Add(new MenuItem
            {
                Header = "No recent items",
                IsEnabled = false,
                Style = (Style)FindResource("MenuItemStyle"),
            });
            return;
        }

        foreach (var entry in entries)
        {
            var rawName = Path.GetFileName(entry.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(rawName)) rawName = entry.Path;

            var iconBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA4, 0xB0));
            var icon = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(entry.IsFolder ? FolderIconPathData : FileIconPathData),
                Fill = iconBrush,
                Width = 14,
                Height = 14,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 7, 0),
            };

            var firstRow = new StackPanel { Orientation = Orientation.Horizontal };
            firstRow.Children.Add(icon);
            firstRow.Children.Add(new TextBlock { Text = rawName, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });

            var header = new StackPanel();
            header.Children.Add(firstRow);
            header.Children.Add(new TextBlock
            {
                Text = entry.Path,
                FontSize = 11,
                Foreground = iconBrush,
                Margin = new Thickness(21, 1, 0, 0),
            });

            var item = new MenuItem
            {
                Header = header,
                Tag = entry,
                Style = (Style)FindResource("MenuItemStyle"),
            };
            item.Click += OnRecentItemClicked;
            RecentMenuItem.Items.Add(item);
        }
    }

    private async void OnRecentItemClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not RecentEntry entry)
            return;

        if (entry.IsFolder)
        {
            if (!Directory.Exists(entry.Path))
            {
                ShowSoftError("Folder no longer exists.");
                return;
            }
            _workspaceRoot = entry.Path;
            SaveLastWorkspace(_workspaceRoot);
            RefreshFileTree();
            if (!_isSidebarVisible)
            {
                _isSidebarVisible = true;
                ApplySidebarVisualState(true);
            }
            AddToRecent(entry.Path, isFolder: true);
        }
        else
        {
            await LoadDocumentAsync(entry.Path, createIfMissing: false);
        }
    }

    private async void OnSaveNowClicked(object sender, RoutedEventArgs e)
    {
        await SavePendingChangesAsync(force: true);
    }

    private async void OnExportHtmlClicked(object sender, RoutedEventArgs e)
    {
        await ExportDocumentAsync();
    }

    private void OnToggleSidebarClicked(object sender, RoutedEventArgs e)
    {
        ToggleSidebar();
    }

    private void OnToggleOutlineClicked(object sender, RoutedEventArgs e)
    {
        ToggleOutline();
    }

    private void OnExpandWorkspaceHandleClicked(object sender, RoutedEventArgs e)
    {
        if (_isSidebarVisible)
        {
            return;
        }

        if (_isImmersive)
        {
            ExitImmersiveMode();
        }

        _isSidebarVisible = true;
        ApplySidebarVisualState(true);
    }

    private void OnExpandOutlineHandleClicked(object sender, RoutedEventArgs e)
    {
        if (_isOutlineVisible)
        {
            return;
        }

        if (_isImmersive)
        {
            ExitImmersiveMode();
        }

        _isOutlineVisible = true;
        ApplyOutlineVisualState(true);
    }

    private async void OnRetrySaveClicked(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_pendingSaveRetryContent))
        {
            _pendingMarkdown = _pendingSaveRetryContent;
        }

        await SavePendingChangesAsync(force: true);
    }

    private async void OnKeepLocalClicked(object sender, RoutedEventArgs e)
    {
        if (!_hasExternalConflict)
        {
            return;
        }

        _hasExternalConflict = false;
        _pendingExternalMarkdown = string.Empty;
        HideSyncConflict();
        SetState(EditorState.Editing, "Kept local");
        await SavePendingChangesAsync(force: true);
    }

    private async void OnAcceptExternalClicked(object sender, RoutedEventArgs e)
    {
        if (!_hasExternalConflict || string.IsNullOrWhiteSpace(_pendingExternalMarkdown))
        {
            return;
        }

        await SaveConflictSnapshotAsync(_pendingMarkdown);
        var nextMarkdown = _pendingExternalMarkdown;
        _hasExternalConflict = false;
        _pendingExternalMarkdown = string.Empty;
        HideSyncConflict();
        await ApplyExternalMarkdownAsync(nextMarkdown);
        SetState(EditorState.Editing, "Applied external");
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

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.O)
        {
            ToggleOutline();
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

    private void OnTopBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (FindVisualParent<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // Ignore invalid drag transitions.
        }
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

    private async Task<bool> EnsureReadyToSwitchDocumentAsync(string nextPath)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath) ||
            nextPath.Equals(_currentFilePath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        _autosaveTimer.Stop();

        if (_isSaving)
        {
            ShowLoading(true, "Finishing save...");
            while (_isSaving)
            {
                await Task.Delay(25);
            }
        }

        if (!_dirty)
        {
            return true;
        }

        ShowLoading(true, "Saving current document...");
        await SavePendingChangesAsync(force: true);
        if (!_dirty)
        {
            return true;
        }

        ShowSoftError("Save current document before switching files.");
        ShowLoading(false);
        SetState(EditorState.Dirty, "Switch cancelled");
        return false;
    }

    private async Task LoadDocumentAsync(string path, bool createIfMissing)
    {
        path = Path.GetFullPath(path);
        var isSwitchingDocument =
            !string.IsNullOrWhiteSpace(_currentFilePath) &&
            !path.Equals(_currentFilePath, StringComparison.OrdinalIgnoreCase);
        if (!await EnsureReadyToSwitchDocumentAsync(path))
        {
            return;
        }

        var loadVersion = Interlocked.Increment(ref _documentLoadVersion);

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
            if (loadVersion != Volatile.Read(ref _documentLoadVersion))
            {
                return;
            }

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
            if (_e2eMode)
            {
                await Task.Delay(E2eLargeFileDelayMilliseconds);
            }
        }

        string markdown;
        await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 8192, useAsync: true))
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            markdown = await reader.ReadToEndAsync();
        }

        if (loadVersion != Volatile.Read(ref _documentLoadVersion))
        {
            return;
        }

        _currentFilePath = path;
        var fileDir = Path.GetDirectoryName(path) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_workspaceRoot))
        {
            _workspaceRoot = fileDir;
        }
        else
        {
            var normalizedRoot = Path.GetFullPath(_workspaceRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(path).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                _workspaceRoot = fileDir;
            }
        }
        _currentMarkdown = markdown;
        _pendingMarkdown = markdown;
        _dirty = false;
        _inputFrozen = false;
        _hasExternalConflict = false;
        _pendingExternalMarkdown = string.Empty;

        FileNameText.Text = Path.GetFileName(path);
        SetSavingDot(false);
        HideSyncConflict();
        RefreshFileTree();
        UpdateOutline(markdown);
        OutlineList.SelectedItem = null;
        _pendingOutlineScrollIndex = null;
        AttachFileWatcher(path);

        if (isSwitchingDocument)
        {
            await ResetWebViewAsync();
        }

        QueueDocumentToWeb(markdown);

        if (!createIfMissing)
        {
            AddToRecent(path, isFolder: false);
        }

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
        _isDocumentRendering = true;

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
            PostError(ErrorCodes.IpcParse, "payload too large");
            return;
        }

        WebMessagePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<WebMessagePayload>(rawJson, _jsonOptions);
        }
        catch
        {
            PostError(ErrorCodes.IpcParse, "invalid json");
            return;
        }

        if (payload is null || !IpcTypes.Allowed.Contains(payload.Type))
        {
            PostError(ErrorCodes.IpcParse, "unknown type");
            return;
        }

        payload.Content ??= string.Empty;
        if (Encoding.UTF8.GetByteCount(payload.Content) > IpcLimits.MaxContentBytes)
        {
            PostError(ErrorCodes.IpcParse, "content too large");
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
                ShowSoftError(ParseErrorMessage(payload.Content));
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
            _isDocumentRendering = false;
            ShowLoading(false);
            WebViewHost.UpdateLayout();
            MainWebView.InvalidateVisual();
            MainWebView.UpdateLayout();

            if (_pendingOutlineScrollIndex is int pendingIndex)
            {
                SendCommand(new HostCommand
                {
                    Name = IpcCommands.ScrollToHeading,
                    Index = pendingIndex,
                });
                _pendingOutlineScrollIndex = null;
            }

            if (_e2eStartupPending && !string.IsNullOrWhiteSpace(_e2eStartupMarkdown))
            {
                _e2eStartupPending = false;
                SendCommand(new HostCommand { Name = IpcCommands.E2eSetMarkdown, Content = _e2eStartupMarkdown });
            }

            if (_e2eForceImmersive && !_e2eImmersiveApplied)
            {
                _e2eImmersiveApplied = true;
                Dispatcher.InvokeAsync(EnterImmersiveMode, DispatcherPriority.Background);
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
        _outlineBeforeImmersive = _isOutlineVisible;
        ApplyTopBarVisualState(false);
        ApplySidebarVisualState(false);
        ApplyOutlineVisualState(false);
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
        ApplyOutlineVisualState(_outlineBeforeImmersive);
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

        var savePath = _currentFilePath;
        var markdownToSave = _pendingMarkdown;
        _isSaving = true;
        SetState(EditorState.Saving);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(savePath) ?? ".");
            await File.WriteAllTextAsync(savePath, markdownToSave, Encoding.UTF8);
            _ignoreWatcherUntilUtc = DateTime.UtcNow.AddMilliseconds(1200);

            var isCurrentSaveContext =
                savePath.Equals(_currentFilePath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(markdownToSave, _pendingMarkdown, StringComparison.Ordinal);
            if (isCurrentSaveContext)
            {
                _currentMarkdown = markdownToSave;
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
        }
        catch (UnauthorizedAccessException)
        {
            if (savePath.Equals(_currentFilePath, StringComparison.OrdinalIgnoreCase))
            {
                _pendingSaveRetryContent = markdownToSave;
                SetState(EditorState.Dirty);
                ShowSoftError($"{ErrorCodes.SaveDenied}: no permission.");
                PostError(ErrorCodes.SaveDenied, "no permission.", savePath, retryable: true);
            }
        }
        catch (IOException)
        {
            if (savePath.Equals(_currentFilePath, StringComparison.OrdinalIgnoreCase))
            {
                _pendingSaveRetryContent = markdownToSave;
                SetState(EditorState.Dirty);
                ShowSoftError($"{ErrorCodes.SaveIo}: write failed.");
                PostError(ErrorCodes.SaveIo, "write failed.", savePath, retryable: true);
            }
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
            _pendingExternalMarkdown = diskMarkdown;
            _hasExternalConflict = true;
            SetState(EditorState.ExternalSync, "Conflict detected");
            ShowSyncConflict();
            PostError(
                ErrorCodes.SyncConflict,
                "external file changed while local has unsaved updates",
                _currentFilePath,
                retryable: true);
            return;
        }

        await ApplyExternalMarkdownAsync(diskMarkdown);
        SetState(EditorState.Editing);
    }

    private async Task ApplyExternalMarkdownAsync(string markdown)
    {
        SetState(EditorState.ExternalSync);
        _inputFrozen = true;
        SendCommand(new HostCommand { Name = IpcCommands.FreezeInput });

        _currentMarkdown = markdown;
        _pendingMarkdown = markdown;
        _dirty = false;
        SetSavingDot(false);
        UpdateOutline(markdown);
        QueueDocumentToWeb(markdown);

        await Task.Delay(120);
        _inputFrozen = false;
        SendCommand(new HostCommand { Name = IpcCommands.ResumeInput });
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

            var hash = ComputePathHash(_currentFilePath);
            var name = $"{hash}-{DateTime.UtcNow:yyyyMMddHHmmss}.md";
            var path = Path.Combine(snapshotRoot, name);
            await File.WriteAllTextAsync(path, markdown, Encoding.UTF8);
        }
        catch
        {
            // Best effort snapshot.
        }
    }

    private async Task TryRestoreSnapshotOnStartupAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath) || _e2eMode)
        {
            return;
        }

        try
        {
            var snapshotRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AuraMark",
                "snapshots");
            if (!Directory.Exists(snapshotRoot))
            {
                return;
            }

            var hash = ComputePathHash(_currentFilePath);
            var latestSnapshot = Directory
                .EnumerateFiles(snapshotRoot, $"{hash}-*.md", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(latestSnapshot))
            {
                return;
            }

            var snapshotTimeUtc = File.GetLastWriteTimeUtc(latestSnapshot);
            var currentFileTimeUtc = File.Exists(_currentFilePath)
                ? File.GetLastWriteTimeUtc(_currentFilePath)
                : DateTime.MinValue;
            if (snapshotTimeUtc <= currentFileTimeUtc)
            {
                return;
            }

            var snapshotMarkdown = await File.ReadAllTextAsync(latestSnapshot, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(snapshotMarkdown) || snapshotMarkdown == _pendingMarkdown)
            {
                return;
            }

            _currentMarkdown = snapshotMarkdown;
            _pendingMarkdown = snapshotMarkdown;
            _dirty = true;
            SetSavingDot(true);
            UpdateOutline(snapshotMarkdown);
            QueueDocumentToWeb(snapshotMarkdown);
            SetState(EditorState.Dirty, "Recovered snapshot");
            ShowSoftError("Recovered local snapshot. Review and save.");
        }
        catch
        {
            // Best effort restore.
        }
    }
    private async Task ExportDocumentAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export Document",
            Filter = "HTML|*.html|PDF|*.pdf",
            FileName = Path.GetFileNameWithoutExtension(_currentFilePath) + ".html",
            AddExtension = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var extension = Path.GetExtension(dialog.FileName);
        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            await ExportPdfAsync(dialog.FileName);
            return;
        }

        await ExportHtmlAsync(dialog.FileName);
    }

    private async Task ExportHtmlAsync(string outputPath)
    {
        var htmlBody = Markdown.ToHtml(_pendingMarkdown);
        var title = Path.GetFileNameWithoutExtension(_currentFilePath);
        var html = BuildHtmlDocument(title, htmlBody);
        await File.WriteAllTextAsync(outputPath, html, Encoding.UTF8);
        SetState(EditorState.Editing, "Exported HTML");
    }

    private async Task ExportPdfAsync(string outputPath)
    {
        if (_webViewCore is null)
        {
            ShowSoftError("PDF export unavailable: editor not ready.");
            return;
        }

        var ok = await _webViewCore.PrintToPdfAsync(outputPath);
        if (!ok)
        {
            ShowSoftError("PDF export failed. Please retry.");
            PostError(ErrorCodes.SaveIo, "pdf export failed", outputPath, retryable: true);
            return;
        }

        SetState(EditorState.Editing, "Exported PDF");
    }

    private static string BuildHtmlDocument(string title, string htmlBody)
    {
        return $@"<!doctype html>
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
        _suppressFileTreeSelectionLoad = true;
        try
        {
            var expandedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            SnapshotExpandedPaths(_fileTreeNodes, expandedPaths);

            _fileTreeNodes.Clear();

            if (string.IsNullOrWhiteSpace(_workspaceRoot) || !Directory.Exists(_workspaceRoot))
            {
                WorkspaceFolderNameText.Text = "WORKSPACE";
                return;
            }

            var folderName = Path.GetFileName(_workspaceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            WorkspaceFolderNameText.Text = string.IsNullOrEmpty(folderName) ? "WORKSPACE" : folderName;

            var root = BuildDirectoryNode(_workspaceRoot, depth: 0);
            RestoreExpandedPaths(root.Children, expandedPaths);
            SyncCurrentFileSelection(root.Children);
            foreach (var child in root.Children)
            {
                _fileTreeNodes.Add(child);
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FileTreeNodes)));
        }
        finally
        {
            _suppressFileTreeSelectionLoad = false;
        }
    }

    private static void SnapshotExpandedPaths(IEnumerable<FileTreeNode> nodes, HashSet<string> paths)
    {
        foreach (var node in nodes)
        {
            if (node.IsDirectory && node.IsExpanded)
            {
                paths.Add(node.FullPath);
                SnapshotExpandedPaths(node.Children, paths);
            }
        }
    }

    private static void RestoreExpandedPaths(IEnumerable<FileTreeNode> nodes, HashSet<string> expandedPaths)
    {
        foreach (var node in nodes)
        {
            if (node.IsDirectory && expandedPaths.Contains(node.FullPath))
            {
                node.IsExpanded = true;
                RestoreExpandedPaths(node.Children, expandedPaths);
            }
        }
    }

    private bool SyncCurrentFileSelection(IEnumerable<FileTreeNode> nodes)
    {
        var found = false;

        foreach (var node in nodes)
        {
            var isMatch = false;
            if (node.IsDirectory)
            {
                isMatch = SyncCurrentFileSelection(node.Children);
                if (isMatch)
                {
                    node.IsExpanded = true;
                }
            }
            else
            {
                isMatch = node.FullPath.Equals(_currentFilePath, StringComparison.OrdinalIgnoreCase);
            }

            node.IsSelected = isMatch;
            found |= isMatch;
        }

        return found;
    }

    private static readonly HashSet<string> SkillsParentNames =
        new(StringComparer.OrdinalIgnoreCase) { ".agents", ".codex", ".claude" };

    private FileTreeNode BuildDirectoryNode(string directory, int depth, bool isSkill = false)
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
            IsSkill = isSkill,
        };

        if (depth >= 4)
        {
            return node;
        }

        // Children of .agents/skills, .codex/skills, .claude/skills are skill nodes.
        var dirName = Path.GetFileName(directory) ?? "";
        var parentDirName = Path.GetFileName(Path.GetDirectoryName(directory) ?? "") ?? "";
        var childrenAreSkills = dirName.Equals("skills", StringComparison.OrdinalIgnoreCase)
                                && SkillsParentNames.Contains(parentDirName);

        try
        {
            foreach (var subDirectory in Directory.EnumerateDirectories(directory).OrderBy(Path.GetFileName))
            {
                var subNode = BuildDirectoryNode(subDirectory, depth + 1, isSkill: childrenAreSkills);
                if (subNode.Children.Count > 0)
                {
                    node.Children.Add(subNode);
                }
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
        if (_suppressFileTreeSelectionLoad)
        {
            return;
        }

        if (e.NewValue is not FileTreeNode node || node.IsDirectory)
        {
            return;
        }

        if (!File.Exists(node.FullPath))
        {
            return;
        }

        if (node.FullPath.Equals(_currentFilePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            await LoadDocumentAsync(node.FullPath, createIfMissing: false);
        }
        catch (Exception ex)
        {
            ShowSoftError($"Open failed: {ex.Message}");
            ShowLoading(false);
            SetState(EditorState.Idle);
        }
    }

    private void OnOutlineSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OutlineList.SelectedItem is not OutlineItem item)
        {
            return;
        }

        if (_isDocumentRendering || !_webReady)
        {
            _pendingOutlineScrollIndex = item.Index;
            OutlineList.SelectedItem = null;
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

    private void ToggleOutline()
    {
        _isOutlineVisible = !_isOutlineVisible;
        if (_isImmersive)
        {
            return;
        }

        ApplyOutlineVisualState(_isOutlineVisible);
    }

    private void ApplySidebarVisualState(bool visible, bool immediate = false)
    {
        var targetWidth = visible ? _sidebarExpandedWidth : 0d;

        if (!visible)
            SidebarResizeHandle.Visibility = Visibility.Collapsed;

        if (immediate)
        {
            SidebarContainer.Width = targetWidth;
            SidebarContainer.Opacity = visible ? 1 : 0;
            SidebarContainer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (visible)
                SidebarResizeHandle.Visibility = Visibility.Visible;
            UpdateCollapsedHandlesVisibility();
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
            else
            {
                SidebarResizeHandle.Visibility = Visibility.Visible;
            }
        };

        SidebarContainer.BeginAnimation(WidthProperty, widthAnimation);
        FadeElement(SidebarContainer, visible);
        UpdateCollapsedHandlesVisibility();
    }

    private void ApplyOutlineVisualState(bool visible, bool immediate = false)
    {
        var targetWidth = visible ? _outlineExpandedWidth : 0d;

        if (!visible)
            OutlineResizeHandle.Visibility = Visibility.Collapsed;

        if (immediate)
        {
            OutlineContainer.Width = targetWidth;
            OutlineContainer.Opacity = visible ? 1 : 0;
            OutlineContainer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (visible)
                OutlineResizeHandle.Visibility = Visibility.Visible;
            UpdateCollapsedHandlesVisibility();
            return;
        }

        if (visible)
        {
            OutlineContainer.Visibility = Visibility.Visible;
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
                OutlineContainer.Visibility = Visibility.Collapsed;
            }
            else
            {
                OutlineResizeHandle.Visibility = Visibility.Visible;
            }
        };

        OutlineContainer.BeginAnimation(WidthProperty, widthAnimation);
        FadeElement(OutlineContainer, visible);
        UpdateCollapsedHandlesVisibility();
    }

    private void OnSidebarResizeMouseEnter(object sender, MouseEventArgs e)
    {
        SidebarResizeGrip.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
    }

    private void OnSidebarResizeMouseLeave(object sender, MouseEventArgs e)
    {
        if (_isDraggingSidebar) return;
        SidebarResizeGrip.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
    }

    private void OnSidebarResizeMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSidebar = true;
        _sidebarDragStartWindowX = e.GetPosition(this).X;
        SidebarContainer.BeginAnimation(WidthProperty, null);
        _sidebarDragStartWidth = SidebarContainer.ActualWidth;
        SidebarContainer.Width = _sidebarDragStartWidth;
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void OnSidebarResizeMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingSidebar) return;
        var delta = e.GetPosition(this).X - _sidebarDragStartWindowX;
        var newWidth = Math.Clamp(_sidebarDragStartWidth + delta, 160, 600);
        _sidebarExpandedWidth = newWidth;
        SidebarContainer.Width = newWidth;
        e.Handled = true;
    }

    private void OnSidebarResizeMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingSidebar) return;
        _isDraggingSidebar = false;
        ((UIElement)sender).ReleaseMouseCapture();
        SaveSettings();
        if (!SidebarResizeHandle.IsMouseOver)
            SidebarResizeGrip.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
        e.Handled = true;
    }

    private void OnOutlineResizeMouseEnter(object sender, MouseEventArgs e)
    {
        OutlineResizeGrip.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
    }

    private void OnOutlineResizeMouseLeave(object sender, MouseEventArgs e)
    {
        if (_isDraggingOutline) return;
        OutlineResizeGrip.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
    }

    private void OnOutlineResizeMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingOutline = true;
        _outlineDragStartWindowX = e.GetPosition(this).X;
        OutlineContainer.BeginAnimation(WidthProperty, null);
        _outlineDragStartWidth = OutlineContainer.ActualWidth;
        OutlineContainer.Width = _outlineDragStartWidth;
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void OnOutlineResizeMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingOutline) return;
        var delta = e.GetPosition(this).X - _outlineDragStartWindowX;
        var newWidth = Math.Clamp(_outlineDragStartWidth - delta, 160, 600);
        _outlineExpandedWidth = newWidth;
        OutlineContainer.Width = newWidth;
        e.Handled = true;
    }

    private void OnOutlineResizeMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingOutline) return;
        _isDraggingOutline = false;
        ((UIElement)sender).ReleaseMouseCapture();
        SaveSettings();
        if (!OutlineResizeHandle.IsMouseOver)
            OutlineResizeGrip.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
        e.Handled = true;
    }

    private sealed class AppSettings
    {
        public double SidebarWidth { get; set; }
        public double OutlineWidth { get; set; }
    }

    private void UpdateCollapsedHandlesVisibility()
    {
        if (WindowState == WindowState.Minimized)
        {
            WorkspaceExpandPopup.IsOpen = false;
            OutlineExpandPopup.IsOpen = false;
            return;
        }

        var showWorkspace = !_isImmersive && !_isSidebarVisible;
        var showOutline = !_isImmersive && !_isOutlineVisible;

        if (showWorkspace || showOutline)
        {
            UpdateExpandPopupPositions();
        }

        WorkspaceExpandPopup.IsOpen = showWorkspace;
        OutlineExpandPopup.IsOpen = showOutline;
    }

    private void UpdateExpandPopupPositions()
    {
        if (!IsLoaded) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var screenOrigin = PointToScreen(new Point(0, 0));
        var wpfX = screenOrigin.X / dpi.DpiScaleX;
        var wpfY = screenOrigin.Y / dpi.DpiScaleY;

        const double topBarHeight = 32.0;
        const double buttonSize = 28.0;
        const double margin = 6.0;

        // 紧贴 TopBar 下方，左上 / 右上角
        WorkspaceExpandPopup.HorizontalOffset = wpfX + margin;
        WorkspaceExpandPopup.VerticalOffset = wpfY + topBarHeight + margin;

        OutlineExpandPopup.HorizontalOffset = wpfX + ActualWidth - buttonSize - margin;
        OutlineExpandPopup.VerticalOffset = wpfY + topBarHeight + margin;
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

    private void ShowSyncConflict()
    {
        SyncConflictText.Text = "External file changed. Keep local or reload external?";
        FadeElement(SyncConflictToast, visible: true);
    }

    private void HideSyncConflict()
    {
        FadeElement(SyncConflictToast, visible: false);
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

    private void PostError(string code, string message, string? path = null, bool retryable = false)
    {
        var payload = new IpcErrorPayload
        {
            Code = code,
            Message = message,
            Path = path,
            Retryable = retryable,
        };

        PostToWeb(new WebMessagePayload
        {
            Type = IpcTypes.Error,
            Content = payload.ToJson(),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        });
    }

    private static string ParseErrorMessage(string content)
    {
        if (IpcErrorPayload.TryParse(content, out var payload) && payload is not null)
        {
            return $"{payload.Code}: {payload.Message}";
        }

        return content;
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

        _webReady = false;
        _editorInitialized = false;

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

    private static string ComputePathHash(string path)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(path));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static T? FindVisualParent<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    // ================================================================
    // Tree context menu helpers
    // ================================================================

    private static FileTreeNode? GetNodeFromMenuSender(object sender)
    {
        if (sender is MenuItem mi
            && mi.Parent is ContextMenu cm
            && cm.PlacementTarget is FrameworkElement fe
            && fe.DataContext is FileTreeNode node)
            return node;
        return null;
    }

    // Clipboard.SetText can throw CLIPBRD_E_CANT_OPEN when another process (e.g. IME) holds
    // the clipboard. Retry a few times with a short sleep before giving up.
    private static void SetClipboardTextSafe(string text)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch (System.Runtime.InteropServices.COMException) when (attempt < 4)
            {
                System.Threading.Thread.Sleep(10);
            }
        }
    }

    private void OnTreeMenuCopyName(object sender, RoutedEventArgs e)
    {
        if (GetNodeFromMenuSender(sender) is { } node)
            SetClipboardTextSafe(node.Name);
    }

    private void OnTreeMenuCopyPath(object sender, RoutedEventArgs e)
    {
        if (GetNodeFromMenuSender(sender) is { } node)
            SetClipboardTextSafe(node.FullPath.Replace('\\', '/'));
    }

    private void OnTreeMenuCopyRelativePath(object sender, RoutedEventArgs e)
    {
        if (GetNodeFromMenuSender(sender) is not { } node) return;
        if (string.IsNullOrWhiteSpace(_workspaceRoot)) return;
        var rel = Path.GetRelativePath(_workspaceRoot, node.FullPath).Replace('\\', '/');
        SetClipboardTextSafe(rel);
    }

    private async void OnTreeMenuNewMarkdown(object sender, RoutedEventArgs e)
    {
        if (GetNodeFromMenuSender(sender) is not { } node) return;
        var dir = node.IsDirectory ? node.FullPath : Path.GetDirectoryName(node.FullPath)!;
        var path = GetUniqueNewFilePath(dir, "Untitled", ".md");
        await File.WriteAllTextAsync(path, string.Empty);
        RefreshFileTree();
        await LoadDocumentAsync(path, createIfMissing: false);
    }

    private static string GetUniqueNewFilePath(string dir, string stem, string ext)
    {
        var path = Path.Combine(dir, stem + ext);
        if (!File.Exists(path)) return path;
        for (var i = 1; ; i++)
        {
            path = Path.Combine(dir, $"{stem}{i}{ext}");
            if (!File.Exists(path)) return path;
        }
    }

    // ================================================================
    // Rename handlers
    // ================================================================

    private void OnTreeMenuRename(object sender, RoutedEventArgs e)
    {
        if (GetNodeFromMenuSender(sender) is not { } node) return;
        node.IsRenaming = true;
        Dispatcher.BeginInvoke(() =>
        {
            if (FindRenameBox(node) is { } box)
            {
                box.Focus();
                box.SelectAll();
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void CommitRename(FileTreeNode node, string newName)
    {
        node.IsRenaming = false;
        newName = newName.Trim();
        if (string.IsNullOrEmpty(newName) || newName == node.Name) return;
        var dest = Path.Combine(Path.GetDirectoryName(node.FullPath)!, newName);
        try
        {
            if (node.IsDirectory) Directory.Move(node.FullPath, dest);
            else
            {
                File.Move(node.FullPath, dest);
                if (node.FullPath.Equals(_currentFilePath, StringComparison.OrdinalIgnoreCase))
                    _currentFilePath = dest;
            }
            RefreshFileTree();
        }
        catch (Exception ex) { ShowSoftError($"Rename failed: {ex.Message}"); }
    }

    private void OnRenameBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not FileTreeNode node) return;
        if (e.Key == Key.Return) { CommitRename(node, box.Text); e.Handled = true; }
        if (e.Key == Key.Escape) { node.IsRenaming = false; e.Handled = true; }
    }

    private void OnRenameBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box && box.DataContext is FileTreeNode node)
            CommitRename(node, box.Text);
    }

    private TextBox? FindRenameBox(FileTreeNode node)
        => FindDescendant<TextBox>(FileTreeView, tb => tb.Name == "RenameBox" && tb.DataContext == node);

    private static T? FindDescendant<T>(DependencyObject parent, Func<T, bool> predicate) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed && predicate(typed))
                return typed;
            var found = FindDescendant(child, predicate);
            if (found != null) return found;
        }
        return null;
    }

    // ================================================================
    // Drag-and-drop handlers
    // ================================================================

    private Point _dragStart;
    private FileTreeNode? _dragNode;

    private void OnTreeItemMouseLeftDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        if (sender is FrameworkElement fe && fe.DataContext is FileTreeNode node)
            _dragNode = node;
    }

    private void OnTreeItemPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragNode == null) return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DragDrop.DoDragDrop(FileTreeView, new DataObject("FileTreeNode", _dragNode), DragDropEffects.Move);
        _dragNode = null;
    }

    private void OnFileTreeDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent("FileTreeNode") ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFileTreeDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("FileTreeNode")) return;
        if (e.Data.GetData("FileTreeNode") is not FileTreeNode src) return;

        var target = GetTreeNodeFromPoint(e.GetPosition(FileTreeView));
        if (target == null || target.FullPath == src.FullPath) return;

        var destDir = target.IsDirectory ? target.FullPath : Path.GetDirectoryName(target.FullPath)!;
        if (destDir.Equals(Path.GetDirectoryName(src.FullPath), StringComparison.OrdinalIgnoreCase)) return;

        var dest = Path.Combine(destDir, src.Name);
        try
        {
            if (src.IsDirectory) Directory.Move(src.FullPath, dest);
            else
            {
                File.Move(src.FullPath, dest);
                if (src.FullPath.Equals(_currentFilePath, StringComparison.OrdinalIgnoreCase))
                    _currentFilePath = dest;
            }
            RefreshFileTree();
        }
        catch (Exception ex) { ShowSoftError($"Move failed: {ex.Message}"); }
    }

    private FileTreeNode? GetTreeNodeFromPoint(Point pos)
    {
        var hit = VisualTreeHelper.HitTest(FileTreeView, pos);
        var el = hit?.VisualHit as DependencyObject;
        while (el != null)
        {
            if (el is FrameworkElement fe && fe.DataContext is FileTreeNode node)
                return node;
            el = VisualTreeHelper.GetParent(el);
        }
        return null;
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
