using System.Globalization;
using RegistroPontosSSG.Core.Models;

namespace RegistroPontosSSG.Core.Validation;

/// <summary>
/// Aplica regras automáticas de ajuste de horários do SSG:
/// - Bloqueia horários redondos (08:00 → 08:01)
/// - Bloqueia almoço de exatamente 1h (12:00→13:00 → 12:00→13:01)
/// - Bloqueia horários duplicados nos últimos N dias
/// - Compensa o total trabalhado quando entrada/retorno são adiantados
/// </summary>
public sealed class TimeValidator
{
    private readonly ValidationRules _rules;
    private readonly Dictionary<string, List<string>> _usedTimes = new();

    public TimeValidator(ValidationRules rules)
    {
        _rules = rules;
    }

    public void LoadExistingTimes(IDictionary<string, List<string>> existing)
    {
        foreach (var (date, times) in existing)
        {
            if (!_usedTimes.TryGetValue(date, out var list))
            {
                list = new List<string>();
                _usedTimes[date] = list;
            }
            list.AddRange(times);
        }
    }

    public void RegisterUsedTimes(string date, IEnumerable<string> times)
    {
        if (!_usedTimes.TryGetValue(date, out var list))
        {
            list = new List<string>();
            _usedTimes[date] = list;
        }
        list.AddRange(times.Where(t => !string.IsNullOrWhiteSpace(t)));
    }

    public AdjustedRecord Adjust(string date, string entry, string lunchOut, string lunchReturn, string exit)
    {
        var adjustments = new List<string>();
        var usedInRecord = new List<string>();

        // 0. Inferência de horários incompletos: completa campos faltantes
        //    quando há informação parcial suficiente. Usa jornada de 8h e almoço de 65 min
        //    (evita o bloqueio "almoço 1h exata" e horários redondos).
        (entry, lunchOut, lunchReturn, exit, var inferAdj) = InferMissingTimes(entry, lunchOut, lunchReturn, exit);
        adjustments.AddRange(inferAdj);

        var hasLunch = !string.IsNullOrWhiteSpace(lunchOut) && !string.IsNullOrWhiteSpace(lunchReturn);

        var originalMinutes = WorkedMinutes(entry, lunchOut, lunchReturn, exit);
        var originalExit = exit;

        // 1. Entrada
        (entry, var entryAdj) = AdjustFull(date, entry, usedInRecord, "Entrada");
        adjustments.AddRange(entryAdj);
        usedInRecord.Add(entry);

        if (hasLunch)
        {
            // 2. Saída almoço
            (lunchOut, var loAdj) = AdjustFull(date, lunchOut, usedInRecord, "Saída almoço");
            adjustments.AddRange(loAdj);
            usedInRecord.Add(lunchOut);

            // 3. Retorno almoço — primeiro checa almoço 1h exata
            if (_rules.BlockExactOneHourLunch && LunchMinutes(lunchOut, lunchReturn) == 60)
            {
                var oldReturn = lunchReturn;
                lunchReturn = AddMinutes(lunchReturn, 1);
                adjustments.Add($"Retorno almoço: {oldReturn} → {lunchReturn} (almoço 1h exata)");
            }

            (lunchReturn, var lrAdj) = AdjustFull(date, lunchReturn, usedInRecord, "Retorno almoço");
            adjustments.AddRange(lrAdj);
            usedInRecord.Add(lunchReturn);
        }

        // 4. Compensa saída para manter o total trabalhado original
        var currentMinutes = WorkedMinutes(entry, lunchOut, lunchReturn, originalExit);
        var diff = originalMinutes - currentMinutes;
        if (diff != 0)
        {
            exit = AddMinutes(originalExit, diff);
            var sign = diff > 0 ? $"+{diff}" : diff.ToString(CultureInfo.InvariantCulture);
            adjustments.Add($"Saída: {originalExit} → {exit} (compensação {sign}min)");
        }

        // 5. Ajusta saída por todas as regras
        (exit, var exitAdj) = AdjustFull(date, exit, usedInRecord, "Saída");
        adjustments.AddRange(exitAdj);
        usedInRecord.Add(exit);

        // 6. CONVERGÊNCIA FINAL: re-valida todos os campos juntos contra TODAS as regras
        //    (redondo + duplicado histórico + duplicado no registro + minutos iguais + almoço 1h).
        //    Repete até estabilizar para garantir que o ajuste de uma regra não quebre outra.
        const int maxIterations = 20;
        for (var iter = 0; iter < maxIterations; iter++)
        {
            var changed = false;

            // 6a. Almoço 1h exata pode ter sido reintroduzido pelas compensações anteriores
            if (hasLunch && _rules.BlockExactOneHourLunch && LunchMinutes(lunchOut, lunchReturn) == 60)
            {
                var oldReturn = lunchReturn;
                lunchReturn = AddMinutes(lunchReturn, 1);
                adjustments.Add($"Retorno almoço: {oldReturn} → {lunchReturn} (almoço 1h exata — reajuste)");
                changed = true;
            }

            // 6b. Re-checa cada campo contra todas as regras considerando os outros como "usados"
            var fields = hasLunch
                ? new[] { ("Entrada", entry), ("Saída almoço", lunchOut), ("Retorno almoço", lunchReturn), ("Saída", exit) }
                : new[] { ("Entrada", entry), ("Saída", exit) };

            for (var i = 0; i < fields.Length; i++)
            {
                var (label, value) = fields[i];
                // Conjunto de "outros" horários para verificar duplicados/minutos iguais
                var others = fields.Where((_, idx) => idx != i).Select(f => f.Item2).ToList();
                var adjustedValue = AdjustAgainstAll(date, value, others);
                if (adjustedValue != value)
                {
                    adjustments.Add($"{label}: {value} → {adjustedValue} (convergência)");
                    fields[i] = (label, adjustedValue);
                    changed = true;
                }
            }

            if (changed)
            {
                entry = fields[0].Item2;
                if (hasLunch)
                {
                    lunchOut = fields[1].Item2;
                    lunchReturn = fields[2].Item2;
                    exit = fields[3].Item2;
                }
                else
                {
                    exit = fields[1].Item2;
                }
            }
            else
            {
                break;
            }
        }

        return new AdjustedRecord
        {
            Date = date,
            Entry = entry,
            LunchOut = lunchOut,
            LunchReturn = lunchReturn,
            Exit = exit,
            Adjustments = adjustments
        };
    }

