using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DropMe.Screenshots;

/// <summary>
/// Renders DropMe's real windows, filled with sample files, into PNGs: screenshots for the README
/// and sprites that tools/make_demo_gif.py animates. Windows stay off-screen, the user's history and
/// settings are not touched, and DropMe's tray icon and mouse hook are never started.
/// </summary>
/// <remarks>Usage: DropMe.Screenshots &lt;docs folder&gt; &lt;sprites folder&gt;</remarks>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        string docs = Path.GetFullPath(args.Length > 0 ? args[0] : "docs");
        string sprites = Path.GetFullPath(args.Length > 1 ? args[1] : Path.Combine(docs, "sprites"));
        Directory.CreateDirectory(docs);
        Directory.CreateDirectory(sprites);

        new CaptureApp(docs, sprites) { ShutdownMode = ShutdownMode.OnExplicitShutdown }.Run();
    }
}

/// <summary>DropMe's App with its startup (single instance, tray icon, mouse hook) replaced by the captures.</summary>
internal sealed class CaptureApp : App
{
    // Render at twice the size so images stay sharp on high-DPI screens and when scaled down.
    private const double Scale = 2;
    private const double Offscreen = -32000;

    private readonly string _docs;
    private readonly string _sprites;

    public CaptureApp(string docs, string sprites)
    {
        _docs = docs;
        _sprites = sprites;
        History = ShelfHistory.InMemory(10);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            Capture();
        }
        finally
        {
            Shutdown();
        }
    }

    private void Capture()
    {
        var files = SampleFiles.Create(Path.Combine(Path.GetTempPath(), "DropMe screenshots"));

        // Sprites for the animation: an empty shelf, the same shelf under a drag, then holding one photo.
        var shelf = new ShelfWindow();
        shelf.Show();
        Save(shelf, Path.Combine(_sprites, "shelf-empty.png"));
        shelf.SetHighlight(true);
        Save(shelf, Path.Combine(_sprites, "shelf-highlight.png"));
        shelf.SetHighlight(false);
        shelf.LoadItems(new[] { files.Sunset });
        Save(shelf, Path.Combine(_sprites, "shelf-one.png"));
        shelf.Close();

        // README: a stack of photos, and the expanded list.
        shelf = new ShelfWindow();
        shelf.Show();
        shelf.LoadItems(new[] { files.Lake, files.Mountains, files.Sunset });
        Save(shelf, Path.Combine(_docs, "shelf.png"));
        shelf.Close();

        shelf = new ShelfWindow();
        shelf.Show();
        shelf.LoadItems(new[] { files.Sunset, files.Mountains, files.CityLights, files.Notes, files.PhotosFolder });
        shelf.SetExpanded(true);
        Save(shelf, Path.Combine(_docs, "shelf-expanded.png"));
        shelf.Close();

        // README: the DropMe window with a few recent shelves (added oldest first).
        var now = DateTime.Now;
        History.Add(new[] { files.PhotosFolder }, now.AddDays(-4));
        History.Add(new[] { files.CityLights, files.Lake }, now.AddDays(-3));
        History.Add(new[] { files.CityLights }, now.AddDays(-1));
        History.Add(new[] { files.Notes, files.PhotosFolder }, now.AddHours(-2));
        History.Add(new[] { files.Sunset, files.Mountains, files.Lake }, now.AddMinutes(-12));
        var library = new LibraryWindow(this) { Left = Offscreen, Top = Offscreen, ShowActivated = false };
        library.Show();
        library.Rebuild();
        Save(library, Path.Combine(_docs, "library.png"));
        library.Close();

        // Sprites: desktop-style icons for the file being dragged and the folder it ends up in.
        SaveElement(DesktopIcon(files.Sunset), Path.Combine(_sprites, "icon-file.png"));
        SaveElement(DesktopIcon(files.PhotosFolder), Path.Combine(_sprites, "icon-folder.png"));
    }

    private static FrameworkElement DesktopIcon(string path)
    {
        var panel = new StackPanel { Width = 110, Margin = new Thickness(8) };
        panel.Children.Add(new Border { Width = 84, Height = 84, Child = ShelfStack.Thumbnail(path, 84) });
        panel.Children.Add(new TextBlock
        {
            Text = ShelfStack.DisplayName(path),
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 13,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.8 },
        });
        return panel;
    }

    private static void Save(Window window, string path)
    {
        // Let layout and the background-priority thumbnail loading finish first.
        Pump();
        window.UpdateLayout();
        Write(window, window.ActualWidth, window.ActualHeight, path);
    }

    private static void SaveElement(FrameworkElement element, string path)
    {
        void Layout()
        {
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            element.Arrange(new Rect(element.DesiredSize));
            element.UpdateLayout();
        }

        Layout();
        Pump();
        Layout();
        var size = element.DesiredSize;
        Write(element, size.Width, size.Height, path);
    }

    private static void Write(Visual visual, double width, double height, string path)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * Scale), (int)Math.Ceiling(height * Scale),
            96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
        Console.WriteLine($"  {Path.GetFileName(path)}  {bitmap.PixelWidth}x{bitmap.PixelHeight}");
    }

    /// <summary>Runs queued dispatcher work (layout, thumbnails) for a moment.</summary>
    private static void Pump(int milliseconds = 600)
    {
        var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        do
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            Thread.Sleep(15);
        } while (DateTime.UtcNow < until);
    }
}
