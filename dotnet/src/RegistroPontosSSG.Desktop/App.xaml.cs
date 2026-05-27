using System.Windows;

namespace RegistroPontosSSG.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Em single-file publish, Assembly.Location fica vazio e o Playwright
        // cai num fallback que procura a pasta .playwright no CurrentDirectory.
        // Quando o .exe é iniciado a partir de outra pasta (ex.: atalho), isso
        // gera "Driver not found". Forçamos o diretório de trabalho para o
        // diretório do executável onde os assets foram extraídos.
        try
        {
            Environment.CurrentDirectory = AppContext.BaseDirectory;
        }
        catch { /* ignora */ }

        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show(
                $"Ocorreu um erro inesperado:\n\n{args.Exception.Message}",
                "Erro",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };
        base.OnStartup(e);
    }
}