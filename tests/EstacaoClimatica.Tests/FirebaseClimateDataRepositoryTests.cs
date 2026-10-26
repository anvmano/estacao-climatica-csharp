using System.Text.Json;
using EstacaoClimatica.Application.Abstractions;
using EstacaoClimatica.Application.Configuration;
using EstacaoClimatica.Infrastructure.Repositories;

namespace EstacaoClimatica.Tests;

public sealed class TestesRepositorioDadosClimaticosFirebase
{
    [Fact]
    public async Task ObterDadosDashboardAsync_SemDataValidaDeveBuscarQuatroRamosCompletos()
    {
        var fonte = new FonteDadosFirebaseSimulada();
        var repositorio = new RepositorioDadosClimaticosFirebase(fonte, new OpcoesFirebase());

        var resultado = await repositorio.ObterDadosDashboardAsync("data-invalida", CancellationToken.None);

        Assert.Equal(
            ["historico/Temperatura", "historico/NascePorDoSol", "historico/Aquario", "historico/AirQuality"],
            fonte.Caminhos);
        Assert.NotNull(resultado.Quarto);
        Assert.NotNull(resultado.Solar);
        Assert.NotNull(resultado.Aquario);
        Assert.NotNull(resultado.Sala);
    }

    [Fact]
    public async Task ObterDadosDashboardAsync_DeveMontarJanelaDaData03DeJunhoDe2026()
    {
        var fonte = new FonteDadosFirebaseSimulada
        {
            RespostaQuery = Elemento("""{"31-05-2026":true,"02-06-2026":true,"03-06-2026":true,"05-06-2026":true,"07-02-2106":true,"invalida":true}""")
        };
        var repositorio = new RepositorioDadosClimaticosFirebase(fonte, new OpcoesFirebase());

        var resultado = await repositorio.ObterDadosDashboardAsync("03-06-2026", CancellationToken.None);

        Assert.DoesNotContain("historico/Temperatura/01-06-2026", fonte.Caminhos);
        Assert.DoesNotContain("historico/Temperatura/30-06-2026", fonte.Caminhos);
        Assert.Equal(3, fonte.Caminhos.Count(caminho => caminho.StartsWith("historico/Temperatura/", StringComparison.Ordinal)));
        Assert.Equal(3, fonte.Caminhos.Count(caminho => caminho.StartsWith("historico/Aquario/", StringComparison.Ordinal)));
        Assert.Contains("historico/Aquario/02-06-2026", fonte.Caminhos);
        Assert.Contains("historico/Aquario/03-06-2026", fonte.Caminhos);
        Assert.Contains("historico/Aquario/05-06-2026", fonte.Caminhos);
        Assert.DoesNotContain(fonte.Caminhos, caminho => caminho.Contains("07-02-2106", StringComparison.Ordinal));
        Assert.Contains(("historico/Temperatura", "shallow=true"), fonte.Consultas);
        Assert.Contains(("historico/Aquario", "shallow=true"), fonte.Consultas);
        Assert.Contains("historico/AirQuality/05-06-2026", fonte.Caminhos);
        Assert.Contains(("historico/AirQuality", "shallow=true"), fonte.Consultas);
        Assert.Contains("historico/NascePorDoSol", fonte.Caminhos);
        Assert.True(resultado.Quarto?.TryGetProperty("03-06-2026", out _));
        Assert.True(resultado.Aquario?.TryGetProperty("03-06-2026", out _));
        Assert.True(resultado.Sala?.TryGetProperty("05-06-2026", out _));
    }

    [Fact]
    public async Task ObterDadosPorRamosAsync_DeveBuscarSomenteSala()
    {
        var fonte = new FonteDadosFirebaseSimulada();
        var repositorio = new RepositorioDadosClimaticosFirebase(fonte, new OpcoesFirebase());

        var resultado = await repositorio.ObterDadosPorRamosAsync(false, false, false, true, CancellationToken.None);

        Assert.Equal(["historico/AirQuality"], fonte.Caminhos);
        Assert.Null(resultado.Quarto);
        Assert.Null(resultado.Solar);
        Assert.Null(resultado.Aquario);
        Assert.NotNull(resultado.Sala);
    }

    [Fact]
    public async Task ObterDadosDashboardAsync_SemChavesShallowDevePreservarBuscaPorDatas()
    {
        var fonte = new FonteDadosFirebaseSimulada();
        var repositorio = new RepositorioDadosClimaticosFirebase(fonte, new OpcoesFirebase());

        await repositorio.ObterDadosDashboardAsync("03-06-2026", CancellationToken.None);

        Assert.Contains("historico/Temperatura/01-06-2026", fonte.Caminhos);
        Assert.Contains("historico/Temperatura/30-06-2026", fonte.Caminhos);
    }

    [Fact]
    public async Task ObterDadosDashboardAsync_DeveConsultarHojeMesmoAntesDeShallowAtualizar()
    {
        var fonte = new FonteDadosFirebaseSimulada { RespostaQuery = Elemento("{}") };
        var repositorio = new RepositorioDadosClimaticosFirebase(fonte, new OpcoesFirebase());
        var hoje = DateTime.Today.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture);

        await repositorio.ObterDadosDashboardAsync(hoje, CancellationToken.None);

