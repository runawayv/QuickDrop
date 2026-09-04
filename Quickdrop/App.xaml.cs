using Microsoft.Win32;
using QuickDrop;
using QuickDrop.Models;
using QuickDrop.Server;
using QuickDrop.Services;
using QuickDrop.Views;
using System;
using System.IO;
using System.Windows;
using SD = System.Drawing;
using WF = System.Windows.Forms;
using System.Linq;

namespace QuickDrop;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();
    public static TransferService Transfers { get; private set; } = new();
    public static HistoryService History { get; private set; } = new();
    public static LocalServer Server { get; private set; } =
        new(new AppSettings(), new TransferService(), new HistoryService());

    public static bool ForceClose { get; set; }
    public static event Action? SettingsApplied;

    private static WF.NotifyIcon? _tray;
    private static WF.ToolStripMenuItem? _startItem;
    private static WF.ToolStripMenuItem? _stopItem;
    private static SD.Icon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Settings = AppSettingsStore.Load();
        ApplyTheme(Settings.DarkTheme);
        try { Directory.CreateDirectory(Settings.DownloadFolder); } catch { }

        Transfers = new TransferService();
        History = new HistoryService();
        History.Load();

        Server = new LocalServer(Settings, Transfers, History);
        Server.StateChanged += () =>
        {
            try { Current?.Dispatcher.BeginInvoke(UpdateTrayMenu); } catch { }
        };

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        InitTrayIcon();

        if (Settings.AutoStartServer)
            StartServer();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DisposeTray();
        base.OnExit(e);
    }

    public static void StartServer()
    {
        if (Server.IsRunning) return;
        Server.UpdateSettings(Settings);
        Server.Start();
    }

    public static void StopServer()
    {
        if (!Server.IsRunning) return;
        Server.Stop();
    }

    public static void SaveSettings(AppSettings s)
    {
        bool wasRunning = Server.IsRunning;
        Settings = s;
        AppSettingsStore.Save(s);
        ApplyTheme(s.DarkTheme);
        ApplyAutoRun(s.StartWithWindows);

        if (wasRunning || s.AutoStartServer)
        {
            StopServer();
            StartServer();
        }

        SettingsApplied?.Invoke();
    }

    public static void ApplyTheme(bool dark)
    {
        var res = Current.Resources;
        if (dark)
        {
            Set(res, "WindowBackground", "#1E1E1E");
            Set(res, "CardBackground", "#252526");
            Set(res, "CardBorder", "#333337");
            Set(res, "TextPrimary", "#F1F1F1");
            Set(res, "TextSecondary", "#999999");
            Set(res, "Accent", "#007ACC");
            Set(res, "AccentHover", "#1C97EA");
            Set(res, "AccentText", "#FFFFFF");
            Set(res, "HoverBackground", "#2D2D30");
            Set(res, "DropZoneBorder", "#3F3F46");
            Set(res, "ProgressTrack", "#3B3B40");
            Set(res, "SuccessColor", "#4DBB74");
            Set(res, "DangerColor", "#E0564E");
        }
        else
        {
            Set(res, "WindowBackground", "#F5F5F5");
            Set(res, "CardBackground", "#FFFFFF");
            Set(res, "CardBorder", "#DDDDDD");
            Set(res, "TextPrimary", "#1F1F1F");
            Set(res, "TextSecondary", "#777777");
            Set(res, "Accent", "#0067C0");
            Set(res, "AccentHover", "#2B84D8");
            Set(res, "AccentText", "#FFFFFF");
            Set(res, "HoverBackground", "#EAEAEA");
            Set(res, "DropZoneBorder", "#C8C8C8");
            Set(res, "ProgressTrack", "#E4E4E4");
            Set(res, "SuccessColor", "#2FA45C");
            Set(res, "DangerColor", "#D8433B");
        }
    }

    public static void SetLanguage(string code)
    {
        var dicts = Application.Current.Resources.MergedDictionaries;
        var next = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/QuickDrop;component/Languages/{code}.xaml")
        };
        var old = dicts.FirstOrDefault(d => d.Source?.OriginalString.Contains("/Languages/") == true);
        dicts.Add(next);
        if (old != null) dicts.Remove(old);
    }

    private static void Set(System.Windows.ResourceDictionary res, string key, string hex)
    {
        res[key] = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
    }

    private static void ApplyAutoRun(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;
            if (enabled && !string.IsNullOrEmpty(Environment.ProcessPath))
                key.SetValue("QuickDrop", "\"" + Environment.ProcessPath + "\"");
            else
                key.DeleteValue("QuickDrop", false);
        }
        catch
        {
        }
    }

    public static void ShowMainWindow()
    {
        if (Current?.MainWindow is MainWindow w)
        {
            w.Show();
            w.WindowState = WindowState.Normal;
            w.Activate();
        }
    }

    public static void ExitApp()
    {
        ForceClose = true;
        StopServer();
        DisposeTray();
        Current?.Shutdown();
    }

    private static void DisposeTray()
    {
        if (_tray == null) return;
        try
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        catch { }
        _tray = null;
    }

    private void InitTrayIcon()
    {
        _trayIcon = CreateTrayIcon();

        var menu = new WF.ContextMenuStrip();
        menu.Items.Add("Открыть QuickDrop", null, (s, e) => ShowMainWindow());

        _startItem = new WF.ToolStripMenuItem("Запустить сервер");
        _startItem.Click += (s, e) => StartServer();
        _stopItem = new WF.ToolStripMenuItem("Остановить сервер");
        _stopItem.Click += (s, e) => StopServer();
        menu.Items.Add(_startItem);
        menu.Items.Add(_stopItem);

        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("Выход", null, (s, e) => ExitApp());

        _tray = new WF.NotifyIcon
        {
            Icon = _trayIcon,
            Text = "QuickDrop",
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (s, e) => ShowMainWindow();

        UpdateTrayMenu();
    }

    private static void UpdateTrayMenu()
    {
        if (_startItem == null || _stopItem == null) return;
        _startItem.Enabled = !Server.IsRunning;
        _stopItem.Enabled = Server.IsRunning;
    }

    private static SD.Icon CreateTrayIcon()
    {
        using var bmp = new SD.Bitmap(32, 32);
        using (var g = SD.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
            using (var bg = new SD.SolidBrush(SD.Color.FromArgb(79, 140, 255)))
                g.FillEllipse(bg, 1, 1, 30, 30);
            using (var white = new SD.SolidBrush(SD.Color.White))
            {
                g.FillRectangle(white, 14f, 6f, 4f, 10f);
                g.FillPolygon(white, new[]
                {
                    new SD.PointF(8f, 15f),
                    new SD.PointF(24f, 15f),
                    new SD.PointF(16f, 26f)
                });
            }
        }
        return SD.Icon.FromHandle(bmp.GetHicon());
    }
}
