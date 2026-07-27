using System.Text.Json.Serialization;

namespace RegistroPontosSSG.Core.Models;

/// <summary>
/// Modelo de configuração persistido em %APPDATA%\RegistroPontosSSG\config.json.
/// Campos sensíveis (Password, TotpSecret) são armazenados criptografados via DPAPI.
/// </summary>
public sealed class AppConfig
{
    public CredentialsConfig Credentials { get; set; } = new();
    public PunchFileConfig PunchFile { get; set; } = new();
    public ValidationRules Validation { get; set; } = new();
    public AutomationConfig Automation { get; set; } = new();
    public SsgUrls Ssg { get; set; } = new();
    public UpdateConfig Update { get; set; } = new();

    /// <summary>
    /// Versão que rodou na última vez. Usada para exibir o resumo de novidades na
    /// primeira execução depois de uma atualização.
    /// </summary>
    public string LastRunVersion { get; set; } = string.Empty;
}

/// <summary>
/// Verificação de novas versões publicadas no GitHub.
/// </summary>
public sealed class UpdateConfig
{
    /// <summary>Consultar as releases ao abrir o aplicativo.</summary>
    public bool CheckOnStartup { get; set; } = true;

    /// <summary>Repositório no formato "usuario/repositorio".</summary>
    public string Repository { get; set; } = "lucasmolc/registroPontosSSG";
}

public sealed class CredentialsConfig
{
    public string Username { get; set; } = string.Empty;
    /// <summary>Senha criptografada via DPAPI (Base64).</summary>
    public string EncryptedPassword { get; set; } = string.Empty;
    /// <summary>TOTP secret criptografada via DPAPI (Base64). Opcional.</summary>
    public string EncryptedTotpSecret { get; set; } = string.Empty;

    [JsonIgnore] public string Password { get; set; } = string.Empty;
    [JsonIgnore] public string TotpSecret { get; set; } = string.Empty;
}

public sealed class PunchFileConfig
{
    /// <summary>
    /// Caminho absoluto do arquivo Excel/CSV escolhido pelo usuário.
    /// Não é persistido em disco: cada execução do app começa sem arquivo selecionado.
    /// </summary>
    [JsonIgnore] public string FilePath { get; set; } = string.Empty;
}

public sealed class AutomationConfig
{
    public int TimeoutMs { get; set; } = 30000;
    public bool Headless { get; set; } = false;
    public int SlowMoMs { get; set; } = 100;
    public bool SelectCurrentMonth { get; set; } = true;
    public bool IgnoreExistingDates { get; set; } = true;
    public bool UseSystemChrome { get; set; } = true;
    public string ChromePath { get; set; } = string.Empty;
    public bool UseChromeProfile { get; set; } = false;
}

public sealed class SsgUrls
{
    public string BaseUrl { get; set; } = "https://ssg.sysmap.com.br";

    /// <summary>
    /// Tela "Registros de Entrada/Saída e Apontamento" da SPA do SSG.
    /// A página antiga (<c>/new/timesheet/timesheetrecording.asp</c>) apenas redireciona
    /// para esta rota, por isso navegamos direto para o hash.
    /// </summary>
    public string AccessEntryUrl { get; set; } = "https://ssg.sysmap.com.br/index.html#/access-entry/get-list";
}
