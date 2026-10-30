using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DropMe;

/// <summary>The last few closed shelves, saved to %APPDATA%\DropMe\history.json so they survive a restart.</summary>
internal sealed class ShelfHistory
{
    public const int Capacity = 5;

    public sealed record Entry(List<string> Items, DateTime ClosedAt);

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DropMe", "history.json");

    private readonly List<Entry> _entries = new();

    public IReadOnlyList<Entry> Entries => _entries;

    public static ShelfHistory Load()
    {
        var history = new ShelfHistory();
        try
        {
            if (File.Exists(FilePath)
                && JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(FilePath)) is { } entries)
                history._entries.AddRange(entries.Where(e => e.Items is { Count: > 0 }).Take(Capacity));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DropMe: could not read history: {ex}");
        }
        return history;
    }

    public void Add(IEnumerable<string> items)
    {
        var list = items.ToList();
        if (list.Count == 0)
            return;
        // The same set of files reopened and closed again just moves back to the top.
        _entries.RemoveAll(e => e.Items.Count == list.Count
                                && e.Items.All(p => list.Contains(p, StringComparer.OrdinalIgnoreCase)));
        _entries.Insert(0, new Entry(list, DateTime.Now));
        if (_entries.Count > Capacity)
            _entries.RemoveRange(Capacity, _entries.Count - Capacity);
        Save();
    }

    public void Remove(Entry entry)
    {
        if (_entries.Remove(entry))
            Save();
    }

    public void Clear()
    {
        _entries.Clear();
        Save();
    }

    public static string RelativeTime(DateTime when)
    {
        var age = DateTime.Now - when;
        if (age.TotalMinutes < 1) return "à l'instant";
        if (age.TotalHours < 1) return $"il y a {(int)age.TotalMinutes} min";
        if (when.Date == DateTime.Today) return $"il y a {(int)age.TotalHours} h";
        if (when.Date == DateTime.Today.AddDays(-1)) return "hier";
        return when.ToString("d MMM");
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_entries));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DropMe: could not save history: {ex}");
        }
    }
}
