using System.Text;
using System.Text.Json;
using EstacaoClimatica.Application.Abstractions;
using EstacaoClimatica.Application.Services;
using EstacaoClimatica.Domain;

namespace EstacaoClimatica.Tests;

public sealed class TestesServicoConsultasClimaticas
{
    [Fact]
    public void Ambientes_DeveListarTresAmbientesETodasAsMetricas()
    {
        var servico = CriarServico();

        var ambientes = servico.ObterAmbientes();

        Assert.Equal(["sala", "quarto", "aquario"], ambientes.Select(item => item.Key));
        Assert.Equal(10, ambientes[0].Metrics.Count);
        Assert.Contains(ambientes[2].Metrics, item => item.Key == "TDS" && item.Unit == "ppm");
    }

    [Fact]
    public async Task UltimaMedicao_DeveOrdenarEAplicarEscalaDoAquario()
    {
        var servico = CriarServico();

        var resposta = await servico.ObterUltimaMedicaoAsync("aquário", CancellationToken.None);

        Assert.Equal("2026-06-03T09:30:00", resposta.Measurement.Timestamp);
        Assert.Equal(330, resposta.Measurement.Values["TDS"]);
        Assert.Equal(4.2, resposta.Measurement.Values["Turbidez"]);
    }

    [Fact]
    public async Task Medicoes_DeveFiltrarMetricasEPaginarODiaObrigatorio()
    {
        var servico = CriarServico();

        var resposta = await servico.ObterMedicoesAsync(
            "sala",
            "03-06-2026",
            "03-06-2026",
            "temperatura,CO2",
            2,
            1,
            CancellationToken.None);

        Assert.Equal(2, resposta.TotalItems);
        Assert.Equal(2, resposta.TotalPages);
        Assert.Single(resposta.Items);
        Assert.Equal("09:30", resposta.Items[0].Time);
        Assert.Equal(["temperatura", "CO2"], resposta.Metrics.Select(item => item.Key));
    }

    [Theory]
    [InlineData("32-06-2026", "03-06-2026", 1, 100)]
    [InlineData("03-06-2026", "02-06-2026", 1, 100)]
    [InlineData("03-06-2026", "03-06-2026", 0, 100)]
    [InlineData("03-06-2026", "03-06-2026", 1, 501)]
    public async Task Medicoes_DeveValidarDatasEPaginacao(string inicio, string fim, int pagina, int tamanho)
    {
        var servico = CriarServico();

        await Assert.ThrowsAsync<ArgumentException>(() => servico.ObterMedicoesAsync(
            "sala", inicio, fim, null, pagina, tamanho, CancellationToken.None));
    }

    [Fact]
    public async Task Serie_DeveAceitarAliasECompararTresAmbientesNoDiaObrigatorio()
    {
        var servico = CriarServico();

        var resposta = await servico.ObterSerieAsync("temperatura", "03-06-2026", null, CancellationToken.None);

        Assert.Equal("03-06-2026", resposta.Date);
        Assert.Equal(3, resposta.Series.Count);
        Assert.All(resposta.Series, serie => Assert.Equal(2, serie.Points.Count));
        Assert.Equal(26.5, resposta.Series.Single(item => item.Environment == "aquario").Points[1].Value);
    }

    [Fact]
    public async Task ConsultasDeDiaUnico_DevemUsarLeituraDelimitadaSemAlterarPontos()
    {
        var repositorio = new RepositorioConsultasSimulado(CriarDados());
        var servico = new ServicoConsultasClimaticas(repositorio, new DashboardConsultaSimulado(CriarDashboard()));

        var serie = await servico.ObterSerieAsync("temperatura", "03-06-2026", null, CancellationToken.None);
        var qualidade = await servico.ObterQualidadeArAsync("03-06-2026", null, null, CancellationToken.None);
        var intervalo = await servico.ObterQualidadeArAsync(null, "02-06-2026", "03-06-2026", CancellationToken.None);

        Assert.Equal(["03-06-2026", "03-06-2026"], repositorio.DatasSolicitadas);
        Assert.Equal(1, repositorio.LeiturasCompletas);
        Assert.All(serie.Series, item => Assert.Equal(2, item.Points.Count));
        Assert.Equal(2, qualidade.Samples.Count);
        Assert.Equal(3, intervalo.Samples.Count);
    }

    [Fact]
    public async Task Resumo_DevePreservarDashboardDaDataObrigatoria()
    {
        var servico = CriarServico();

        var resposta = await servico.ObterResumoAsync("03-06-2026", CancellationToken.None);

        Assert.Equal("03-06-2026", resposta.Date);
        Assert.Equal(3, resposta.Environments.Count);
        Assert.Equal(160, resposta.Aqi?.Value);
        Assert.Equal(6.5, resposta.Solar?.Sunrise);
    }

