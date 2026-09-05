using System.Linq;
using System.Windows;
using QuickDrop.Services;

namespace QuickDrop.Views;

public partial class HistoryWindow : Window
{
    public HistoryWindow()
    {
        InitializeComponent();
        Refresh();
        App.History.Changed += OnHistoryChanged;
        Closed += (s, e) => App.History.Changed -= OnHistoryChanged;
    }

    private void OnHistoryChanged()
    {
        try { Dispatcher.BeginInvoke(Refresh); } catch { }
    }

    private void Refresh()
    {
        List.ItemsSource = App.History.GetSnapshot().OrderByDescending(h => h.Time).ToList();
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(this, "Очистить всю историю передач?", "QuickDrop",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
            App.History.Clear();
    }
}