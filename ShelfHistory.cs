using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DropMe;

/// <summary>The last few closed shelves, saved to %APPDATA%\DropMe\history.json so they survive a restart.</summary>
internal sealed class ShelfHistory
{
    public sealed record Entry(List<string> Items, DateTime ClosedAt);

    private static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DropMe", "history.json");

    private readonly List<Entry> _entries = new();
    private int _capacity = Settings.DefaultHistorySize;
    // Null keeps the history in memory only (used when rendering screenshots).
    private string? _filePath = DefaultFilePath;

    /// <summary>A history that is never read from or written to disk.</summary>
    public static ShelfHistory InMemory(int capacity) => new() { _capacity = Math.Max(1, capacity), _filePath = null };

    public IReadOnlyList<Entry> Entries => _entries;

    /// <summary>How many closed shelves are kept. Lowering it drops the oldest ones right away.</summary>
    public int Capacity
    {
        get => _capacity;
        set
        {
            _capacity = Math.Max(1, value);
            if (_entries.Count > _capacity)
            {
                _entries.RemoveRange(_capacity, _entries.Count - _capacity);
                Save();
            }
        }
    }

    public static ShelfHistory Load(int capacity)
    {
        var history = new ShelfHistory { _capacity = Math.Max(1, capacity) };
        try
        {
            if (File.Exists(DefaultFilePath)
                && JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(DefaultFilePath)) is { } entries)
                history._entries.AddRange(entries.Where(e => e.Items is { Count: > 0 }).Take(history._capacity));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DropMe: could not read history: {ex}");
        }
        return history;
    }

    public void Add(IEnumerable<string> items, DateTime? closedAt = null)
    {
        var list = items.ToList();
        if (list.Count == 0)
            return;
        // The same set of files reopened and closed again just moves back to the top.
        _entries.RemoveAll(e => e.Items.Count == list.Count
                                && e.Items.All(p => list.Contains(p, StringComparer.OrdinalIgnoreCase)));
        _entries.Insert(0, new Entry(list, closedAt ?? DateTime.Now));
        if (_entries.Count > _capacity)
            _entries.RemoveRange(_capacity, _entries.Count - _capacity);
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
        if (age.TotalMinutes < 1) return "just now";
        if (age.TotalHours < 1) return $"{(int)age.TotalMinutes} min ago";
        if (when.Date == DateTime.Today) return $"{(int)age.TotalHours} h ago";
        if (when.Date == DateTime.Today.AddDays(-1)) return "yesterday";
        return when.ToString("MMM d", CultureInfo.InvariantCulture);
    }

    private void Save()
    {
        if (_filePath == null)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_entries));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DropMe: could not save history: {ex}");
        }
    }
}
