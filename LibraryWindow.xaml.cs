using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DropMe;

/// <summary>The DropMe window opened from the tray icon: open shelves and the recent ones, as cards.</summary>
public partial class LibraryWindow : Window
{
    private static readonly Brush CardIdle = ShelfStack.Frozen(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    private static readonly Brush CardHover = ShelfStack.Frozen(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
    private static readonly Brush OpenBadge = ShelfStack.Frozen(Color.FromRgb(0x3B, 0xA5, 0x5D));

    private readonly App _app;
    private bool _closing;

    internal LibraryWindow(App app)
    {
        _app = app;
        InitializeComponent();
        Deactivated += (_, _) =>
        {
            if (!_closing)
                Close();
        };
        Closing += (_, _) => _closing = true;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
    }

    /// <summary>Shows the window above the notification area, like a Windows flyout.</summary>
    public void ShowFlyout()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width;
        Top = area.Bottom - Height;
        Rebuild();
        Show();
        Activate();
    }

    private void Rebuild()
    {
        Cards.Children.Clear();

        var open = _app.OpenShelves.Where(s => !s.IsEmpty).ToList();
        foreach (var shelf in open)
            Cards.Children.Add(MakeCard(shelf.Items, "Open", isOpen: true,
                onOpen: () => _app.BringShelf(shelf), onRemove: null));

        foreach (var entry in _app.History.Entries)
            Cards.Children.Add(MakeCard(entry.Items, ShelfHistory.RelativeTime(entry.ClosedAt), isOpen: false,
                onOpen: () => _app.ReopenFromHistory(entry),
                onRemove: () =>
                {
                    _app.History.Remove(entry);
                    Rebuild();
                }));

        int recent = _app.History.Entries.Count;
        string kept = $"history: {_app.History.Capacity} max";
        Subtitle.Text = (open.Count, recent) switch
        {
            (0, 0) => $"Your shelves will show up here · {kept}",
            (0, _) => $"{recent} recent {(recent == 1 ? "shelf" : "shelves")} · {kept}",
            (_, 0) => $"{open.Count} open · {kept}",
            _ => $"{open.Count} open · {recent} recent · {kept}",
        };
        ClearButton.Visibility = recent > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateButton.Visibility = _app.AvailableUpdate != null ? Visibility.Visible : Visibility.Collapsed;
        UpdateButton.Content = _app.AvailableUpdate is { } update ? $"⬆  Update to {update.Version}" : null;
        EmptyState.Visibility = Cards.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private FrameworkElement MakeCard(IReadOnlyList<string> items, string caption, bool isOpen,
        Action onOpen, Action? onRemove)
    {
        var stack = new Grid { Height = 112, Margin = new Thickness(10, 12, 10, 0) };
        ShelfStack.Build(stack, items, 72);

        var title = new TextBlock
        {
            Text = ShelfStack.Describe(items),
            Foreground = Brushes.White,
            FontSize = 12.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(10, 8, 10, 0),
        };

        var captionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 12),
        };
        if (isOpen)
            captionRow.Children.Add(new Border
            {
                Width = 7,
                Height = 7,
                CornerRadius = new CornerRadius(4),
                Background = OpenBadge,
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
        captionRow.Children.Add(new TextBlock
        {
            Text = caption,
            Foreground = ShelfStack.TextSecondary,
            FontSize = 11,
        });

        var content = new StackPanel();
        content.Children.Add(stack);
        content.Children.Add(title);
        content.Children.Add(captionRow);

        var layers = new Grid();
        layers.Children.Add(content);

        Button? remove = null;
        if (onRemove != null)
        {
            remove = new Button
            {
                Style = (Style)FindResource("IconButton"),
                Content = "✕",
                ToolTip = "Remove from history",
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8),
                Visibility = Visibility.Hidden,
            };
            remove.Click += (_, _) => onRemove();
            layers.Children.Add(remove);
        }

        var card = new Border
        {
            Width = 166,
            Margin = new Thickness(0, 0, 10, 10),
            CornerRadius = new CornerRadius(14),
            Background = CardIdle,
            Cursor = Cursors.Hand,
            Child = layers,
            ToolTip = string.Join(Environment.NewLine, items.Take(10)) + (items.Count > 10 ? $"{Environment.NewLine}…" : ""),
        };
        card.MouseEnter += (_, _) =>
        {
            card.Background = CardHover;
            if (remove != null)
                remove.Visibility = Visibility.Visible;
        };
        card.MouseLeave += (_, _) =>
        {
            card.Background = CardIdle;
            if (remove != null)
                remove.Visibility = Visibility.Hidden;
        };
        card.MouseLeftButtonUp += (_, _) =>
        {
            if (_closing)
                return;
            Close();
            onOpen();
        };
        return card;
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (_closing)
            return;
        Close();
        _app.OpenNewShelf();
    }

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_closing)
            return;
        Close();
        _app.InstallUpdate();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = SettingsButton };
        menu.Items.Add(new MenuItem { Header = "History size", IsEnabled = false });
        foreach (int size in Settings.HistorySizeChoices)
        {
            int choice = size;
            var item = new MenuItem
            {
                Header = $"{choice} shelves",
                IsCheckable = true,
                IsChecked = _app.Settings.HistorySize == choice,
            };
            item.Click += (_, _) =>
            {
                _app.SetHistorySize(choice);
                Rebuild();
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _app.History.Clear();
        Rebuild();
    }
}
