using RegistroPontosSSG.Core.Reading;

namespace RegistroPontosSSG.Core.Tests;

/// <summary>
/// Cobre os formatos aceitos pelo leitor. Dois grupos de testes correspondem a
/// falhas reais observadas em uso:
/// - o relatório do SSG não era reconhecido (cabeçalho fora da janela de varredura)
///   e o arquivo era lido como vazio, sem erro;
/// - no formato padrão, a coluna "saida" casava por substring com "saida_almoco"
///   e o horário de saída do dia nunca era lido.
/// </summary>
public sealed class PunchFileReaderTests
{
    private static readonly PunchFileReader Leitor = new();

    // ---------------------------------------------------------------
    // Relatório exportado pelo SSG
    // ---------------------------------------------------------------

    /// <summary>
    /// Monta um relatório do SSG com o mesmo preâmbulo do arquivo real: título,
    /// empresa, período, linha vazia, mês e só então o cabeçalho (linha 6).
    /// </summary>
    private static string?[][] RelatorioSsg(params string?[][] linhasDeDados)
    {
        var linhas = new List<string?[]>
        {
            new string?[] { "TIME SHEET REPORT" },
            new string?[] { "PJUS" },
            new string?[] { "Period: 01/07/2026 - 31/07/2026", null, null, null, "Generated on: 27/07/2026 14:25" },
            new string?[] { null },
            new string?[] { "JULY 2026" },
            new string?[] { "DATE", "PUNCH IN", "PUNCH OUT", "TIME WORKED", "BALANCE" }
        };
        linhas.AddRange(linhasDeDados);
        linhas.Add(new string?[] { "Total time", null, null, "08:00" });
        return linhas.ToArray();
    }

    [Fact]
    public void RelatorioSsg_ComPreambulo_LeOsDiasEOsDoisPares()
    {
        using var fixture = new PlanilhaFixture();
        var caminho = fixture.Criar("relatorio.xlsx", RelatorioSsg(
            new string?[] { "Wed, 01/07/26", "09:56", "12:14", "06:57", "-01:03" },
            new string?[] { null, "12:53", "17:32" },
            new string?[] { "Thu, 02/07/26", "07:30", "14:12", "08:39", "00:39" },
            new string?[] { null, "15:21", "17:18" }), aba: "July 2026");

        var registros = Leitor.Read(caminho);

        Assert.Equal(2, registros.Count);

        Assert.Equal("01/07/2026", registros[0].Date);
        Assert.Equal("09:56", registros[0].Entry);
        Assert.Equal("12:14", registros[0].LunchOut);
        Assert.Equal("12:53", registros[0].LunchReturn);
        Assert.Equal("17:32", registros[0].Exit);

        Assert.Equal("02/07/2026", registros[1].Date);
        Assert.Equal("07:30", registros[1].Entry);
        Assert.Equal("17:18", registros[1].Exit);
    }

    [Fact]
    public void RelatorioSsg_DiaSemMarcacao_EIgnorado()
    {
        using var fixture = new PlanilhaFixture();
        var caminho = fixture.Criar("sem_marcacao.xlsx", RelatorioSsg(
            new string?[] { "Wed, 01/07/26", "08:00", "12:00", "08:00", "00:00" },
            new string?[] { null, "13:00", "17:00" },
            new string?[] { "Tue, 28/07/26", "--:--", "--:--", "00:00", "00:00" },
            new string?[] { null, "No time punches registered!" }));

        var registros = Leitor.Read(caminho);

        Assert.Single(registros);
        Assert.Equal("01/07/2026", registros[0].Date);
    }

    [Fact]
    public void RelatorioSsg_BatidaSemSaida_NaoEntraNoRegistroMasVaiParaObservacao()
    {
        using var fixture = new PlanilhaFixture();
        var caminho = fixture.Criar("em_aberto.xlsx", RelatorioSsg(
            new string?[] { "Mon, 27/07/26", "08:53", "13:02", "04:09", "-03:51" },
            new string?[] { null, "13:39", "--:--" }));

        var registros = Leitor.Read(caminho);

        var registro = Assert.Single(registros);
        Assert.Equal("08:53", registro.Entry);
        Assert.Equal("13:02", registro.Exit);
        Assert.Equal(string.Empty, registro.LunchOut);
        Assert.Contains("13:39", registro.Notes);
    }

    [Fact]
    public void RelatorioSsg_DiaComTresPares_SinalizaOExcedente()
    {
        using var fixture = new PlanilhaFixture();
        var caminho = fixture.Criar("tres_pares.xlsx", RelatorioSsg(
            new string?[] { "Wed, 01/07/26", "08:00", "10:00", "09:00", "01:00" },
            new string?[] { null, "10:30", "12:00" },
            new string?[] { null, "13:00", "18:00" }));

        var registros = Leitor.Read(caminho);

        var registro = Assert.Single(registros);
        Assert.Equal("08:00", registro.Entry);
        Assert.Equal("10:00", registro.LunchOut);
        Assert.Equal("10:30", registro.LunchReturn);
        Assert.Equal("12:00", registro.Exit);
        Assert.Contains("13:00-18:00", registro.Notes);
    }