    /// <summary>
    /// Completa horários faltantes APENAS pelo complemento natural do par
    /// (lunch ↔ entrada/saída). NÃO inventa um dia inteiro a partir de uma
    /// jornada-padrão — se o sheet só trouxer um par e o outro estiver vazio,
    /// quem completa o resto é a etapa de "complete partial date" da automação,
    /// usando os horários reais que existem no arquivo de origem.
    ///
    /// Casos cobertos:
    /// - Almoço com apenas um lado preenchido: completa o par (LunchOut↔LunchReturn).
    /// - Falta Entrada mas há Saída-almoço: Entrada = LunchOut − 4h.
    /// - Falta Saída mas há Retorno-almoço: Saída = LunchReturn + 4h.
    /// </summary>
    private static (string entry, string lunchOut, string lunchReturn, string exit, List<string> log)
        InferMissingTimes(string entry, string lunchOut, string lunchReturn, string exit)
    {
        const int LunchDefaultMin = 65; // > 60 para não bater no BlockExactOneHourLunch
        const int MorningMin = 4 * 60;  // 4h antes do almoço
        const int AfternoonMin = 4 * 60; // 4h após o almoço

        var log = new List<string>();
        bool hasE = !string.IsNullOrWhiteSpace(entry);
        bool hasLo = !string.IsNullOrWhiteSpace(lunchOut);
        bool hasLr = !string.IsNullOrWhiteSpace(lunchReturn);
        bool hasX = !string.IsNullOrWhiteSpace(exit);

        // Completa par do almoço quando apenas um lado está preenchido
        if (hasLo && !hasLr)
        {
            lunchReturn = AddMinutes(lunchOut, LunchDefaultMin);
            log.Add($"Retorno almoço inferido: {lunchReturn} (= Saída almoço + {LunchDefaultMin}min)");
            hasLr = true;
        }
        else if (!hasLo && hasLr)
        {
            lunchOut = AddMinutes(lunchReturn, -LunchDefaultMin);
            log.Add($"Saída almoço inferida: {lunchOut} (= Retorno almoço − {LunchDefaultMin}min)");
            hasLo = true;
        }

        // Inferências envolvendo Entrada/Saída — só quando há almoço para "ancorar".
        // Nunca derivamos um dia inteiro de um único ponto (E ou X), pois isso seria
        // um chute baseado em jornada-padrão e não nos dados reais do arquivo.
        if (!hasE && hasLo)
        {
            entry = AddMinutes(lunchOut, -MorningMin);
            log.Add($"Entrada inferida: {entry} (= Saída almoço − 4h)");
        }

        if (!hasX && hasLr)
        {
            exit = AddMinutes(lunchReturn, AfternoonMin);
            log.Add($"Saída inferida: {exit} (= Retorno almoço + 4h)");
        }

        return (entry, lunchOut, lunchReturn, exit, log);
    }


