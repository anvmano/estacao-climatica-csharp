using System.Text.Json;
using EstacaoClimatica.Application.Abstractions;
using EstacaoClimatica.Application.Configuration;
using EstacaoClimatica.Application.Services;

namespace EstacaoClimatica.Tests;

public sealed class TestesServicoDashboardClimatico
{
    [Fact]
    public async Task ObterDashboardAsync_CalculaEstatisticasETabelaDaDataSelecionada()
    {
        // Usa dados controlados para validar média, min/max, delta e ordenação da tabela sem depender do Firebase real.
        var servico = CriarServico();

        var dashboard = await servico.ObterDashboardAsync("03-06-2026", CancellationToken.None);

        var temperatura = dashboard.Quarto.Stats.Single(estatistica => estatistica.Key == "Temperatura");
        Assert.Equal(22, temperatura.Average);
        Assert.Equal(20, temperatura.Min);
        Assert.Equal(24, temperatura.Max);
        Assert.Equal(4, temperatura.Delta);
        Assert.Equal("Subindo", temperatura.Trend.Label);

        Assert.Equal("03/06/2026", dashboard.Quarto.Table.Rows[0][0]);
        Assert.Equal("09:00", dashboard.Quarto.Table.Rows[0][1]);
    }

    [Fact]
    public async Task ObterDashboardAsync_SeparaUltimasMedicoesGlobaisDaDataSelecionada()
    {
        var servico = CriarServico();

        var dashboard = await servico.ObterDashboardAsync("03-06-2026", CancellationToken.None);

        Assert.NotNull(dashboard.Latest);
        Assert.Equal(99, dashboard.Latest!.QuartoTemperatura!.Value);
        Assert.Equal("09/06/2026", dashboard.Latest.QuartoTemperatura.Date);
        Assert.Equal("10:00", dashboard.Latest.QuartoTemperatura.Time);
        Assert.Equal(21, dashboard.Latest.SalaTemperatura!.Value);
        Assert.Equal(26, dashboard.Latest.AquarioTemperatura!.Value);
        Assert.All(dashboard.Quarto.Table.Rows, linha => Assert.Equal("03/06/2026", linha[0]));
    }

    [Fact]
    public async Task ObterDashboardAsync_LeCamposSolaresComVariacoesDeNome()
    {
        // Garante compatibilidade com nomes solares antigos e novos gravados pelos sensores.
        var servico = CriarServico();

        var dashboard = await servico.ObterDashboardAsync("03-06-2026", CancellationToken.None);

        Assert.NotNull(dashboard.Solar.Day);
        Assert.Equal(5.5, dashboard.Solar.Day!.Dawn);
        Assert.Equal(6, dashboard.Solar.Day.Sunrise);
        Assert.Equal(12.25, dashboard.Solar.Day.Zenith);
        Assert.Equal(18.5, dashboard.Solar.Day.Sunset);
        Assert.Equal(19, dashboard.Solar.Day.Dusk);
    }

    [Fact]
    public async Task ObterDashboardAsync_SeparaCicloSelecionadoDoCicloAtualDoHeader()
    {
        var servico = CriarServico(() => new DateTime(2026, 6, 9, 17, 0, 0));

        var dashboard = await servico.ObterDashboardAsync("03-06-2026", CancellationToken.None);

        Assert.Equal("03-06-2026", dashboard.Solar.Day?.Date);
        Assert.Equal("09-06-2026", dashboard.Solar.CurrentDay?.Date);
        Assert.Equal(18.5, dashboard.Solar.Day?.Sunset);
        Assert.Equal(17.75, dashboard.Solar.CurrentDay?.Sunset);
    }

