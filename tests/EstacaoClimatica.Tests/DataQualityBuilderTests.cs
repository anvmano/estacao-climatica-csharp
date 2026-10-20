using System.Text.Json;
using EstacaoClimatica.Application.Builders;
using EstacaoClimatica.Application.Configuration;
using EstacaoClimatica.Application.Models;

namespace EstacaoClimatica.Tests;

public sealed class TestesMontadorQualidadeDados
{
    [Fact]
    public void UmaLeitura_DevePreservarValorMasMarcarCoberturaETendenciaInsuficiente()
    {
        using var documento = JsonDocument.Parse("""
        { "03-06-2026": { "00-00": { "registro": { "Temperatura": 28.06 } } } }
        """);

        var analise = MontadorQualidadeDados.AnalisarSerie(documento.RootElement, CamposFirebase.Quarto.Temperatura);
        var resumo = Assert.Single(MontadorEstatisticas.Montar(documento.RootElement, new[]
        {
            new DefinicaoMetrica(CamposFirebase.Quarto.Temperatura, "Temperatura", "°C")
        }));

        Assert.Equal("incompleta", analise.Qualidade.Level);
        Assert.Equal("parcial", analise.Qualidade.State);
        Assert.Equal(1, analise.Qualidade.ValidReadings);
        Assert.Equal(24, analise.Qualidade.ExpectedReadings);
        Assert.Contains("1/24 leituras", string.Join(' ', analise.Qualidade.Warnings));
        Assert.Equal(28.06, resumo.Average);
        Assert.Null(resumo.Delta);
        Assert.Equal("Dados insuficientes", resumo.Trend.Label);
    }

    [Fact]
    public void PhComFaixaSaltoERepeticao_DeveSerSuspeitoCritico()
    {
        using var documento = CriarDia(hora => new Dictionary<string, double>
        {
            [CamposFirebase.Aquario.Ph] = hora <= 1 ? 7 + hora * 0.07 : hora <= 15 ? 2.38 : 7.44
        });

        var analise = MontadorQualidadeDados.AnalisarSerie(documento.RootElement, CamposFirebase.Aquario.Ph);

        Assert.Equal("critica", analise.Qualidade.Level);
        Assert.Equal("suspeito", analise.Qualidade.State);
        var avisos = string.Join(' ', analise.Qualidade.Warnings);
        Assert.Contains("faixa crítica", avisos, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("salto", avisos, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("repetido", avisos, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TurbidezConstanteEmZero_DeveManterValoresESinalizarSensor()
    {
        using var documento = CriarDia(_ => new Dictionary<string, double> { [CamposFirebase.Aquario.Turbidez] = 0 });

        var analise = MontadorQualidadeDados.AnalisarSerie(documento.RootElement, CamposFirebase.Aquario.Turbidez);
        var tabela = MontadorDadosTabela.Montar(documento.RootElement, new[] { "Data", "Hora", CamposFirebase.Aquario.Turbidez });

        Assert.Equal("suspeita", analise.Qualidade.Level);
        Assert.Equal(24, analise.Qualidade.ValidReadings);
        Assert.Contains("constante em zero", string.Join(' ', analise.Qualidade.Warnings), StringComparison.OrdinalIgnoreCase);
        Assert.All(tabela.Rows, linha => Assert.Equal("0.00", linha[2]));
        Assert.All(tabela.CellQuality!, linha => Assert.Equal("suspeita", linha[2]));
    }

    private static JsonDocument CriarDia(Func<int, Dictionary<string, double>> valores)
    {
        var horarios = Enumerable.Range(0, 24).ToDictionary(
            hora => $"{hora:00}-00",
            hora => new Dictionary<string, Dictionary<string, double>> { ["registro"] = valores(hora) });
        return JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, object> { ["03-06-2026"] = horarios }));
    }
}
