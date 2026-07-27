using System.Text.Json;
using RegistroPontosSSG.Core.Models;
using RegistroPontosSSG.Core.Security;

namespace RegistroPontosSSG.Core.Configuration;

/// <summary>
/// Carrega e persiste a configuração em %APPDATA%\RegistroPontosSSG\config.json.
/// Campos sensíveis são criptografados via DPAPI antes de gravar.
/// </summary>
public sealed class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RegistroPontosSSG");

    public static string ConfigFilePath => Path.Combine(ConfigDirectory, "config.json");

    public static string LogsDirectory => Path.Combine(ConfigDirectory, "logs");

    public static string BrowserDataDirectory => Path.Combine(ConfigDirectory, "browser_data");

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigFilePath))
                return new AppConfig();

            var json = File.ReadAllText(ConfigFilePath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();

            // Descriptografa campos sensíveis em memória
            config.Credentials.Password = ProtectedStorage.Decrypt(config.Credentials.EncryptedPassword);
            config.Credentials.TotpSecret = ProtectedStorage.Decrypt(config.Credentials.EncryptedTotpSecret);
            return config;
        }
        catch
        {
            return new AppConfig();
        }
    }

    /// <summary>
    /// Copia o config.json para config.backup.json e devolve o caminho da cópia.
    /// Chamado antes de aplicar uma atualização: a troca do executável não toca no
    /// %APPDATA%, mas a cópia protege contra qualquer falha durante o processo.
    /// Devolve null se ainda não existe configuração salva.
    /// </summary>
    public static string? BackupConfig()
    {
        try
        {
            if (!File.Exists(ConfigFilePath)) return null;
            var backup = Path.Combine(ConfigDirectory, "config.backup.json");
            File.Copy(ConfigFilePath, backup, overwrite: true);
            return backup;
        }
        catch
        {
            return null;
        }
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDirectory);

        // Criptografa antes de serializar
        config.Credentials.EncryptedPassword = ProtectedStorage.Encrypt(config.Credentials.Password);
        config.Credentials.EncryptedTotpSecret = ProtectedStorage.Encrypt(config.Credentials.TotpSecret);

        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigFilePath, json);
    }
}
