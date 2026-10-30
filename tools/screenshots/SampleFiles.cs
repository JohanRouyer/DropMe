using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DropMe.Screenshots;

/// <summary>Believable files to put on the shelves: generated landscapes, a text file and a folder.</summary>
internal sealed class SampleFiles
{
    public string Sunset { get; private init; } = "";
    public string Mountains { get; private init; } = "";
    public string Lake { get; private init; } = "";
    public string CityLights { get; private init; } = "";
    public string Notes { get; private init; } = "";
    public string PhotosFolder { get; private init; } = "";

    public static SampleFiles Create(string folder)
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);

        var files = new SampleFiles
        {
            Sunset = Path.Combine(folder, "Sunset.jpg"),
            Mountains = Path.Combine(folder, "Mountains.jpg"),
            Lake = Path.Combine(folder, "Lake.jpg"),
            CityLights = Path.Combine(folder, "City lights.jpg"),
            Notes = Path.Combine(folder, "Meeting notes.txt"),
            PhotosFolder = Path.Combine(folder, "Vacation photos"),
        };

        Landscape(files.Sunset, "#FF7E5F", "#5B2A86", "#FFE29A", "#2B1B3D", 0.62);
        Landscape(files.Mountains, "#8EC5FC", "#E0F0FF", "#FFFFFF", "#3E5C76", 0.55);
        Landscape(files.Lake, "#43CEA2", "#185A9D", "#F6FFB8", "#0F3B4F", 0.7);
        Landscape(files.CityLights, "#0F2027", "#2C5364", "#FFD86B", "#0A1418", 0.6);

        // Only file types with Windows' own icons, so no third-party app logo shows up in the images.
        File.WriteAllText(files.Notes, string.Concat(System.Linq.Enumerable.Repeat(
            "Agenda\n- Launch date\n- Budget review\n- Next steps\n\n", 24)));
        Directory.CreateDirectory(files.PhotosFolder);
        return files;
    }

    /// <summary>A simple sky gradient with a sun and two mountain ridges.</summary>
    private static void Landscape(string path, string skyTop, string skyBottom, string sun, string ridge, double horizon)
    {
        const int width = 1200, height = 800;
        Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(C(skyTop), C(skyBottom), 90), null, new Rect(0, 0, width, height));
            dc.DrawEllipse(new RadialGradientBrush(C(sun), Color.FromArgb(0, 255, 255, 255)), null,
                new Point(width * 0.68, height * (horizon - 0.12)), 210, 210);
            dc.DrawEllipse(new SolidColorBrush(C(sun)), null, new Point(width * 0.68, height * (horizon - 0.12)), 70, 70);

            var back = Color.FromArgb(170, C(ridge).R, C(ridge).G, C(ridge).B);
            dc.DrawGeometry(new SolidColorBrush(back), null, Ridge(width, height, horizon - 0.08, 0.9));
            dc.DrawGeometry(new SolidColorBrush(C(ridge)), null, Ridge(width, height, horizon + 0.05, 2.1));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static Geometry Ridge(int width, int height, double level, double phase)
    {
        var figure = new PathFigure { StartPoint = new Point(0, height), IsClosed = true };
        for (int x = 0; x <= width; x += 40)
        {
            double y = height * level - 70 * Math.Sin(x / 170.0 + phase) - 35 * Math.Sin(x / 63.0 + phase * 2);
            figure.Segments.Add(new LineSegment(new Point(x, y), true));
        }
        figure.Segments.Add(new LineSegment(new Point(width, height), true));
        return new PathGeometry(new[] { figure });
    }
}
