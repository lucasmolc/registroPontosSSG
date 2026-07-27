using System.Windows;
using System.Windows.Controls;
using RegistroPontosSSG.Desktop.ViewModels;

namespace RegistroPontosSSG.Desktop;

public partial class MainWindow : Window
{
    private bool _passwordVisible;
    private bool _syncing;

    public MainWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && !string.IsNullOrEmpty(vm.Password))
        {
            _syncing = true;
            PasswordBox.Password = vm.Password;
            PasswordTextBox.Text = vm.Password;
            _syncing = false;
        }
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        if (DataContext is MainViewModel vm && sender is PasswordBox pb)
        {
            vm.Password = pb.Password;
            _syncing = true;
            PasswordTextBox.Text = pb.Password;
            _syncing = false;
        }
    }

    private void PasswordTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        if (DataContext is MainViewModel vm && sender is TextBox tb)
        {
            vm.Password = tb.Text;
            _syncing = true;
            PasswordBox.Password = tb.Text;
            _syncing = false;
        }
    }

    private void TogglePassword_Click(object sender, RoutedEventArgs e)
    {
        _passwordVisible = !_passwordVisible;
        if (_passwordVisible)
        {
            PasswordTextBox.Visibility = Visibility.Visible;
            PasswordBox.Visibility = Visibility.Collapsed;
            TogglePasswordButton.Content = "🙈";
        }
        else
        {
            PasswordTextBox.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;
            TogglePasswordButton.Content = "👁";
        }
    }
}
