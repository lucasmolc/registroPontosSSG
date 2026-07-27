using System.Globalization;
using RegistroPontosSSG.Core.Models;
using RegistroPontosSSG.Core.Validation;

namespace RegistroPontosSSG.Core.Tests;

/// <summary>
/// Verifica as regras de ajuste automático de horários. As asserções são feitas
/// sobre invariantes (nada redondo, nada duplicado, almoço nunca de 1h exata) em
/// vez de valores exatos, porque as regras interagem entre si e a etapa de
/// convergência pode chegar ao mesmo resultado por caminhos diferentes.
/// </summary>
public sealed class TimeValidatorTests
{
    private static ValidationRules TodasAsRegras() => new()
    {
        BlockRoundTimes = true,
        BlockDuplicateTimes = true,
        BlockExactOneHourLunch = true,
        BlockSameMinutes = true,
        DaysToCheckDuplicates = 5
    };

    private static int ParaMinutos(string hora)
        => (int)DateTime.ParseExact(hora, "HH:mm", CultureInfo.InvariantCulture).TimeOfDay.TotalMinutes;

    private static int MinutosTrabalhados(string entrada, string saidaAlmoco, string retornoAlmoco, string saida)
        => (ParaMinutos(saidaAlmoco) - ParaMinutos(entrada)) + (ParaMinutos(saida) - ParaMinutos(retornoAlmoco));

    [Fact]
    public void HorariosRedondos_SaoAjustados()
    {
        var validador = new TimeValidator(TodasAsRegras());

        var ajustado = validador.Adjust("01/07/2026", "08:00", "12:00", "13:00", "17:00");

        Assert.DoesNotContain(":00", ajustado.Entry[3..]);
        foreach (var hora in new[] { ajustado.Entry, ajustado.LunchOut, ajustado.LunchReturn, ajustado.Exit })
            Assert.NotEqual("00", hora[3..]);
        Assert.True(ajustado.HasAdjustments);
    }

    [Fact]
    public void AlmocoDeUmaHoraExata_DeixaDeSerUmaHora()
    {
        var validador = new TimeValidator(TodasAsRegras());

        var ajustado = validador.Adjust("01/07/2026", "08:07", "12:09", "13:09", "17:11");

        var minutosAlmoco = ParaMinutos(ajustado.LunchReturn) - ParaMinutos(ajustado.LunchOut);
        Assert.NotEqual(60, minutosAlmoco);
    }

    [Fact]
    public void TotalTrabalhado_EPreservadoAposOsAjustes()
    {
        var validador = new TimeValidator(TodasAsRegras());
        var original = MinutosTrabalhados("08:00", "12:00", "13:00", "17:00");

        var ajustado = validador.Adjust("01/07/2026", "08:00", "12:00", "13:00", "17:00");
        var final = MinutosTrabalhados(ajustado.Entry, ajustado.LunchOut, ajustado.LunchReturn, ajustado.Exit);

        // A compensação atua na saída; a convergência posterior pode deslocar
        // horários em alguns minutos para não violar outras regras.
        Assert.InRange(final, original - 5, original + 5);
    }

    [Fact]
    public void MinutosIguaisNoMesmoDia_SaoEvitados()
    {
        var validador = new TimeValidator(TodasAsRegras());

        // 09:58 e 12:58 compartilham o minuto :58 — o SSG rejeita esse caso.
        var ajustado = validador.Adjust("01/07/2026", "09:58", "12:58", "13:46", "18:42");

        var minutos = new[] { ajustado.Entry, ajustado.LunchOut, ajustado.LunchReturn, ajustado.Exit }
            .Select(h => h[3..])
            .ToList();

        Assert.Equal(minutos.Count, minutos.Distinct().Count());
    }

    [Fact]
    public void HorarioJaUsadoEmDiaProximo_NaoERepetido()
    {
        var validador = new TimeValidator(TodasAsRegras());
        validador.LoadExistingTimes(new Dictionary<string, List<string>>
        {
            ["01/07/2026"] = new() { "08:11", "12:22", "13:33", "17:44" }
        });

        var ajustado = validador.Adjust("02/07/2026", "08:11", "12:22", "13:33", "17:44");

        Assert.NotEqual("08:11", ajustado.Entry);
        Assert.NotEqual("12:22", ajustado.LunchOut);
        Assert.NotEqual("13:33", ajustado.LunchReturn);
        Assert.NotEqual("17:44", ajustado.Exit);
    }

    [Fact]
    public void HorarioUsadoForaDaJanelaDeDias_PodeRepetir()
    {
        var regras = TodasAsRegras();
        regras.DaysToCheckDuplicates = 2;
        var validador = new TimeValidator(regras);
        validador.LoadExistingTimes(new Dictionary<string, List<string>>
        {
            ["01/07/2026"] = new() { "08:11" }
        });

        // 10/07 está muito além da janela de 2 dias, então 08:11 é reutilizável.
        var ajustado = validador.Adjust("10/07/2026", "08:11", "12:22", "13:33", "17:44");

        Assert.Equal("08:11", ajustado.Entry);
    }

    [Fact]
    public void RegrasDesligadas_NaoAlteramOsHorarios()
    {
        var validador = new TimeValidator(new ValidationRules
        {
            BlockRoundTimes = false,
            BlockDuplicateTimes = false,
            BlockExactOneHourLunch = false,
            BlockSameMinutes = false
        });

        var ajustado = validador.Adjust("01/07/2026", "08:00", "12:00", "13:00", "17:00");

        Assert.Equal("08:00", ajustado.Entry);
        Assert.Equal("12:00", ajustado.LunchOut);
        Assert.Equal("13:00", ajustado.LunchReturn);
        Assert.Equal("17:00", ajustado.Exit);
        Assert.False(ajustado.HasAdjustments);
    }

    [Fact]
    public void RegistroSemAlmoco_MantemApenasEntradaESaida()
    {
        var validador = new TimeValidator(TodasAsRegras());

        var ajustado = validador.Adjust("01/07/2026", "08:03", string.Empty, string.Empty, "12:07");

        Assert.Equal("08:03", ajustado.Entry);
        Assert.Equal("12:07", ajustado.Exit);
        Assert.True(string.IsNullOrEmpty(ajustado.LunchOut));
        Assert.True(string.IsNullOrEmpty(ajustado.LunchReturn));
    }
}
