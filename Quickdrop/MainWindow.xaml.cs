using Microsoft.Win32;
using QuickDrop.Models;
using QuickDrop.Services;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace QuickDrop;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private string _statusText = "";
    private Brush _statusBrush = Brushes.Gray;
    private string _serverUrl = "";
    private ImageSource? _qrImage;
    private string _sharedInfo = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<TransferItem> Transfers => App.Transfers.Transfers;

    public string StatusText { get => _statusText; set { _statusText = value; OnChanged(); } }
    public Brush StatusBrush { get => _statusBrush; set { _statusBrush = value; OnChanged(); } }
    public string ServerUrl { get => _serverUrl; set { _serverUrl = value; OnChanged(); } }
    public ImageSource? QrImage { get => _qrImage; set { _qrImage = value; OnChanged(); } }
    public string SharedInfo { get => _sharedInfo; set { _sharedInfo = value; OnChanged(); } }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        App.Server.StateChanged += OnServerStateChanged;
        App.SettingsApplied += OnSettingsApplied;
        App.Transfers.SharedChanged += OnSharedChanged;
        App.Transfers.Transfers.CollectionChanged += OnTransfersChanged;

        Loaded += (s, e) =>
        {
            RefreshConnectionInfo();
            UpdateEmptyState();
        };
    }

    private void OnServerStateChanged() { try { Dispatcher.BeginInvoke(RefreshConnectionInfo); } catch { } }
    private void OnSettingsApplied() { try { Dispatcher.BeginInvoke(RefreshConnectionInfo); } catch { } }
    private void OnSharedChanged() { try { Dispatcher.BeginInvoke(UpdateSharedInfo); } catch { } }

    private void OnTransfersChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateEmptyState();

    private void RefreshConnectionInfo()
    {
        string ip = App.Server.IsRunning ? App.Server.BoundIp : DisplayIp();
        ServerUrl = $"http://{ip}:{App.Server.Port}";

        if (App.Server.IsRunning)
        {
            StatusText = "Сервер запущен";
            StatusBrush = (Brush)(TryFindResource("SuccessColor") ?? Brushes.Green);
        }
        else
        {
            StatusText = string.IsNullOrEmpty(App.Server.LastError)
                ? "Сервер остановлен"
                : "Ошибка запуска сервера";
            StatusBrush = (Brush)(TryFindResource("DangerColor") ?? Brushes.Red);
        }

        try { QrImage = QrService.Generate(ServerUrl); } catch { QrImage = null; }
        UpdateSharedInfo();
    }

    private static string DisplayIp()
    {
        if (!string.IsNullOrEmpty(App.Settings.SelectedIp)) return App.Settings.SelectedIp;
        return NetworkService.GetBestAdapter()?.Ip ?? "0.0.0.0";
    }

    private void UpdateSharedInfo()
    {
        int n = App.Transfers.GetSharedSnapshot().Count;
        SharedInfo = n == 0
            ? "Файлы не выбраны"
            : $"Доступно на телефоне: {n} файл(ов)";
    }

    private void UpdateEmptyState()
        => EmptyHint.Visibility = Transfers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    private void OnCopyLink(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(ServerUrl); } catch { }
        if (sender is Button b)
        {
            object old = b.Content;
            b.Content = "Скопировано ✓";
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
            t.Tick += (s2, e2) => { t.Stop(); b.Content = old; };
            t.Start();
        }
    }

    private void OnPickFiles(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Выберите файлы для отправки",
            Filter = "Все файлы|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog(this) == true)
            foreach (var p in dlg.FileNames)
                App.Transfers.AddFile(p);
    }

    private void OnPickFolder(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Выберите папку для отправки" };
        if (dlg.ShowDialog(this) == true)
            App.Transfers.AddFolder(dlg.FolderName);
    }

    private void OnHistory(object sender, RoutedEventArgs e)
        => new Views.HistoryWindow { Owner = this }.ShowDialog();

    private void OnSettings(object sender, RoutedEventArgs e)
        => new Views.SettingsWindow { Owner = this }.ShowDialog();

    private void OnClearFinished(object sender, RoutedEventArgs e)
        => App.Transfers.ClearFinished();
    private void OnDragEnter(object sender, DragEventArgs e) => UpdateDropVisual(e);
    private void OnDragOver(object sender, DragEventArgs e) => UpdateDropVisual(e);
    private void OnDragLeave(object sender, DragEventArgs e) => ResetDropVisual();

    private void UpdateDropVisual(DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
        DropZone.BorderBrush = (Brush)(TryFindResource("Accent") ?? Brushes.SteelBlue);
        DropZone.Background = (Brush)(TryFindResource("HoverBackground") ?? Brushes.Transparent);
    }

    private void ResetDropVisual()
    {
        DropZone.BorderBrush = (Brush)(TryFindResource("DropZoneBorder") ?? Brushes.Gray);
        DropZone.Background = (Brush)(TryFindResource("CardBackground") ?? Brushes.Transparent);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        ResetDropVisual();
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            foreach (var p in paths)
            {
                try
                {
                    if (Directory.Exists(p)) App.Transfers.AddFolder(p);
                    else if (File.Exists(p)) App.Transfers.AddFile(p);
                }
                catch { }
            }
        }
        e.Handled = true;
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (App.ForceClose) return;

        if (App.Settings.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            e.Cancel = true;
            App.ExitApp();
        }
    }

    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}