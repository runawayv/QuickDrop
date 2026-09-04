using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuickDrop.Models;

namespace QuickDrop.Services;

public class HistoryEntry
{
    public string Name { get; set; } = "";
    public long SizeBytes { get; set; }
    public TransferDirection Direction { get; set; }
    public DateTime Time { get; set; } = DateTime.Now;
    public string Status { get; set; } = "Готово";

    [JsonIgnore]
    public string DirectionText => Direction == TransferDirection.PcToPhone ? "ПК → телефон" : "телефон → ПК";
    [JsonIgnore]
    public string SizeText => TransferItem.FormatSize(SizeBytes);
    [JsonIgnore]
    public string TimeText => Time.ToString("dd.MM.yyyy HH:mm");
}

public class HistoryService
{
    private readonly object _lock = new();
    private readonly List<HistoryEntry> _entries = new();

    public event Action? Changed;

    private static string FilePath => Path.Combine(AppSettingsStore.Folder, "history.json");

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var list = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(FilePath));
            if (list == null) return;
            lock (_lock)
            {
                _entries.Clear();
                _entries.AddRange(list);
            }
        }
        catch { }
    }

    public List<HistoryEntry> GetSnapshot()
    {
        lock (_lock) return _entries.ToList();
    }

    public void Add(string name, long size, TransferDirection direction, string status = "Готово")
    {
        lock (_lock)
        {
            _entries.Add(new HistoryEntry
            {
                Name = name,
                SizeBytes = size,
                Direction = direction,
                Status = status,
                Time = DateTime.Now
            });
            while (_entries.Count > 500) _entries.RemoveAt(0);

            try
            {
                Directory.CreateDirectory(AppSettingsStore.Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(_entries,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
        Changed?.Invoke();
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            try
            {
                Directory.CreateDirectory(AppSettingsStore.Folder);
                File.WriteAllText(FilePath, "[]");
            }
            catch { }
        }
        Changed?.Invoke();
    }
}