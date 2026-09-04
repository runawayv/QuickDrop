using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using QuickDrop.Models;
using QuickDrop.Services;

namespace QuickDrop.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _draft = new();

    public SettingsWindow()
    {
        InitializeComponent();

        _draft.Port = App.Settings.Port;
        _draft.SelectedIp = App.Settings.SelectedIp;
        _draft.AutoStartServer = App.Settings.AutoStartServer;
        _draft.DownloadFolder = App.Settings.DownloadFolder;
        _draft.RequirePin = App.Settings.RequirePin;
        _draft.PinHash = App.Settings.PinHash;
        _draft.DarkTheme = App.Settings.DarkTheme;
        _draft.StartWithWindows = App.Settings.StartWithWindows;
        _draft.MinimizeToTray = App.Settings.MinimizeToTray;

        PortBox.Text = _draft.Port.ToString();
        FolderBox.Text = _draft.DownloadFolder;
        AutoStartCheck.IsChecked = _draft.AutoStartServer;
        PinCheck.IsChecked = _draft.RequirePin;
        AutorunCheck.IsChecked = _draft.StartWithWindows;
        TrayCheck.IsChecked = _draft.MinimizeToTray;
        ThemeBox.SelectedIndex = _draft.DarkTheme ? 0 : 1;

        var adapters = NetworkService.GetActiveAdapters();
        AdapterBox.ItemsSource = adapters;
        if (adapters.Count > 0)
        {
            var selected = adapters.FirstOrDefault(a => a.Ip == _draft.SelectedIp) ?? adapters[0];
            AdapterBox.SelectedValue = selected.Ip;
        }

        UpdatePinUi();
    }

    private void OnPinChanged(object sender, RoutedEventArgs e) => UpdatePinUi();

    private void UpdatePinUi()
    {
        bool need = PinCheck.IsChecked == true;
        PinBox.IsEnabled = need;
        PinBox2.IsEnabled = need;
        PinHint.Text = need
            ? (string.IsNullOrEmpty(_draft.PinHash)
                ? "Придумайте PIN — его попросит ввести телефон при первом подключении."
                : "PIN уже задан. Оставьте поля пустыми, чтобы сохранить его без изменений.")
            : "PIN отключён — страница телефона будет доступна всем в локальной сети.";
    }

    private void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Папка для файлов с телефона",
            FolderName = FolderBox.Text
        };
        if (dlg.ShowDialog(this) == true)
            FolderBox.Text = dlg.FolderName;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortBox.Text.Trim(), out int port) || port is < 1024 or > 65535)
        {
            MessageBox.Show(this, "Порт должен быть числом от 1024 до 65535.",
                "QuickDrop", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string folder = FolderBox.Text.Trim();
        if (folder.Length == 0)
        {
            MessageBox.Show(this, "Укажите папку для сохранения файлов.",
                "QuickDrop", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string p1 = PinBox.Password;
        string p2 = PinBox2.Password;
        bool needPin = PinCheck.IsChecked == true;

        if (needPin && (p1.Length > 0 || p2.Length > 0))
        {
            if (p1 != p2)
            {
                MessageBox.Show(this, "Введённые PIN не совпадают.",
                    "QuickDrop", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (p1.Length < 4)
            {
                MessageBox.Show(this, "PIN должен содержать минимум 4 символа.",
                    "QuickDrop", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _draft.PinHash = AppSettingsStore.HashPin(p1);
        }

        if (needPin && string.IsNullOrEmpty(_draft.PinHash))
        {
            MessageBox.Show(this, "Задайте PIN или отключите его требование.",
                "QuickDrop", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _draft.Port = port;
        _draft.DownloadFolder = folder;
        _draft.AutoStartServer = AutoStartCheck.IsChecked == true;
        _draft.SelectedIp = AdapterBox.SelectedValue as string ?? "";
        _draft.RequirePin = needPin;
        if (!needPin) _draft.PinHash = "";
        _draft.DarkTheme = ThemeBox.SelectedIndex == 0;
        _draft.StartWithWindows = AutorunCheck.IsChecked == true;
        _draft.MinimizeToTray = TrayCheck.IsChecked == true;

        try { Directory.CreateDirectory(_draft.DownloadFolder); } catch { }

        App.SaveSettings(_draft);
        Close();
    }
}