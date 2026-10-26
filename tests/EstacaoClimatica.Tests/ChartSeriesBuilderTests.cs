using System.Text.Json;
using EstacaoClimatica.Application.Builders;
using EstacaoClimatica.Application.Models;

namespace EstacaoClimatica.Tests;

public sealed class TestesMontadorSeriesGraficos
{
    private static readonly DefinicaoGrafico Grafico = new("temperatura", "Temperatura", "temperatura", "°C", "#38bdf8");

    [Fact]
    public void Montar_DeveIncluirInstanteIsoEPreservarViradaDeDia()
    {
        var dados = CriarJson("""
            {
              "02-06-2026": { "23-45": { "a": { "temperatura": 19.5 } } },
              "03-06-2026": { "00-15-30": { "a": { "temperatura": 20.0 } } }
            }
            """);

        var serie = MontadorSeriesGraficos.Montar(dados, Grafico);

        Assert.Equal(new[] { "2026-06-02T23:45:00", "2026-06-03T00:15:30" }, serie.Points.Select(ponto => ponto.Instante));
    }

    [Fact]
    public void Montar_DeveManterInstanteQuandoValorForNulo()
    {
        var dados = CriarJson("""{ "03-06-2026": { "08-30": { "a": { "temperatura": null } } } }""");

        var ponto = Assert.Single(MontadorSeriesGraficos.Montar(dados, Grafico).Points);

        Assert.Null(ponto.Value);
        Assert.Equal("2026-06-03T08:30:00", ponto.Instante);
    }

    [Fact]
    public void Montar_DeveDescartarDataOuHoraInvalida()
    {
        var dados = CriarJson("""
            {
              "data-invalida": { "08-30": { "a": { "temperatura": 20 } } },
              "03-06-2026": { "25-90": { "a": { "temperatura": 21 } } }
            }
            """);

        var serie = MontadorSeriesGraficos.Montar(dados, Grafico);

        Assert.Empty(serie.Points);
    }

    private static JsonElement CriarJson(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
