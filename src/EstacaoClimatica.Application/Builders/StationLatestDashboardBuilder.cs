using System.Text.Json;
using EstacaoClimatica.Application.Configuration;
using EstacaoClimatica.Application.Utilities;
using EstacaoClimatica.Domain;

namespace EstacaoClimatica.Application.Builders;

internal static class MontadorUltimasMedicoesEstacao
{
    public static StationLatestDashboard Montar(JsonElement? quarto, JsonElement? sala, JsonElement? aquario, DateTime? agora = null) =>
        new(
            ObterUltimaMedicao(sala, CamposFirebase.Sala.Temperatura, agora),
            ObterUltimaMedicao(sala, CamposFirebase.Sala.Umidade, agora),
            ObterUltimaMedicao(quarto, CamposFirebase.Quarto.Temperatura, agora),
            ObterUltimaMedicao(quarto, CamposFirebase.Quarto.Umidade, agora),
            ObterUltimaMedicao(aquario, CamposFirebase.Aquario.Temperatura, agora));

    private static LatestMetricMeasurement? ObterUltimaMedicao(JsonElement? raiz, string chave, DateTime? agora)
    {
        var referencia = agora ?? RelogioClimatico.Agora;
        foreach (var registro in LeitorRegistrosFirebase.EnumerarRegistros(raiz, maisRecentesPrimeiro: true))
        {
            if (!LeitorRegistrosFirebase.LeituraNoPassadoValido(registro.Data, registro.Horario, referencia)) continue;
            var valor = NormalizadorMedicoesAquario.NormalizarValorMedicao(chave, LeitorNumerosJson.ObterNumero(registro.Item, chave));
            if (valor is null) continue;

            var dadosDia = LeitorRegistrosFirebase.FiltrarDataSelecionada(raiz, registro.Data);
            return new LatestMetricMeasurement(
                valor.Value,
                registro.Data.Replace('-', '/'),
                LeitorRegistrosFirebase.FormatarRotuloHora(registro.Horario),
                MontadorQualidadeDados.AnalisarSerie(dadosDia, chave, referencia).Qualidade);
        }

        return null;
    }
}
