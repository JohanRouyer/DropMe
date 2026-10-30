using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DropMe;

/// <summary>Visual pieces shared by the shelves and the DropMe window.</summary>
internal static class ShelfStack
{
    public static readonly Brush Accent = Frozen(Color.FromRgb(0x58, 0x65, 0xF2));
    private static readonly Brush CardFill = Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
    private static readonly Brush CardStroke = Frozen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));

    // Thumbnails are slow to build; the same files get drawn over and over.
    private static readonly Dictionary<(string Path, int Size), ImageSource?> Cache = new();

    /// <summary>Fills <paramref name="host"/> with a fanned stack of up to three cards, newest on top.</summary>
    public static void Build(Panel host, IReadOnlyList<string> items, double cardSize)
    {
        host.Children.Clear();
        double[] angles = { -9, 7, 0 };
        var shown = items.TakeLast(3).ToList();
        for (int i = 0; i < shown.Count; i++)
            host.Children.Add(MakeCard(shown[i], angles[3 - shown.Count + i], cardSize));

        if (items.Count > 1)
        {
            host.Children.Add(new Border
            {
                Background = Accent,
                CornerRadius = new CornerRadius(10),
                MinWidth = 20,
                Height = 20,
                Padding = new Thickness(6, 0, 6, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = items.Count.ToString(),
                    Foreground = Brushes.White,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            });
        }
    }

    public static FrameworkElement Thumbnail(string path, double size)
    {
        int pixels = (int)(size * 2);
        var key = (path.ToLowerInvariant(), pixels);
        if (!Cache.TryGetValue(key, out var thumb))
        {
            if (Cache.Count > 300)
                Cache.Clear();
            thumb = Cache[key] = ShellThumbnail.Get(path, pixels);
        }

        return thumb != null
            ? new Image { Source = thumb, Stretch = Stretch.Uniform }
            : new TextBlock
            {
                Text = "📄",
                FontSize = size * 0.4,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
    }

    public static string DisplayName(string path) =>
        Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name ? name : path;

    public static string Describe(IReadOnlyList<string> items) =>
        items.Count == 1 ? DisplayName(items[0]) : $"{items.Count} éléments";

    private static FrameworkElement MakeCard(string path, double angle, double size) => new Border
    {
        Width = size,
        Height = size,
        Padding = new Thickness(size / 15),
        CornerRadius = new CornerRadius(size / 9),
        Background = CardFill,
        BorderBrush = CardStroke,
        BorderThickness = new Thickness(1),
        RenderTransformOrigin = new Point(0.5, 0.5),
        RenderTransform = new RotateTransform(angle),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Child = Thumbnail(path, size),
    };

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