    private string AdjustAgainstAll(string date, string value, IReadOnlyList<string> others)
    {
        var current = value;
        for (var tries = 0; tries < 120; tries++)
        {
            if (_rules.BlockRoundTimes && IsRound(current)) { current = AddMinutes(current, 1); continue; }
            if (_rules.BlockDuplicateTimes && IsHistoricalDuplicate(date, current)) { current = AddMinutes(current, 1); continue; }
            if (_rules.BlockDuplicateTimes && others.Contains(current)) { current = AddMinutes(current, 1); continue; }
            if (_rules.BlockSameMinutes && HasSameMinutesInDay(current, others.ToList())) { current = AddMinutes(current, 1); continue; }
            return current;
        }
        return current;
    }

    private (string adjusted, List<string> log) AdjustFull(string date, string original, List<string> usedInRecord, string field)
    {
        var log = new List<string>();
        var current = original;
        var firstReason = string.Empty;
        var tries = 0;
        const int maxTries = 60;

        while (tries < maxTries)
        {
            string? reason = null;
            if (_rules.BlockRoundTimes && IsRound(current)) reason = "horário redondo";
            else if (_rules.BlockDuplicateTimes && IsHistoricalDuplicate(date, current)) reason = "duplicado histórico";
            else if (_rules.BlockDuplicateTimes && usedInRecord.Contains(current)) reason = "duplicado no registro";
            else if (_rules.BlockSameMinutes && HasSameMinutesInDay(current, usedInRecord)) reason = "minutos iguais no dia";

            if (reason is null) break;

            if (tries == 0) firstReason = reason;
            current = AddMinutes(current, 1);
            tries++;
        }

        if (current != original)
            log.Add($"{field}: {original} → {current} ({firstReason})");

        return (current, log);
    }

    /// <summary>
    /// Retorna true se algum horário do mesmo dia (já usado neste registro)
    /// possui o mesmo valor de minuto que o candidato.
    /// Ex.: 09:58 ↔ 12:58 tem mesmo minuto :58.
    /// </summary>
    private static bool HasSameMinutesInDay(string candidate, List<string> usedInRecord)
    {
        if (!TryParse(candidate, out var c)) return false;
        var cm = c.Minute;
        foreach (var t in usedInRecord)
        {
            if (TryParse(t, out var dt) && dt.Minute == cm) return true;
        }
        return false;
    }

    private bool IsHistoricalDuplicate(string date, string time)
    {
        if (!_rules.BlockDuplicateTimes) return false;
        if (!DateTime.TryParseExact(date, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var currentDate))
            return false;

        foreach (var (storedDate, times) in _usedTimes)
        {
            if (!DateTime.TryParseExact(storedDate, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var registered))
                continue;
            var dayDiff = (currentDate - registered).Days;
            if (dayDiff > 0 && dayDiff <= _rules.DaysToCheckDuplicates && times.Contains(time))
                return true;
        }
        return false;
    }

    private static bool IsRound(string time)
    {
        var parts = time.Split(':');
        return parts.Length == 2 && int.TryParse(parts[1], out var m) && m == 0;
    }

    private static int LunchMinutes(string lunchOut, string lunchReturn)
        => DiffMinutes(lunchOut, lunchReturn);

    private static int WorkedMinutes(string entry, string lunchOut, string lunchReturn, string exit)
    {
        if (!string.IsNullOrWhiteSpace(lunchOut) && !string.IsNullOrWhiteSpace(lunchReturn))
        {
            return DiffMinutes(entry, lunchOut) + DiffMinutes(lunchReturn, exit);
        }
        return DiffMinutes(entry, exit);
    }

    private static int DiffMinutes(string from, string to)
    {
        if (!TryParse(from, out var f) || !TryParse(to, out var t)) return 0;
        return (int)(t - f).TotalMinutes;
    }

    private static string AddMinutes(string time, int minutes)
    {
        if (!TryParse(time, out var t)) return time;
        return t.AddMinutes(minutes).ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    private static bool TryParse(string time, out DateTime value)
        => DateTime.TryParseExact(time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
}
