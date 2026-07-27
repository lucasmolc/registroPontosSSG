using RegistroPontosSSG.Core.Update;

namespace RegistroPontosSSG.Core.Tests;

/// <summary>
/// Cobre a leitura do CHANGELOG.md, usado para mostrar "o que mudou" na primeira
/// execução após uma atualização.
/// </summary>
public sealed class ChangelogReaderTests
{
    private const string Exemplo = """
        # Changelog

        Texto introdutório que não deve virar seção.

        ## [1.3.0] - 2026-07-27

        ### Adicionado
        - Aviso de nova versão.
        - Atualização automática.

        ## [1.2.0] - 2026-07-20

        ### Corrigido
        - Leitura do relatório do SSG.

        ## 1.1.0

        ### Alterado
        - Ajuste de horários.
        """;

    [Fact]
    public void ReadAll_RetornaSecoesDaMaisNovaParaAMaisAntiga()
    {
        var secoes = ChangelogReader.ReadAll(Exemplo);

        Assert.Equal(3, secoes.Count);
        Assert.Equal(new Version(1, 3, 0), secoes[0].Version);
        Assert.Equal(new Version(1, 2, 0), secoes[1].Version);
        Assert.Equal(new Version(1, 1, 0), secoes[2].Version);
    }

    [Fact]
    public void ReadAll_IgnoraOTextoIntrodutorio()
    {
        var secoes = ChangelogReader.ReadAll(Exemplo);

        Assert.DoesNotContain(secoes, s => s.Body.Contains("Texto introdutório"));
    }

    [Fact]
    public void ReadAll_PreservaOCorpoDaSecao()
    {
        var secao = ChangelogReader.ReadAll(Exemplo)[0];

        Assert.Contains("Adicionado", secao.Body);
        Assert.Contains("Aviso de nova versão.", secao.Body);
        Assert.Contains("2026-07-27", secao.Title);
        // O corpo não deve invadir a seção seguinte.
        Assert.DoesNotContain("Leitura do relatório", secao.Body);
    }

    [Fact]
    public void ReadForVersion_EncontraIgnorandoComponentesAusentes()
    {
        Assert.NotNull(ChangelogReader.ReadForVersion(new Version(1, 3, 0), Exemplo));
        Assert.NotNull(ChangelogReader.ReadForVersion(new Version(1, 3, 0, 0), Exemplo));
        Assert.Null(ChangelogReader.ReadForVersion(new Version(9, 9, 9), Exemplo));
    }

    [Fact]
    public void ReadBetween_TrazTodasAsVersoesPuladas()
    {
        // Quem estava na 1.1.0 e atualizou para a 1.3.0 precisa ver 1.2.0 e 1.3.0.
        var secoes = ChangelogReader.ReadBetween(new Version(1, 1, 0), new Version(1, 3, 0), Exemplo);

        Assert.Equal(2, secoes.Count);
        Assert.Contains(secoes, s => s.Version == new Version(1, 3, 0));
        Assert.Contains(secoes, s => s.Version == new Version(1, 2, 0));
        Assert.DoesNotContain(secoes, s => s.Version == new Version(1, 1, 0));
    }

    [Fact]
    public void ReadBetween_SemVersaoAnterior_TrazAteAAtual()
    {
        var secoes = ChangelogReader.ReadBetween(null, new Version(1, 2, 0), Exemplo);

        Assert.Equal(2, secoes.Count);
        Assert.DoesNotContain(secoes, s => s.Version == new Version(1, 3, 0));
    }

    [Fact]
    public void ReadBetween_MesmaVersao_NaoTrazNada()
        => Assert.Empty(ChangelogReader.ReadBetween(new Version(1, 3, 0), new Version(1, 3, 0), Exemplo));

    [Fact]
    public void ReadAll_ConteudoVazio_NaoLanca()
        => Assert.Empty(ChangelogReader.ReadAll(string.Empty));

    /// <summary>
    /// O CHANGELOG.md real precisa estar embutido no assembly — sem isso o app não
    /// tem o que mostrar depois de atualizar.
    /// </summary>
    [Fact]
    public void ChangelogReal_EstaEmbutidoEEhLegivel()
    {
        var secoes = ChangelogReader.ReadAll();

        Assert.NotEmpty(ChangelogReader.RawContent);
        Assert.NotEmpty(secoes);
        Assert.All(secoes, s => Assert.False(string.IsNullOrWhiteSpace(s.Body)));
    }
}
