using System.Text;
using EstacaoClimatica.Application.Abstractions;
using EstacaoClimatica.Domain;
using EstacaoClimatica.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace EstacaoClimatica.Tests;

public sealed class TestesControladorConsultasClimaticas
{
    [Fact]
    public void Ambientes_DeveRetornarCatalogo()
    {
        var servico = new ServicoConsultasSimulado();
        var controlador = new ClimateQueryController(servico);

        var resultado = Assert.IsType<OkObjectResult>(controlador.ObterAmbientes());

        Assert.Same(servico.Ambientes, resultado.Value);
    }

    [Fact]
    public async Task UltimaMedicao_DeveEncaminharAmbiente()
    {
        var servico = new ServicoConsultasSimulado();
        var controlador = new ClimateQueryController(servico);

        Assert.IsType<OkObjectResult>(await controlador.ObterUltimaMedicao("sala", CancellationToken.None));

        Assert.Equal("latest:sala", servico.Chamada);
    }

    [Fact]
    public async Task Medicoes_DeveEncaminharFiltrosEPaginacao()
    {
        var servico = new ServicoConsultasSimulado();
        var controlador = new ClimateQueryController(servico);

        Assert.IsType<OkObjectResult>(await controlador.ObterMedicoes(
            "quarto", "03-06-2026", "03-06-2026", "temperatura", 2, 25, CancellationToken.None));

        Assert.Equal("measurements:quarto:03-06-2026:03-06-2026:temperatura:2:25", servico.Chamada);
    }

    [Fact]
    public async Task Serie_DeveEncaminharDataEAmbientes()
    {
        var servico = new ServicoConsultasSimulado();
        var controlador = new ClimateQueryController(servico);

        Assert.IsType<OkObjectResult>(await controlador.ObterSerie(
            "temperatura", "03-06-2026", "sala,quarto", CancellationToken.None));

        Assert.Equal("series:temperatura:03-06-2026:sala,quarto", servico.Chamada);
    }

    [Fact]
    public async Task Resumo_DeveEncaminharDataObrigatoria()
    {
        var servico = new ServicoConsultasSimulado();
        var controlador = new ClimateQueryController(servico);

        Assert.IsType<OkObjectResult>(await controlador.ObterResumo("03-06-2026", CancellationToken.None));

        Assert.Equal("summary:03-06-2026", servico.Chamada);
    }

    [Fact]
    public async Task QualidadeAr_DeveEncaminharDataEIntervalo()
    {
        var servico = new ServicoConsultasSimulado();
        var controlador = new ClimateQueryController(servico);

        Assert.IsType<OkObjectResult>(await controlador.ObterQualidadeAr(
            null, "02-06-2026", "03-06-2026", CancellationToken.None));

        Assert.Equal("aqi::02-06-2026:03-06-2026", servico.Chamada);
    }