    [Fact]
    public async Task ObterDashboardAsync_RetornaMetricasVaziasQuandoDataNaoTemDados()
    {
        // Uma data inexistente deve preservar o contrato da API, mas sem inventar métricas ou linhas.
        var servico = CriarServicoVazio();

        var dashboard = await servico.ObterDashboardAsync("03-06-2026", CancellationToken.None);

        Assert.All(dashboard.Quarto.Stats, estatistica => Assert.Null(estatistica.Average));
        Assert.All(dashboard.Quarto.Stats, estatistica => Assert.Equal("--", estatistica.Trend.Label));
        Assert.Empty(dashboard.Quarto.Table.Rows);
    }

    [Fact]
    public async Task ObterDashboardAsync_MantemTituloOriginalDoGraficoDePressao()
    {
        var servico = CriarServico();

        var dashboard = await servico.ObterDashboardAsync("03-06-2026", CancellationToken.None);

        Assert.Equal("Pressão (hPa)", dashboard.Sala.Charts.Single(grafico => grafico.Id == "plotsPressaoSala").Label);
    }

    [Fact]
    public async Task ObterDashboardAsync_AplicaUnidadesDoAquario()
    {
        var servico = CriarServico(() => new DateTime(2026, 6, 3, 9, 30, 0));

        var dashboard = await servico.ObterDashboardAsync("03-06-2026", CancellationToken.None);

        Assert.Equal("ppm", dashboard.Aquario.Stats.Single(estatistica => estatistica.Key == "TDS").Suffix);
        Assert.Equal("NTU", dashboard.Aquario.Stats.Single(estatistica => estatistica.Key == "Turbidez").Suffix);
        Assert.Equal("ppm", dashboard.Aquario.Charts.Single(grafico => grafico.Id == "plotsTDS").Suffix);
        Assert.Equal("NTU", dashboard.Aquario.Charts.Single(grafico => grafico.Id == "plotsTurbidez").Suffix);
        Assert.Equal(20, dashboard.Aquario.Stats.Single(estatistica => estatistica.Key == "TDS").Average);
        Assert.Equal(1.2, dashboard.Aquario.Stats.Single(estatistica => estatistica.Key == "Turbidez").Average);
        Assert.Equal(20, dashboard.Aquario.Charts.Single(grafico => grafico.Id == "plotsTDS").Points.Single().Value);
        Assert.Equal(1.2, dashboard.Aquario.Charts.Single(grafico => grafico.Id == "plotsTurbidez").Points.Single().Value);
        Assert.Contains("20.00", dashboard.Aquario.Table.Rows.Single());
        Assert.Contains("1.20", dashboard.Aquario.Table.Rows.Single());
    }

    [Fact]
    public async Task ObterDashboardAsync_IncluiToluenoNaTabelaDaSala()
    {
        var servico = CriarServico();

        var dashboard = await servico.ObterDashboardAsync("03-06-2026", CancellationToken.None);

        Assert.Contains("Tolueno", dashboard.Sala.Table.Headers);
        Assert.Equal("0.40", dashboard.Sala.Table.Rows.Single()[^1]);
    }

    [Fact]
    public async Task ObterDashboardAsync_CalculaAqiEstimadoDaSalaNoBackend()
    {
        var servico = CriarServico();

        var dashboard = await servico.ObterDashboardAsync("03-06-2026", CancellationToken.None);

        Assert.NotNull(dashboard.Aqi);
        Assert.Equal(29, dashboard.Aqi!.Value);
        Assert.Equal("Boa", dashboard.Aqi.Category);
        Assert.Equal("good", dashboard.Aqi.CategoryClass);
        Assert.Equal("CO2", dashboard.Aqi.Dominant);
        Assert.Equal("03/06 08:30", dashboard.Aqi.Timestamp);
        Assert.Contains(dashboard.Aqi.SubIndexes, indice => indice.Label == "Tolueno" && indice.Value == 20);
    }