        Assert.Contains($"historico/Temperatura/{hoje}", fonte.Caminhos);
        Assert.Contains($"historico/Aquario/{hoje}", fonte.Caminhos);
        Assert.Contains($"historico/AirQuality/{hoje}", fonte.Caminhos);
    }

    [Fact]
    public async Task ObterDadosDashboardAsync_PrimeiroDiaDoMesDeveConsultarDiaAnterior()
    {
        var fonte = new FonteDadosFirebaseSimulada
        {
            RespostaQuery = Elemento("""{"31-05-2026":true,"01-06-2026":true}""")
        };
        var repositorio = new RepositorioDadosClimaticosFirebase(fonte, new OpcoesFirebase());

        await repositorio.ObterDadosDashboardAsync("01-06-2026", CancellationToken.None);

        Assert.Contains("historico/Temperatura/31-05-2026", fonte.Caminhos);
        Assert.Contains("historico/Aquario/31-05-2026", fonte.Caminhos);
        Assert.Contains("historico/AirQuality/31-05-2026", fonte.Caminhos);
    }

    [Fact]
    public async Task ObterDadosDashboardAsync_SensorSemDadosNaoDeveBuscarDiasAusentes()
    {
        var fonte = new FonteDadosFirebaseSimulada { RespostaQuery = Elemento("{}") };
        var repositorio = new RepositorioDadosClimaticosFirebase(fonte, new OpcoesFirebase());

        var dados = await repositorio.ObterDadosDashboardAsync("03-06-2026", CancellationToken.None);

        Assert.Equal(["historico/NascePorDoSol"], fonte.Caminhos);
        Assert.Null(dados.Quarto);
        Assert.Null(dados.Aquario);
        Assert.Null(dados.Sala);
    }

    private static JsonElement Elemento(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    [Fact]
    public async Task UltimaMedicao_DeveBuscarDiaAnteriorAoMesQuandoUltimaDataSoTemSentinelas()
    {
        var fonte = new FonteDadosFirebaseSimulada
        {
            RespostaQuery = Elemento("""{"31-05-2026":true,"03-06-2026":true}"""),
            Consultar = caminho => Elemento(caminho.EndsWith("31-05-2026")
                ? """{"12-00":{"1":{"Temperatura":25,"Umidade":50,"temperatura":25,"umidade":50,"temperaturaDS18B20":25}}}"""
                : """{"12-00":{"1":{"Temperatura":85,"Umidade":50,"temperatura":-127,"umidade":50,"temperaturaDS18B20":85}}}""")
        };
        var repositorio = new RepositorioDadosClimaticosFirebase(fonte, new OpcoesFirebase());
        var dados = await repositorio.ObterDadosDashboardAsync("03-06-2026", CancellationToken.None);
        Assert.True(dados.Quarto?.TryGetProperty("31-05-2026", out _));
        Assert.True(dados.Sala?.TryGetProperty("31-05-2026", out _));
        Assert.True(dados.Aquario?.TryGetProperty("31-05-2026", out _));
        Assert.Empty(dados.RamosComFalha);
    }

    [Fact]
    public async Task FalhaDeRamo_DevePreservarOutrosAmbientesESinalizarFalha()
    {
        var fonte = new FonteDadosFirebaseSimulada
        {
            RespostaQuery = Elemento("""{"03-06-2026":true}"""),
            Consultar = caminho => caminho.Contains("AirQuality")
                ? throw new HttpRequestException("indisponivel") : Elemento("{}")
        };
        var dados = await new RepositorioDadosClimaticosFirebase(fonte, new OpcoesFirebase())
            .ObterDadosDashboardAsync("03-06-2026", CancellationToken.None);
        Assert.Equal(["sala"], dados.RamosComFalha);
        Assert.Null(dados.Sala);
        Assert.NotNull(dados.Quarto);
        Assert.NotNull(dados.Aquario);
        Assert.NotNull(dados.Solar);
    }

    [Fact]
    public async Task Cancelamento_NaoDeveVirarRespostaParcial()
    {
        using var controle = new CancellationTokenSource();
        controle.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new RepositorioDadosClimaticosFirebase(new FonteDadosFirebaseSimulada(), new OpcoesFirebase())
                .ObterDadosDashboardAsync("03-06-2026", controle.Token));
    }

    private sealed class FonteDadosFirebaseSimulada : IFonteDadosFirebase
    {
        public List<string> Caminhos { get; } = [];
        public List<(string Caminho, string Query)> Consultas { get; } = [];
        public JsonElement? RespostaQuery { get; init; }
        public Func<string, JsonElement?>? Consultar { get; init; }

        public Task<JsonElement?> ObterCaminhoAsync(string caminho, CancellationToken tokenCancelamento)
        {
            tokenCancelamento.ThrowIfCancellationRequested();
            Caminhos.Add(caminho);
            if (Consultar is not null) return Task.FromResult(Consultar(caminho));
            return Task.FromResult<JsonElement?>(Elemento("""{"12-00":{"registro":{"Temperatura":25,"Umidade":50,"temperatura":25,"umidade":50,"temperaturaDS18B20":25}}}"""));
        }

        public Task<JsonElement?> ObterCaminhoComQueryAsync(string caminho, string parametrosConsulta, CancellationToken tokenCancelamento)
        {
            Consultas.Add((caminho, parametrosConsulta));
            return Task.FromResult(RespostaQuery);
        }
    }
}
