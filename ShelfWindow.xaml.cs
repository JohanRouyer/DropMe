using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DropMe;

/// <summary>A small floating shelf that holds references to dropped files until they are dragged out again.</summary>
public partial class ShelfWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    // Matches Left="-32000" in the XAML, with room for DPI rounding.
    private const int ParkedOffscreen = -30000;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    private static readonly string TempFolder = Path.Combine(Path.GetTempPath(), "DropMe");
    private static readonly Brush IdleOutline = ShelfStack.Frozen(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF));
    private static readonly Brush IdleBorder = ShelfStack.Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
    private static readonly Brush RowHover = ShelfStack.Frozen(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF));

    /// <summary>True while one of the shelves is the source of a drag, so a shake doesn't spawn a new shelf.</summary>
    public static bool IsDraggingOut { get; private set; }

    /// <summary>Close this shelf automatically if the drag ends without anything being dropped on it.</summary>
    public bool AutoCloseIfEmpty { get; set; }

    public bool IsEmpty => _items.Count == 0;

    public IReadOnlyList<string> Items => _items;

    private readonly List<string> _items = new();

    private bool _draggingFromHere;
    private bool _expanded;
    private bool _pressed;
    private Point _pressPoint;

    public ShelfWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Never steal focus from the app the user is dragging from, and stay out of Alt+Tab.
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    /// <summary>Creates the window off-screen so a later <see cref="ShowNearCursor"/> is instant.</summary>
    public void Prewarm()
    {
        Show();
    }

    public void LoadItems(IEnumerable<string> paths)
    {
        _items.AddRange(paths.Where(p => File.Exists(p) || Directory.Exists(p)));
        Refresh();
    }

    public void ShowNearCursor()
    {
        if (!IsVisible)
            Show();

        GetCursorPos(out var cursor);
        var dpi = VisualTreeHelper.GetDpi(this);
        int width = (int)(Width * dpi.DpiScaleX);
        int height = (int)(Height * dpi.DpiScaleY);
        int gap = (int)(24 * dpi.DpiScaleX);
        var area = Forms.Screen.FromPoint(new Drawing.Point(cursor.X, cursor.Y)).WorkingArea;

        // Right of the cursor, or left of it when there is no room on the right.
        int x = cursor.X + gap;
        if (x + width > area.Right)
            x = cursor.X - gap - width;
        int y = Math.Clamp(cursor.Y - height / 2, area.Top, Math.Max(area.Top, area.Bottom - height));

        SetWindowPos(new WindowInteropHelper(this).Handle, HWND_TOPMOST, x, y, 0, 0,
            SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        PlayAppearAnimation();
    }

    private void PlayAppearAnimation()
    {
        var duration = TimeSpan.FromMilliseconds(160);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.85, 1, duration) { EasingFunction = ease });
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.85, 1, duration) { EasingFunction = ease });
    }

    // ---- Dropping onto the shelf ----

    protected override void OnDragEnter(DragEventArgs e)
    {
        UpdateDragEffect(e);
        SetHighlight(e.Effects != DragDropEffects.None);
    }

    protected override void OnDragOver(DragEventArgs e) => UpdateDragEffect(e);

    protected override void OnDragLeave(DragEventArgs e)
    {
        SetHighlight(false);
        e.Handled = true;
    }

    protected override async void OnDrop(DragEventArgs e)
    {
        SetHighlight(false);
        e.Handled = true;
        if (_draggingFromHere)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        // The data object is only valid during this call, so read everything before awaiting.
        var paths = ExtractPaths(e.Data, out string? imageUrl, out string? linkText);
        // The shelf only keeps references: never report Move, or Explorer would delete the originals.
        e.Effects = paths.Count > 0 || imageUrl != null || linkText != null ? DragDropEffects.Copy : DragDropEffects.None;
        AutoCloseIfEmpty = false;

        if (imageUrl != null && await ImageDownloader.TryDownloadAsync(imageUrl, TempFolder, UniquePath) is string image)
            paths.Add(image);
        else if (linkText != null && SaveText(linkText) is string textFile)
            paths.Add(textFile);

        foreach (var path in paths.Where(p => !_items.Contains(p, StringComparer.OrdinalIgnoreCase)))
            _items.Add(path);
        Refresh();
    }

    private void UpdateDragEffect(DragEventArgs e)
    {
        e.Effects = !_draggingFromHere && CanAccept(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private static bool CanAccept(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop)
        || data.GetDataPresent(VirtualFiles.DescriptorFormat)
        || data.GetDataPresent(DataFormats.Html)
        || data.GetDataPresent(DataFormats.Bitmap)
        || data.GetDataPresent(DataFormats.UnicodeText);

    /// <summary>
    /// Returns files that can be added right away. An image that still has to be downloaded comes
    /// back as <paramref name="imageUrl"/>; plain text or a link as <paramref name="text"/>.
    /// </summary>
    private static List<string> ExtractPaths(IDataObject data, out string? imageUrl, out string? text)
    {
        imageUrl = null;
        text = null;
        try
        {
            if (data.GetData(DataFormats.FileDrop) is string[] files)
                return files.ToList();

            Directory.CreateDirectory(TempFolder);

            // Browsers hand over the actual image bytes this way (same as dropping on the desktop).
            var virtualFiles = VirtualFiles.Save(data, TempFolder, UniquePath);
            if (virtualFiles.Count > 0)
                return virtualFiles;

            var html = data.GetData(DataFormats.Html) as string;
            var plain = data.GetData(DataFormats.UnicodeText) as string;
            imageUrl = ImageDownloader.FindImageUrl(html, plain);
            if (imageUrl != null)
            {
                // A local image referenced by the HTML is already a file.
                if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) && uri.IsFile && File.Exists(uri.LocalPath))
                {
                    imageUrl = null;
                    return new List<string> { uri.LocalPath };
                }
                text = plain;
                return new List<string>();
            }

            if (data.GetData(DataFormats.Bitmap) is BitmapSource image)
            {
                string stamp = DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss");
                string path = UniquePath(Path.Combine(TempFolder, $"Image {stamp}.png"));
                using var stream = File.Create(path);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                encoder.Save(stream);
                return new List<string> { path };
            }

            if (!string.IsNullOrEmpty(plain))
                text = plain;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DropMe: could not read dropped data: {ex}");
        }
        return new List<string>();
    }

    /// <summary>Saves dropped text as a .txt file, or as a .url shortcut when it is a web link.</summary>
    private static string? SaveText(string text)
    {
        try
        {
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss");
            bool isUrl = Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)
                         && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
            string path = isUrl
                ? UniquePath(Path.Combine(TempFolder, $"Link {stamp}.url"))
                : UniquePath(Path.Combine(TempFolder, $"Text {stamp}.txt"));
            File.WriteAllText(path, isUrl ? $"[InternetShortcut]\r\nURL={uri}\r\n" : text, Encoding.UTF8);
            return path;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DropMe: could not save text: {ex}");
            return null;
        }
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path))
            return path;
        string dir = Path.GetDirectoryName(path)!, name = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            string candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }
    }

    internal void SetHighlight(bool on)
    {
        var accent = (Brush)FindResource("Accent");
        DropOutline.Stroke = on ? accent : IdleOutline;
        Card.BorderBrush = on ? accent : IdleBorder;
    }

    // ---- Dragging out of the shelf ----

    // Collapsed stack: a click expands it, a drag takes every file.
    private void Stack_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => BeginPress(e);

    private void Stack_MouseMove(object sender, MouseEventArgs e)
    {
        if (DragThresholdPassed(e))
            StartDragOut(_items.ToArray(), StackHost);
    }

    private void Stack_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed)
            return;
        _pressed = false;
        SetExpanded(true);
    }

    // Expanded footer: drag it to take every file at once.
    private void Footer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_expanded)
            BeginPress(e);
    }

    private void Footer_MouseMove(object sender, MouseEventArgs e)
    {
        if (_expanded && DragThresholdPassed(e))
            StartDragOut(_items.ToArray(), Footer);
    }

    private void Footer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _pressed = false;

    private void BeginPress(MouseButtonEventArgs e)
    {
        _pressed = true;
        _pressPoint = e.GetPosition(this);
    }

    private bool DragThresholdPassed(MouseEventArgs e)
    {
        if (!_pressed || e.LeftButton != MouseButtonState.Pressed)
            return false;
        var delta = e.GetPosition(this) - _pressPoint;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return false;
        _pressed = false;
        return true;
    }

    private void StartDragOut(string[] paths, DependencyObject source)
    {
        paths = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
        if (paths.Length == 0)
        {
            PruneMissing();
            return;
        }

        var data = new DataObject(DataFormats.FileDrop, paths);
        _draggingFromHere = true;
        IsDraggingOut = true;
        try
        {
            DragDrop.DoDragDrop(source, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }
        finally
        {
            _draggingFromHere = false;
            IsDraggingOut = false;
        }

        // Explorer may report "None" even after a move, so check the disk instead of the result.
        PruneMissing();
        if (_items.Count == 0)
            Close();
    }

    private void PruneMissing()
    {
        int removed = _items.RemoveAll(p => !File.Exists(p) && !Directory.Exists(p));
        if (removed > 0)
            Refresh();
    }

    // ---- Header and menu ----

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        AutoCloseIfEmpty = false;
        DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Menu_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = MenuButton };
        menu.Items.Add(MakeMenuItem("Show in Explorer", RevealInExplorer, !IsEmpty));
        menu.Items.Add(MakeMenuItem("Copy paths", CopyPaths, !IsEmpty));
        menu.Items.Add(MakeMenuItem("Clear shelf", () => { _items.Clear(); Refresh(); }, !IsEmpty));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Close", Close, true));
        menu.IsOpen = true;
    }

    private static MenuItem MakeMenuItem(string header, Action action, bool enabled)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }

    private void RevealInExplorer()
    {
        PruneMissing();
        if (_items.Count == 0)
            return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_items[0]}\"") { UseShellExecute = true });
    }

    private void CopyPaths()
    {
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, _items));
        }
        catch (COMException)
        {
            // Clipboard is busy with another app; nothing useful to do.
        }
    }

    // ---- Rendering ----

    private void Collapse_Click(object sender, RoutedEventArgs e) => SetExpanded(false);

    internal void SetExpanded(bool expanded)
    {
        _expanded = expanded && !IsEmpty;
        Refresh();
    }

    private void Refresh()
    {
        bool empty = _items.Count == 0;
        if (empty)
            _expanded = false;
        else
            AutoCloseIfEmpty = false;

        EmptyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        StackHost.Visibility = !empty && !_expanded ? Visibility.Visible : Visibility.Collapsed;
        ListScroller.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        CollapseButton.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        Footer.Cursor = _expanded ? Cursors.Hand : null;
        Footer.ToolTip = _expanded ? "Drag to move everything" : null;

        if (_expanded)
        {
            CountText.Text = _items.Count > 1 ? $"⠿  Drag all {_items.Count} items" : "⠿  Drag";
            ListHost.Children.Clear();
            foreach (var path in _items)
                ListHost.Children.Add(MakeRow(path));
            StackHost.Children.Clear();
        }
        else
        {
            CountText.Text = empty ? "" : ShelfStack.Describe(_items);
            ShelfStack.Build(StackHost, _items, 92);
            ListHost.Children.Clear();
        }
        ApplySize();
    }

    private FrameworkElement MakeRow(string path)
    {
        var remove = new Button
        {
            Style = (Style)FindResource("IconButton"),
            Content = "✕",
            Width = 20,
            Height = 20,
            FontSize = 9,
            ToolTip = "Remove from shelf",
            Visibility = Visibility.Hidden,
            VerticalAlignment = VerticalAlignment.Center,
        };
        remove.Click += (_, _) =>
        {
            _items.Remove(path);
            Refresh();
        };

        var text = new StackPanel { Margin = new Thickness(10, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = ShelfStack.DisplayName(path),
            Foreground = ShelfStack.TextPrimary,
            FontSize = 12.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        text.Children.Add(new TextBlock
        {
            Text = DescribeFile(path),
            Foreground = ShelfStack.TextSecondary,
            FontSize = 11,
        });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var thumb = new Border { Width = 34, Height = 34, Child = ShelfStack.Thumbnail(path, 34) };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(remove, 2);
        grid.Children.Add(thumb);
        grid.Children.Add(text);
        grid.Children.Add(remove);

        var row = new Border
        {
            Child = grid,
            Padding = new Thickness(6, 5, 6, 5),
            Margin = new Thickness(0, 0, 0, 2),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = $"{path}\nDrag to move · double-click to open",
        };
        row.MouseEnter += (_, _) =>
        {
            row.Background = RowHover;
            remove.Visibility = Visibility.Visible;
        };
        row.MouseLeave += (_, _) =>
        {
            row.Background = Brushes.Transparent;
            remove.Visibility = Visibility.Hidden;
        };
        row.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
                OpenFile(path);
            else
                BeginPress(e);
        };
        row.MouseMove += (_, e) =>
        {
            if (DragThresholdPassed(e))
                StartDragOut(new[] { path }, row);
        };
        row.MouseLeftButtonUp += (_, _) => _pressed = false;
        return row;
    }

    private static string DescribeFile(string path)
    {
        if (Directory.Exists(path))
            return "Folder";
        try
        {
            long size = new FileInfo(path).Length;
            string ext = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
            string formatted = size switch
            {
                < 1024 => $"{size} B",
                < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{size / 1024.0:0.#} KB"),
                < 1024L * 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{size / (1024.0 * 1024):0.#} MB"),
                _ => string.Create(CultureInfo.InvariantCulture, $"{size / (1024.0 * 1024 * 1024):0.##} GB"),
            };
            return ext.Length > 0 ? $"{ext} · {formatted}" : formatted;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "Not found";
        }
    }

    private static void OpenFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DropMe: could not open {path}: {ex}");
        }
    }

    private void ApplySize()
    {
        double width = _expanded ? 300 : 190;
        // Header + footer + margins, then one 46px row per file, capped so long lists scroll.
        double height = _expanded ? Math.Clamp(100 + _items.Count * 46, 214, 480) : 214;
        if (Width == width && Height == height)
            return;
        Width = width;
        Height = height;
        Dispatcher.BeginInvoke(KeepOnScreen, DispatcherPriority.Loaded);
    }

    // Growing the shelf can push it past the screen edge; slide it back in.
    private void KeepOnScreen()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect))
            return;
        // Parked off-screen on purpose (the pre-created spare shelf): leave it there.
        if (rect.Left <= ParkedOffscreen)
            return;
        var area = Forms.Screen.FromHandle(hwnd).WorkingArea;
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        int x = Math.Clamp(rect.Left, area.Left, Math.Max(area.Left, area.Right - width));
        int y = Math.Clamp(rect.Top, area.Top, Math.Max(area.Top, area.Bottom - height));
        if (x != rect.Left || y != rect.Top)
            SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }
}
