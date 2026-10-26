using System.Text.Json;
using Microsoft.Playwright;

namespace EstacaoClimatica.Tests;

public sealed partial class TestesInterfaceNavegador
{
    [Theory]
    [InlineData(320)]
    [InlineData(390)]
    [InlineData(1440)]
    public async Task Paridade_CargaInicialPublicaDeveAdiarChartChatPdfEExibirGraficosSemTabelaHoraria(int largura)
    {
        await using var contexto = await CriarContextoAsync(autorizarInterno: false);
        var pagina = await contexto.NewPageAsync();
        await pagina.SetViewportSizeAsync(largura, 844);
        await pagina.RouteAsync("**/api/public-weather/cep/**", rota => rota.FulfillAsync(new()
        {
            ContentType = "application/json", Body = CriarPublicWeatherComPrevisaoJson()
        }));
        await pagina.GotoAsync(_servidor.UrlBase);
        Assert.True(await pagina.EvaluateAsync<bool>("""
            () => !window.Chart && !document.querySelector('script[src*="chat.js"], script[src*="pdf-report.js"]')
            """));
        await pagina.Locator("#publicSearchInput").FillAsync("01001000");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Buscar", Exact = true }).ClickAsync();
        await pagina.WaitForFunctionAsync("() => !!window.Chart?.getChart('publicChartForecastUv')");
        Assert.Equal(6, await pagina.Locator(".public-metric-charts canvas").CountAsync());
        Assert.Equal(10, await pagina.Locator("#publicResults canvas").CountAsync());
        Assert.Equal(0, await pagina.Locator("#publicHourlyData").CountAsync());
        Assert.Equal(0, await pagina.Locator("script[src*='chat.js'], script[src*='pdf-report.js']").CountAsync());
        if (largura <= 640)
        {
            Assert.True(await pagina.EvaluateAsync<bool>("""
                () => {
                    const lua = document.querySelector('.public-moon');
                    const detalhes = lua.querySelector('.moon-summary__details').getBoundingClientRect();
                    const conteudo = lua.querySelector('.moon-summary__content').getBoundingClientRect();
                    const grupo = document.querySelector('.public-metric-charts');
                    const cartao = grupo.querySelector('.chart-card').getBoundingClientRect();
                    return detalhes.top >= conteudo.bottom && Math.abs(cartao.width - grupo.clientWidth) < 2
                        && document.documentElement.scrollWidth <= innerWidth;
                }
                """));
        }
    }

    [Fact]
    public async Task Paridade_AqiEChuvaAusentesNaoDevemParecerZeroOuCondicaoBoa()
    {
        await using var contexto = await CriarContextoAsync(autorizarInterno: false);
        var pagina = await contexto.NewPageAsync();
        var dados = System.Text.Json.Nodes.JsonNode.Parse(CriarPublicWeatherComPrevisaoJson())!;
        dados["aqi"]!["valor"] = null;
        dados["climaAtual"] = new System.Text.Json.Nodes.JsonObject();
        await pagina.RouteAsync("**/api/public-weather/cep/**", rota => rota.FulfillAsync(new()
        {
            ContentType = "application/json", Body = dados.ToJsonString()
        }));
        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.Locator("#publicSearchInput").FillAsync("01001000");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Buscar", Exact = true }).ClickAsync();
        await pagina.WaitForSelectorAsync(".public-card--aqi");
        Assert.Equal("--", await pagina.Locator(".public-card--aqi .stats-card__value").InnerTextAsync());
        Assert.DoesNotContain("Boa", await pagina.Locator(".public-card--aqi").InnerTextAsync());
        Assert.Contains("AQI --", await pagina.Locator("#aqiIndicator").GetAttributeAsync("aria-label"));
        Assert.Contains("--", await pagina.Locator(".environment-insight").Nth(1).InnerTextAsync());
    }

