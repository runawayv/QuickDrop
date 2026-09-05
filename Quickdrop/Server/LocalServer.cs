using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QuickDrop.Models;
using QuickDrop.Services;
namespace QuickDrop.Server;
public class LocalServer
{
    private AppSettings _settings;
    private readonly TransferService _transfers;
    private readonly HistoryService _history;
    private readonly string _wwwRoot;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public bool IsRunning { get; private set; }
    public string BoundIp { get; private set; } = "";
    public string LastError { get; private set; } = "";
    public int Port => _settings.Port;

    public event Action? StateChanged;

    public event Action<string, bool>? TextReceived;

    public LocalServer(AppSettings settings, TransferService transfers, HistoryService history)
    {
        _settings = settings;
        _transfers = transfers;
        _history = history;
        _wwwRoot = Path.Combine(AppContext.BaseDirectory, "www");
    }

    public void UpdateSettings(AppSettings settings) => _settings = settings;

    public void Start()
    {
        if (IsRunning) return;
        LastError = "";
        try
        {
            string ip = ResolveBindIp();
            var address = ip == "0.0.0.0" ? IPAddress.Any : IPAddress.Parse(ip);
            var listener = new TcpListener(address, _settings.Port);
            listener.Start();

            _listener = listener;
            BoundIp = ip;
            IsRunning = true;
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => AcceptLoopAsync(listener, _cts.Token));
        }
        catch (Exception ex)
        {
            IsRunning = false;
            LastError = ex.Message;
            try { _listener?.Stop(); } catch { }
            _listener = null;
        }
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        _listener = null;
        StateChanged?.Invoke();
    }

    private string ResolveBindIp()
    {
        var adapters = NetworkService.GetActiveAdapters();
        if (!string.IsNullOrEmpty(_settings.SelectedIp))
        {
            var selected = adapters.FirstOrDefault(a => a.Ip == _settings.SelectedIp);
            if (selected != null) return selected.Ip;
        }
        return adapters.FirstOrDefault()?.Ip ?? "0.0.0.0";
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct);
            }
            catch
            {
                break;
            }
            _ = Task.Run(() => HandleClientAsync(client, ct));
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            client.NoDelay = true;
            using (client)
            await using (var raw = client.GetStream())
            {
                var conn = new HttpConnection(raw);

                using (var headCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    headCts.CancelAfter(TimeSpan.FromSeconds(60));
                    var req = await conn.ReadRequestAsync(headCts.Token);
                    if (req == null) return;

                    if (req.Method == "GET")
                        await HandleGetAsync(conn, req, ct);
                    else if (req.Method == "POST")
                        await HandlePostAsync(conn, req, ct);
                    else
                        await conn.SendJsonAsync(405, "Method Not Allowed", "{\"ok\":false}", ct);
                }
            }
        }
        catch
        {

        }
    }
    private async Task HandleGetAsync(HttpConnection conn, HttpRequest req, CancellationToken ct)
    {
        string path = req.Path == "/" ? "/index.html" : req.Path;

        switch (path)
        {
            case "/index.html":
            case "/style.css":
            case "/app.js":
                await SendStaticAsync(conn, path, ct);
                break;

            case "/api/info":
                await conn.SendJsonAsync(200, "OK", JsonSerializer.Serialize(
                    new { app = "QuickDrop", requirePin = _settings.RequirePin }), ct);
                break;

            case "/api/files":
                if (!IsAuthorized(req))
                {
                    await conn.SendJsonAsync(401, "Unauthorized", "{\"ok\":false,\"error\":\"pin\"}", ct);
                    break;
                }
                await conn.SendJsonAsync(200, "OK", BuildFilesJson(), ct);
                break;

            case "/download":
                if (!IsAuthorized(req))
                {
                    await conn.SendJsonAsync(401, "Unauthorized", "{\"ok\":false,\"error\":\"pin\"}", ct);
                    break;
                }
                await SendDownloadAsync(conn, req, ct);
                break;

            default:
                await conn.SendJsonAsync(404, "Not Found", "{\"ok\":false}", ct);
                break;
        }
    }

    private async Task SendStaticAsync(HttpConnection conn, string path, CancellationToken ct)
    {
        string file = Path.Combine(_wwwRoot, path.TrimStart('/'));
        if (!File.Exists(file))
        {
            await conn.SendJsonAsync(404, "Not Found", "404", ct);
            return;
        }

        string mime = path.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ? "text/css; charset=utf-8"
                    : path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ? "application/javascript; charset=utf-8"
                    : "text/html; charset=utf-8";

        var body = await File.ReadAllBytesAsync(file, ct);
        await conn.SendAsync(200, "OK", mime, body, null, ct);
    }

    private string BuildFilesJson()
    {
        var files = _transfers.GetSharedSnapshot()
            .Select(f => new { id = f.Id, name = f.FileName, size = f.SizeBytes })
            .ToArray();
        return JsonSerializer.Serialize(new { files });
    }

    private async Task SendDownloadAsync(HttpConnection conn, HttpRequest req, CancellationToken ct)
    {
        string id = GetQueryParam(req.Query, "id");
        var file = _transfers.FindShared(id);
        if (file == null || !File.Exists(file.FilePath))
        {
            await conn.SendJsonAsync(404, "Not Found", "{\"ok\":false}", ct);
            return;
        }

        var info = new FileInfo(file.FilePath);
        string encoded = Uri.EscapeDataString(file.FileName);
        var headers = new List<KeyValuePair<string, string>>
        {
            new("Content-Disposition",
                "attachment; filename=\"" + SanitizeHeaderName(file.FileName) +
                "\"; filename*=UTF-8''" + encoded)
        };

        var item = _transfers.CreateItem(file.FileName, TransferDirection.PcToPhone, info.Length, file.FilePath);
        item.BeginTransfer("Отправка…");

        long sent = 0;
        try
        {
            await using var fs = new FileStream(file.FilePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 64 * 1024, useAsync: true);
            await conn.SendHeadersAsync(200, "OK", "application/octet-stream", info.Length, headers, ct);

            var buffer = new byte[64 * 1024];
            var meter = new SpeedMeter();
            int read;
            while ((read = await fs.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                await conn.WriteAsync(buffer, read, ct);
                sent += read;
                item.UpdateProgress(sent, info.Length, meter.Update(sent));
            }
            item.Complete("Отправлено");
            _history.Add(file.FileName, info.Length, TransferDirection.PcToPhone, "Готово");
        }
        catch
        {
            item.Fail("Прервано");
            _history.Add(file.FileName, sent, TransferDirection.PcToPhone, "Прервано");
        }
    }
    private async Task HandlePostAsync(HttpConnection conn, HttpRequest req, CancellationToken ct)
    {
        if (!IsAuthorized(req))
        {
            if (req.Path == "/upload" && req.ContentLength > 0 && req.ContentLength < 5_000_000)
            {
                try { await conn.CopyToAsync(Stream.Null, req.ContentLength, ct); } catch { }
            }
            await conn.SendJsonAsync(401, "Unauthorized", "{\"ok\":false,\"error\":\"pin\"}", ct);
            return;
        }

        switch (req.Path)
        {
            case "/api/login":
                await HandleLoginAsync(conn, req, ct);
                break;
            case "/api/text":
                await HandleTextLikeAsync(conn, req, ct, isText: true);
                break;
            case "/api/link":
                await HandleTextLikeAsync(conn, req, ct, isText: false);
                break;
            case "/upload":
                await HandleUploadAsync(conn, req, ct);
                break;
            default:
                await conn.SendJsonAsync(404, "Not Found", "{\"ok\":false}", ct);
                break;
        }
    }

    private async Task HandleLoginAsync(HttpConnection conn, HttpRequest req, CancellationToken ct)
    {
        long len = Math.Min(req.ContentLength, 4096);
        string body = len > 0 ? await conn.ReadBodyStringAsync(len, ct) : "";

        string? pin = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("pin", out var p) &&
                p.ValueKind == JsonValueKind.String)
                pin = p.GetString();
        }
        catch { }
        bool ok = _settings.RequirePin
                  && !string.IsNullOrEmpty(_settings.PinHash)
                  && AppSettingsStore.HashPin(pin ?? "") == _settings.PinHash;

        if (ok)
            await conn.SendJsonAsync(200, "OK",
                JsonSerializer.Serialize(new { ok = true, token = _settings.PinHash }), ct);
        else
            await conn.SendJsonAsync(403, "Forbidden", "{\"ok\":false}", ct);
    }

    private async Task HandleTextLikeAsync(HttpConnection conn, HttpRequest req, CancellationToken ct, bool isText)
    {
        long len = Math.Min(req.ContentLength, 200_000);
        string body = len > 0 ? await conn.ReadBodyStringAsync(len, ct) : "";
        string value = ExtractJsonString(body, isText ? "text" : "link").Trim();

        if (value.Length == 0)
        {
            await conn.SendJsonAsync(400, "Bad Request", "{\"ok\":false}", ct);
            return;
        }
        TextReceived?.Invoke(value, isText);
        string preview = value.Replace("\r", " ").Replace("\n", " ");
        if (preview.Length > 40) preview = preview[..40] + "…";
        string name = (isText ? "Текст: " : "Ссылка: ") + preview;

        var item = _transfers.CreateItem(name, TransferDirection.PhoneToPc, value.Length, "");
        item.Complete("В буфер обмена");
        _history.Add(name, value.Length, TransferDirection.PhoneToPc, isText ? "Текст" : "Ссылка");

        await conn.SendJsonAsync(200, "OK", "{\"ok\":true}", ct);
    }
    private async Task HandleUploadAsync(HttpConnection conn, HttpRequest req, CancellationToken ct)
    {
        string? boundary = GetBoundary(req.ContentType);
        if (boundary == null)
        {
            await conn.SendJsonAsync(400, "Bad Request", "{\"ok\":false}", ct);
            return;
        }

        byte[] pattern = Encoding.ASCII.GetBytes("\r\n--" + boundary);

        string firstLine = await conn.ReadLineAsync(ct);
        if (firstLine != "--" + boundary)
        {
            await conn.SendJsonAsync(400, "Bad Request", "{\"ok\":false}", ct);
            return;
        }

        int saved = 0;
        try
        {
            while (true)
            {
                string? disposition = null;
                while (true)
                {
                    string line = await conn.ReadLineAsync(ct);
                    if (line.Length == 0) break;
                    if (line.StartsWith("Content-Disposition:", StringComparison.OrdinalIgnoreCase))
                        disposition = line;
                }
                if (disposition == null) throw new InvalidDataException("Нет заголовка части");

                string? filename = GetDispositionValue(disposition, "filename");
                filename = string.IsNullOrWhiteSpace(filename) ? null : SanitizeFileName(filename);

                if (filename != null)
                {
                    Directory.CreateDirectory(_settings.DownloadFolder);
                    string target = UniquePath(Path.Combine(_settings.DownloadFolder, filename));

                    var item = _transfers.CreateItem(filename, TransferDirection.PhoneToPc, 0, target);
                    item.BeginTransfer("Приём…");

                    long written = 0;
                    var meter = new SpeedMeter();
                    try
                    {
                        await using (var fs = new FileStream(target, FileMode.Create, FileAccess.Write,
                            FileShare.None, 81920, useAsync: true))
                        {
                            bool ok = await conn.ReadUntilPatternAsync(fs, pattern,
                                count =>
                                {
                                    written += count;
                                    item.UpdateProgress(written, 0, meter.Update(written));
                                }, ct);
                            if (!ok) throw new EndOfStreamException();
                        }
                        item.Complete("Сохранено");
                        _history.Add(filename, written, TransferDirection.PhoneToPc, "Готово");
                        saved++;
                    }
                    catch
                    {
                        item.Fail("Ошибка");
                        try { File.Delete(target); } catch { }
                        throw;
                    }
                }
                else
                {
                    using var field = new MemoryStream();
                    bool ok = await conn.ReadUntilPatternAsync(field, pattern, null, ct);
                    if (!ok) throw new EndOfStreamException();
                }
                byte[] tail = await conn.ReadExactAsync(2, ct);
                if (tail.Length < 2) break;
                if (tail[0] == (byte)'-' && tail[1] == (byte)'-')
                {
                    await conn.ReadLineAsync(ct);
                    break;
                }
            }
        }
        catch
        {
            return;
        }

        await conn.SendJsonAsync(200, "OK", JsonSerializer.Serialize(new { ok = true, saved }), ct);
    }
    private bool IsAuthorized(HttpRequest req)
    {
        if (!_settings.RequirePin) return true;
        string token = req.GetHeader("X-Pin-Token") ?? "";
        if (string.IsNullOrEmpty(token)) token = GetQueryParam(req.Query, "token");
        return !string.IsNullOrEmpty(_settings.PinHash) && token == _settings.PinHash;
    }

    private static string SanitizeFileName(string name)
    {
        name = name.Trim().Replace('\\', '/');
        name = Path.GetFileName(name);

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);

        string result = sb.ToString().Trim();
        if (result.Length == 0 || result == "." || result == "..") result = "file";
        if (result.Length > 150) result = result[^150..];
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON","PRN","AUX","NUL",
            "COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
            "LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"
        };
        string stem = result.Contains('.') ? result.Substring(0, result.IndexOf('.')) : result;
        if (reserved.Contains(stem)) result = "file_" + result;

        return result;
    }

    private static string SanitizeHeaderName(string name)
    {
        var sb = new StringBuilder();
        foreach (char c in name)
            sb.Append(c >= 32 && c < 127 && c != '"' && c != '\\' ? c : '_');
        return sb.Length == 0 ? "file" : sb.ToString();
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        string dir = Path.GetDirectoryName(path) ?? ".";
        string name = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        for (int i = 1; i < 1000; i++)
        {
            string candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        return Path.Combine(dir, $"{name} ({Guid.NewGuid():N}){ext}");
    }

    private static string? GetBoundary(string contentType)
    {
        foreach (var part in contentType.Split(';'))
        {
            var p = part.Trim();
            if (p.StartsWith("boundary=", StringComparison.OrdinalIgnoreCase))
            {
                string b = p.Substring(9).Trim().Trim('"');
                return b.Length == 0 ? null : b;
            }
        }
        return null;
    }

    private static string? GetDispositionValue(string disposition, string key)
    {
        foreach (var raw in disposition.Split(';'))
        {
            var p = raw.Trim();
            int eq = p.IndexOf('=');
            if (eq <= 0) continue;
            if (!p.Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            string val = p.Substring(eq + 1).Trim();
            if (val.Length >= 2 && val.StartsWith("\"") && val.EndsWith("\""))
                val = val.Substring(1, val.Length - 2);
            return val;
        }
        return null;
    }

    private static string GetQueryParam(string query, string key)
    {
        if (string.IsNullOrEmpty(query)) return "";
        foreach (var pair in query.Split('&'))
        {
            int eq = pair.IndexOf('=');
            string k = eq < 0 ? pair : pair.Substring(0, eq);
            if (k.Equals(key, StringComparison.OrdinalIgnoreCase))
                return eq < 0 ? "" : Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' '));
        }
        return "";
    }

    private static string ExtractJsonString(string json, string prop)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty(prop, out var el) &&
                el.ValueKind == JsonValueKind.String)
                return el.GetString() ?? "";
        }
        catch { }
        return "";
    }
}
public sealed class SpeedMeter
{
    private long _lastBytes;
    private DateTime _lastTime = DateTime.UtcNow;
    private double _speed;

