using System.Text.Json;
using Microsoft.Playwright;

namespace EstacaoClimatica.Tests;

public sealed partial class TestesInterfaceNavegador
{
    [Theory]
    [InlineData(320, 568)]
    [InlineData(390, 844)]
    [InlineData(640, 768)]
    [InlineData(641, 768)]
    [InlineData(844, 390)]
    [InlineData(900, 768)]
    [InlineData(901, 768)]
    [InlineData(1440, 900)]
    public async Task Paridade_AqiCompactoDeveRespeitarBreakpointETextosLongos(int largura, int altura)
    {
        await using var contexto = await CriarContextoAsync(autorizarInterno: false);
        await contexto.AddInitScriptAsync("sessionStorage.clear();");
        var pagina = await contexto.NewPageAsync();
        await pagina.Clock.SetFixedTimeAsync(new DateTime(2026, 6, 3, 19, 30, 0, DateTimeKind.Utc));
        await pagina.SetViewportSizeAsync(largura, altura);
        var erros = new List<string>();
        pagina.PageError += (_, erro) => erros.Add(erro);
        var dados = System.Text.Json.Nodes.JsonNode.Parse(CriarPublicWeatherComPrevisaoJson())!;
        await pagina.RouteAsync("**/api/public-weather/cep/**", rota => rota.FulfillAsync(new()
        {
            ContentType = "application/json", Body = dados.ToJsonString()
        }));
        var pasta = Path.Combine(AmbienteTestesInterfaceNavegador.LocalizarRaizProjeto(), "outputs", "paridade-2026-10-30", "dotnet");
        Directory.CreateDirectory(pasta);
        foreach (int? valor in new int?[] { 42, 125, null })
        {
            dados["aqi"]!["valor"] = valor;
            await pagina.GotoAsync(_servidor.UrlBase);
            await pagina.Locator("#publicSearchInput").FillAsync("01001000");
            await pagina.GetByRole(AriaRole.Button, new() { Name = "Buscar", Exact = true }).ClickAsync();
            await pagina.WaitForFunctionAsync("() => !!window.Chart?.getChart('publicChartForecastUv')");
            var cartao = pagina.Locator(".public-card--aqi");
            Assert.Equal(valor?.ToString() ?? "--", await cartao.Locator(".stats-card__value").InnerTextAsync());
            Assert.Equal(valor == 125 ? "Insalubre para grupos sensíveis" : valor == 42 ? "Boa" : "Indisponível",
                await cartao.Locator(".stats-card__trend").InnerTextAsync());
            await cartao.ScreenshotAsync(new() { Path = Path.Combine(pasta, $"aqi-{largura}x{altura}-{valor?.ToString() ?? "ausente"}.png") });
            var geometria = await cartao.EvaluateAsync<JsonElement>("""
                el => {
                    const caixa = el.getBoundingClientRect();
                    const grade = el.parentElement.getBoundingClientRect();
                    const filhos = [...el.children].map(filho => filho.getBoundingClientRect());
                    const dentro = filhos.every((r, i) => r.left >= caixa.left && r.right <= caixa.right + 1
                        && r.top >= caixa.top && r.bottom <= caixa.bottom + 1
                        && el.children[i].scrollWidth <= el.children[i].clientWidth + 1);
                    const semColisao = filhos.every((a, i) => filhos.slice(i + 1).every(b =>
                        a.right <= b.left + 1 || b.right <= a.left + 1 || a.bottom <= b.top + 1 || b.bottom <= a.top + 1));
                    return { display: getComputedStyle(el).display, alturaMinima: getComputedStyle(el).minHeight,
                        largura: caixa.width, larguraGrade: grade.width, dentro, semColisao,
                        horizontal: filhos.every(r => Math.abs((r.top + r.bottom) / 2 - (filhos[0].top + filhos[0].bottom) / 2) < 2),
                        semOverflow: document.documentElement.scrollWidth <= innerWidth + 1 };
                }
                """);
            Assert.True(geometria.GetProperty("dentro").GetBoolean(), geometria.ToString());
            Assert.True(geometria.GetProperty("semColisao").GetBoolean(), geometria.ToString());
            Assert.True(geometria.GetProperty("semOverflow").GetBoolean(), geometria.ToString());
            if (largura <= 900)
            {
                Assert.Equal("flex", geometria.GetProperty("display").GetString());
                Assert.Equal("64px", geometria.GetProperty("alturaMinima").GetString());
                Assert.Equal(geometria.GetProperty("larguraGrade").GetDouble(), geometria.GetProperty("largura").GetDouble(), 1);
                if (valor == 42) Assert.True(geometria.GetProperty("horizontal").GetBoolean());
            }
            else Assert.NotEqual("flex", geometria.GetProperty("display").GetString());
        }
        Assert.Empty(erros);
    }

