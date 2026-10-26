using System.Globalization;
using System.Text.Json;
using EstacaoClimatica.Application.Models;
using EstacaoClimatica.Application.Utilities;
using EstacaoClimatica.Domain;

namespace EstacaoClimatica.Application.Builders;

internal static class MontadorMapaCalor
{
    public static AdvancedClimateDashboard Montar(JsonElement? raiz, string dataSelecionada, string chaveMetrica)
    {
        // Heatmaps usam a raiz completa para calcular médias do mês selecionado, não só o dia aberto.
        var registros = LeitorRegistrosFirebase.EnumerarRegistros(raiz)
            .Select(registro =>
            {
                var valor = LeitorNumerosJson.ObterNumero(registro.Item, chaveMetrica);
                var data = DataFirebase.InterpretarDataFirebase(registro.Data);
                var hora = LeitorRegistrosFirebase.ConverterHora(registro.Horario);
                return valor is null || data is null || hora is null
                    ? null
                    : new RegistroClimatico(registro.Data, data.Value, hora.Value.Hour, valor.Value);
            })
            .OfType<RegistroClimatico>()
            .Where(registro => DataFirebase.EhMesmoMes(registro.Data, dataSelecionada))
            .ToList();

        // Três leituras complementares: calendário mensal, intensidade por hora do dia e padrão semanal.
        var selecionada = DataFirebase.InterpretarDataFirebase(dataSelecionada) ?? RelogioClimatico.Hoje;
        var mensal = MontarMensal(registros, selecionada);
        var porHora = MontarPorHora(registros, dataSelecionada);
        var semanal = MontarSemanal(registros, selecionada);

        return new AdvancedClimateDashboard(mensal, porHora, semanal);
    }

    private static IReadOnlyList<HeatmapCell> MontarMensal(IReadOnlyList<RegistroClimatico> registros, DateTime dataSelecionada)
    {
        // Dias sem leitura continuam no calendário com valor nulo para preservar a grade do mês.
        var dias = DateTime.DaysInMonth(dataSelecionada.Year, dataSelecionada.Month);
        var porDia = registros.GroupBy(registro => registro.Data.Day).ToDictionary(grupo => grupo.Key, grupo => (double?)grupo.Average(registro => registro.Valor));

        return Enumerable.Range(1, dias)
            .Select(dia => new HeatmapCell(dia, dia.ToString(CultureInfo.InvariantCulture), porDia.GetValueOrDefault(dia)))
            .ToList();
    }

    private static IReadOnlyList<HeatmapCell> MontarPorHora(IReadOnlyList<RegistroClimatico> registros, string dataSelecionada)
    {
        // O heatmap horário considera somente a data selecionada.
        var porHoraDoDia = registros
            .Where(registro => registro.DataFirebase == dataSelecionada)
            .GroupBy(registro => registro.Hora)
            .ToDictionary(grupo => grupo.Key, grupo => (double?)grupo.Average(registro => registro.Valor));

        return Enumerable.Range(0, 24)
            .Select(hora => new HeatmapCell(hora, $"{hora:00}h", porHoraDoDia.GetValueOrDefault(hora)))
            .ToList();
    }

    private static IReadOnlyList<WeeklyHeatmapCell> MontarSemanal(IReadOnlyList<RegistroClimatico> registros, DateTime dataSelecionada)
    {
        // O projeto Web mostra apenas a semana corrente: domingo da semana selecionada até o dia aberto.
        var inicioSemana = dataSelecionada.Date.AddDays(-(int)dataSelecionada.DayOfWeek);
        var fimSelecionado = dataSelecionada.Date;

        var semanal = registros
            .Where(registro => registro.Data.Date >= inicioSemana && registro.Data.Date <= fimSelecionado)
            .GroupBy(registro => ((int)registro.Data.DayOfWeek, registro.Hora))
            .ToDictionary(grupo => grupo.Key, grupo => (double?)grupo.Average(registro => registro.Valor));

        return Enumerable.Range(0, 7)
            .SelectMany(diaSemana => Enumerable.Range(0, 24)
                .Select(hora => new WeeklyHeatmapCell(diaSemana, hora, semanal.GetValueOrDefault((diaSemana, hora)))))
            .ToList();
    }
}
