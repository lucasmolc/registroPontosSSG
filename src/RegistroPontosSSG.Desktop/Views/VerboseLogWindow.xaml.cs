using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RegistroPontosSSG.Core.Configuration;

namespace RegistroPontosSSG.Desktop.Views;

public partial class VerboseLogWindow : Window
{
    private readonly ObservableCollection<string> _items;

    public VerboseLogWindow(ObservableCollection<string> items)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _items = items;
        LogList.ItemsSource = _items;

        _items.CollectionChanged += OnCollectionChanged;
        Closed += (_, _) => _items.CollectionChanged -= OnCollectionChanged;
        Loaded += (_, _) => ScrollToEnd();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (AutoScrollCheck.IsChecked == true)
            Dispatcher.BeginInvoke(new Action(ScrollToEnd));
    }

    private void ScrollToEnd()
    {
        if (LogList.Items.Count == 0) return;
        var last = LogList.Items[LogList.Items.Count - 1];
        LogList.ScrollIntoView(last!);
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        foreach (var line in _items) sb.AppendLine(line);
        try { Clipboard.SetText(sb.ToString()); }
        catch (Exception ex)
        {
            MessageBox.Show($"Falha ao copiar: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Arquivo de log (*.log)|*.log|Texto (*.txt)|*.txt",
            FileName = $"verbose-{DateTime.Now:yyyyMMdd-HHmmss}.log"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            File.WriteAllLines(dlg.FileName, _items);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Falha ao salvar: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(ConfigService.LogsDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = ConfigService.LogsDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Falha ao abrir pasta: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => _items.Clear();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