    [Theory]
    [InlineData(320, 568)]
    [InlineData(390, 844)]
    [InlineData(844, 390)]
    [InlineData(1440, 900)]
    public async Task Paridade_MapaPrevisaoDeveManterRotulosOpacosESelecaoDuranteRolagem(int largura, int altura)
    {
        await using var contexto = await CriarContextoAsync(autorizarInterno: false, toque: largura < 900);
        var pagina = await contexto.NewPageAsync();
        await pagina.Clock.SetFixedTimeAsync(new DateTime(2026, 6, 3, 19, 30, 0, DateTimeKind.Utc));
        await pagina.SetViewportSizeAsync(largura, altura);
        var erros = new List<string>();
        pagina.PageError += (_, erro) => erros.Add(erro);
        await pagina.RouteAsync("**/api/public-weather/cep/**", rota => rota.FulfillAsync(new()
        {
            ContentType = "application/json", Body = CriarPublicWeatherComPrevisaoJson()
        }));
        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.Locator("#publicSearchInput").FillAsync("01001000");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Buscar", Exact = true }).ClickAsync();
        await pagina.WaitForFunctionAsync("() => !!window.Chart?.getChart('publicChartForecastUv')");
        await pagina.EvaluateAsync("() => Object.values(Chart.instances).forEach(g => { g.options.animation = false; g.stop(); g.update('none'); })");
        var mapa = pagina.Locator("#publicForecastMap");
        var pasta = Path.Combine(AmbienteTestesInterfaceNavegador.LocalizarRaizProjeto(), "outputs", "paridade-2026-10-30", "dotnet");
        Directory.CreateDirectory(pasta);
        foreach (var fracao in new[] { 0.0, 0.5, 1.0 })
        {
            await mapa.EvaluateAsync("(el, fracao) => el.scrollLeft = (el.scrollWidth - el.clientWidth) * fracao", fracao);
            await pagina.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(resolve))");
            await mapa.ScreenshotAsync(new() { Path = Path.Combine(pasta, $"mapa-{largura}x{altura}-{fracao * 100:0}.png") });
            Assert.True(await mapa.EvaluateAsync<bool>("""
                el => {
                    const rotulos = el.querySelector('.public-forecast-map__labels');
                    const estilo = getComputedStyle(rotulos);
                    const caixa = el.getBoundingClientRect(), fixa = rotulos.getBoundingClientRect();
                    return estilo.position === 'sticky' && estilo.left === '0px' && estilo.zIndex === '2'
                        && estilo.backgroundColor === 'rgb(23, 32, 51)' && estilo.boxShadow !== 'none'
                        && fixa.left >= caixa.left && fixa.left <= caixa.left + 14
                        && document.documentElement.scrollWidth <= innerWidth + 1;
                }
                """));
            var indice = await mapa.EvaluateAsync<int>("""
                el => {
                    const fixa = el.querySelector('.public-forecast-map__labels').getBoundingClientRect();
                    const borda = el.getBoundingClientRect().right;
                    return [...el.querySelectorAll('[data-forecast-index]')].findIndex(dia => {
                        const r = dia.getBoundingClientRect(), x = (r.left + r.right) / 2;
                        return x > fixa.right + 8 && r.right < borda - 3;
                    });
                }
                """);
            Assert.True(indice >= 0);
            var dia = mapa.Locator($"[data-forecast-index='{indice}']");
            if (largura < 900) await dia.TapAsync();
            else await dia.ClickAsync();
            Assert.Equal(new[] { indice, indice, indice }, await pagina.EvaluateAsync<int[]>("""
                () => ['publicChartForecastTemperature', 'publicChartForecastRain', 'publicChartForecastUv']
                    .map(id => Chart.getChart(id).getActiveElements()[0]?.index ?? -1)
                """));
            Assert.True(await dia.EvaluateAsync<bool>("el => el.classList.contains('is-active')"));
        }
        await pagina.Mouse.MoveAsync(0, 0);
        await mapa.Locator("[data-forecast-index='14']").FocusAsync();
        await pagina.Keyboard.PressAsync("Home");
        Assert.True(await mapa.Locator("[data-forecast-index='0']").EvaluateAsync<bool>("el => document.activeElement === el && el.classList.contains('is-active')"));
        await pagina.Keyboard.PressAsync("End");
        Assert.True(await mapa.Locator("[data-forecast-index='14']").EvaluateAsync<bool>("el => document.activeElement === el && el.classList.contains('is-active')"));
        Assert.Equal(8, await mapa.Locator(".public-forecast-day--extended").CountAsync());
        Assert.Empty(erros);
    }

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
        Assert.DoesNotContain("Histórico da data selecionada", await pagina.Locator("#privateApp").TextContentAsync());
        Assert.DoesNotContain("janela de 24 horas", await pagina.Locator("#privateApp").TextContentAsync());
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
