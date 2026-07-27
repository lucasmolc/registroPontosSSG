using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using RegistroPontosSSG.Core.Models;

namespace RegistroPontosSSG.Core.Reading;

/// <summary>
/// Lê arquivos de pontos em formato Excel (.xlsx) ou CSV.
/// Detecta automaticamente:
/// - Relatório SSG (linhas "Mon, 05/01/26" + horários)
/// - Planilha padrão (header: data, entrada, saida_almoco, retorno_almoco, saida)
/// </summary>
public sealed class PunchFileReader
{
    private static readonly Regex SsgDateRegex = new(
        @"^(Mon|Tue|Wed|Thu|Fri|Sat|Sun),\s*(\d{2}/\d{2}/\d{2})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TimeRegex = new(@"(\d{1,2}):(\d{2})", RegexOptions.Compiled);

    public List<PunchRecord> Read(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Arquivo não encontrado: {filePath}");

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".xlsx" or ".xlsm" => ReadExcel(filePath),
            ".csv" => ReadCsv(filePath),
            _ => throw new NotSupportedException($"Formato não suportado: {ext}")
        };
    }

    private List<PunchRecord> ReadExcel(string path)
    {
        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet(1);
        var rows = new List<string?[]>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var lastCol = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (var r = 1; r <= lastRow; r++)
        {
            var row = new string?[lastCol];
            for (var c = 1; c <= lastCol; c++)
            {
                var cell = sheet.Cell(r, c);
                row[c - 1] = cell.IsEmpty() ? null : cell.GetFormattedString();
            }
            rows.Add(row);
        }

        return IsSsgReport(rows) ? ParseSsgReport(rows) : ParseStandard(rows);
    }

    private List<PunchRecord> ReadCsv(string path)
    {
        var lines = File.ReadAllLines(path);
        var rows = lines.Select(l => l.Split(',').Cast<string?>().ToArray()).ToList();
        return IsSsgReport(rows) ? ParseSsgReport(rows) : ParseStandard(rows);
    }

    /// <summary>
    /// Quantas linhas iniciais são inspecionadas em busca do cabeçalho. O relatório
    /// exportado pelo SSG traz 6 linhas de preâmbulo (título, empresa, período,
    /// mês) antes do cabeçalho, então uma janela pequena não encontra nada e o
    /// arquivo era lido como vazio.
    /// </summary>
    private const int HeaderScanRows = 25;

