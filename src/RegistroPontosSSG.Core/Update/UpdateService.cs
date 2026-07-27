using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RegistroPontosSSG.Core.Update;

/// <summary>
/// Verifica se existe uma release mais recente no GitHub, baixa o executável e
/// aplica a atualização.
///
/// A troca do próprio .exe não pode ser feita pelo processo em execução — o Windows
/// mantém o arquivo bloqueado. Por isso <see cref="ApplyAndRestart"/> delega a um
/// script PowerShell que espera o processo encerrar, substitui o arquivo e reabre o app.
///
/// A API pública do GitHub é consultada sem autenticação (o repositório é público),
/// com limite de 60 requisições por hora por IP — suficiente para uma verificação
/// na inicialização.
/// </summary>
public sealed class UpdateService : IDisposable
{
    /// <summary>
    /// Versão usada em builds locais (o número real é injetado no publish a partir da
    /// tag). Serve de sentinela para não oferecer "atualização" durante o desenvolvimento.
    /// </summary>
    public static readonly Version DevelopmentVersion = new(0, 0, 0);

    private static readonly Regex VersionPattern = new(@"(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:\.(\d+))?",
        RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly string _repository;

    public UpdateService(string repository, HttpMessageHandler? handler = null, Version? currentVersion = null)
    {
        _repository = string.IsNullOrWhiteSpace(repository)
            ? throw new ArgumentException("Repositório não informado.", nameof(repository))
            : repository.Trim();

        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _ownsHttpClient = true;
        _http.Timeout = TimeSpan.FromSeconds(30);
        // O GitHub rejeita requisições sem User-Agent.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("RegistroPontosSSG-Updater");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        CurrentVersion = currentVersion ?? CurrentApplicationVersion;
    }

    /// <summary>
    /// Versão do executável em execução, lida uma única vez dos metadados do assembly.
    /// </summary>
    public static Version CurrentApplicationVersion { get; } = DetectCurrentVersion();

    /// <summary>Versão considerada por esta instância nas comparações.</summary>
    public Version CurrentVersion { get; }

    /// <summary>True em build local, onde a verificação de atualização é dispensada.</summary>
    public bool IsDevelopmentBuild => CurrentVersion == DevelopmentVersion;

    /// <summary>Diretório onde os downloads ficam antes da troca.</summary>
    public static string UpdatesDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RegistroPontosSSG", "updates");

    /// <summary>
    /// Consulta a última release. Devolve null quando já está atualizado, quando o
    /// repositório ainda não tem releases ou quando a consulta falha (sem rede, por
    /// exemplo) — a ausência de atualização nunca deve interromper o uso do app.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (IsDevelopmentBuild) return null;

        try
        {
            var url = $"https://api.github.com/repos/{_repository}/releases/latest";
            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);

            // 404 = repositório sem releases publicadas.
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ParseRelease(json, CurrentVersion);
        }
        catch (Exception)
        {
            // Sem rede, DNS bloqueado, proxy corporativo: silenciosamente sem atualização.
            return null;
        }
    }

    /// <summary>
    /// Interpreta o JSON de uma release e devolve <see cref="UpdateInfo"/> apenas se a
    /// tag for maior que <paramref name="currentVersion"/> e houver um .exe anexado.
    /// </summary>
    internal static UpdateInfo? ParseRelease(string json, Version currentVersion)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object) return null;
        if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) return null;

        var tag = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(tag)) return null;

        var version = ParseVersion(tag!);
        // Normaliza os dois lados: Version(1,2,0) tem Revision = -1, então uma
        // comparação direta consideraria 1.2.0.0 mais recente que 1.2.0 e o app
        // ofereceria atualização para a versão que já está instalada.
        if (version is null || Normalizar(version) <= Normalizar(currentVersion)) return null;

        // Procura o executável entre os anexos da release.
        string? downloadUrl = null;
        long size = 0;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var nome = asset.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                if (!nome.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

                downloadUrl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var bytes) ? bytes : 0;
                break;
            }
        }
        if (string.IsNullOrWhiteSpace(downloadUrl)) return null;

        DateTimeOffset? publicado = null;
        if (root.TryGetProperty("published_at", out var pubProp)
            && DateTimeOffset.TryParse(pubProp.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsed))
        {
            publicado = parsed;
        }

        return new UpdateInfo
        {
            Version = version,
            TagName = tag!,
            ReleaseName = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty,
            ReleaseUrl = root.TryGetProperty("html_url", out var htmlProp) ? htmlProp.GetString() ?? string.Empty : string.Empty,
            DownloadUrl = downloadUrl!,
            SizeBytes = size,
            PublishedAt = publicado,
            Notes = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? string.Empty : string.Empty
        };
    }

    /// <summary>
    /// Completa componentes ausentes com zero, para que 1.2, 1.2.0 e 1.2.0.0 sejam
    /// equivalentes na comparação.
    /// </summary>
    internal static Version Normalizar(Version versao)
        => new(versao.Major, versao.Minor, Math.Max(versao.Build, 0), Math.Max(versao.Revision, 0));

    /// <summary>
    /// Extrai a versão de uma tag. Aceita "v1.2.3", "1.2", "release-2.0.1" e
    /// descarta sufixos como "-beta".
    /// </summary>
    internal static Version? ParseVersion(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        var match = VersionPattern.Match(tag);
        if (!match.Success) return null;

        int Grupo(int i) => match.Groups[i].Success
            ? int.Parse(match.Groups[i].Value, CultureInfo.InvariantCulture)
            : 0;

        return new Version(Grupo(1), Grupo(2), Grupo(3), Grupo(4));
    }

    /// <summary>
    /// Lê a versão do executável em uso. Usa o InformationalVersion (definido no publish)
    /// e cai para a versão do assembly quando não há.
    /// </summary>
    private static Version DetectCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

        var informacional = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informacional))
        {
            // Pode vir como "1.2.0+9f8a1c2" ou "1.2.0-beta".
            var limpo = informacional.Split('+', '-')[0];
            var versao = ParseVersion(limpo);
            if (versao is not null) return versao;
        }

        return assembly.GetName().Version ?? DevelopmentVersion;
    }

    /// <summary>
    /// Baixa o executável da release para <see cref="UpdatesDirectory"/> e devolve o
    /// caminho. Valida o tamanho informado pela API e a assinatura "MZ" de um
    /// executável do Windows, para não substituir o app por uma página de erro HTML.
    /// </summary>
    public async Task<string> DownloadAsync(UpdateInfo info, IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);

        Directory.CreateDirectory(UpdatesDirectory);
        var destino = Path.Combine(UpdatesDirectory, $"RegistroPontosSSG-{info.TagName}.exe");
        var parcial = destino + ".part";
        if (File.Exists(parcial)) File.Delete(parcial);

        using (var response = await _http
            .GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? info.SizeBytes;
            await using var origem = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var arquivo = File.Create(parcial);

            var buffer = new byte[81920];
            long baixado = 0;
            int lidos;
            var ultimoPercentual = -1;

            while ((lidos = await origem.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await arquivo.WriteAsync(buffer.AsMemory(0, lidos), cancellationToken).ConfigureAwait(false);
                baixado += lidos;

                if (total > 0)
                {
                    var percentual = (int)(baixado * 100 / total);
                    if (percentual != ultimoPercentual)
                    {
                        ultimoPercentual = percentual;
                        progress?.Report(percentual);
                    }
                }
            }
        }

        ValidateDownload(parcial, info.SizeBytes);

        if (File.Exists(destino)) File.Delete(destino);
        File.Move(parcial, destino);
        progress?.Report(100);
        return destino;
    }

    /// <summary>
    /// Confere se o arquivo baixado tem o tamanho esperado e é um executável do Windows.
    /// </summary>
    internal static void ValidateDownload(string caminho, long tamanhoEsperado)
    {
        var info = new FileInfo(caminho);
        if (!info.Exists || info.Length == 0)
            throw new InvalidOperationException("Download vazio ou inexistente.");

        if (tamanhoEsperado > 0 && info.Length != tamanhoEsperado)
            throw new InvalidOperationException(
                $"Tamanho inesperado: baixados {info.Length} bytes, esperados {tamanhoEsperado}.");

        using var stream = File.OpenRead(caminho);
        var assinatura = new byte[2];
        if (stream.Read(assinatura, 0, 2) != 2 || assinatura[0] != (byte)'M' || assinatura[1] != (byte)'Z')
            throw new InvalidOperationException("O arquivo baixado não é um executável do Windows.");
    }

    /// <summary>
    /// Gera o script que troca o executável e reabre o app, dispara-o em segundo plano
    /// e devolve o caminho do script. O chamador deve encerrar a aplicação em seguida —
    /// o script aguarda o processo terminar antes de sobrescrever o arquivo.
    /// </summary>
    public static string ApplyAndRestart(string executavelBaixado, string? executavelAtual = null)
    {
        if (!File.Exists(executavelBaixado))
            throw new FileNotFoundException("Executável baixado não encontrado.", executavelBaixado);

        var alvo = executavelAtual ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("Não foi possível determinar o executável em uso.");

        Directory.CreateDirectory(UpdatesDirectory);
        var script = Path.Combine(UpdatesDirectory, "aplicar-atualizacao.ps1");
        var log = Path.Combine(UpdatesDirectory, "atualizacao.log");

        var conteudo = BuildUpdateScript(Environment.ProcessId, alvo, executavelBaixado, log);
        File.WriteAllText(script, conteudo, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });

        return script;
    }

    /// <summary>
    /// Script de troca: espera o processo encerrar, substitui o executável e reabre o app.
    /// Se a cópia falhar (por exemplo, sem permissão de escrita na pasta do .exe), o app
    /// antigo é reaberto e o motivo fica registrado no log.
    /// </summary>
    internal static string BuildUpdateScript(int processId, string alvo, string origem, string log)
    {
        var linhas = new[]
        {
            "$ErrorActionPreference = 'Stop'",
            $"$processId = {processId}",
            $"$alvo   = '{alvo.Replace("'", "''")}'",
            $"$origem = '{origem.Replace("'", "''")}'",
            $"$log    = '{log.Replace("'", "''")}'",
            "",
            "function Registrar($mensagem) {",
            "    \"$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  $mensagem\" | Add-Content -LiteralPath $log",
            "}",
            "",
            "try {",
            "    Registrar \"aguardando encerramento do processo $processId\"",
            "    try { Wait-Process -Id $processId -Timeout 60 -ErrorAction Stop } catch { }",
            "    Start-Sleep -Milliseconds 800",
            "",
            "    Copy-Item -LiteralPath $origem -Destination $alvo -Force",
            "    Registrar \"executavel substituido: $alvo\"",
            "    Remove-Item -LiteralPath $origem -Force -ErrorAction SilentlyContinue",
            "}",
            "catch {",
            "    Registrar \"FALHA: $($_.Exception.Message)\"",
            "}",
            "finally {",
            "    Start-Process -FilePath $alvo",
            "    Registrar 'aplicativo reaberto'",
            "}"
        };
        return string.Join(Environment.NewLine, linhas) + Environment.NewLine;
    }

    public void Dispose()
    {
        if (_ownsHttpClient) _http.Dispose();
    }
}
