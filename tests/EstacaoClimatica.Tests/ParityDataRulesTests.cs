using System.Text.Json;
using EstacaoClimatica.Application.Builders;
using EstacaoClimatica.Application.Utilities;

namespace EstacaoClimatica.Tests;

public sealed class TestesRegrasParidade
{
    [Fact]
    public async Task Relogio_DeveRespeitarFusoDaConsultaSemVazarEntreEscopos()
    {
        var fuso = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        var instante = new DateTimeOffset(2026, 6, 4, 1, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTime(2026, 6, 3, 22, 0, 0), RelogioClimatico.Converter(instante, fuso));
        using (RelogioClimatico.UsarFuso(TimeZoneInfo.Utc))
        {
            await Task.Run(() =>
            {
                using (RelogioClimatico.UsarFuso(fuso))
                    Assert.InRange((RelogioClimatico.Agora - DateTime.UtcNow).TotalHours, -3.01, -2.99);
            });
            Assert.InRange(Math.Abs((RelogioClimatico.Agora - DateTime.UtcNow).TotalSeconds), 0, 2);
        }
    }

    [Theory]
    [InlineData("Temperatura", -127, null)]
    [InlineData("temperatura", 85, null)]
    [InlineData("temperaturaDS18B20", 85, null)]
    [InlineData("temperaturaDS18B20", 0, 0d)]
    [InlineData("umidade", 85, 85d)]
    [InlineData("TDS", 100, 10d)]
    [InlineData("Turbidez", 1000, 1d)]
    public void Normalizacao_DeveRespeitarSchemaDoOriginal(string campo, double entrada, double? esperado) =>
        Assert.Equal(esperado, NormalizadorMedicoesAquario.NormalizarValorMedicao(campo, entrada));

    [Theory]
    [InlineData("99-00")]
    [InlineData("12-60")]
    [InlineData("12-x")]
    [InlineData("12--30")]
    [InlineData("12-00-60")]
    [InlineData("-1-00")]
    public void HorarioInvalido_DeveSerDescartadoSemExcecao(string horario)
    {
        var dados = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["03-06-2026"] = new Dictionary<string, object> { [horario] = new { registro = new { Temperatura = 25 } } }
        });
        Assert.Null(LeitorRegistrosFirebase.ConverterHora(horario));
        Assert.Empty(LeitorRegistrosFirebase.EnumerarRegistros(dados));
        Assert.Null(LeitorRegistrosFirebase.FiltrarDadosPorJanelaHoras(dados, "03-06-2026", 24, new(2026, 6, 3, 12, 30, 0)));
    }

    [Fact]
    public void UltimaMedicao_DeveIgnorarFuturoSentinelaEUsarQualidadeDoMesmoDia()
    {
        var dados = JsonSerializer.Deserialize<JsonElement>("""
        {"02-06-2026":{"12-00":{"1":{"temperaturaDS18B20":25}}},
         "03-06-2026":{"12-00":{"1":{"temperaturaDS18B20":85}},"13-00":{"1":{"temperaturaDS18B20":30}}},
         "03-06-2999":{"12-00":{"1":{"temperaturaDS18B20":40}}}}
        """);
        var ultima = MontadorUltimasMedicoesEstacao.Montar(null, null, dados, new(2026, 6, 3, 12, 30, 0)).AquarioTemperatura;
        Assert.NotNull(ultima);
        Assert.Equal(25, ultima.Value);
        Assert.Equal("02/06/2026", ultima.Date);
        Assert.Equal(1, ultima.Quality!.ValidReadings);
        Assert.Equal(24, ultima.Quality.ExpectedReadings);
        Assert.Equal("02-06-2026", ultima.Quality.LatestReading!.Date);
    }
}