    [Fact]
    public async Task Paridade_ZoomDeveAdiarEstiloEPermitirNovaTentativaAposFalha()
    {
        await using var contexto = await CriarContextoAsync(autorizarInterno: false);
        var pagina = await contexto.NewPageAsync();
        var tentativas = 0;
        await pagina.RouteAsync("**/styles/zoom.css*", async rota =>
        {
            if (++tentativas == 1) await rota.AbortAsync();
            else await rota.ContinueAsync();
        });
        await pagina.RouteAsync("**/api/public-weather/cep/**", rota => rota.FulfillAsync(new()
        {
            ContentType = "application/json", Body = CriarPublicWeatherComPrevisaoJson()
        }));
        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.Locator("#publicSearchInput").FillAsync("01001000");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Buscar", Exact = true }).ClickAsync();
        await pagina.WaitForFunctionAsync("() => !!window.Chart?.getChart('publicChartForecastUv')");
        Assert.Equal(0, tentativas);
        var cartao = pagina.Locator("#publicChartTemperature").Locator("..");
        await pagina.EvaluateAsync("async () => await tratarAmpliacao(document.getElementById('publicChartTemperature').parentElement)");
        Assert.Equal(1, tentativas);
        Assert.Equal(0, await pagina.Locator(".plot-zoom-overlay").CountAsync());
        await cartao.GetByRole(AriaRole.Button, new() { Name = "Ampliar gráfico" }).ClickAsync();
        await pagina.WaitForSelectorAsync(".plot-zoom-overlay");
        Assert.Equal(2, tentativas);
        Assert.Equal("fixed", await pagina.Locator(".plot-zoom-overlay").EvaluateAsync<string>("e => getComputedStyle(e).position"));
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Fechar gráfico ampliado" }).ClickAsync();
        await cartao.GetByRole(AriaRole.Button, new() { Name = "Ampliar gráfico" }).ClickAsync();
        await pagina.WaitForSelectorAsync(".plot-zoom-overlay");
        Assert.Equal(2, tentativas);
    }

    [Fact]
    public async Task Paridade_RespostaAntigaNaoDeveSobrescreverDataAtualNemReabrirPainelAposSair()
    {
        await using var contexto = await CriarContextoAsync();
        var pagina = await CriarPaginaComDashboardMockadoAsync(contexto, []);
        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.WaitForSelectorAsync("#statsEstacao .stats-card");
        Assert.True(await pagina.EvaluateAsync<bool>("""
            async () => {
                const original = ClimateAuth.fetchInterno;
                const base = JSON.parse(JSON.stringify(dashboardAtual));
                const pendentes = new Map();
                ClimateAuth.fetchInterno = url => new Promise(resolve => pendentes.set(new URL(url, location.href).searchParams.get('date'), resolve));
                const responder = data => pendentes.get(data)(new Response(JSON.stringify({ ...base, date: data })));
                try {
                    dataSelecionada = '04-06-2026';
                    const antiga = carregarDashboard();
                    dataSelecionada = '03-06-2026';
                    const atual = carregarDashboard();
                    responder('03-06-2026'); await atual;
                    responder('04-06-2026'); await antiga;
                    if (dashboardAtual.date !== '03-06-2026' || dataSelecionada !== '03-06-2026') return false;
                    const depoisSaida = carregarDashboard();
                    await ClimateAuth.sair();
                    responder('03-06-2026'); await depoisSaida;
                    return dashboardAtual === null && document.getElementById('privateApp').hidden;
                } finally { ClimateAuth.fetchInterno = original; }
            }
            """));
    }

    [Fact]
    public async Task Paridade_AtualizacaoDeveReutilizarGraficosPreservarLegendaEAdiarHeatmaps()
    {
        await using var contexto = await CriarContextoAsync();
        var pagina = await CriarPaginaComDashboardMockadoAsync(contexto, []);
        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.WaitForSelectorAsync("#statsEstacao .stats-card");
        Assert.Equal(0, await pagina.Locator("#chartsSala canvas").CountAsync());
        await pagina.Locator("#tabSala").ClickAsync();
        Assert.Equal(0, await pagina.Locator("#weeklyHeatmapSala .weekly-heatmap__day").CountAsync());
        Assert.True(await pagina.EvaluateAsync<bool>("""
            async () => {
                const grafico = Chart.getChart('plotsTempSala');
                grafico.setDatasetVisibility(0, false);
                await carregarDashboard();
                return Chart.getChart('plotsTempSala') === grafico && !grafico.isDatasetVisible(0);
            }
            """));
        await pagina.Locator("#Tab1 .collapsible-trigger").First.ClickAsync();
        Assert.True(await pagina.Locator("#weeklyHeatmapSala .weekly-heatmap__day").CountAsync() > 0);
    }

