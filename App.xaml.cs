using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DropMe;

public partial class App : Application
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "DropMe";
    // Passed by the Windows startup entry, so the welcome balloon only shows on a manual launch.
    private const string StartupArgument = "--startup";

    private Mutex? _singleInstance;
    private MouseShakeHook? _hook;
    private Forms.NotifyIcon? _tray;
    private readonly List<ShelfWindow> _shelves = new();
    private LibraryWindow? _library;
    private DateTime _libraryClosedAt;

    internal ShelfHistory History { get; private set; } = new();
    internal IReadOnlyList<ShelfWindow> OpenShelves => _shelves;
    // A shelf created ahead of time, so shaking shows one instantly instead of building a window.
    private ShelfWindow? _spare;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, "DropMe.SingleInstance", out bool created);
        if (!created)
        {
            Shutdown();
            return;
        }

        // A tray app has no window to show an error in: log it and keep running instead of vanishing.
        DispatcherUnhandledException += (_, args) =>
        {
            LogError(args.Exception);
            args.Handled = true;
        };

        History = ShelfHistory.Load();
        PrepareSpareShelf();

        _hook = new MouseShakeHook();
        // Hook events arrive on the hook's own thread: hand them over to the UI thread.
        _hook.Shaken += () => Dispatcher.BeginInvoke(() => OpenShelfAtCursor(autoClose: true));
        _hook.ButtonReleased += () => Dispatcher.BeginInvoke(ScheduleEmptyShelfCleanup);
        _hook.Start();

        CreateTrayIcon(showWelcome: !e.Args.Contains(StartupArgument));
    }

    private static void LogError(Exception ex)
    {
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DropMe");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "errors.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Nowhere left to report it.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hook?.Dispose();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void PrepareSpareShelf()
    {
        // Two shelves taken in a row queue two calls; the second must not orphan the first spare.
        if (_spare != null)
            return;
        _spare = new ShelfWindow();
        _spare.Prewarm();
    }

    private ShelfWindow TakeShelf()
    {
        var shelf = _spare ?? new ShelfWindow();
        _spare = null;
        Dispatcher.BeginInvoke(PrepareSpareShelf, DispatcherPriority.ApplicationIdle);

        shelf.Closed += (_, _) =>
        {
            _shelves.Remove(shelf);
            History.Add(shelf.Items);
        };
        _shelves.Add(shelf);
        return shelf;
    }

    private void OpenShelfAtCursor(bool autoClose)
    {
        if (ShelfWindow.IsDraggingOut)
            return;

        var shelf = _shelves.FirstOrDefault(s => s.IsEmpty) ?? TakeShelf();
        shelf.AutoCloseIfEmpty = autoClose;
        shelf.ShowNearCursor();
    }

    internal void OpenNewShelf() => OpenShelfAtCursor(autoClose: false);

    /// <summary>Brings an open shelf next to the cursor.</summary>
    internal void BringShelf(ShelfWindow shelf) => shelf.ShowNearCursor();

    internal void ReopenFromHistory(ShelfHistory.Entry entry)
    {
        History.Remove(entry);
        if (!entry.Items.Any(p => File.Exists(p) || Directory.Exists(p)))
        {
            _tray?.ShowBalloonTip(3000, "DropMe", "Ces fichiers n'existent plus.", Forms.ToolTipIcon.Warning);
            return;
        }

        var shelf = TakeShelf();
        shelf.LoadItems(entry.Items);
        shelf.ShowNearCursor();
    }

    // When the user lets go of the mouse without dropping anything on a freshly
    // shaken-open shelf, it disappears. The delay lets the Drop event land first.
    private void ScheduleEmptyShelfCleanup()
    {
        // This runs on every click anywhere in Windows: bail out unless a shelf is waiting for a drop.
        if (!_shelves.Any(s => s.IsEmpty && s.AutoCloseIfEmpty))
            return;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            foreach (var shelf in _shelves.Where(s => s.IsEmpty && s.AutoCloseIfEmpty && !s.IsMouseOver).ToList())
                shelf.Close();
        };
        timer.Start();
    }

    private void CreateTrayIcon(bool showWelcome)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Opening += (_, _) => BuildTrayMenu(menu);

        _tray = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "DropMe — secouez la souris pendant un glisser",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
                ToggleLibrary();
        };
        if (showWelcome)
            _tray.ShowBalloonTip(3000, "DropMe est actif",
                "Glissez un fichier puis secouez la souris pour faire apparaître l'étagère.", Forms.ToolTipIcon.Info);
    }

    private void ToggleLibrary()
    {
        if (_library != null)
        {
            _library.Close();
            return;
        }
        // Clicking the icon while the window is open first deactivates (and closes) it;
        // that same click must not open it again.
        if ((DateTime.Now - _libraryClosedAt).TotalMilliseconds < 400)
            return;

        _library = new LibraryWindow(this);
        _library.Closed += (_, _) =>
        {
            _library = null;
            _libraryClosedAt = DateTime.Now;
        };
        _library.ShowFlyout();
    }

    private void BuildTrayMenu(Forms.ContextMenuStrip menu)
    {
        menu.Items.Clear();
        menu.Items.Add("Ouvrir DropMe", null, (_, _) => ToggleLibrary());
        menu.Items.Add("Nouvelle étagère", null, (_, _) => OpenNewShelf());
        if (_shelves.Count > 0)
        {
            menu.Items.Add("Fermer toutes les étagères", null, (_, _) =>
            {
                foreach (var shelf in _shelves.ToList())
                    shelf.Close();
            });
        }
        menu.Items.Add(new Forms.ToolStripSeparator());

        var startup = new Forms.ToolStripMenuItem("Lancer au démarrage de Windows") { Checked = IsStartupEnabled() };
        startup.Click += (_, _) => SetStartupEnabled(!startup.Checked);
        menu.Items.Add(startup);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quitter", null, (_, _) => Shutdown());
    }

    private static Drawing.Icon LoadTrayIcon()
    {
        using var stream = GetResourceStream(new Uri("pack://application:,,,/Assets/DropMe.ico")).Stream;
        return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) is string;
    }

    private static void SetStartupEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled && Environment.ProcessPath is string exe)
            key.SetValue(RunValueName, $"\"{exe}\" {StartupArgument}");
        else
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
    }
}
