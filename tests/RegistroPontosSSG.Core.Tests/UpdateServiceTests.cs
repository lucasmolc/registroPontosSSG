using System.Diagnostics;
using RegistroPontosSSG.Core.Update;

namespace RegistroPontosSSG.Core.Tests;

/// <summary>
/// Cobre a leitura da API de releases do GitHub, a validação do arquivo baixado e a
/// mecânica de troca do executável. Nenhum teste acessa a rede: o JSON é fixo e a
/// troca é exercitada com arquivos temporários.
/// </summary>
public sealed class UpdateServiceTests
{
    /// <summary>JSON no mesmo formato devolvido por /releases/latest.</summary>
    private static string Release(string tag, string assetName = "RegistroPontosSSG.exe",
        long size = 103_000_000, bool draft = false, bool prerelease = false) => $$"""
        {
          "tag_name": "{{tag}}",
          "name": "Registro Automático de Pontos SSG {{tag}}",
          "draft": {{(draft ? "true" : "false")}},
          "prerelease": {{(prerelease ? "true" : "false")}},
          "html_url": "https://github.com/exemplo/repo/releases/tag/{{tag}}",
          "published_at": "2026-07-27T18:13:17Z",
          "body": "### Corrigido\n- alguma coisa",
          "assets": [
            {
              "name": "{{assetName}}",
              "size": {{size}},
              "browser_download_url": "https://github.com/exemplo/repo/releases/download/{{tag}}/{{assetName}}"
            }
          ]
        }
        """;

