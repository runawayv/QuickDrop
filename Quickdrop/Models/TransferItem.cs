using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QuickDrop.Models;

public enum TransferDirection { PcToPhone, PhoneToPc }
public enum TransferStatus { Pending, Transferring, Completed, Failed }

public class TransferItem : INotifyPropertyChanged
{
    private string _fileName = "";
    private string _filePath = "";
    private TransferDirection _direction;
    private TransferStatus _status = TransferStatus.Pending;
    private long _totalBytes;
    private long _receivedBytes;
    private string _statusText = "";
    private string _speedText = "";
    private string _sizeText = "";
    private string _percentText = "";
    private bool _indeterminate;
    private double _progressPercent;
    private readonly DateTime _time = DateTime.Now;
    private DateTime _lastRaiseUtc = DateTime.MinValue;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FileName { get => _fileName; set { _fileName = value; OnChanged(); } }
    public string FilePath { get => _filePath; set { _filePath = value; OnChanged(); } }
    public TransferDirection Direction
    {
        get => _direction;
        set { _direction = value; OnChanged(); OnChanged(nameof(DirectionIcon)); }
    }
    public TransferStatus Status { get => _status; set { _status = value; OnChanged(); } }
    public long TotalBytes { get => _totalBytes; set { _totalBytes = value; } }
    public long ReceivedBytes => _receivedBytes;
    public DateTime Time => _time;
    public string DirectionIcon => _direction == TransferDirection.PcToPhone ? "↑" : "↓";

    public string StatusText { get => _statusText; set { _statusText = value; OnChanged(); } }
    public string SpeedText => _speedText;
    public string SizeText => _sizeText;
    public string PercentText => _percentText;
    public bool Indeterminate => _indeterminate;
    public double ProgressPercent => _progressPercent;

    public void SetWaiting(string text)
    {
        _status = TransferStatus.Pending;
        _statusText = text;
        _speedText = "";
        Recompute();
        RaiseAll();
    }

    public void BeginTransfer(string text = "Передача…")
    {
        _status = TransferStatus.Transferring;
        _statusText = text;
        Recompute();
        RaiseAll();
    }

    public void UpdateProgress(long received, long total, double speedBytesPerSec)
    {
        _receivedBytes = Math.Max(0, received);
        if (total > 0) _totalBytes = total;
        _speedText = speedBytesPerSec > 1 ? FormatSpeed(speedBytesPerSec) : "";
        Recompute();

        bool done = _totalBytes > 0 && _receivedBytes >= _totalBytes;
        if (done || (DateTime.UtcNow - _lastRaiseUtc).TotalMilliseconds >= 100)
        {
            _lastRaiseUtc = DateTime.UtcNow;
            RaiseAll();
        }
    }

    public void Complete(string text = "Готово")
    {
        if (_totalBytes <= 0) _totalBytes = _receivedBytes;
        _receivedBytes = _totalBytes;
        _speedText = "";
        _status = TransferStatus.Completed;
        _statusText = text;
        Recompute();
        RaiseAll();
    }

    public void Fail(string text)
    {
        _status = TransferStatus.Failed;
        _statusText = text;
        _speedText = "";
        Recompute();
        RaiseAll();
    }

    private void Recompute()
    {
        if (_totalBytes > 0)
        {
            _progressPercent = Math.Min(100.0, _receivedBytes * 100.0 / _totalBytes);
            _sizeText = FormatSize(_receivedBytes) + " / " + FormatSize(_totalBytes);
            _percentText = Math.Round(_progressPercent) + "%";
            _indeterminate = false;
        }
        else
        {
            _progressPercent = 0;
            _sizeText = FormatSize(_receivedBytes);
            _percentText = "";
            _indeterminate = _status == TransferStatus.Transferring;
        }
    }

    private void RaiseAll()
    {
        OnChanged(nameof(StatusText));
        OnChanged(nameof(Status));
        OnChanged(nameof(SpeedText));
        OnChanged(nameof(SizeText));
        OnChanged(nameof(PercentText));
        OnChanged(nameof(Indeterminate));
        OnChanged(nameof(ProgressPercent));
        OnChanged(nameof(ReceivedBytes));
    }

    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public static string FormatSize(long bytes)
    {
        double v = bytes;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return i == 0 ? ((long)v).ToString() + " B" : v.ToString("0.0") + " " + units[i];
    }

    public static string FormatSpeed(double bytesPerSec)
        => bytesPerSec <= 0 ? "" : FormatSize((long)bytesPerSec) + "/с";
}
public class SharedFile
{
    public string Id { get; set; } = "";
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public long SizeBytes { get; set; }
    public TransferItem Item { get; set; } = new();
}