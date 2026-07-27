using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace RegistroPontosSSG.Core.Update;

/// <summary>
/// Lê o CHANGELOG.md embutido no assembly e extrai as seções por versão, para que o
/// aplicativo mostre "o que mudou" na primeira execução depois de uma atualização.
///
/// O arquivo é embutido (e não baixado) para que o resumo apareça mesmo sem rede e
/// sem depender das notas da release.
/// </summary>
public static class ChangelogReader
{
    private const string RecursoEmbutido = "RegistroPontosSSG.Core.CHANGELOG.md";

    /// <summary>Cabeçalhos aceitos: "## [1.3.0] - 2026-07-27", "## 1.3.0" ou "## v1.3.0".</summary>
    private static readonly Regex CabecalhoVersao = new(
        @"^##\s*\[?v?(?<versao>\d+(?:\.\d+){0,3})\]?",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>Conteúdo bruto do changelog, ou string vazia se o recurso não existir.</summary>
    public static string RawContent
    {
        get
        {
            var assembly = typeof(ChangelogReader).Assembly;
            using var stream = assembly.GetManifestResourceStream(RecursoEmbutido);
            if (stream is null) return string.Empty;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
    }

    /// <summary>
    /// Todas as seções do changelog, da versão mais recente para a mais antiga.
    /// </summary>
    public static IReadOnlyList<ChangelogSection> ReadAll(string? conteudo = null)
    {
        var texto = conteudo ?? RawContent;
        if (string.IsNullOrWhiteSpace(texto)) return Array.Empty<ChangelogSection>();

        var secoes = new List<ChangelogSection>();
        var matches = CabecalhoVersao.Matches(texto);

        for (var i = 0; i < matches.Count; i++)
        {
            var atual = matches[i];
            if (!Version.TryParse(Normalizar(atual.Groups["versao"].Value), out var versao)) continue;

            var inicioCorpo = atual.Index + atual.Length;
            var fim = i + 1 < matches.Count ? matches[i + 1].Index : texto.Length;
            var linhaCabecalho = texto[atual.Index..inicioCorpo];
            var corpo = texto[inicioCorpo..fim];

            // Resto da linha do cabeçalho costuma trazer a data ("] - 2026-07-27").
            var quebra = corpo.IndexOf('\n');
            if (quebra >= 0)
            {
                linhaCabecalho += corpo[..quebra];
                corpo = corpo[(quebra + 1)..];
            }

            secoes.Add(new ChangelogSection
            {
                Version = versao,
                Title = linhaCabecalho.TrimStart('#', ' ').Trim(),
                Body = corpo.Trim()
            });
        }

        return secoes.OrderByDescending(s => s.Version).ToList();
    }

    /// <summary>Seção de uma versão específica, ou null se não houver.</summary>
    public static ChangelogSection? ReadForVersion(Version versao, string? conteudo = null)
        => ReadAll(conteudo).FirstOrDefault(s => VersoesEquivalentes(s.Version, versao));

    /// <summary>
    /// Seções lançadas depois de <paramref name="anterior"/> até
    /// <paramref name="atual"/> (inclusive). Cobre o caso de o usuário pular versões:
    /// quem estava na 1.1.0 e atualizou para a 1.3.0 vê 1.2.0 e 1.3.0.
    /// </summary>
    public static IReadOnlyList<ChangelogSection> ReadBetween(Version? anterior, Version atual, string? conteudo = null)
        => ReadAll(conteudo)
            .Where(s => s.Version <= NormalizarVersao(atual)
                        && (anterior is null || s.Version > NormalizarVersao(anterior)))
            .ToList();

    /// <summary>Compara ignorando componentes ausentes (1.3 == 1.3.0 == 1.3.0.0).</summary>
    private static bool VersoesEquivalentes(Version a, Version b)
        => NormalizarVersao(a) == NormalizarVersao(b);

    private static Version NormalizarVersao(Version v)
        => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    private static string Normalizar(string versao)
    {
        var partes = versao.Split('.');
        return partes.Length switch
        {
            1 => versao + ".0",
            _ => versao
        };
    }
}

/// <summary>Uma entrada do changelog.</summary>
public sealed record ChangelogSection
{
    public required Version Version { get; init; }

    /// <summary>Linha do cabeçalho, ex.: "[1.3.0] - 2026-07-27".</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Corpo em Markdown (listas de Adicionado/Corrigido/Alterado).</summary>
    public string Body { get; init; } = string.Empty;
}
