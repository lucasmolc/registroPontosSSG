namespace RegistroPontosSSG.Core.Models;

/// <summary>
/// Regras de ajuste automático de horários conforme o SSG.
/// </summary>
public sealed class ValidationRules
{
    public bool BlockRoundTimes { get; set; } = true;
    public int DaysToCheckDuplicates { get; set; } = 5;
    public bool BlockDuplicateTimes { get; set; } = true;
    public bool BlockExactOneHourLunch { get; set; } = true;
    /// <summary>
    /// Bloqueia que dois horários do mesmo dia compartilhem o mesmo valor de minuto
    /// (ex.: 09:58 e 12:58). O SSG marca esses registros com um "thumbs-down" vermelho.
    /// </summary>
    public bool BlockSameMinutes { get; set; } = true;
}

/// <summary>
/// Registro de ponto após ajustes automáticos.
/// </summary>
public sealed class AdjustedRecord
{
    public string Date { get; init; } = string.Empty;
    public string Entry { get; init; } = string.Empty;
    public string LunchOut { get; init; } = string.Empty;
    public string LunchReturn { get; init; } = string.Empty;
    public string Exit { get; init; } = string.Empty;
    public List<string> Adjustments { get; init; } = new();
    public bool HasAdjustments => Adjustments.Count > 0;
}