    [Fact]
    public async Task Paridade_FalhaParcialDevePreservarLeiturasEInsightsDaMesmaData()
    {
        await using var contexto = await CriarContextoAsync();
        var pagina = await CriarPaginaComDashboardMockadoAsync(contexto, []);
        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.WaitForSelectorAsync("#statsEstacao .stats-card");
        Assert.True(await pagina.EvaluateAsync<bool>("""
            async () => {
                const anterior = dashboardAtual;
                anterior.latest = { salaTemperatura: { value: 25, date: '03/06/2026', time: '12:00' } };
                const original = ClimateAuth.fetchInterno;
                ClimateAuth.fetchInterno = async () => new Response(JSON.stringify({
                    ...anterior, failedBranches: ['sala'], sala: null, aqi: null,
                    latest: { ...anterior.latest, salaTemperatura: null },
                    insights: { ...anterior.insights, riscoSala: null, ventilacao: null }
                }));
                try {
                    await carregarDashboard();
                    return dashboardAtual.sala === anterior.sala && dashboardAtual.aqi === anterior.aqi
                        && dashboardAtual.latest.salaTemperatura === anterior.latest.salaTemperatura
                        && dashboardAtual.insights.riscoSala === anterior.insights.riscoSala;
                } finally { ClimateAuth.fetchInterno = original; }
            }
            """));
        Assert.Contains("sala", await pagina.Locator("#sensorConnectionAlert").InnerTextAsync());
        Assert.True(await pagina.Locator("#sensorConnectionAlert").IsVisibleAsync());
    }

    [Fact]
    public async Task Paridade_EventosDevemAtualizarPainelMostrarFalhaEEncerrarNoLogout()
    {
        await using var contexto = await CriarContextoAsync();
        var pagina = await CriarPaginaComDashboardMockadoAsync(contexto, []);
        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.WaitForSelectorAsync("#statsEstacao .stats-card");
        await pagina.EvaluateAsync("""
            () => {
                const original = ClimateAuth.fetchInterno;
                window.consultasParidade = 0;
                ClimateAuth.fetchInterno = (url, opcoes) => {
                    if (url !== '/api/dashboard/updates') { window.consultasParidade++; return original(url, opcoes); }
                    return Promise.resolve(new Response(new ReadableStream({ start(controle) {
                        window.eventoParidade = evento => controle.enqueue(new TextEncoder().encode(`data: ${JSON.stringify(evento)}\n\n`));
                        opcoes.signal.addEventListener('abort', () => { window.streamCancelado = true; controle.error(new DOMException('cancelado', 'AbortError')); });
                    }})));
                };
                void iniciarAtualizacoes();
            }
            """);
        await pagina.WaitForFunctionAsync("() => !!window.eventoParidade");
        await pagina.EvaluateAsync("() => eventoParidade({branch:'sala',failed:true})");
        await pagina.WaitForSelectorAsync("#sensorConnectionAlert:not([hidden])");
        await pagina.EvaluateAsync("() => eventoParidade({branch:'sala',failed:false})");
        await pagina.WaitForFunctionAsync("() => window.consultasParidade === 1");
        await pagina.WaitForSelectorAsync("#sensorConnectionAlert[hidden]", new() { State = WaitForSelectorState.Attached });
        await pagina.Locator("#authLogoutButton").ClickAsync();
        Assert.True(await pagina.EvaluateAsync<bool>("() => window.streamCancelado && !conexaoAtualizacoes && !reconexaoAtualizacoes"));
    }
}
