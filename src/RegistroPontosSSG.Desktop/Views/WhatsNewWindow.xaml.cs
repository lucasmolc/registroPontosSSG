using System.Diagnostics;
using System.Text;
using System.Windows;
using RegistroPontosSSG.Core.Update;

namespace RegistroPontosSSG.Desktop.Views;

/// <summary>
/// Mostra o resumo do changelog na primeira execução após uma atualização.
/// O texto vem do CHANGELOG.md embutido no assembly, então funciona sem rede.
/// </summary>
public partial class WhatsNewWindow : Window
{
    private readonly string _urlReleases;

    public WhatsNewWindow(IReadOnlyList<ChangelogSection> secoes, Version versaoAtual,
        Version? versaoAnterior, string repositorio)
    {
        InitializeComponent();

        _urlReleases = $"https://github.com/{repositorio}/releases";

        TituloTexto.Text = $"Atualizado para a versão {versaoAtual.ToString(3)}";
        SubtituloTexto.Text = versaoAnterior is null
            ? "Resumo das alterações desta versão."
            : $"O que mudou desde a versão {versaoAnterior.ToString(3)}.";

        ConteudoTexto.Text = Formatar(secoes);
    }

    /// <summary>
    /// Converte as seções em texto legível. É uma limpeza simples do Markdown —
    /// remove marcadores de cabeçalho e normaliza os itens de lista — suficiente
    /// para o formato do nosso changelog e sem dependência de renderizador.
    /// </summary>
    private static string Formatar(IReadOnlyList<ChangelogSection> secoes)
    {
        if (secoes.Count == 0) return "Nenhuma novidade registrada para esta versão.";

        var sb = new StringBuilder();
        foreach (var secao in secoes)
        {
            sb.AppendLine($"Versão {secao.Version.ToString(3)}");
            sb.AppendLine();

            foreach (var linha in secao.Body.Split('\n'))
            {
                var texto = linha.TrimEnd();
                if (texto.Length == 0)
                {
                    sb.AppendLine();
                    continue;
                }

                if (texto.StartsWith("###", StringComparison.Ordinal))
                {
                    sb.AppendLine(texto.TrimStart('#', ' ').ToUpperInvariant());
                }
                else if (texto.TrimStart().StartsWith("- ", StringComparison.Ordinal))
                {
                    sb.AppendLine("  • " + texto.TrimStart().Substring(2).Replace("`", string.Empty));
                }
                else
                {
                    sb.AppendLine("    " + texto.Trim().Replace("`", string.Empty));
                }
            }

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private void AbrirGitHub_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_urlReleases) { UseShellExecute = true });
        }
        catch
        {
            /* navegador indisponível — ignorar */
        }
    }

    private void Fechar_Click(object sender, RoutedEventArgs e) => Close();
}