    // ---------------------------------------------------------------
    // Comparação de versões
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("v2.0", 2, 0, 0)]
    [InlineData("v1.4.0-beta", 1, 4, 0)]
    [InlineData("release-3.1.2", 3, 1, 2)]
    public void ParseVersion_AceitaFormatosComuns(string tag, int maior, int menor, int correcao)
    {
        var versao = UpdateService.ParseVersion(tag);

        Assert.NotNull(versao);
        Assert.Equal(maior, versao!.Major);
        Assert.Equal(menor, versao.Minor);
        Assert.Equal(correcao, versao.Build);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sem-numero")]
    public void ParseVersion_RejeitaTagSemVersao(string tag)
        => Assert.Null(UpdateService.ParseVersion(tag));

    // ---------------------------------------------------------------
    // Leitura da release
    // ---------------------------------------------------------------

    [Fact]
    public void ParseRelease_VersaoMaior_RetornaAtualizacao()
    {
        var info = UpdateService.ParseRelease(Release("v1.3.0"), new Version(1, 2, 0));

        Assert.NotNull(info);
        Assert.Equal("v1.3.0", info!.TagName);
        Assert.Equal(new Version(1, 3, 0, 0), info.Version);
        Assert.EndsWith("RegistroPontosSSG.exe", info.DownloadUrl);
        Assert.Equal(103_000_000, info.SizeBytes);
        Assert.Contains("MB", info.SizeLabel);
        Assert.Contains("Corrigido", info.Notes);
    }

    [Theory]
    [InlineData("v1.2.0")] // mesma versão
    [InlineData("v1.1.0")] // anterior
    public void ParseRelease_VersaoIgualOuAnterior_NaoOferece(string tag)
        => Assert.Null(UpdateService.ParseRelease(Release(tag), new Version(1, 2, 0)));

    [Fact]
    public void ParseRelease_SemExecutavelAnexado_NaoOferece()
        => Assert.Null(UpdateService.ParseRelease(
            Release("v1.3.0", assetName: "codigo-fonte.zip"), new Version(1, 2, 0)));

    [Fact]
    public void ParseRelease_Rascunho_NaoOferece()
        => Assert.Null(UpdateService.ParseRelease(Release("v1.3.0", draft: true), new Version(1, 2, 0)));

    [Fact]
    public void ParseRelease_PreLancamento_NaoOferece()
        => Assert.Null(UpdateService.ParseRelease(Release("v1.3.0", prerelease: true), new Version(1, 2, 0)));

    [Fact]
    public async Task CheckForUpdateAsync_BuildLocal_NaoConsultaNada()
    {
        // Versão 0.0.0 identifica compilação local: a verificação é dispensada.
        using var servico = new UpdateService("exemplo/repo",
            handler: new HandlerQueFalha(), currentVersion: UpdateService.DevelopmentVersion);

        Assert.True(servico.IsDevelopmentBuild);
        Assert.Null(await servico.CheckForUpdateAsync());
    }

    [Fact]
    public async Task CheckForUpdateAsync_ErroDeRede_NaoPropagaExcecao()
    {
        using var servico = new UpdateService("exemplo/repo",
            handler: new HandlerQueFalha(), currentVersion: new Version(1, 0, 0));

        // Sem rede o app precisa seguir funcionando, sem atualização e sem erro.
        Assert.Null(await servico.CheckForUpdateAsync());
    }

    private sealed class HandlerQueFalha : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new HttpRequestException("sem rede");
    }

    // ---------------------------------------------------------------
    // Validação do arquivo baixado
    // ---------------------------------------------------------------

    [Fact]
    public void ValidateDownload_ConteudoQueNaoEExecutavel_Rejeita()
    {
        var caminho = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllText(caminho, "<html>404 not found</html>");
        try
        {
            var erro = Assert.Throws<InvalidOperationException>(
                () => UpdateService.ValidateDownload(caminho, 0));
            Assert.Contains("executável", erro.Message);
        }
        finally { File.Delete(caminho); }
    }

    [Fact]
    public void ValidateDownload_TamanhoDiferenteDoEsperado_Rejeita()
    {
        var caminho = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(caminho, new byte[] { (byte)'M', (byte)'Z', 0, 0 });
        try
        {
            var erro = Assert.Throws<InvalidOperationException>(
                () => UpdateService.ValidateDownload(caminho, tamanhoEsperado: 999));
            Assert.Contains("Tamanho inesperado", erro.Message);
        }
        finally { File.Delete(caminho); }
    }

    [Fact]
    public void ValidateDownload_ExecutavelValido_Aceita()
    {
        var caminho = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".bin");
        var conteudo = new byte[] { (byte)'M', (byte)'Z', 1, 2, 3 };
        File.WriteAllBytes(caminho, conteudo);
        try
        {
            UpdateService.ValidateDownload(caminho, conteudo.Length); // não deve lançar
        }
        finally { File.Delete(caminho); }
    }

    [Fact]
    public void ValidateDownload_ArquivoVazio_Rejeita()
    {
        var caminho = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(caminho, Array.Empty<byte>());
        try
        {
            Assert.Throws<InvalidOperationException>(() => UpdateService.ValidateDownload(caminho, 0));
        }
        finally { File.Delete(caminho); }
    }

    // ---------------------------------------------------------------
    // Troca do executável
    // ---------------------------------------------------------------

    [Fact]
    public void BuildUpdateScript_ContemAsEtapasEsperadas()
    {
        var script = UpdateService.BuildUpdateScript(4321, @"C:\app\RegistroPontosSSG.exe",
            @"C:\temp\novo.exe", @"C:\temp\log.txt");

        Assert.Contains("$processId = 4321", script);
        Assert.Contains("Wait-Process", script);
        Assert.Contains("Copy-Item", script);
        Assert.Contains("Start-Process", script);
        Assert.Contains(@"C:\app\RegistroPontosSSG.exe", script);
    }

    [Fact]
    public void BuildUpdateScript_CaminhoComApostrofo_EEscapado()
    {
        var script = UpdateService.BuildUpdateScript(1, @"C:\pasta d'agua\app.exe", @"C:\t\n.exe", @"C:\t\l.txt");

        // Em PowerShell, apóstrofo dentro de string literal é escapado duplicando-o.
        Assert.Contains("d''agua", script);
    }

    /// <summary>
    /// Executa o script de verdade: com um PID já encerrado, ele deve copiar o arquivo
    /// novo sobre o antigo e registrar o resultado no log.
    /// </summary>
    [Fact]
    public void ScriptDeTroca_SubstituiOArquivoERegistraNoLog()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "rp-troca-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(pasta);
        try
        {
            // Usamos .cmd para que o "reabrir aplicativo" do final seja inofensivo.
            var alvo = Path.Combine(pasta, "app.cmd");
            var origem = Path.Combine(pasta, "novo.cmd");
            var log = Path.Combine(pasta, "atualizacao.log");
            File.WriteAllText(alvo, "@echo off\r\nrem VERSAO-ANTIGA\r\n");
            File.WriteAllText(origem, "@echo off\r\nrem VERSAO-NOVA\r\n");

            var encerrado = Process.Start(new ProcessStartInfo("cmd", "/c exit") { CreateNoWindow = true })!;
            encerrado.WaitForExit();

            var script = Path.Combine(pasta, "aplicar.ps1");
            File.WriteAllText(script, UpdateService.BuildUpdateScript(encerrado.Id, alvo, origem, log));

            var ps = Process.Start(new ProcessStartInfo("powershell",
                $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"")
            { UseShellExecute = false, CreateNoWindow = true })!;
            Assert.True(ps.WaitForExit(60_000), "o script de atualização não terminou em 60s");

            Assert.Contains("VERSAO-NOVA", File.ReadAllText(alvo));
            Assert.False(File.Exists(origem), "o arquivo baixado deveria ser removido após a cópia");
            Assert.True(File.Exists(log), "o script deveria registrar o resultado em log");
            Assert.Contains("executavel substituido", File.ReadAllText(log));
        }
        finally
        {
            try { Directory.Delete(pasta, recursive: true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void ApplyAndRestart_SemArquivoBaixado_Lanca()
        => Assert.Throws<FileNotFoundException>(
            () => UpdateService.ApplyAndRestart(Path.Combine(Path.GetTempPath(), "nao-existe-update.exe")));
}
