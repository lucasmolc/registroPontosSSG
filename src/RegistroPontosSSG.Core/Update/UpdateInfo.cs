namespace RegistroPontosSSG.Core.Update;

/// <summary>
/// Dados de uma release do GitHub mais recente que a versão em execução.
/// </summary>
public sealed record UpdateInfo
{
    /// <summary>Versão extraída da tag (ex.: tag "v1.3.0" → 1.3.0).</summary>
    public required Version Version { get; init; }

    /// <summary>Tag da release, como está no GitHub (ex.: "v1.3.0").</summary>
    public required string TagName { get; init; }

    /// <summary>Título da release.</summary>
    public string ReleaseName { get; init; } = string.Empty;

    /// <summary>Página da release, para quando a atualização automática não for possível.</summary>
    public string ReleaseUrl { get; init; } = string.Empty;

    /// <summary>URL do executável anexado à release.</summary>
    public required string DownloadUrl { get; init; }

    /// <summary>Tamanho do executável em bytes, usado para validar o download.</summary>
    public long SizeBytes { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>Notas da release (corpo em Markdown).</summary>
    public string Notes { get; init; } = string.Empty;

    public string SizeLabel => SizeBytes > 0
        ? $"{SizeBytes / 1024d / 1024d:N1} MB"
        : "tamanho desconhecido";
}
