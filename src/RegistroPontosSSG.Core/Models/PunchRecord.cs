namespace RegistroPontosSSG.Core.Models;

/// <summary>
/// Representa um registro de ponto lido do arquivo de entrada.
/// </summary>
public sealed class PunchRecord
{
    public string Date { get; init; } = string.Empty;
    public string Entry { get; init; } = string.Empty;
    public string LunchOut { get; init; } = string.Empty;
    public string LunchReturn { get; init; } = string.Empty;
    public string Exit { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;

    public bool HasLunch => !string.IsNullOrWhiteSpace(LunchOut) && !string.IsNullOrWhiteSpace(LunchReturn);

    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(Date)) return false;

        // Registros incompletos são aceitos desde que haja informação suficiente para
        // inferir os horários faltantes em TimeValidator.InferMissingTimes:
        // - Precisa de algo no "lado manhã" (Entrada ou Saída-almoço)
        // - Precisa de algo no "lado tarde" (Retorno-almoço ou Saída)
        // Casos suportados, por exemplo:
        //   • Entrada + Saída (sem almoço)
        //   • Retorno-almoço + Saída (Entrada e Saída-almoço inferidas)
        //   • Entrada + Saída-almoço (Retorno-almoço e Saída inferidos)
        //   • Apenas Entrada + Saída-almoço + Saída (Retorno-almoço inferido)
        bool morningSide = !string.IsNullOrWhiteSpace(Entry) || !string.IsNullOrWhiteSpace(LunchOut);
        bool afternoonSide = !string.IsNullOrWhiteSpace(LunchReturn) || !string.IsNullOrWhiteSpace(Exit);
        return morningSide && afternoonSide;
    }

    public override string ToString() => HasLunch
        ? $"Ponto {Date}: {Entry} - {LunchOut} | {LunchReturn} - {Exit}"
        : $"Ponto {Date}: {Entry} - {Exit}";
}