    [Fact]
    public async Task ObterDashboardAsync_HeatmapSemanal_ConsideraSomenteSemanaAteDataSelecionada()
    {
        var servico = CriarServico();

        var dashboard = await servico.ObterDashboardAsync("03-06-2026", CancellationToken.None);

        var semanal = dashboard.Quarto.Advanced!.WeeklyHeatmap;
        Assert.Equal(10, semanal.Single(celula => celula.Weekday == 2 && celula.Hour == 10).Value);
        Assert.Equal(20, semanal.Single(celula => celula.Weekday == 3 && celula.Hour == 8).Value);
        Assert.Null(semanal.Single(celula => celula.Weekday == 3 && celula.Hour == 10).Value);
    }

    [Fact]
    public async Task ObterDashboardAsync_GraficosUsamJanelaMovelDe24HorasSemAlterarResumoETabela()
    {
        var servico = CriarServico(() => new DateTime(2026, 6, 3, 9, 30, 0));

        var dashboard = await servico.ObterDashboardAsync("03-06-2026", CancellationToken.None);

        var pontosTemperatura = dashboard.Quarto.Charts.Single(grafico => grafico.Id == "plotsTemp").Points;
        Assert.Equal(new[] { "10:00", "08:30", "09:00" }, pontosTemperatura.Select(ponto => ponto.Label));
        Assert.Equal(new double?[] { 10, 20, 24 }, pontosTemperatura.Select(ponto => ponto.Value));
        Assert.Equal(22, dashboard.Quarto.Stats.Single(estatistica => estatistica.Key == "Temperatura").Average);
        Assert.All(dashboard.Quarto.Table.Rows, linha => Assert.Equal("03/06/2026", linha[0]));
    }

    private static ServicoDashboardClimatico CriarServico(Func<DateTime>? obterAgora = null)
    {
        // Monta o serviço real com repository/data source fake para testar a regra da Application isolada de HTTP.
        var opcoes = new OpcoesFirebase
        {
            Paths = new CaminhosFirebase
            {
                Room = "room",
                Solar = "solar",
                Aquarium = "aquarium",
                LivingRoom = "living"
            }
        };

        return obterAgora is null
            ? new ServicoDashboardClimatico(new RepositorioDadosClimaticosSimulado(new FonteDadosFirebaseSimulada(), opcoes), new CoresAplicacao())
            : new ServicoDashboardClimatico(new RepositorioDadosClimaticosSimulado(new FonteDadosFirebaseSimulada(), opcoes), new CoresAplicacao(), obterAgora);
    }

    private static ServicoDashboardClimatico CriarServicoVazio()
    {
        var opcoes = new OpcoesFirebase
        {
            Paths = new CaminhosFirebase { Room = "room", Solar = "solar", Aquarium = "aquarium", LivingRoom = "living" }
        };
        return new ServicoDashboardClimatico(new RepositorioDadosClimaticosSimulado(new FonteDadosFirebaseVazia(), opcoes), new CoresAplicacao());
    }

    private sealed class RepositorioDadosClimaticosSimulado : IRepositorioDadosClimaticos
    {
        private readonly IFonteDadosFirebase _firebase;
        private readonly OpcoesFirebase _opcoes;

        public RepositorioDadosClimaticosSimulado(IFonteDadosFirebase firebase, OpcoesFirebase opcoes)
        {
            _firebase = firebase;
            _opcoes = opcoes;
        }

        public async Task<DadosDashboardFirebase> ObterDadosDashboardAsync(CancellationToken tokenCancelamento)
        {
            var caminhos = _opcoes.Paths;

            // Replica o papel do repository real: juntar os quatro ramos esperados pelo dashboard.
            return new DadosDashboardFirebase(
                await _firebase.ObterCaminhoAsync(caminhos.Room, tokenCancelamento),
                await _firebase.ObterCaminhoAsync(caminhos.Solar, tokenCancelamento),
                await _firebase.ObterCaminhoAsync(caminhos.Aquarium, tokenCancelamento),
                await _firebase.ObterCaminhoAsync(caminhos.LivingRoom, tokenCancelamento));
        }

