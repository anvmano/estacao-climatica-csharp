using System.Text.Json;
using EstacaoClimatica.Application.Configuration;
using EstacaoClimatica.Application.Utilities;
using EstacaoClimatica.Domain;

namespace EstacaoClimatica.Application.Builders;

internal static class MontadorUltimasMedicoesEstacao
{
    public static StationLatestDashboard Montar(JsonElement? quarto, JsonElement? sala, JsonElement? aquario) =>
        new(
            ObterUltimaMedicao(sala, CamposFirebase.Sala.Temperatura),
            ObterUltimaMedicao(sala, CamposFirebase.Sala.Umidade),
            ObterUltimaMedicao(quarto, CamposFirebase.Quarto.Temperatura),
            ObterUltimaMedicao(quarto, CamposFirebase.Quarto.Umidade),
            ObterUltimaMedicao(aquario, CamposFirebase.Aquario.Temperatura));

    private static LatestMetricMeasurement? ObterUltimaMedicao(JsonElement? raiz, string chave)
    {
        foreach (var registro in LeitorRegistrosFirebase.EnumerarRegistros(raiz, maisRecentesPrimeiro: true))
        {
            var valor = NormalizadorMedicoesAquario.NormalizarValorMedicao(chave, LeitorNumerosJson.ObterNumero(registro.Item, chave));
            if (valor is null) continue;

            var dadosHoje = LeitorRegistrosFirebase.FiltrarDataSelecionada(raiz, DateTime.Now.ToString("dd-MM-yyyy"));
            return new LatestMetricMeasurement(
                valor.Value,
                registro.Data.Replace('-', '/'),
                LeitorRegistrosFirebase.FormatarRotuloHora(registro.Horario),
                MontadorQualidadeDados.AnalisarSerie(dadosHoje, chave).Qualidade);
        }

        return null;
    }
}
