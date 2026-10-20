using System.Globalization;
using System.Text.Json;
using EstacaoClimatica.Application.Models;
using EstacaoClimatica.Application.Utilities;
using EstacaoClimatica.Domain;

namespace EstacaoClimatica.Application.Builders;

internal static class MontadorSeriesGraficos
{
    public static ChartSeries Montar(JsonElement? dados, DefinicaoGrafico grafico, JsonElement? dadosQualidade = null)
    {
        // Cada ponto mantém o rótulo de hora do Firebase e o valor pode ser nulo para o frontend exibir lacunas.
        var pontos = LeitorRegistrosFirebase.EnumerarRegistros(dados)
            .Select(registro => new ChartPoint(
                LeitorRegistrosFirebase.FormatarRotuloHora(registro.Horario),
                NormalizadorMedicoesAquario.NormalizarValorMedicao(grafico.Chave, LeitorNumerosJson.ObterNumero(registro.Item, grafico.Chave)),
                MontarInstante(registro.Data, registro.Horario)))
            .ToList();

        return new ChartSeries(
            grafico.Id,
            grafico.Rotulo,
            grafico.Cor,
            grafico.Sufixo,
            pontos,
            MontadorQualidadeDados.AnalisarSerie(dadosQualidade ?? dados, grafico.Chave).Qualidade);
    }

    private static string? MontarInstante(string dataFirebase, string horarioFirebase)
    {
        var data = DataFirebase.InterpretarDataFirebase(dataFirebase);
        var horario = LeitorRegistrosFirebase.ConverterHora(horarioFirebase);
        if (data is null || horario is null) return null;

        try
        {
            return new DateTime(
                    data.Value.Year,
                    data.Value.Month,
                    data.Value.Day,
                    horario.Value.Hour,
                    horario.Value.Minute,
                    horario.Value.Second ?? 0,
                    DateTimeKind.Unspecified)
                .ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