    [Fact]
    public async Task QualidadeAr_DeveCalcularUmaAmostraPorRegistroDaSala()
    {
        var servico = CriarServico();

        var resposta = await servico.ObterQualidadeArAsync("03-06-2026", null, null, CancellationToken.None);

        Assert.Equal(2, resposta.Samples.Count);
        Assert.Equal("2026-06-03T09:30:00", resposta.Samples[^1].Timestamp);
        Assert.NotNull(resposta.Latest);
        Assert.Contains(resposta.Samples[^1].SubIndexes, item => item.Label == "CO2");
    }

    [Fact]
    public async Task Solar_DeveCalcularDuracaoDoDia()
    {
        var servico = CriarServico();

        var resposta = await servico.ObterSolarAsync("03-06-2026", CancellationToken.None);

        Assert.Equal(40500, resposta.DaylightSeconds);
    }

    [Fact]
    public async Task Solar_DeveRetornarAusenciaQuandoDashboardNaoTemDia()
    {
        var dashboard = CriarDashboard() with { Solar = CriarDashboard().Solar with { Day = null } };
        var servico = CriarServico(dashboard);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => servico.ObterSolarAsync("03-06-2026", CancellationToken.None));
    }

    [Fact]
    public async Task Alertas_DeveAplicarFaixasDaFonteDaVerdadeEFiltros()
    {
        var servico = CriarServico();

        var todos = await servico.ObterAlertasAsync("03-06-2026", null, null, CancellationToken.None);
        var criticosSala = await servico.ObterAlertasAsync("03-06-2026", "critical", "sala", CancellationToken.None);

        Assert.Contains(todos.Items, item => item.Environment == "sala" && item.Metric == "temperatura");
        Assert.Contains(todos.Items, item => item.Environment == "aquario" && item.Metric == "temperaturaDS18B20");
        Assert.Single(criticosSala.Items);
        Assert.Equal("aqi", criticosSala.Items[0].Metric);
    }

    [Fact]
    public async Task Comparacao_DeveCalcularMediasEDiferencasComDadosBrutos()
    {
        var servico = CriarServico();
        var requisicao = new ComparisonRequest(
            ["02-06-2026", "03-06-2026"],
            ["sala"],
            ["temperatura", "CO2"]);

        var resposta = await servico.CompararAsync(requisicao, CancellationToken.None);

        Assert.Equal(2, resposta.Series.Count);
        var temperatura = resposta.Series.Single(item => item.Metric == "temperatura");
        Assert.Equal(20, temperatura.Samples[0].Average);
        Assert.Equal(29, temperatura.Samples[1].Average);
        Assert.Equal(9, temperatura.AbsoluteChange);
        Assert.Equal(45, temperatura.PercentChange);
    }

    [Fact]
    public async Task Exportacao_DeveGerarJsonDaEstacaoECsvDaTabela()
    {
        var servico = CriarServico();

        var json = await servico.ExportarAsync("json", "03-06-2026", "Tab0", CancellationToken.None);
        var csv = await servico.ExportarAsync("csv", "03-06-2026", "Tab1", CancellationToken.None);

        Assert.EndsWith("estacao-03-06-2026.json", json.FileName);
        Assert.Contains("\"date\": \"03-06-2026\"", Encoding.UTF8.GetString(json.Content));
        Assert.EndsWith("sala-03-06-2026.csv", csv.FileName);
        Assert.Contains("Data;Hora;CO;CO2", Encoding.UTF8.GetString(csv.Content));
    }

    [Fact]
    public async Task ExportacaoCsv_DeveNeutralizarFormulasSemAlterarNumerosNegativos()
    {
        var dashboard = CriarDashboard();
        var sala = dashboard.Sala with
        {
            Table = new TableData(["Hora", "Valor"], [[" =1+1", "-2.5"], ["@SUM(A1)", "+cmd"]])
        };
        var servico = CriarServico(dashboard with { Sala = sala });

        var csv = await servico.ExportarAsync("csv", "03-06-2026", "Tab1", CancellationToken.None);
        var texto = Encoding.UTF8.GetString(csv.Content);

        Assert.Contains("' =1+1;-2.5", texto);
        Assert.Contains("'@SUM(A1);'+cmd", texto);
    }

    [Theory]
    [InlineData("xml", "Tab0")]
    [InlineData("json", "Tab9")]
    public async Task Exportacao_DeveRejeitarFormatoEAbaInvalidos(string formato, string aba)
    {
        var servico = CriarServico();

        await Assert.ThrowsAsync<ArgumentException>(() => servico.ExportarAsync(
            formato, "03-06-2026", aba, CancellationToken.None));
    }

    private static ServicoConsultasClimaticas CriarServico(DashboardResponse? dashboard = null)
    {
        var repositorio = new RepositorioConsultasSimulado(CriarDados());
        return new ServicoConsultasClimaticas(repositorio, new DashboardConsultaSimulado(dashboard ?? CriarDashboard()));
    }

    private static DadosDashboardFirebase CriarDados() => new(
        CriarJson("""
        {
          "02-06-2026": { "12-00": { "0": { "Temperatura": 21, "Sensacao termica": 21.5, "Umidade": 50 } } },
          "03-06-2026": {
            "08-00": { "0": { "Temperatura": 22, "Sensacao termica": 22.5, "Umidade": 51 } },
            "09-30": { "0": { "Temperatura": 24, "Sensacao termica": 24.5, "Umidade": 52 } }
          }
        }
        """),
        null,
        CriarJson("""
        {
          "02-06-2026": { "12-00": { "0": { "temperaturaDS18B20": 25, "PH": 7, "TDS": 3000, "Turbidez": 3500 } } },
          "03-06-2026": {
            "08-00": { "0": { "temperaturaDS18B20": 26, "PH": 7.1, "TDS": 3200, "Turbidez": 4000 } },
            "09-30": { "0": { "temperaturaDS18B20": 26.5, "PH": 7.2, "TDS": 3300, "Turbidez": 4200 } }
          }
        }
        """),
        CriarJson("""
        {
          "02-06-2026": { "12-00": { "0": { "temperatura": 20, "sensacaoTermica": 20.5, "umidade": 50, "pressao": 1015, "CO": 2, "CO2": 600, "Aceton": 0.4, "Alcohol": 0.5, "NH4": 0.6, "Toluen": 0.3 } } },
          "03-06-2026": {
            "08-00": { "0": { "temperatura": 28, "sensacaoTermica": 29, "umidade": 65, "pressao": 1013, "CO": 5, "CO2": 1200, "Aceton": 0.8, "Alcohol": 0.7, "NH4": 0.9, "Toluen": 0.6 } },
            "09-30": { "0": { "temperatura": 30, "sensacaoTermica": 31, "umidade": 70, "pressao": 1012, "CO": 6, "CO2": 1600, "Aceton": 1.2, "Alcohol": 1.1, "NH4": 1.3, "Toluen": 1.4 } }
          }
        }
        """));

    private static DashboardResponse CriarDashboard()
    {
        var sala = Ambiente([
            Metrica("temperatura", "Temperatura", "°C", 29),
            Metrica("sensacaoTermica", "Sensação", "°C", 30),
            Metrica("umidade", "Umidade", "%", 67.5),
            Metrica("pressao", "Pressão", " hPa", 1012.5)
        ], ["Data", "Hora", "CO", "CO2"], [["03/06/2026", "09:30", "6.00ppm", "1600.00ppm"]]);
        var quarto = Ambiente([
            Metrica("Temperatura", "Temperatura", "°C", 23),
            Metrica("Sensacao termica", "Sensação", "°C", 23.5),
            Metrica("Umidade", "Umidade", "%", 51.5)
        ]);
        var aquario = Ambiente([
            Metrica("temperaturaDS18B20", "Temperatura", "°C", 24),
            Metrica("PH", "PH", "", 7.15),
            Metrica("TDS", "TDS", "ppm", 325),
            Metrica("Turbidez", "Turbidez", "NTU", 4.1)
        ]);
        var serie = new ChartSeries("solar", "Solar", "#fff", "", []);
        var aqi = new AqiDashboard(160, "Insalubre", "unhealthy", "Impacto", "CO2", "03/06 09:30", []);
        return new DashboardResponse(
            "03-06-2026",
            quarto,
            sala,
            aquario,
            new SolarDashboard(serie, serie, serie, serie, new SolarDay("03-06-2026", 5.5, 6.5, 12.125, 17.75, 18.75)),
            aqi);
    }

    private static EnvironmentDashboard Ambiente(
        IReadOnlyList<MetricSummary> metricas,
        IReadOnlyList<string>? cabecalhos = null,
        IReadOnlyList<IReadOnlyList<string>>? linhas = null) =>
        new(metricas, [], new TableData(cabecalhos ?? [], linhas ?? []));

    private static MetricSummary Metrica(string chave, string rotulo, string sufixo, double media) =>
        new(chave, rotulo, sufixo, media, media - 1, media + 1, 1, new TrendInfo("Estável", "stable", "→"));

    private static JsonElement CriarJson(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private sealed class RepositorioConsultasSimulado(DadosDashboardFirebase dados) : IRepositorioDadosClimaticos
    {
        public int LeiturasCompletas { get; private set; }
        public List<string?> DatasSolicitadas { get; } = [];
        public Task<DadosDashboardFirebase> ObterDadosDashboardAsync(CancellationToken tokenCancelamento)
        {
            LeiturasCompletas++;
            return Task.FromResult(dados);
        }
        public Task<DadosDashboardFirebase> ObterDadosDashboardAsync(string? dataSelecionada, CancellationToken tokenCancelamento)
        {
            DatasSolicitadas.Add(dataSelecionada);
            return Task.FromResult(dados);
        }
    }

    private sealed class DashboardConsultaSimulado(DashboardResponse resposta) : IServicoDashboardClimatico
    {
        public Task<DashboardResponse> ObterDashboardAsync(string? dataFirebase, CancellationToken tokenCancelamento) =>
            Task.FromResult(resposta with { Date = dataFirebase ?? resposta.Date });
    }
}