    // ---------------------------------------------------------------
    // Planilha no formato padrão
    // ---------------------------------------------------------------

    [Fact]
    public void PlanilhaPadrao_LeTodasAsColunas()
    {
        using var fixture = new PlanilhaFixture();
        var caminho = fixture.Criar("padrao.xlsx", new[]
        {
            new string?[] { "data", "entrada", "saida_almoco", "retorno_almoco", "saida", "observacao" },
            new string?[] { "01/07/2026", "08:00", "12:00", "13:00", "17:00", null },
            new string?[] { "02/07/2026", "08:30", "12:00", "13:00", "17:30", "Home Office" }
        });

        var registros = Leitor.Read(caminho);

        Assert.Equal(2, registros.Count);
        // Regressão: "saida" casava com "saida_almoco" e Exit vinha 12:00.
        Assert.Equal("12:00", registros[0].LunchOut);
        Assert.Equal("17:00", registros[0].Exit);
        Assert.Equal("17:30", registros[1].Exit);
        Assert.Equal("Home Office", registros[1].Notes);
    }

    [Fact]
    public void PlanilhaPadrao_CabecalhoComAcentoEEspaco_EReconhecido()
    {
        using var fixture = new PlanilhaFixture();
        var caminho = fixture.Criar("acentos.xlsx", new[]
        {
            new string?[] { "Data", "Entrada", "Saída Almoço", "Retorno Almoço", "Saída" },
            new string?[] { "03/07/2026", "09:00", "12:00", "13:00", "18:00" }
        });

        var registros = Leitor.Read(caminho);

        var registro = Assert.Single(registros);
        Assert.Equal("12:00", registro.LunchOut);
        Assert.Equal("13:00", registro.LunchReturn);
        Assert.Equal("18:00", registro.Exit);
    }

    [Fact]
    public void PlanilhaPadrao_ComPreambulo_EncontraOCabecalho()
    {
        using var fixture = new PlanilhaFixture();
        var caminho = fixture.Criar("preambulo.xlsx", new[]
        {
            new string?[] { "Relatorio interno" },
            new string?[] { null },
            new string?[] { "Equipe: PJUS" },
            new string?[] { null },
            new string?[] { null },
            new string?[] { null },
            new string?[] { null },
            new string?[] { "data", "entrada", "saida_almoco", "retorno_almoco", "saida" },
            new string?[] { "03/07/2026", "09:00", "12:00", "13:00", "18:00" }
        });

        var registros = Leitor.Read(caminho);

        var registro = Assert.Single(registros);
        Assert.Equal("03/07/2026", registro.Date);
        Assert.Equal("18:00", registro.Exit);
    }

    // ---------------------------------------------------------------
    // Detecção de período
    // ---------------------------------------------------------------

    [Fact]
    public void DetectMonth_DatasDoMesCorrente_RetornaMesAtual()
    {
        var hoje = DateTime.Today;
        var registros = new List<Models.PunchRecord>
        {
            new() { Date = new DateTime(hoje.Year, hoje.Month, 1).ToString("dd/MM/yyyy"), Entry = "08:01", Exit = "17:02" }
        };

        Assert.Equal("mes_atual", PunchFileReader.DetectMonth(registros));
    }

    [Fact]
    public void DetectMonth_DatasDoMesAnterior_RetornaMesPassado()
    {
        var mesAnterior = DateTime.Today.AddMonths(-1);
        var registros = new List<Models.PunchRecord>
        {
            new() { Date = new DateTime(mesAnterior.Year, mesAnterior.Month, 1).ToString("dd/MM/yyyy"), Entry = "08:01", Exit = "17:02" }
        };

        Assert.Equal("mes_passado", PunchFileReader.DetectMonth(registros));
    }

    [Fact]
    public void DetectMonth_SemRegistros_RetornaMesAtual()
        => Assert.Equal("mes_atual", PunchFileReader.DetectMonth(new List<Models.PunchRecord>()));

    // ---------------------------------------------------------------
    // Erros
    // ---------------------------------------------------------------

    [Fact]
    public void ArquivoInexistente_LancaFileNotFound()
        => Assert.Throws<FileNotFoundException>(() => Leitor.Read(Path.Combine(Path.GetTempPath(), "nao-existe-pontos.xlsx")));

    [Fact]
    public void FormatoNaoSuportado_LancaNotSupported()
    {
        var caminho = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(caminho, "conteudo");
        try
        {
            Assert.Throws<NotSupportedException>(() => Leitor.Read(caminho));
        }
        finally
        {
            File.Delete(caminho);
        }
    }
}