    /// <summary>
    /// Detecta o relatório exportado pelo SSG por marcadores inequívocos: o título
    /// "TIME SHEET REPORT" ou uma linha de data no formato "Wed, 01/07/26".
    /// Não basta a célula conter "Date" nem "Punch in": planilhas padrão também têm
    /// essas colunas e seriam roteadas para o parser errado.
    /// </summary>
    private static bool IsSsgReport(List<string?[]> rows)
    {
        var limite = Math.Min(HeaderScanRows, rows.Count);
        for (var i = 0; i < limite; i++)
        {
            var linhaToda = string.Join(" ", rows[i].Select(v => v ?? string.Empty));

            if (linhaToda.Contains("TIME SHEET REPORT", StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (var celula in rows[i])
                if (SsgDateRegex.IsMatch(celula ?? string.Empty))
                    return true;
        }
        return false;
    }

    /// <summary>
    /// Interpreta o relatório do SSG. Cada dia começa numa linha com a data
    /// ("Wed, 01/07/26") e os pares Entrada/Saída seguintes vêm em linhas sem data:
    ///
    ///   Wed, 01/07/26 | 09:56 | 12:14 | ...
    ///                 | 12:53 | 17:32
    ///
    /// Dias sem marcação trazem "--:--" ou "No time punches registered!" e são ignorados.
    /// </summary>
    private static List<PunchRecord> ParseSsgReport(List<string?[]> rows)
    {
        var records = new List<PunchRecord>();

        for (var i = 0; i < rows.Count; i++)
        {
            var match = SsgDateRegex.Match(GetCol(rows[i], 0) ?? string.Empty);
            if (!match.Success) continue;

            var dateStr = match.Groups[2].Value; // dd/MM/yy
            var formattedDate = DateTime.TryParseExact(dateStr, "dd/MM/yy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                : dateStr;

            // Coleta os pares da linha da data e de TODAS as linhas de continuação
            // (dias com 3+ marcações têm mais de uma linha extra).
            var pares = new List<(string Entrada, string Saida)>();
            var emAberto = new List<string>();
            void ColetarPar(string?[] linha)
            {
                var entrada = ExtractTime(GetCol(linha, 1));
                var saida = ExtractTime(GetCol(linha, 2));
                if (!string.IsNullOrEmpty(entrada) && !string.IsNullOrEmpty(saida))
                    pares.Add((entrada!, saida!));
                else if (!string.IsNullOrEmpty(entrada))
                    emAberto.Add(entrada!); // batida sem saída (turno em andamento)
            }

            ColetarPar(rows[i]);
            var j = i + 1;
            while (j < rows.Count && !SsgDateRegex.IsMatch(GetCol(rows[j], 0) ?? string.Empty))
            {
                ColetarPar(rows[j]);
                j++;
            }

            if (pares.Count == 0) continue; // dia sem marcação

            // PunchRecord representa no máximo dois pares; o excedente e as batidas
            // sem saída vão para a observação, para não desaparecerem silenciosamente.
            var avisos = new List<string>();
            if (pares.Count > 2)
                avisos.Add("pares adicionais nao importados: " +
                    string.Join(", ", pares.Skip(2).Select(p => $"{p.Entrada}-{p.Saida}")));
            if (emAberto.Count > 0)
                avisos.Add("batida sem saida ignorada: " + string.Join(", ", emAberto));
            var observacao = string.Join(" | ", avisos);

            var record = pares.Count >= 2
                ? new PunchRecord
                {
                    Date = formattedDate,
                    Entry = pares[0].Entrada,
                    LunchOut = pares[0].Saida,
                    LunchReturn = pares[1].Entrada,
                    Exit = pares[1].Saida,
                    Notes = observacao
                }
                : new PunchRecord
                {
                    Date = formattedDate,
                    Entry = pares[0].Entrada,
                    Exit = pares[0].Saida,
                    Notes = observacao
                };

            if (record.IsValid()) records.Add(record);
        }

        return records;
    }

    private static List<PunchRecord> ParseStandard(List<string?[]> rows)
    {
        var records = new List<PunchRecord>();
        var headerRow = -1;
        for (var i = 0; i < Math.Min(HeaderScanRows, rows.Count); i++)
        {
            var values = rows[i].Select(v => (v ?? string.Empty).ToLowerInvariant()).ToArray();
            if (values.Any(v => v.Contains("data") || v.Contains("date") || v.Contains("dia")))
            {
                headerRow = i;
                break;
            }
        }
        if (headerRow < 0) return records;

        var headers = rows[headerRow].Select(h => NormalizeHeader(h)).ToArray();

        // Uma coluna só pode ser atribuída a um campo. Sem isso, a busca por "saida"
        // casaria com "saida_almoco" (substring) e o horário de saída do dia era perdido.
        var usados = new HashSet<int>();
        int Idx(params string[] candidates)
        {
            // 1ª passada: nome exato (evita que "saida" case com "saida almoco")
            for (var k = 0; k < headers.Length; k++)
            {
                if (usados.Contains(k) || headers[k].Length == 0) continue;
                if (candidates.Any(c => headers[k] == c)) { usados.Add(k); return k; }
            }
            // 2ª passada: substring, para cabeçalhos como "data do apontamento"
            for (var k = 0; k < headers.Length; k++)
            {
                if (usados.Contains(k) || headers[k].Length == 0) continue;
                if (candidates.Any(c => headers[k].Contains(c))) { usados.Add(k); return k; }
            }
            return -1;
        }

        // Ordem importa: os nomes compostos são resolvidos antes dos simples.
        var iDate = Idx("data", "date", "dia");
        var iLunchOut = Idx("saida almoco", "almoco saida", "saida para almoco", "inicio almoco");
        var iLunchRet = Idx("retorno almoco", "almoco retorno", "volta almoco", "fim almoco");
        var iEntry = Idx("entrada", "entry", "punch in", "inicio");
        var iExit = Idx("saida", "exit", "fim", "punch out");
        var iNotes = Idx("observacao", "obs", "observation", "nota");

        for (var r = headerRow + 1; r < rows.Count; r++)
        {
            var row = rows[r];
            var date = FormatDate(GetCol(row, iDate));
            if (string.IsNullOrWhiteSpace(date)) continue;

            var record = new PunchRecord
            {
                Date = date,
                Entry = ExtractTime(GetCol(row, iEntry)) ?? string.Empty,
                LunchOut = ExtractTime(GetCol(row, iLunchOut)) ?? string.Empty,
                LunchReturn = ExtractTime(GetCol(row, iLunchRet)) ?? string.Empty,
                Exit = ExtractTime(GetCol(row, iExit)) ?? string.Empty,
                Notes = GetCol(row, iNotes) ?? string.Empty
            };
            if (record.IsValid()) records.Add(record);
        }

        return records;
    }

    /// <summary>
    /// Normaliza um cabeçalho para comparação: minúsculas, sem acentos, com '_' e
    /// espaços múltiplos reduzidos a um espaço simples. Assim "Saída_Almoço",
    /// "saida almoco" e "SAIDA_ALMOCO" viram a mesma chave.
    /// </summary>
    private static string NormalizeHeader(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return string.Empty;

        var decomposto = header.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);
        foreach (var ch in decomposto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(ch == '_' || ch == '-' ? ' ' : ch);
        }
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    private static string? GetCol(string?[] row, int idx)
        => idx >= 0 && idx < row.Length ? row[idx] : null;

    private static string? ExtractTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        if (v.Contains("No time punches", StringComparison.OrdinalIgnoreCase)) return null;
        var m = TimeRegex.Match(v);
        if (!m.Success) return null;
        var h = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var min = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        return $"{h:D2}:{min:D2}";
    }

    private static string FormatDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var s = value.Trim();
        // Tenta vários formatos
        string[] formats = { "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "yyyy-MM-dd", "MM/dd/yyyy" };
        if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            || DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
        {
            return d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }
        return s;
    }

    /// <summary>
    /// Detecta se os registros são do mês atual ou do mês passado.
    /// </summary>
    public static string DetectMonth(List<PunchRecord> records)
    {
        if (records.Count == 0) return "mes_atual";
        var today = DateTime.Today;
        var prev = 0;
        var current = 0;
        foreach (var rec in records)
        {
            if (!DateTime.TryParseExact(rec.Date, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                continue;
            if (d.Year == today.Year && d.Month == today.Month) current++;
            else if ((d.Year == today.Year && d.Month == today.Month - 1)
                     || (today.Month == 1 && d.Year == today.Year - 1 && d.Month == 12)) prev++;
        }
        return prev > 0 && prev >= current ? "mes_passado" : "mes_atual";
    }
}