    [Fact]
    public async Task Solar_DeveMapearAusenciaParaNotFound()
    {
        var servico = new ServicoConsultasSimulado { Erro = new KeyNotFoundException("Sem dados solares.") };
        var controlador = new ClimateQueryController(servico);

        var resultado = await controlador.ObterSolar("03-06-2026", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(resultado);
    }

    [Fact]
    public async Task Alertas_DeveEncaminharFiltros()
    {
        var servico = new ServicoConsultasSimulado();
        var controlador = new ClimateQueryController(servico);

        Assert.IsType<OkObjectResult>(await controlador.ObterAlertas(
            "03-06-2026", "critical", "sala", CancellationToken.None));

        Assert.Equal("alerts:03-06-2026:critical:sala", servico.Chamada);
    }

    [Fact]
    public async Task Comparacao_DeveEncaminharCorpo()
    {
        var servico = new ServicoConsultasSimulado();
        var controlador = new ClimateQueryController(servico);
        var requisicao = new ComparisonRequest(["02-06-2026", "03-06-2026"]);

        Assert.IsType<OkObjectResult>(await controlador.Comparar(requisicao, CancellationToken.None));

        Assert.Same(requisicao, servico.ComparacaoRecebida);
    }

    [Fact]
    public async Task Exportacao_DeveRetornarArquivoComNomeETipo()
    {
        var servico = new ServicoConsultasSimulado();
        var controlador = new ClimateQueryController(servico);

        var resultado = Assert.IsType<FileContentResult>(await controlador.Exportar(
            "csv", "03-06-2026", "Tab1", CancellationToken.None));

        Assert.Equal("arquivo.csv", resultado.FileDownloadName);
        Assert.Equal("text/csv", resultado.ContentType);
        Assert.Equal("export:csv:03-06-2026:Tab1", servico.Chamada);
    }

    [Fact]
    public async Task ErroDeValidacao_DeveRetornarBadRequest()
    {
        var servico = new ServicoConsultasSimulado { Erro = new ArgumentException("Data inválida.") };
        var controlador = new ClimateQueryController(servico);

        var resultado = await controlador.ObterResumo("2026-06-03", CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(resultado);
    }

    private sealed class ServicoConsultasSimulado : IServicoConsultasClimaticas
    {
        public IReadOnlyList<EnvironmentDescriptor> Ambientes { get; } =
            [new EnvironmentDescriptor("sala", "Sala", [])];
        public string? Chamada { get; private set; }
        public ComparisonRequest? ComparacaoRecebida { get; private set; }
        public Exception? Erro { get; init; }

        public IReadOnlyList<EnvironmentDescriptor> ObterAmbientes() => Ambientes;

        public Task<LatestMeasurementResponse> ObterUltimaMedicaoAsync(string ambiente, CancellationToken tokenCancelamento)
        {
            Chamada = $"latest:{ambiente}";
            return Resultado(new LatestMeasurementResponse(ambiente, ambiente, Ponto()));
        }

        public Task<MeasurementsResponse> ObterMedicoesAsync(string ambiente, string? inicio, string? fim, string? metricas, int pagina, int tamanhoPagina, CancellationToken tokenCancelamento)
        {
            Chamada = $"measurements:{ambiente}:{inicio}:{fim}:{metricas}:{pagina}:{tamanhoPagina}";
            return Resultado(new MeasurementsResponse(ambiente, ambiente, inicio!, fim!, pagina, tamanhoPagina, 1, 1, [], [Ponto()]));
        }

        public Task<MetricSeriesResponse> ObterSerieAsync(string metrica, string? data, string? ambientes, CancellationToken tokenCancelamento)
        {
            Chamada = $"series:{metrica}:{data}:{ambientes}";
            return Resultado(new MetricSeriesResponse(metrica, data!, []));
        }

        public Task<DashboardSummaryResponse> ObterResumoAsync(string? data, CancellationToken tokenCancelamento)
        {
            Chamada = $"summary:{data}";
            return Resultado(new DashboardSummaryResponse(data!, [], null, null));
        }

        public Task<AirQualityResponse> ObterQualidadeArAsync(string? data, string? inicio, string? fim, CancellationToken tokenCancelamento)
        {
            Chamada = $"aqi:{data}:{inicio}:{fim}";
            return Resultado(new AirQualityResponse(inicio!, fim!, null, []));
        }

        public Task<SolarDetailsResponse> ObterSolarAsync(string? data, CancellationToken tokenCancelamento)
        {
            Chamada = $"solar:{data}";
            return Resultado(new SolarDetailsResponse(data!, new SolarDay(data!, 5, 6, 12, 18, 19), 43200));
        }

        public Task<AlertsResponse> ObterAlertasAsync(string? data, string? severidade, string? ambiente, CancellationToken tokenCancelamento)
        {
            Chamada = $"alerts:{data}:{severidade}:{ambiente}";
            return Resultado(new AlertsResponse(data!, 0, []));
        }

        public Task<ComparisonResponse> CompararAsync(ComparisonRequest requisicao, CancellationToken tokenCancelamento)
        {
            Chamada = "comparison";
            ComparacaoRecebida = requisicao;
            return Resultado(new ComparisonResponse(requisicao.Dates, []));
        }

        public Task<ClimateExport> ExportarAsync(string formato, string? data, string? aba, CancellationToken tokenCancelamento)
        {
            Chamada = $"export:{formato}:{data}:{aba}";
            return Resultado(new ClimateExport("arquivo.csv", "text/csv", Encoding.UTF8.GetBytes("a;b")));
        }

        private Task<T> Resultado<T>(T valor) => Erro is null
            ? Task.FromResult(valor)
            : Task.FromException<T>(Erro);

        private static MeasurementPoint Ponto() => new(
            "03-06-2026", "09:00", "2026-06-03T09:00:00", new Dictionary<string, double?>());
    }
}