    public double Update(long totalBytes)
    {
        var now = DateTime.UtcNow;
        double dt = (now - _lastTime).TotalSeconds;
        if (dt >= 0.4)
        {
            double instant = (totalBytes - _lastBytes) / Math.Max(dt, 0.001);
            _speed = _speed == 0 ? instant : _speed * 0.6 + instant * 0.4;
            _lastBytes = totalBytes;
            _lastTime = now;
        }
        return _speed;
    }
}

public sealed class HttpRequest
{
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public string Query { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long ContentLength { get; set; }
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? GetHeader(string name) => Headers.TryGetValue(name, out var v) ? v : null;
}


public sealed class HttpConnection
{
    private readonly NetworkStream _stream;
    private readonly byte[] _buf = new byte[16 * 1024];
    private int _len;
    private int _pos;
    public HttpConnection(NetworkStream stream) => _stream = stream;
    public async Task<HttpRequest?> ReadRequestAsync(CancellationToken ct)
    {
        string requestLine = await ReadLineAsync(ct);
        var parts = requestLine.Split(' ');
        if (parts.Length != 3) return null;
        if (parts[2] != "HTTP/1.1" && parts[2] != "HTTP/1.0") return null;

        var req = new HttpRequest { Method = parts[0].ToUpperInvariant() };
        string url = parts[1];
        int q = url.IndexOf('?');
        req.Path = q < 0 ? url : url.Substring(0, q);
        req.Query = q < 0 ? "" : url.Substring(q + 1);
        if (!req.Path.StartsWith('/')) return null;

        int headerCount = 0;
        while (true)
        {
            string line = await ReadLineAsync(ct);
            if (line.Length == 0) break;
            if (++headerCount > 100) return null;
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            req.Headers[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
        }

        req.ContentType = req.GetHeader("Content-Type") ?? "";
        _ = long.TryParse(req.GetHeader("Content-Length"), out long cl);
        req.ContentLength = Math.Max(0, cl);
        return req;
    }

    public async Task<string> ReadLineAsync(CancellationToken ct)
    {
        while (true)
        {
            for (int i = _pos; i < _len; i++)
            {
                if (_buf[i] == (byte)'\n')
                {
                    int start = _pos;
                    int count = i - _pos;
                    _pos = i + 1;
                    if (count > 0 && _buf[start + count - 1] == (byte)'\r') count--;
                    return Encoding.UTF8.GetString(_buf, start, count);
                }
            }
            if (_pos > 0)
            {
                Buffer.BlockCopy(_buf, _pos, _buf, 0, _len - _pos);
                _len -= _pos;
                _pos = 0;
            }
            if (_len >= _buf.Length) throw new InvalidDataException("Слишком длинная строка");
            int n = await _stream.ReadAsync(_buf.AsMemory(_len, _buf.Length - _len), ct);
            if (n <= 0) throw new EndOfStreamException();
            _len += n;
        }
    }
    public async Task<bool> ReadUntilPatternAsync(Stream output, byte[] pattern, Action<long>? onWrite, CancellationToken ct)
    {
        while (true)
        {
            int avail = _len - _pos;
            int idx = IndexOf(_buf, _pos, avail, pattern);
            if (idx >= 0)
            {
                int count = idx - _pos;
                if (count > 0)
                {
                    await output.WriteAsync(_buf.AsMemory(_pos, count), ct);
                    onWrite?.Invoke(count);
                }
                _pos = idx + pattern.Length;
                return true;
            }
            int partial = PartialPrefixLength(_buf, _pos, avail, pattern);
            int safe = avail - partial;
            if (safe > 0)
            {
                await output.WriteAsync(_buf.AsMemory(_pos, safe), ct);
                onWrite?.Invoke(safe);
                _pos += safe;
                avail = _len - _pos;
            }

            if (_pos > 0)
            {
                Buffer.BlockCopy(_buf, _pos, _buf, 0, avail);
                _len = avail;
                _pos = 0;
            }
            if (_len >= _buf.Length) throw new InvalidDataException("Слишком большая часть");
            int n = await _stream.ReadAsync(_buf.AsMemory(_len, _buf.Length - _len), ct);
            if (n <= 0) return false;
            _len += n;
        }
    }

    public async Task<byte[]> ReadExactAsync(int count, CancellationToken ct)
    {
        if (count < 0) count = 0;
        var result = new byte[count];
        int got = 0;
        while (got < count)
        {
            int avail = _len - _pos;
            if (avail > 0)
            {
                int take = Math.Min(avail, count - got);
                Buffer.BlockCopy(_buf, _pos, result, got, take);
                _pos += take;
                got += take;
            }
            else
            {
                int n = await _stream.ReadAsync(_buf.AsMemory(0, _buf.Length), ct);
                if (n <= 0) throw new EndOfStreamException();
                _len = n;
                _pos = 0;
            }
        }
        return result;
    }

    public async Task CopyToAsync(Stream output, long count, CancellationToken ct)
    {
        int avail = _len - _pos;
        if (avail > 0)
        {
            int take = (int)Math.Min(avail, count);
            await output.WriteAsync(_buf.AsMemory(_pos, take), ct);
            _pos += take;
            count -= take;
        }
        var buffer = new byte[16 * 1024];
        while (count > 0)
        {
            int n = await _stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, count)), ct);
            if (n <= 0) throw new EndOfStreamException();
            await output.WriteAsync(buffer.AsMemory(0, n), ct);
            count -= n;
        }
    }

    public async Task<string> ReadBodyStringAsync(long count, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await CopyToAsync(ms, count, ct);
        return Encoding.UTF8.GetString(ms.ToArray());
    }
    public async Task SendJsonAsync(int code, string reason, string json, CancellationToken ct)
        => await SendAsync(code, reason, "application/json; charset=utf-8",
            Encoding.UTF8.GetBytes(json), null, ct);

    public async Task SendAsync(int code, string reason, string contentType, byte[] body,
        IEnumerable<KeyValuePair<string, string>>? extraHeaders, CancellationToken ct)
    {
        await SendHeadersAsync(code, reason, contentType, body.Length, extraHeaders, ct);
        await _stream.WriteAsync(body, ct);
        await _stream.FlushAsync(ct);
    }

    public async Task SendHeadersAsync(int code, string reason, string contentType, long contentLength,
        IEnumerable<KeyValuePair<string, string>>? extraHeaders, CancellationToken ct)
    {
        var sb = new StringBuilder(256);
        sb.Append("HTTP/1.1 ").Append(code).Append(' ').Append(reason).Append("\r\n");
        sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
        sb.Append("Content-Length: ").Append(contentLength).Append("\r\n");
        sb.Append("Connection: close\r\n");
        sb.Append("Cache-Control: no-store\r\n");
        if (extraHeaders != null)
            foreach (var h in extraHeaders)
                sb.Append(h.Key).Append(": ").Append(h.Value).Append("\r\n");
        sb.Append("\r\n");

        var head = Encoding.ASCII.GetBytes(sb.ToString());
        await _stream.WriteAsync(head, ct);
        await _stream.FlushAsync(ct);
    }

    public async Task WriteAsync(byte[] buffer, int count, CancellationToken ct)
    {
        await _stream.WriteAsync(buffer.AsMemory(0, count), ct);
        await _stream.FlushAsync(ct);
    }
    private static int IndexOf(byte[] data, int start, int count, byte[] pattern)
    {
        if (count < pattern.Length) return -1;
        int last = start + count - pattern.Length;
        for (int i = start; i <= last; i++)
        {
            int j = 0;
            while (j < pattern.Length && data[i + j] == pattern[j]) j++;
            if (j == pattern.Length) return i;
        }
        return -1;
    }

    private static int PartialPrefixLength(byte[] data, int start, int count, byte[] pattern)
    {
        int max = Math.Min(count, pattern.Length - 1);
        for (int k = max; k >= 1; k--)
        {
            bool ok = true;
            for (int j = 0; j < k; j++)
            {
                if (data[start + count - k + j] != pattern[j]) { ok = false; break; }
            }
            if (ok) return k;
        }
        return 0;
    }
}