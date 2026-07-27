using ClosedXML.Excel;

namespace RegistroPontosSSG.Core.Tests;

/// <summary>
/// Cria planilhas temporárias para os testes de leitura. As fixtures são geradas em
/// código (e não versionadas como .xlsx) porque o .gitignore bloqueia planilhas —
/// regra que existe para impedir o commit de arquivos de ponto pessoais.
/// </summary>
internal sealed class PlanilhaFixture : IDisposable
{
    private readonly string _diretorio;

    public PlanilhaFixture()
    {
        _diretorio = Path.Combine(Path.GetTempPath(), "RegistroPontosSSG.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_diretorio);
    }

    /// <summary>Grava as linhas informadas na primeira aba e devolve o caminho do arquivo.</summary>
    public string Criar(string nome, string?[][] linhas, string aba = "Planilha1")
    {
        var caminho = Path.Combine(_diretorio, nome);
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(aba);

        for (var r = 0; r < linhas.Length; r++)
        {
            for (var c = 0; c < linhas[r].Length; c++)
            {
                var valor = linhas[r][c];
                if (!string.IsNullOrEmpty(valor))
                    sheet.Cell(r + 1, c + 1).Value = valor;
            }
        }

        workbook.SaveAs(caminho);
        return caminho;
    }

    public void Dispose()
    {
        try { Directory.Delete(_diretorio, recursive: true); } catch { /* temp */ }
    }
}
