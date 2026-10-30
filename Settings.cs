using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace DropMe;

/// <summary>User preferences, saved to %APPDATA%\DropMe\settings.json.</summary>
internal sealed class Settings
{
    public static readonly int[] HistorySizeChoices = { 3, 5, 10, 20, 50 };
    public const int DefaultHistorySize = 5;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DropMe", "settings.json");

    public int HistorySize { get; set; } = DefaultHistorySize;

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) is { } settings)
            {
                settings.HistorySize = Math.Clamp(settings.HistorySize, 1, HistorySizeChoices[^1]);
                return settings;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DropMe: could not read settings: {ex}");
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DropMe: could not save settings: {ex}");
        }
    }
}
