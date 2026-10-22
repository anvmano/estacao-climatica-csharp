using System.Net;
using System.Text.Json;
using EstacaoClimatica.Application.Abstractions;
using EstacaoClimatica.Domain;
using EstacaoClimatica.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace EstacaoClimatica.Tests;

public sealed class TestesControladores
{
    [Fact]
    public async Task Dashboard_DeveEncaminharDataObrigatoriaERetornarOk()
    {
        var servico = new ServicoDashboardSimulado(CriarDashboard());
        var controlador = new DashboardController(servico);

        var resultado = await controlador.ObterDashboard("03-06-2026", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(resultado);
        Assert.Same(servico.Resposta, ok.Value);
        Assert.Equal("03-06-2026", servico.DataRecebida);
    }

    [Theory]
    [InlineData("32-06-2026")]
    [InlineData("2026-06-03")]
    [InlineData("")]
    public async Task Dashboard_DeveRejeitarDataInvalidaSemConsultarServico(string data)
    {
        var servico = new ServicoDashboardSimulado(CriarDashboard());
        var controlador = new DashboardController(servico);

        Assert.IsType<BadRequestObjectResult>(await controlador.ObterDashboard(data, CancellationToken.None));
        Assert.Equal(0, servico.Chamadas);
    }

    [Fact]
    public void Saude_DeveRetornarStatusOk()
    {
        var controlador = new DashboardController(new ServicoDashboardSimulado(CriarDashboard()));

        var resultado = Assert.IsType<OkObjectResult>(controlador.ObterSaude());

        Assert.Equal("ok", resultado.Value!.GetType().GetProperty("status")!.GetValue(resultado.Value));
    }

    [Fact]
    public async Task Chat_DeveRejeitarPerguntaVaziaSemChamarServico()
    {
        var servico = new ServicoAssistenteSimulado(CriarRespostaChat());
        var controlador = new ChatController(servico);

        var resultado = await controlador.Post(new ChatRequest("  ", "Tab0", "03-06-2026"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(resultado.Result);
        Assert.Null(servico.RequisicaoRecebida);
    }

    [Fact]
    public async Task Chat_DeveEncaminharContextoDaDataObrigatoria()
    {
        var resposta = CriarRespostaChat();
        var servico = new ServicoAssistenteSimulado(resposta);
        var controlador = new ChatController(servico);
        var requisicao = new ChatRequest("Temperatura média?", "Tab1", "03-06-2026");

        var resultado = await controlador.Post(requisicao, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(resultado.Result);
        Assert.Same(resposta, ok.Value);
        Assert.Same(requisicao, servico.RequisicaoRecebida);
    }

    [Fact]
    public async Task ClimaPublicoPorCep_DeveEncaminharCepERetornarOk()
    {
        var resposta = CriarClimaPublico();
        var servico = new ServicoClimaPublicoSimulado { Resposta = resposta };
        var controlador = new PublicWeatherController(servico);

        var resultado = await controlador.BuscarPorCep("01001-000", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(resultado.Result);
        Assert.Same(resposta, ok.Value);
        Assert.Equal("01001-000", servico.CepRecebido);
    }

    [Fact]
    public async Task ClimaPublicoPorLocalizacao_DeveEncaminharCoordenadasEPrecisao()
    {
        var servico = new ServicoClimaPublicoSimulado { Resposta = CriarClimaPublico() };
        var controlador = new PublicWeatherController(servico);

        var resultado = await controlador.BuscarPorLocalizacao(-23.55, -46.63, 18.5, CancellationToken.None);

        Assert.IsType<OkObjectResult>(resultado.Result);
        Assert.Equal((-23.55, -46.63, 18.5), servico.LocalizacaoRecebida);
    }

    [Fact]
    public async Task PesquisaPublicaDeCidades_DeveEncaminharTermoERetornarOpcoes()
    {
        var cidade = CriarCidadePublica();
        var servico = new ServicoClimaPublicoSimulado { Cidades = [cidade] };
        var controlador = new PublicWeatherController(servico);

        var resultado = await controlador.PesquisarCidades("Gramado", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(resultado.Result);
        Assert.Same(servico.Cidades, ok.Value);
        Assert.Equal("Gramado", servico.TermoCidadeRecebido);
    }

    [Fact]
    public async Task ClimaPublicoPorCidade_DeveEncaminharCidadeSelecionada()
    {
        var cidade = CriarCidadePublica();
        var servico = new ServicoClimaPublicoSimulado { Resposta = CriarClimaPublico() };
        var controlador = new PublicWeatherController(servico);

        var resultado = await controlador.BuscarPorCidade(cidade, CancellationToken.None);

        Assert.IsType<OkObjectResult>(resultado.Result);
        Assert.Same(cidade, servico.CidadeRecebida);
    }

    [Theory]
    [InlineData("argumento", 400)]
    [InlineData("nao-encontrado", 404)]
    [InlineData("http", 502)]
    [InlineData("json", 502)]
    public async Task ClimaPublico_DeveMapearFalhasParaStatusHttp(string falha, int statusEsperado)
    {
        var servico = new ServicoClimaPublicoSimulado
        {
            Erro = falha switch
            {
                "argumento" => new ArgumentException("Entrada inválida."),
                "nao-encontrado" => new KeyNotFoundException("CEP não encontrado."),
                "http" => new HttpRequestException("Serviço indisponível."),
                _ => new JsonException("Resposta inválida.")
            }
        };
        var controlador = new PublicWeatherController(servico);

        var resultado = await controlador.BuscarPorCep("01001000", CancellationToken.None);

        var objeto = Assert.IsAssignableFrom<ObjectResult>(resultado.Result);
        Assert.Equal(statusEsperado, objeto.StatusCode);
    }

    private static DashboardResponse CriarDashboard()
    {
        var ambiente = new EnvironmentDashboard([], [], new TableData([], []));
        var serie = new ChartSeries("solar", "Solar", "#fff", "", []);
        return new DashboardResponse(
            "03-06-2026",
            ambiente,
            ambiente,
            ambiente,
            new SolarDashboard(serie, serie, serie, serie, new SolarDay("03-06-2026", 5.5, 6.5, 12.125, 17.75, 18.75)),
            null);
    }

    private static CidadePublica CriarCidadePublica() => new(
        3459038,
        "Gramado",
        "Rio Grande do Sul",
        "Gramado",
        "Brasil",
        "BR",
        -29.3788,
        -50.8726,
        "America/Sao_Paulo",
        "Gramado - Rio Grande do Sul");

    private static ChatResponse CriarRespostaChat() => new(
        "Resposta calculada.",
        "local",
        new ChatIntentSummary(["Sala"], ["Temperatura"], "media", "03/06/2026", 2));

    private static PublicWeatherData CriarClimaPublico() => new(
        new PublicWeatherOrigin("cep", "São Paulo - SP", -23.55, -46.63, "01001000"),
        "2026-06-03T12:00",
        new PublicCurrentWeather(21.4, 20.8, 63, 1017.2),
        new PublicHourlySeries([], [], [], [], []),
        new PublicAirQuality(42, [], []),
        new PublicSolarCycle(5.5, 6.5, 12.125, 17.75, 18.75, 40500));

    private sealed class ServicoDashboardSimulado(DashboardResponse resposta) : IServicoDashboardClimatico
    {
        public DashboardResponse Resposta { get; } = resposta;
        public string? DataRecebida { get; private set; }
        public int Chamadas { get; private set; }

        public Task<DashboardResponse> ObterDashboardAsync(string? dataFirebase, CancellationToken tokenCancelamento)
        {
            Chamadas++;
            DataRecebida = dataFirebase;
            return Task.FromResult(Resposta);
        }
    }

    private sealed class ServicoAssistenteSimulado(ChatResponse resposta) : IServicoAssistenteClimatico
    {
        public ChatRequest? RequisicaoRecebida { get; private set; }

        public Task<ChatResponse> ResponderAsync(ChatRequest requisicao, CancellationToken tokenCancelamento)
        {
            RequisicaoRecebida = requisicao;
            return Task.FromResult(resposta);
        }
    }

    private sealed class ServicoClimaPublicoSimulado : IServicoClimaPublico
    {
        public PublicWeatherData? Resposta { get; init; }
        public IReadOnlyList<CidadePublica> Cidades { get; init; } = [];
        public Exception? Erro { get; init; }
        public string? CepRecebido { get; private set; }
        public string? TermoCidadeRecebido { get; private set; }
        public CidadePublica? CidadeRecebida { get; private set; }
        public (double Latitude, double Longitude, double? Precisao)? LocalizacaoRecebida { get; private set; }

        public Task<PublicWeatherData> BuscarPorCepAsync(string cep, CancellationToken tokenCancelamento)
        {
            CepRecebido = cep;
            return Resultado();
        }

        public Task<IReadOnlyList<CidadePublica>> PesquisarCidadesAsync(string termo, CancellationToken tokenCancelamento)
        {
            TermoCidadeRecebido = termo;
            return Erro is null
                ? Task.FromResult(Cidades)
                : Task.FromException<IReadOnlyList<CidadePublica>>(Erro);
        }

        public Task<PublicWeatherData> BuscarPorCidadeAsync(CidadePublica cidade, CancellationToken tokenCancelamento)
        {
            CidadeRecebida = cidade;
            return Resultado();
        }

        public Task<PublicWeatherData> BuscarPorCoordenadasAsync(
            double latitude,
            double longitude,
            string tipoOrigem,
            string rotuloOrigem,
            string? cep,
            double? precisao,
            CancellationToken tokenCancelamento)
        {
            LocalizacaoRecebida = (latitude, longitude, precisao);
            return Resultado();
        }

        private Task<PublicWeatherData> Resultado() => Erro is null
            ? Task.FromResult(Resposta!)
            : Task.FromException<PublicWeatherData>(Erro);
    }
}
