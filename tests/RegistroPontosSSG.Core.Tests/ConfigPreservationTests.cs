using RegistroPontosSSG.Core.Configuration;

namespace RegistroPontosSSG.Core.Tests;

/// <summary>
/// Garante a promessa central da atualização automática: trocar o executável não pode
/// levar as configurações do usuário embora.
/// </summary>
public sealed class ConfigPreservationTests
{
    [Fact]
    public void ConfiguracaoFicaEmAppDataEnaoJuntoDoExecutavel()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        Assert.StartsWith(appData, ConfigService.ConfigDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("config.json", Path.GetFileName(ConfigService.ConfigFilePath));

        // O diretório do executável em teste (bin/...) não pode conter o config:
        // é justamente essa separação que preserva os dados na troca do .exe.
        var pastaDoExecutavel = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        Assert.False(
            ConfigService.ConfigDirectory.StartsWith(pastaDoExecutavel, StringComparison.OrdinalIgnoreCase),
            "o config não pode ficar dentro da pasta do executável");
    }

    [Fact]
    public void LogsEPerfilDoNavegadorTambemFicamForaDaPastaDoExecutavel()
    {
        var pastaDoExecutavel = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

        foreach (var caminho in new[] { ConfigService.LogsDirectory, ConfigService.BrowserDataDirectory })
        {
            Assert.False(caminho.StartsWith(pastaDoExecutavel, StringComparison.OrdinalIgnoreCase),
                $"{caminho} não deveria ficar junto do executável");
            Assert.StartsWith(ConfigService.ConfigDirectory, caminho, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void BackupConfig_SemArquivoSalvo_DevolveNull()
    {
        // Em máquina de CI não existe config salvo; o backup deve ser silencioso.
        if (File.Exists(ConfigService.ConfigFilePath))
        {
            var backup = ConfigService.BackupConfig();
            Assert.NotNull(backup);
            Assert.True(File.Exists(backup));
            Assert.Equal("config.backup.json", Path.GetFileName(backup));
        }
        else
        {
            Assert.Null(ConfigService.BackupConfig());
        }
    }
}
