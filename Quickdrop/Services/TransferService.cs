using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using QuickDrop.Models;

namespace QuickDrop.Services;

public class TransferService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, SharedFile> _shared = new(StringComparer.Ordinal);
    public ObservableCollection<TransferItem> Transfers { get; } = new();
    public event Action? SharedChanged;
    public List<SharedFile> GetSharedSnapshot()
    {
        lock (_gate) return _shared.Values.ToList();
    }

    public SharedFile? FindShared(string id)
    {
        lock (_gate) return _shared.TryGetValue(id, out var f) ? f : null;
    }
    public SharedFile AddFile(string path)
    {
        string full = Path.GetFullPath(path);
        var file = new SharedFile
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            FileName = Path.GetFileName(full),
            FilePath = full,
            SizeBytes = new FileInfo(full).Length
        };

        var item = CreateItem(file.FileName, TransferDirection.PcToPhone, file.SizeBytes, full);
        item.SetWaiting("Ожидает скачивания");
        file.Item = item;

        lock (_gate) { _shared[file.Id] = file; }
        SharedChanged?.Invoke();
        return file;
    }


    public int AddFolder(string path)
    {
        int count = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(path))
            {
                try { AddFile(f); count++; }
                catch { /* ! */ }
            }
        }
        catch { }
        return count;
    }
    public TransferItem CreateItem(string fileName, TransferDirection direction, long totalBytes, string filePath)
    {
        var item = new TransferItem
        {
            FileName = fileName,
            Direction = direction,
            TotalBytes = totalBytes,
            FilePath = filePath
        };
        Application.Current?.Dispatcher.BeginInvoke(() => Transfers.Add(item));
        return item;
    }

    public void ClearFinished()
    {
        var done = Transfers
            .Where(t => t.Status is TransferStatus.Completed or TransferStatus.Failed)
            .ToList();
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            foreach (var t in done) Transfers.Remove(t);
        });
    }
}