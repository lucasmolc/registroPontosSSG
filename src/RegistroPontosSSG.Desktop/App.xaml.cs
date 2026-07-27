using System.IO;
using System.Windows;
using RegistroPontosSSG.Core.Configuration;

namespace RegistroPontosSSG.Desktop;

public partial class App : Application
{
    /// <summary>
    /// Grava a exceção completa (tipo, mensagem e stack trace) em
    /// %APPDATA%\RegistroPontosSSG\logs\crash-&lt;timestamp&gt;.log
    /// e devolve o caminho. Sem isso a caixa de diálogo mostra apenas Exception.Message
    /// e o stack se perde, deixando falhas de inicialização sem diagnóstico.
    /// </summary>
    private static string? RegistrarFalha(Exception ex, string origem)
    {
        try
        {
            Directory.CreateDirectory(ConfigService.LogsDirectory);
            var caminho = Path.Combine(ConfigService.LogsDirectory, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(caminho,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] origem={origem}{Environment.NewLine}{ex}{Environment.NewLine}");
            return caminho;
        }
        catch { return null; }
    }

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
            var log = RegistrarFalha(args.Exception, "DispatcherUnhandledException");
            var detalhe = log is null ? string.Empty : $"\n\nDetalhes em:\n{log}";
            MessageBox.Show(
                $"Ocorreu um erro inesperado:\n\n{args.Exception.Message}{detalhe}",
                "Erro",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        // Exceções fora do dispatcher (threads de background, finalizers) não passam
        // pelo handler acima e derrubariam o processo sem deixar rastro.
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                RegistrarFalha(ex, "AppDomain.UnhandledException");
        };

        base.OnStartup(e);
    }
}