        public Task<DadosDashboardFirebase> ObterDadosDashboardAsync(string? dataSelecionada, CancellationToken tokenCancelamento)
        {
            return ObterDadosDashboardAsync(tokenCancelamento);
        }
    }

    private sealed class FonteDadosFirebaseSimulada : IFonteDadosFirebase
    {
        private readonly Dictionary<string, string> _dados = new()
        {
            // A estrutura imita o Firebase: data -> hora -> item -> campos de sensor.
            ["room"] = """
            {
              "02-06-2026": {
                "10-00": { "a": { "Temperatura": 10, "Sensacao termica": 11, "Umidade": 50 } }
              },
              "03-06-2026": {
                "08-30": { "a": { "Temperatura": 20, "Sensacao termica": 21, "Umidade": 60 } },
                "09-00": { "a": { "Temperatura": 24, "Sensacao termica": 25, "Umidade": 62 } }
              },
              "09-06-2026": {
                "10-00": { "a": { "Temperatura": 99, "Sensacao termica": 99, "Umidade": 99 } }
              }
            }
            """,
            ["living"] = """
            {
              "03-06-2026": {
                "08-30": { "a": { "temperatura": 21, "sensacaoTermica": 22, "umidade": 55, "pressao": 1012, "CO": 1, "CO2": 400, "Aceton": 0, "Alcohol": 0, "NH4": 0, "Toluen": 0.4 } }
              }
            }
            """,
            ["aquarium"] = """
            {
              "03-06-2026": {
                "08-30": { "a": { "temperaturaDS18B20": 26, "PH": 7, "TDS": 200, "Turbidez": 1200 } }
              }
            }
            """,
            // Mistura de nomes em inglês/português para cobrir compatibilidade com registros históricos.
            ["solar"] = """
            {
              "03-06-2026": {
                "08-30": {
                  "HourAmanhecer": 5,
                  "MinuteAmanhecer": 30,
                  "HoraNascerDoSol": 6,
                  "MinutoNascerDoSol": 0,
                  "HoraZenite": 12,
                  "MinutoZenite": 15,
                  "HourPorDoSol": 18,
                  "MinutePorDoSol": 30,
                  "HoraAnoitecer": 19,
                  "MinutoAnoitecer": 0
                }
              },
              "09-06-2026": {
                "17-00": {
                  "HourAmanhecer": 5,
                  "MinuteAmanhecer": 45,
                  "HoraNascerDoSol": 6,
                  "MinutoNascerDoSol": 15,
                  "HoraZenite": 12,
                  "MinutoZenite": 0,
                  "HourPorDoSol": 17,
                  "MinutePorDoSol": 45,
                  "HoraAnoitecer": 18,
                  "MinutoAnoitecer": 15
                }
              }
            }
            """
        };

        public Task<JsonElement?> ObterCaminhoAsync(string caminho, CancellationToken tokenCancelamento)
        {
            if (!_dados.TryGetValue(caminho, out var json)) return Task.FromResult<JsonElement?>(null);

            using var documento = JsonDocument.Parse(json);
            return Task.FromResult<JsonElement?>(documento.RootElement.Clone());
        }

        public Task<JsonElement?> ObterCaminhoComQueryAsync(string caminho, string parametrosConsulta, CancellationToken tokenCancelamento)
        {
            return Task.FromResult<JsonElement?>(null);
        }
    }

    private sealed class FonteDadosFirebaseVazia : IFonteDadosFirebase
    {
        public Task<JsonElement?> ObterCaminhoAsync(string caminho, CancellationToken tokenCancelamento) =>
            Task.FromResult<JsonElement?>(null);

        public Task<JsonElement?> ObterCaminhoComQueryAsync(string caminho, string parametrosConsulta, CancellationToken tokenCancelamento) =>
            Task.FromResult<JsonElement?>(null);
    }
}
