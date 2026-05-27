using System.Globalization;
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

    private static bool IsSsgReport(List<string?[]> rows)
    {
        for (var i = 0; i < Math.Min(5, rows.Count); i++)
        {
            for (var j = 0; j < Math.Min(3, rows[i].Length); j++)
            {
                var v = rows[i][j] ?? string.Empty;
                if (v.Contains("Punch in", StringComparison.OrdinalIgnoreCase)
                    || v.Contains("Date", StringComparison.OrdinalIgnoreCase)
                    || SsgDateRegex.IsMatch(v))
                    return true;
            }
        }
        return false;
    }

    private static List<PunchRecord> ParseSsgReport(List<string?[]> rows)
    {
        var records = new List<PunchRecord>();
        var i = 0;
        while (i < rows.Count)
        {
            var col0 = rows[i].Length > 0 ? rows[i][0] ?? string.Empty : string.Empty;
            var match = SsgDateRegex.Match(col0);

            if (!match.Success) { i++; continue; }

            var dateStr = match.Groups[2].Value; // dd/MM/yy
            string formattedDate = DateTime.TryParseExact(dateStr, "dd/MM/yy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                : dateStr;

            var in1 = ExtractTime(GetCol(rows[i], 1));
            var out1 = ExtractTime(GetCol(rows[i], 2));
            if (string.IsNullOrEmpty(in1)) { i++; continue; }

            string? in2 = null, out2 = null;
            if (i + 1 < rows.Count)
            {
                var nextCol0 = GetCol(rows[i + 1], 0);
                if (!SsgDateRegex.IsMatch(nextCol0 ?? string.Empty))
                {
                    in2 = ExtractTime(GetCol(rows[i + 1], 1));
                    out2 = ExtractTime(GetCol(rows[i + 1], 2));
                }
            }

            PunchRecord record;
            if (!string.IsNullOrEmpty(in2) && !string.IsNullOrEmpty(out2))
            {
                record = new PunchRecord
                {
                    Date = formattedDate,
                    Entry = in1!,
                    LunchOut = out1!,
                    LunchReturn = in2!,
                    Exit = out2!
                };
            }
            else
            {
                record = new PunchRecord
                {
                    Date = formattedDate,
                    Entry = in1!,
                    Exit = out1 ?? string.Empty
                };
            }

            if (record.IsValid()) records.Add(record);
            i++;
        }
        return records;
    }

    private static List<PunchRecord> ParseStandard(List<string?[]> rows)
    {
        var records = new List<PunchRecord>();
        var headerRow = -1;
        for (var i = 0; i < Math.Min(5, rows.Count); i++)
        {
            var values = rows[i].Select(v => (v ?? string.Empty).ToLowerInvariant()).ToArray();
            if (values.Any(v => v.Contains("data") || v.Contains("date") || v.Contains("dia")))
            {
                headerRow = i;
                break;
            }
        }
        if (headerRow < 0) return records;

        var headers = rows[headerRow].Select(h => (h ?? string.Empty).Trim().ToLowerInvariant()).ToArray();
        int Idx(params string[] candidates)
        {
            for (var k = 0; k < headers.Length; k++)
                foreach (var c in candidates)
                    if (headers[k].Contains(c)) return k;
            return -1;
        }

        var iDate = Idx("data", "date", "dia");
        var iEntry = Idx("entrada", "entry", "punch in", "inicio");
        var iLunchOut = Idx("saida_almoco", "saida almoco", "almoco_saida");
        var iLunchRet = Idx("retorno_almoco", "retorno almoco", "almoco_retorno");
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
