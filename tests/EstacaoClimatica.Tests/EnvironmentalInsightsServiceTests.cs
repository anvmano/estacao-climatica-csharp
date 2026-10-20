using EstacaoClimatica.Application.Services;
using EstacaoClimatica.Domain;

namespace EstacaoClimatica.Tests;

public sealed class TestesServicoAnalisesAmbientais
{
    private readonly ServicoAnalisesAmbientais _servico = new();

    [Fact]
    public void CalcularPontoOrvalho_DeveUsarEquacaoDeMagnusEPreservarAusencia()
    {
        Assert.Equal(15.12, _servico.CalcularPontoOrvalho(22, 65)!.Value, 2);
        Assert.Null(_servico.CalcularPontoOrvalho(null, 65));
        Assert.Null(_servico.CalcularPontoOrvalho(22, 0));
    }

    [Theory]
    [InlineData(22, 50, "Risco baixo")]
    [InlineData(22, 65, "Monitorar")]
    [InlineData(22, 72, "Atenção")]
    [InlineData(22, 82, "Risco alto")]
    public void AvaliarRiscoMofo_DeveClassificarUmidadeEMargem(double temperatura, double umidade, string esperado)
    {
        var resultado = _servico.AvaliarRiscoMofo(temperatura, umidade);

        Assert.Equal(esperado, resultado.Rotulo);
        Assert.NotNull(resultado.PontoOrvalho);
    }

    [Theory]
    [InlineData(2.9, "Baixo")]
    [InlineData(3, "Moderado")]
    [InlineData(6, "Alto")]
    [InlineData(8, "Muito alto")]
    [InlineData(11, "Extremo")]
    public void AnalisarIndiceUv_DeveRespeitarFaixas(double valor, string esperado)
    {
        Assert.Equal(esperado, _servico.AnalisarIndiceUv(valor).Rotulo);
    }

    [Fact]
    public void AnalisarIndiceUv_DeveIdentificarFallbackDaMaximaENaoInventarZero()
    {
        var alternativa = _servico.AnalisarIndiceUv(null, 7.2);
        var ausente = _servico.AnalisarIndiceUv(null, null);

        Assert.True(alternativa.UsaMaximoComoFallback);
        Assert.Equal(7.2, alternativa.Valor);
        Assert.Equal("Indisponível", ausente.Rotulo);
        Assert.Null(ausente.Valor);
    }

    [Fact]
    public void ResumirChuva_DeveAnalisarSomenteProximasSeisHoras()
    {
        var previsao = new[]
        {
            Previsao("2026-06-03T12:00", 20, 0),
            Previsao("2026-06-03T14:00", 70, 3.2),
            Previsao("2026-06-03T17:00", 80, 1.1),
            Previsao("2026-06-03T20:00", 100, 20)
        };

        var resultado = _servico.ResumirChuva(previsao, "2026-06-03T12:00");

        Assert.Equal("Chuva moderada", resultado.Rotulo);
        Assert.Equal(80, resultado.Probabilidade);
        Assert.Equal(3.2, resultado.IntensidadeMaxima);
        Assert.Equal(4.3, resultado.Acumulado!.Value, 2);
        Assert.Equal("14:00", resultado.HorarioPico);
    }

    [Fact]
    public void ResumirChuva_SemPrevisaoDeveRetornarNulos()
    {
        var resultado = _servico.ResumirChuva([], "2026-06-03T12:00");

        Assert.Equal("Indisponível", resultado.Rotulo);
        Assert.Null(resultado.Probabilidade);
        Assert.Null(resultado.IntensidadeMaxima);
        Assert.Null(resultado.Acumulado);
    }

    [Fact]
    public void RecomendarVentilacao_DeveConsiderarNecessidadeInternaELimitacaoExterna()
    {
        var risco = _servico.AvaliarRiscoMofo(28, 75);
        var interna = _servico.RecomendarVentilacaoInterna(28, 75, 120, risco);
        var favoravel = _servico.RecomendarVentilacaoExterna(
            new PublicCurrentWeather(23, 22, 55, 1015, RajadaVento: 10), 35,
            new RainForecastInsight("Sem chuva relevante", "favoravel", "", 5, 0, 0, 6, "12:00"));
        var limitada = _servico.RecomendarVentilacaoExterna(
            new PublicCurrentWeather(23, 22, 55, 1015, RajadaVento: 45), 35,
            new RainForecastInsight("Sem chuva relevante", "favoravel", "", 5, 0, 0, 6, "12:00"));

        Assert.Equal("Renovação recomendada", interna.Rotulo);
        Assert.Equal("Ventile agora", _servico.RecomendarVentilacaoCombinada(interna, favoravel).Rotulo);
        Assert.Equal("Evite por enquanto", _servico.RecomendarVentilacaoCombinada(interna, limitada).Rotulo);
    }

    private static PublicShortTermForecast Previsao(string horario, double? probabilidade, double? precipitacao) =>
        new(horario, probabilidade, precipitacao, null, null, null, null, null, null, null);
}
