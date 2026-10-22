using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace EstacaoClimatica.Tests;

public sealed class TestesInterfaceNavegador : IClassFixture<AmbienteTestesInterfaceNavegador>
{
    private static readonly SemaphoreSlim TravaNavegador = new(1, 1);
    private static IPlaywright? playwrightCompartilhado;
    private static IBrowser? navegadorCompartilhado;

    private readonly AmbienteTestesInterfaceNavegador _servidor;

    public TestesInterfaceNavegador(AmbienteTestesInterfaceNavegador servidor)
    {
        _servidor = servidor;
    }

    [Fact]
    public async Task Dashboard_DeveRenderizarDomRealSemErrosDeConsole()
    {
        await using var contexto = await CriarContextoAsync();
        var errosConsole = new List<string>();
        var pagina = await CriarPaginaComDashboardMockadoAsync(contexto, errosConsole);

        // Exercita HTML, scripts e Chart.js em browser real, com resposta de API previsível.
        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.WaitForSelectorAsync("#statsEstacao .stats-card", new() { State = WaitForSelectorState.Attached, Timeout = 15_000 });
        await pagina.WaitForSelectorAsync("#plotGlobalTemperature", new() { State = WaitForSelectorState.Attached, Timeout = 15_000 });
        await pagina.WaitForSelectorAsync("#plotGlobalHumidity", new() { State = WaitForSelectorState.Attached, Timeout = 15_000 });
        Assert.Equal(4, await pagina.Locator(".tablink").CountAsync());
        Assert.False(await pagina.Locator("#Tab0").EvaluateAsync<bool>("el => el.hasAttribute('hidden')"));
        Assert.Equal(6, await pagina.Locator("#statsEstacao .stats-card").CountAsync());
        Assert.Equal(5, await pagina.Locator("#environmentInsights .environment-insight").CountAsync());
        var textoInsightsInternos = await pagina.Locator("#environmentInsights").InnerTextAsync();
        Assert.Contains("Ambiente equilibrado", textoInsightsInternos);
        Assert.Contains("Ponto de orvalho · Sala", textoInsightsInternos, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Ponto de orvalho · Quarto", textoInsightsInternos, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Leitura prática", textoInsightsInternos);
        Assert.DoesNotContain("Condições e recomendações", textoInsightsInternos);
        Assert.True(await pagina.Locator("#environmentLocationButton").IsVisibleAsync());
        Assert.Equal("Duração do dia: 11h18", await pagina.Locator("#solarDayDuration").InnerTextAsync());
        Assert.Equal(1, await pagina.Locator("#seasonIndicator").CountAsync());
        Assert.Equal(1, await pagina.Locator("#moonIndicator").CountAsync());
        await pagina.WaitForSelectorAsync("#statsSala .stats-card", new() { State = WaitForSelectorState.Attached, Timeout = 15_000 });
        await pagina.WaitForSelectorAsync("#chartsSala .chart-card", new() { State = WaitForSelectorState.Attached, Timeout = 15_000 });
        await pagina.WaitForSelectorAsync("#dataSala table", new() { State = WaitForSelectorState.Attached, Timeout = 15_000 });
        await pagina.WaitForSelectorAsync("#monthlyClimateCalendarSala .heatmap-cell", new() { State = WaitForSelectorState.Attached, Timeout = 15_000 });

        var tabelaSala = await pagina.Locator("#dataSala table").InnerTextAsync();
        Assert.Contains("410.00ppm", tabelaSala);
        Assert.Contains("Tolueno", tabelaSala);
        Assert.Contains("0.40ppm", tabelaSala);
        Assert.Equal("", await pagina.Locator("#dataSala tbody tr:nth-child(2) td:first-child").InnerTextAsync());
        Assert.Contains("de 24 horários", await pagina.Locator("#dataSala .table-tools__count").InnerTextAsync());
        Assert.Equal(1, await pagina.Locator("#dataSala [data-table-sort]").CountAsync());
        Assert.Equal(1, await pagina.Locator("#dataSala [data-table-csv]").CountAsync());

        var faixas = await pagina.EvaluateAsync<string>(
            "() => JSON.stringify({ sala: window.graficosInstancias.plotsTempSala.$faixaConforto, umidade: window.graficosInstancias.plotsUmidadeSala.$faixaConforto, aquario: window.graficosInstancias.plotsTempAquario.$faixaConforto })");
        Assert.Contains("\"min\":20", faixas);
        Assert.Contains("\"max\":26", faixas);
        Assert.Contains("\"min\":40", faixas);
        Assert.Contains("\"max\":60", faixas);
        Assert.Contains("\"min\":25", faixas);
        Assert.Contains("\"max\":27", faixas);

        var escalaSala = await pagina.EvaluateAsync<string>(
            "() => JSON.stringify(window.graficosInstancias.plotsTempSala.options.scales.y)");
        Assert.DoesNotContain("suggestedMin", escalaSala);
        Assert.DoesNotContain("suggestedMax", escalaSala);
        var eixos = await pagina.EvaluateAsync<string>("""
            () => JSON.stringify(['plotsTempSala', 'plotsSTSala', 'plotsUmidadeSala', 'plotsPressaoSala']
                .map(id => {
                    const eixo = window.graficosInstancias[id].options.scales.y;
                    return { id, title: eixo.title.text, sampleTick: String(eixo.ticks.callback(20)) };
                }))
            """);
        Assert.Equal("""[{"id":"plotsTempSala","title":"(°C)","sampleTick":"20°"},{"id":"plotsSTSala","title":"(°C)","sampleTick":"20°"},{"id":"plotsUmidadeSala","title":"%","sampleTick":"20%"},{"id":"plotsPressaoSala","title":"hPa","sampleTick":"20hPa"}]""", eixos);

        Assert.Equal(4, await pagina.Locator("#statsSala .stats-card").CountAsync());
        Assert.Equal(4, await pagina.Locator("#chartsSala .chart-card").CountAsync());
        Assert.Equal(1, await pagina.Locator("#chart-container-temp-sala").CountAsync());
        Assert.Equal(1, await pagina.Locator("#chart-container-st-sala").CountAsync());
        Assert.Equal(1, await pagina.Locator("#chart-container-umidade-sala").CountAsync());
        Assert.Equal(1, await pagina.Locator("#chart-container-pressao-sala").CountAsync());
        var protecaoSwipe = await pagina.EvaluateAsync<string>(
            @"() => {
                const container = document.querySelector('.container');
                const scroller = document.createElement('div');
                scroller.style.overflowX = 'auto';
                scroller.style.width = '20px';
                scroller.style.maxWidth = '20px';
                scroller.style.whiteSpace = 'nowrap';
                const conteudoLargo = document.createElement('div');
                conteudoLargo.style.display = 'inline-block';
                conteudoLargo.style.width = '200px';
                conteudoLargo.textContent = 'conteudo horizontal longo';
                scroller.appendChild(conteudoLargo);
                container.appendChild(scroller);

                return JSON.stringify({
                    tabela: window.deveIgnorarSwipeAbas(document.querySelector('#dataSala table')),
                    heatmapSemanal: window.deveIgnorarSwipeAbas(document.querySelector('#weeklyHeatmapSala')),
                    areaRolavel: window.deveIgnorarSwipeAbas(conteudoLargo),
                    areaNormal: window.deveIgnorarSwipeAbas(document.querySelector('#statsSala'))
                });
            }");
        Assert.Contains("\"tabela\":true", protecaoSwipe);
        Assert.Contains("\"heatmapSemanal\":true", protecaoSwipe);
        Assert.Contains("\"areaRolavel\":true", protecaoSwipe);
        Assert.Contains("\"areaNormal\":false", protecaoSwipe);
        await pagina.Locator("button[data-tab-target=\"Tab2\"]").ClickAsync();
        Assert.Equal(1, await pagina.Locator("#chart-container-temp").CountAsync());
        Assert.Equal(1, await pagina.Locator("#chart-container-st").CountAsync());
        Assert.Equal(1, await pagina.Locator("#chart-container-umidade").CountAsync());
        Assert.Equal(3, await pagina.Locator("#statsQuarto .stats-card").CountAsync());
        Assert.Equal(4, await pagina.Locator("#statsAquario .stats-card").CountAsync());
        await pagina.Locator("button[data-tab-target=\"Tab3\"]").ClickAsync();
        Assert.Equal(1, await pagina.Locator("#chart-container-temp-aquario").CountAsync());
        Assert.Equal(1, await pagina.Locator("#chart-container-ph").CountAsync());
        Assert.Equal(1, await pagina.Locator("#chart-container-tds").CountAsync());
        Assert.Equal(1, await pagina.Locator("#chart-container-turbidez").CountAsync());
        await pagina.WaitForSelectorAsync("#chartsAquario .chart-message", new() { State = WaitForSelectorState.Attached, Timeout = 15_000 });
        var textoStatsAquario = NormalizarTexto(await pagina.Locator("#statsAquario .stats-card").AllInnerTextsAsync());
        var textoGraficosAquario = await pagina.Locator("#chartsAquario").InnerTextAsync();
        var textoTabelaAquario = await pagina.Locator("#dataAquario table").InnerTextAsync();
        Assert.Contains("PH -- -- MÍN -- MÁX -- DELTA --", textoStatsAquario);
        Assert.Contains("TDS -- -- MÍN -- MÁX -- DELTA --", textoStatsAquario);
        Assert.Contains("TURBIDEZ -- -- MÍN -- MÁX -- DELTA --", textoStatsAquario);
        Assert.Contains("Sem dados de PH em ", textoGraficosAquario);
        Assert.Contains("Sem dados de TDS em ", textoGraficosAquario);
        Assert.DoesNotContain("Sem dados de ph", textoGraficosAquario);
        Assert.DoesNotContain("Sem dados de tds", textoGraficosAquario);
        Assert.Contains("320.00ppm", textoTabelaAquario);
        Assert.Contains("4.00NTU", textoTabelaAquario);
        Assert.Equal(1, await pagina.Locator("#plotSolarToday").CountAsync());
        Assert.Equal(1, await pagina.Locator("#plotSunriseSunset").CountAsync());
        Assert.Equal(0, await pagina.Locator("#Tab1 #plotSolarToday, #Tab1 #plotSunriseSunset").CountAsync());
        Assert.Equal(0, await pagina.Locator("#Tab2 #plotSolarToday, #Tab2 #plotSunriseSunset").CountAsync());
        Assert.Equal(0, await pagina.Locator("#Tab3 #plotSolarToday, #Tab3 #plotSunriseSunset").CountAsync());
        var indicadorAstronomico = pagina.Locator("#astroIndicator");
        Assert.True(await indicadorAstronomico.IsVisibleAsync());
        Assert.Equal("", (await indicadorAstronomico.Locator(".astro-indicator__label").InnerTextAsync()).Trim());
        var tituloIndicador = await indicadorAstronomico.GetAttributeAsync("title");
        var ariaLabelIndicador = await indicadorAstronomico.GetAttributeAsync("aria-label");
        Assert.Contains("Nascer do sol:", tituloIndicador);
        Assert.Contains("Pôr do sol:", tituloIndicador);
        Assert.Equal(tituloIndicador, ariaLabelIndicador);
        await indicadorAstronomico.ClickAsync();
        var popoverAstronomico = pagina.Locator("#astroPopover");
        Assert.True(await popoverAstronomico.IsVisibleAsync());
        Assert.Equal("true", await indicadorAstronomico.GetAttributeAsync("aria-expanded"));
        var textoPopoverAstronomico = await popoverAstronomico.InnerTextAsync();
        Assert.Contains("Ciclo solar de hoje", textoPopoverAstronomico, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Amanhecer", textoPopoverAstronomico);
        Assert.Contains("Nascer do sol", textoPopoverAstronomico);
        Assert.Contains("Zênite", textoPopoverAstronomico);
        Assert.Contains("Pôr do sol", textoPopoverAstronomico);
        Assert.Contains("Anoitecer", textoPopoverAstronomico);
        Assert.Contains("Duração do dia", textoPopoverAstronomico);
        await pagina.Keyboard.PressAsync("Escape");
        Assert.True(await popoverAstronomico.IsHiddenAsync());
        Assert.Equal("false", await indicadorAstronomico.GetAttributeAsync("aria-expanded"));
        var indicadorAqi = pagina.Locator("#aqiIndicator");
        var popoverAqi = pagina.Locator("#aqiPopover");
        await indicadorAqi.ClickAsync();
        Assert.True(await popoverAqi.IsVisibleAsync());
        await indicadorAstronomico.ClickAsync();
        Assert.True(await popoverAstronomico.IsVisibleAsync());
        Assert.True(await popoverAqi.IsHiddenAsync());
        await indicadorAqi.ClickAsync();
        Assert.True(await popoverAqi.IsVisibleAsync());
        Assert.True(await popoverAstronomico.IsHiddenAsync());
        await pagina.Keyboard.PressAsync("Escape");
        Assert.True(await popoverAqi.IsHiddenAsync());
        Assert.Empty(errosConsole);
    }

    [Fact]
    public async Task Assistente_PrimeiroClique_DeveAbrirPainelEPosicionarFoco()
    {
        await using var contexto = await CriarContextoAsync();
        var pagina = await CriarPaginaComDashboardMockadoAsync(contexto, []);

        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.WaitForSelectorAsync("#statsEstacao .stats-card", new() { State = WaitForSelectorState.Attached, Timeout = 15_000 });
        await pagina.WaitForSelectorAsync("#aiChat", new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        var alternador = pagina.Locator("#aiChatToggle");
        await alternador.ClickAsync();

        Assert.Equal("true", await alternador.GetAttributeAsync("aria-expanded"));
        await pagina.WaitForSelectorAsync("#aiChat.is-open #aiChatPanel", new() { State = WaitForSelectorState.Visible });
        Assert.False(await pagina.Locator("#aiChatPanel").IsHiddenAsync());
        await pagina.WaitForTimeoutAsync(100);
        Assert.Equal("aiChatInput", await pagina.EvaluateAsync<string>("document.activeElement?.id || ''"));
    }

    [Fact]
    public async Task Zoom_DeveSerDialogComFocoEscEFocoDevolvido()
    {
        await using var contexto = await CriarContextoAsync();
        var pagina = await CriarPaginaComDashboardMockadoAsync(contexto, []);

        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.Locator("button[data-tab-target=\"Tab1\"]").ClickAsync();
        await pagina.WaitForSelectorAsync("#chartsSala .chart-card .chart-zoom-button", new() { State = WaitForSelectorState.Attached });
        var botaoZoom = pagina.Locator("#chartsSala .chart-card .chart-zoom-button").First;
        await botaoZoom.ScrollIntoViewIfNeededAsync();
        var rolagemAntes = await pagina.EvaluateAsync<double>("window.scrollY");
        await botaoZoom.ClickAsync();

        var sobreposicao = pagina.Locator(".plot-zoom-overlay");
        Assert.Equal("dialog", await sobreposicao.GetAttributeAsync("role"));
        Assert.Equal("true", await sobreposicao.GetAttributeAsync("aria-modal"));
        Assert.Equal("Fechar gráfico ampliado", await pagina.EvaluateAsync<string>("document.activeElement?.getAttribute('aria-label') || ''"));
        Assert.Equal(rolagemAntes, await pagina.EvaluateAsync<double>("window.scrollY"));
        await pagina.Keyboard.PressAsync("Tab");
        Assert.Equal("Fechar gráfico ampliado", await pagina.EvaluateAsync<string>("document.activeElement?.getAttribute('aria-label') || ''"));
        await pagina.Keyboard.PressAsync("Shift+Tab");
        Assert.Equal("Fechar gráfico ampliado", await pagina.EvaluateAsync<string>("document.activeElement?.getAttribute('aria-label') || ''"));

        await pagina.Keyboard.PressAsync("Escape");
        Assert.Equal(0, await pagina.Locator(".plot-zoom-overlay").CountAsync());
        await pagina.WaitForFunctionAsync("() => document.activeElement?.getAttribute('aria-label') === 'Ampliar gráfico'");
        Assert.Equal("Ampliar gráfico", await pagina.EvaluateAsync<string>("document.activeElement?.getAttribute('aria-label') || ''"));
    }

    [Fact]
    public async Task ModoPublico_DeveConsultarCepSemCarregarDadosInternos()
    {
        await using var contexto = await CriarContextoAsync(autorizarInterno: false);
        var pagina = await contexto.NewPageAsync();
        var requisicoesDashboard = 0;
        pagina.Request += (_, requisicao) =>
        {
            if (requisicao.Url.Contains("/api/dashboard", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref requisicoesDashboard);
            }
        };
        await pagina.RouteAsync("**/api/public-weather/cep/01001000", rota => rota.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = JsonClimaPublico
        }));

        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.WaitForSelectorAsync("#publicApp", new() { State = WaitForSelectorState.Visible });
        Assert.True(await pagina.Locator("#privateApp").IsHiddenAsync());
        Assert.True(await pagina.Locator("#aiChat").IsHiddenAsync());

        await pagina.GetByRole(AriaRole.Textbox, new() { Name = "CEP" }).FillAsync("123");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Buscar" }).ClickAsync();
        Assert.Contains("Informe um CEP com 8 dígitos.", await pagina.Locator("#publicResults").InnerTextAsync());
        Assert.Equal("true", await pagina.GetByRole(AriaRole.Textbox, new() { Name = "CEP" }).GetAttributeAsync("aria-invalid"));
        Assert.Equal("Informe um CEP com 8 dígitos.", await pagina.Locator("#publicSearchStatus").InnerTextAsync());

        await pagina.GetByRole(AriaRole.Textbox, new() { Name = "CEP" }).FillAsync("01001000");
        Assert.Equal("01001-000", await pagina.GetByRole(AriaRole.Textbox, new() { Name = "CEP" }).InputValueAsync());
        Assert.Null(await pagina.GetByRole(AriaRole.Textbox, new() { Name = "CEP" }).GetAttributeAsync("aria-invalid"));
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Buscar" }).ClickAsync();
        await pagina.WaitForSelectorAsync("#publicChartTemperature", new() { State = WaitForSelectorState.Attached });
        var titulosEixos = await pagina.EvaluateAsync<string>("""
            () => JSON.stringify(['publicChartTemperature', 'publicChartFeelsLike', 'publicChartHumidity', 'publicChartPressure']
                .map(id => window.Chart.getChart(id).options.scales.y.title.text))
            """);
        Assert.Equal("""["°C","°C","%","hPa"]""", titulosEixos);
        Assert.Equal(5, await pagina.Locator("#publicResults .public-card").CountAsync());
        Assert.Equal(4, await pagina.Locator("#publicResults .environment-insight").CountAsync());
        var textoPublico = await pagina.Locator("#publicResults").InnerTextAsync();
        Assert.Contains("São Paulo - SP", textoPublico);
        Assert.Contains("Atualizado 03/06 12:00", textoPublico);
        var chavesOrigemCache = await pagina.EvaluateAsync<string>("() => JSON.stringify(Object.keys(JSON.parse(sessionStorage.getItem('climatePublicLastQueryV1')).dados.origem))");
        Assert.DoesNotContain("latitude", chavesOrigemCache, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("longitude", chavesOrigemCache, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("precisao", chavesOrigemCache, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"cep\"", chavesOrigemCache, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Boa hora para ventilar", textoPublico);
        Assert.Contains("Chuva moderada", textoPublico);
        Assert.Contains("Moderado", textoPublico);
        Assert.Contains("Monitorar", textoPublico);
        Assert.DoesNotContain("Clima externo atual", textoPublico);
        Assert.Equal("Duração do dia: 11h15", await pagina.Locator("#publicSolarDuration").InnerTextAsync());
        Assert.Contains("min · desatualizado", textoPublico);
        Assert.Equal(7, await pagina.Locator("#publicResults .chart-zoom-button").CountAsync());
        Assert.Equal(1, await pagina.Locator("#publicChartRain").CountAsync());

        var botaoZoomPublico = pagina.Locator("#publicResults .chart-zoom-button").First;
        await botaoZoomPublico.ScrollIntoViewIfNeededAsync();
        var rolagemPublicaAntes = await pagina.EvaluateAsync<double>("window.scrollY");
        await botaoZoomPublico.ClickAsync();
        Assert.Equal("Fechar gráfico ampliado", await pagina.EvaluateAsync<string>("document.activeElement?.getAttribute('aria-label') || ''"));
        Assert.Equal(rolagemPublicaAntes, await pagina.EvaluateAsync<double>("window.scrollY"));
        await pagina.Keyboard.PressAsync("Escape");
        await pagina.WaitForFunctionAsync("() => document.activeElement?.getAttribute('aria-label') === 'Ampliar gráfico'");

        await pagina.Locator("#aqiIndicator").ClickAsync();
        var textoAqi = await pagina.Locator("#aqiPopover").InnerTextAsync();
        Assert.Contains("Ar satisfatório, risco mínimo.", textoAqi);
        Assert.Contains("Origem", textoAqi);
        Assert.Contains("São Paulo - SP", textoAqi);
        Assert.Contains("03/06 12:00", textoAqi);
        Assert.Contains("Open-Meteo Air Quality", textoAqi);

        await pagina.Locator("#astroIndicator").ClickAsync();
        var textoSolar = await pagina.Locator("#astroPopover").InnerTextAsync();
        Assert.Contains("Origem", textoSolar);
        Assert.Contains("São Paulo - SP", textoSolar);
        Assert.Contains("05:30", textoSolar);
        Assert.Contains("06:30", textoSolar);
        Assert.Contains("12:08", textoSolar);
        Assert.Contains("17:45", textoSolar);
        Assert.Contains("18:45", textoSolar);
        Assert.Contains("11h15", textoSolar);
        Assert.DoesNotContain("06:00", textoSolar);
        Assert.DoesNotContain("18:00", textoSolar);
        Assert.Equal(0, requisicoesDashboard);
    }

    [Fact]
    public async Task ModoPublico_DeveRestaurarUltimaConsultaDaSessaoAposReload()
    {
        await using var contexto = await CriarContextoAsync(autorizarInterno: false);
        var pagina = await contexto.NewPageAsync();
        var consultas = 0;
        await pagina.RouteAsync("**/api/public-weather/cep/01001000", rota =>
        {
            Interlocked.Increment(ref consultas);
            return rota.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = JsonClimaPublico
            });
        });

        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.WaitForSelectorAsync("#publicApp", new() { State = WaitForSelectorState.Visible });
        await pagina.GetByRole(AriaRole.Textbox, new() { Name = "CEP" }).FillAsync("01001000");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Buscar" }).ClickAsync();
        await pagina.WaitForSelectorAsync("#publicChartTemperature", new() { State = WaitForSelectorState.Attached });

        await pagina.ReloadAsync();
        await pagina.WaitForFunctionAsync("() => document.getElementById('publicSearchStatus')?.textContent === 'Última consulta desta sessão restaurada.'");
        await pagina.WaitForTimeoutAsync(250);

        Assert.Contains("São Paulo - SP", await pagina.Locator("#publicResults").InnerTextAsync());
        Assert.Equal("Última consulta desta sessão restaurada.", await pagina.Locator("#publicSearchStatus").InnerTextAsync());
        Assert.Equal(7, await pagina.Locator("#publicResults .chart-zoom-button").CountAsync());
        Assert.Equal(1, consultas);
    }

    [Fact]
    public async Task ModoPublico_CidadeUnicaDevePesquisarECarregarMesmoFluxoClimatico()
    {
        await using var contexto = await CriarContextoAsync(autorizarInterno: false);
        var pagina = await contexto.NewPageAsync();
        string? corpoCidade = null;
        await pagina.RouteAsync("**/api/public-weather/cities?term=*", rota => rota.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """[{"id":1,"nome":"Gramado","estado":"Rio Grande do Sul","regiaoAdministrativa":"Gramado","pais":"Brasil","codigoPais":"BR","latitude":-29.3788,"longitude":-50.8726,"fusoHorario":"America/Sao_Paulo","rotulo":"Gramado - Rio Grande do Sul"}]"""
        }));
        await pagina.RouteAsync("**/api/public-weather/city", rota =>
        {
            corpoCidade = rota.Request.PostData;
            return rota.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = CriarPublicWeatherCidadeJson("Gramado - Rio Grande do Sul")
            });
        });

        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.Locator("#publicSearchModeCity").ClickAsync();
        await pagina.GetByRole(AriaRole.Textbox, new() { Name = "Cidade" }).FillAsync("Gramado");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Buscar" }).ClickAsync();
        await pagina.WaitForSelectorAsync("#publicChartTemperature", new() { State = WaitForSelectorState.Attached });

        Assert.Equal("true", await pagina.Locator("#publicSearchModeCity").GetAttributeAsync("aria-pressed"));
        Assert.Contains("Gramado - Rio Grande do Sul", await pagina.Locator("#publicResults").InnerTextAsync());
        Assert.NotNull(corpoCidade);
        Assert.Contains("\"latitude\":-29.3788", corpoCidade);
        Assert.Equal(7, await pagina.Locator("#publicResults .chart-zoom-button").CountAsync());
    }

    [Fact]
    public async Task ModoPublico_CidadesHomonimasDevemSerAcessiveisESelecionaveisPorTeclado()
    {
        await using var contexto = await CriarContextoAsync(autorizarInterno: false);
        var pagina = await contexto.NewPageAsync();
        await pagina.SetViewportSizeAsync(390, 844);
        await pagina.RouteAsync("**/api/public-weather/cities?term=*", rota => rota.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """
            [
              {"id":1,"nome":"Gramado","estado":"Rio Grande do Sul","regiaoAdministrativa":"Gramado","pais":"Brasil","codigoPais":"BR","latitude":-29.3788,"longitude":-50.8726,"fusoHorario":"America/Sao_Paulo","rotulo":"Gramado - Rio Grande do Sul"},
              {"id":2,"nome":"Gramado","estado":"Rio Grande do Sul","regiaoAdministrativa":"Sobradinho","pais":"Brasil","codigoPais":"BR","latitude":-29.4,"longitude":-53,"fusoHorario":"America/Sao_Paulo","rotulo":"Gramado (Sobradinho) - Rio Grande do Sul"},
              {"id":3,"nome":"Gramado","estado":"Rio Grande do Sul","regiaoAdministrativa":"Marau","pais":"Brasil","codigoPais":"BR","latitude":-28.45,"longitude":-52.2,"fusoHorario":"America/Sao_Paulo","rotulo":"Gramado (Marau) - Rio Grande do Sul"}
            ]
            """
        }));
        await pagina.RouteAsync("**/api/public-weather/city", rota => rota.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = CriarPublicWeatherCidadeJson("Gramado - Rio Grande do Sul")
        }));

        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.Locator("#publicSearchModeCity").ClickAsync();
        await pagina.GetByRole(AriaRole.Textbox, new() { Name = "Cidade" }).FillAsync("Gramado");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Buscar" }).ClickAsync();
        await pagina.WaitForSelectorAsync(".public-city-option", new() { State = WaitForSelectorState.Visible });

        Assert.Equal(3, await pagina.Locator(".public-city-option").CountAsync());
        Assert.Equal("Gramado - Rio Grande do Sul", await pagina.EvaluateAsync<string>("() => document.activeElement?.textContent"));
        Assert.Contains("3 cidades encontradas", await pagina.Locator("#publicSearchStatus").InnerTextAsync());
        Assert.True(await pagina.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"));

        await pagina.Locator(".public-city-option").First.PressAsync("Enter");
        await pagina.WaitForSelectorAsync("#publicChartTemperature", new() { State = WaitForSelectorState.Attached });
        Assert.Contains("Gramado - Rio Grande do Sul", await pagina.Locator("#publicResults").InnerTextAsync());
    }

    [Fact]
    public async Task ModoPublico_DevePreservarValoresSeparadosAoAlternarCepECidade()
    {
        await using var contexto = await CriarContextoAsync(autorizarInterno: false);
        var pagina = await contexto.NewPageAsync();
        await pagina.GotoAsync(_servidor.UrlBase);

        await pagina.GetByRole(AriaRole.Textbox, new() { Name = "CEP" }).FillAsync("01001000");
        await pagina.Locator("#publicSearchModeCity").ClickAsync();
        await pagina.GetByRole(AriaRole.Textbox, new() { Name = "Cidade" }).FillAsync("Campinas, SP");
        await pagina.Locator("#publicSearchModeCep").ClickAsync();
        Assert.Equal("01001-000", await pagina.Locator("#publicSearchInput").InputValueAsync());
        await pagina.Locator("#publicSearchModeCity").ClickAsync();
        Assert.Equal("Campinas, SP", await pagina.Locator("#publicSearchInput").InputValueAsync());
        Assert.Equal("Ex.: Campinas, SP", await pagina.Locator("#publicSearchInput").GetAttributeAsync("placeholder"));
    }

    [Fact]
    public async Task ModoPublico_DeveValidarCidadeCurtaEExibirCidadeNaoEncontrada()
    {
        await using var contexto = await CriarContextoAsync(autorizarInterno: false);
        var pagina = await contexto.NewPageAsync();
        var pesquisas = 0;
        await pagina.RouteAsync("**/api/public-weather/cities?term=*", rota =>
        {
            pesquisas++;
            return rota.FulfillAsync(new()
            {
                Status = 404,
                ContentType = "application/json",
                Body = """{"message":"Cidade não encontrada. Confira o nome e tente novamente."}"""
            });
        });
        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.Locator("#publicSearchModeCity").ClickAsync();

        await pagina.GetByRole(AriaRole.Textbox, new() { Name = "Cidade" }).FillAsync("A");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Buscar" }).ClickAsync();
        Assert.Equal("true", await pagina.Locator("#publicSearchInput").GetAttributeAsync("aria-invalid"));
        Assert.Contains("pelo menos 2 caracteres", await pagina.Locator("#publicResults").InnerTextAsync());
        Assert.Equal(0, pesquisas);

        await pagina.GetByRole(AriaRole.Textbox, new() { Name = "Cidade" }).FillAsync("Cidade inexistente");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Buscar" }).ClickAsync();
        await pagina.WaitForFunctionAsync("() => document.getElementById('publicResults')?.textContent.includes('Cidade não encontrada')");
        Assert.Equal(1, pesquisas);
        Assert.Equal("alert", await pagina.Locator("#publicSearchStatus").GetAttributeAsync("role"));
    }

    [Fact]
    public async Task ModoPublico_RespostaAntigaDeCidadeNaoDeveSobrescreverPesquisaRecente()
    {
        await using var contexto = await CriarContextoAsync(autorizarInterno: false);
        var pagina = await contexto.NewPageAsync();
        await pagina.RouteAsync("**/api/public-weather/cities?term=*", async rota =>
        {
            var antiga = rota.Request.Url.Contains("Antiga", StringComparison.Ordinal);
            if (antiga) await Task.Delay(350);
            var prefixo = antiga ? "Antiga" : "Nova";
            await rota.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = $$"""[{"id":1,"nome":"{{prefixo}} 1","estado":"SP","regiaoAdministrativa":"{{prefixo}}","pais":"Brasil","codigoPais":"BR","latitude":-23.5,"longitude":-46.6,"fusoHorario":"America/Sao_Paulo","rotulo":"{{prefixo}} 1 - SP"},{"id":2,"nome":"{{prefixo}} 2","estado":"SP","regiaoAdministrativa":"{{prefixo}}","pais":"Brasil","codigoPais":"BR","latitude":-23.6,"longitude":-46.7,"fusoHorario":"America/Sao_Paulo","rotulo":"{{prefixo}} 2 - SP"}]"""
            });
        });

        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.Locator("#publicSearchModeCity").ClickAsync();
        await pagina.Locator("#publicSearchInput").FillAsync("Antiga");
        await pagina.Locator("#publicSearchButton").ClickAsync();
        await pagina.WaitForTimeoutAsync(40);
        await pagina.Locator("#publicSearchInput").EvaluateAsync("el => el.disabled = false");
        await pagina.Locator("#publicSearchButton").EvaluateAsync("el => el.disabled = false");
        await pagina.Locator("#publicSearchInput").FillAsync("Nova");
        await pagina.Locator("#publicSearchForm").EvaluateAsync("form => form.requestSubmit()");
        await pagina.WaitForFunctionAsync("() => document.getElementById('publicCityResults')?.textContent.includes('Nova 1')");
        await pagina.WaitForTimeoutAsync(450);

        var opcoes = await pagina.Locator(".public-city-option").AllTextContentsAsync();
        Assert.All(opcoes, opcao => Assert.StartsWith("Nova", opcao));
        Assert.DoesNotContain("Antiga", await pagina.Locator("#publicCityResults").InnerTextAsync());
    }

    [Fact]
    public async Task ModoPublico_JanelaTemporalDeveCobrirHoraCheiaMinutosViradaENulos()
    {
        await using var contexto = await CriarContextoAsync(autorizarInterno: false);
        var pagina = await contexto.NewPageAsync();
        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.WaitForSelectorAsync("#publicApp", new() { State = WaitForSelectorState.Visible });

        var contrato = await pagina.EvaluateAsync<string>("""
            () => {
                const dois = valor => String(valor).padStart(2, '0');
                const formatar = data => `${data.getFullYear()}-${dois(data.getMonth() + 1)}-${dois(data.getDate())}T${dois(data.getHours())}:00`;
                const horarios = [];
                const temperatura = [];
                const inicio = new Date(2026, 5, 2, 16, 0, 0);
                for (let indice = 0; indice < 37; indice += 1) {
                    horarios.push(formatar(new Date(inicio.getTime() + indice * 3600000)));
                    temperatura.push(indice === 27 ? null : 20 + indice / 10);
                }
                const series = {
                    horarios,
                    temperatura,
                    sensacaoTermica: temperatura.map(valor => valor === null ? null : valor - 0.5),
                    umidade: temperatura.map(valor => valor === null ? null : 60 - (valor - 20)),
                    pressao: temperatura.map(valor => valor === null ? null : 1018 - (valor - 20))
                };
                const cheia = window.PublicWeatherApp.montarJanelaObservadaEPrevista(series, '2026-06-03T16:00');
                const comMinutos = window.PublicWeatherApp.montarJanelaObservadaEPrevista(series, '2026-06-03T16:35');
                const invertidas = Object.fromEntries(Object.entries(series).map(([chave, valores]) => [chave, [...valores].reverse()]));
                const ordenada = window.PublicWeatherApp.montarJanelaObservadaEPrevista(invertidas, '2026-06-03T16:00');
                const indiceAusente = cheia.horarios.indexOf('2026-06-03T19:00');
                const resumir = janela => ({
                    medidos: janela.tipos.filter(tipo => tipo === 'medido').length,
                    previstos: janela.tipos.filter(tipo => tipo === 'previsao').length,
                    ultimoMedido: janela.horarios[janela.indiceAgora],
                    primeiroPrevisto: janela.horarios[janela.tipos.indexOf('previsao')],
                    ultimoPrevisto: janela.horarios[janela.horarios.length - 1]
                });
                return JSON.stringify({
                    cheia: resumir(cheia),
                    comMinutos: resumir(comMinutos),
                    ausente: cheia.temperatura[indiceAusente],
                    cronologica: ordenada.horarios.every((horario, indice) => indice === 0 || new Date(ordenada.horarios[indice - 1]) < new Date(horario))
                });
            }
            """);

        using var documento = JsonDocument.Parse(contrato);
        foreach (var (nome, medidosEsperados) in new[] { ("cheia", 25), ("comMinutos", 24) })
        {
            var janela = documento.RootElement.GetProperty(nome);
            Assert.Equal(medidosEsperados, janela.GetProperty("medidos").GetInt32());
            Assert.Equal(12, janela.GetProperty("previstos").GetInt32());
            Assert.Equal("2026-06-03T16:00", janela.GetProperty("ultimoMedido").GetString());
            Assert.Equal("2026-06-03T17:00", janela.GetProperty("primeiroPrevisto").GetString());
            Assert.Equal("2026-06-04T04:00", janela.GetProperty("ultimoPrevisto").GetString());
        }
        Assert.Equal(JsonValueKind.Null, documento.RootElement.GetProperty("ausente").ValueKind);
        Assert.True(documento.RootElement.GetProperty("cronologica").GetBoolean());
    }

    [Fact]
    public async Task ModoPublico_GraficosDevemMostrarMedidoPrevisaoAgoraTooltipsEZoom()
    {
        await using var contexto = await CriarContextoAsync(autorizarInterno: false);
        var pagina = await contexto.NewPageAsync();
        await pagina.RouteAsync("**/api/public-weather/cep/01001000", rota => rota.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = CriarPublicWeatherComPrevisaoJson()
        }));

        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.GetByRole(AriaRole.Textbox, new() { Name = "CEP" }).FillAsync("01001000");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Buscar" }).ClickAsync();
        await pagina.WaitForSelectorAsync("#publicChartTemperature", new() { State = WaitForSelectorState.Attached });

        var contrato = await pagina.EvaluateAsync<string>("""
            () => JSON.stringify({
                meteorologicos: ['publicChartTemperature', 'publicChartFeelsLike', 'publicChartHumidity', 'publicChartPressure'].map(id => {
                    const grafico = window.Chart.getChart(id);
                    const previsto = grafico.data.datasets.find(dataset => dataset.tipoDado === 'previsao');
                    const medido = grafico.data.datasets.find(dataset => dataset.tipoDado === 'medido');
                    return {
                        id,
                        labels: grafico.data.labels.length,
                        datasets: grafico.data.datasets.map(dataset => dataset.label),
                        previstos: previsto.data.filter((valor, indice) => indice > grafico.$marcadorAgora.indice && valor !== null).length,
                        tracejado: previsto.borderDash,
                        corMedida: medido.borderColor,
                        corPrevista: previsto.borderColor,
                        marcador: grafico.$marcadorAgora.indice,
                        pluginAgora: grafico.config.plugins.some(plugin => plugin.id === 'marcadorAgoraPublico'),
                        unidade: grafico.options.scales.y.title.text,
                        ticks: grafico.options.scales.x.ticks.maxTicksLimit,
                        tooltipMedido: grafico.options.plugins.tooltip.callbacks.label({ dataset: medido, parsed: { y: 21.25 } }),
                        tooltipPrevisto: grafico.options.plugins.tooltip.callbacks.label({ dataset: previsto, parsed: { y: 22.5 } })
                    };
                }),
                chuva: (() => {
                    const grafico = window.Chart.getChart('publicChartRain');
                    return {
                        datasets: grafico.data.datasets.map(dataset => dataset.label),
                        eixos: Object.keys(grafico.options.scales),
                        grupo: grafico.$grupoSincronizacao,
                        marcador: grafico.$indiceAgora
                    };
                })(),
                solarDatasets: window.Chart.getChart('publicChartSolar').data.datasets.map(dataset => dataset.label)
            })
            """);

        using (var documento = JsonDocument.Parse(contrato))
        {
            var graficos = documento.RootElement.GetProperty("meteorologicos").EnumerateArray().ToArray();
            Assert.Equal(4, graficos.Length);
            Assert.All(graficos, grafico =>
            {
                Assert.Equal(37, grafico.GetProperty("labels").GetInt32());
                Assert.Equal(["Medido", "Previsão"], grafico.GetProperty("datasets").EnumerateArray().Select(item => item.GetString()).ToArray());
                Assert.Equal(12, grafico.GetProperty("previstos").GetInt32());
                Assert.Equal([6, 4], grafico.GetProperty("tracejado").EnumerateArray().Select(item => item.GetInt32()).ToArray());
                Assert.NotEqual(grafico.GetProperty("corMedida").GetString(), grafico.GetProperty("corPrevista").GetString());
                Assert.Equal(24, grafico.GetProperty("marcador").GetInt32());
                Assert.True(grafico.GetProperty("pluginAgora").GetBoolean());
                Assert.Equal(13, grafico.GetProperty("ticks").GetInt32());
                Assert.StartsWith("Medido: 21.25", grafico.GetProperty("tooltipMedido").GetString());
                Assert.StartsWith("Previsão: 22.50", grafico.GetProperty("tooltipPrevisto").GetString());
            });
            Assert.Equal(["Luz do dia", "Eventos solares"], documento.RootElement.GetProperty("solarDatasets").EnumerateArray().Select(item => item.GetString()).ToArray());
            Assert.Equal(["°C", "°C", "%", "hPa"], graficos.Select(grafico => grafico.GetProperty("unidade").GetString()).ToArray());
            var chuva = documento.RootElement.GetProperty("chuva");
            Assert.Equal(["Precipitação observada", "Precipitação prevista"], chuva.GetProperty("datasets").EnumerateArray().Select(item => item.GetString()).ToArray());
            Assert.Contains("yMilimetros", chuva.GetProperty("eixos").EnumerateArray().Select(item => item.GetString()));
            Assert.Contains("yProbabilidade", chuva.GetProperty("eixos").EnumerateArray().Select(item => item.GetString()));
            Assert.Equal("publico", chuva.GetProperty("grupo").GetString());
        }

        await pagina.Locator("#publicResults .chart-zoom-button").First.ClickAsync();
        await pagina.WaitForSelectorAsync(".plot-zoom-overlay", new() { State = WaitForSelectorState.Visible });
        var zoom = await pagina.EvaluateAsync<string>("""
            () => {
                const grafico = window.Chart.getChart(document.querySelector('.plot--zoom'));
                const previsto = grafico.data.datasets.find(dataset => dataset.tipoDado === 'previsao');
                return JSON.stringify({
                    datasets: grafico.data.datasets.map(dataset => dataset.label),
                    tracejado: previsto.borderDash,
                    marcador: grafico.$marcadorAgora.indice,
                    pluginAgora: grafico.config.plugins.some(plugin => plugin.id === 'marcadorAgoraPublico'),
                    unidade: grafico.options.scales.y.title.text,
                    tooltip: grafico.options.plugins.tooltip.callbacks.label({ dataset: previsto, parsed: { y: 22.5 } })
                });
            }
            """);
        using var documentoZoom = JsonDocument.Parse(zoom);
        Assert.Equal(["Medido", "Previsão"], documentoZoom.RootElement.GetProperty("datasets").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal([6, 4], documentoZoom.RootElement.GetProperty("tracejado").EnumerateArray().Select(item => item.GetInt32()).ToArray());
        Assert.Equal(24, documentoZoom.RootElement.GetProperty("marcador").GetInt32());
        Assert.True(documentoZoom.RootElement.GetProperty("pluginAgora").GetBoolean());
        Assert.Equal("°C", documentoZoom.RootElement.GetProperty("unidade").GetString());
        Assert.Equal("Previsão: 22.50°C", documentoZoom.RootElement.GetProperty("tooltip").GetString());
    }

    [Fact]
    public async Task ModoPublicoMobile_DeveManterPontosEReduzirSomenteRotulos()
    {
        await using var contexto = await CriarContextoAsync(autorizarInterno: false);
        var pagina = await contexto.NewPageAsync();
        await pagina.SetViewportSizeAsync(390, 844);
        await pagina.RouteAsync("**/api/public-weather/cep/01001000", rota => rota.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = CriarPublicWeatherComPrevisaoJson()
        }));

        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.GetByRole(AriaRole.Textbox, new() { Name = "CEP" }).FillAsync("01001000");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Buscar" }).ClickAsync();
        await pagina.WaitForSelectorAsync("#publicChartTemperature", new() { State = WaitForSelectorState.Attached });

        var contrato = await pagina.EvaluateAsync<string>("""
            () => JSON.stringify(['publicChartTemperature', 'publicChartFeelsLike', 'publicChartHumidity', 'publicChartPressure'].map(id => {
                const grafico = window.Chart.getChart(id);
                return { pontos: grafico.data.labels.length, ticks: grafico.options.scales.x.ticks.maxTicksLimit };
            }))
            """);
        using var documento = JsonDocument.Parse(contrato);
        Assert.All(documento.RootElement.EnumerateArray(), grafico =>
        {
            Assert.Equal(37, grafico.GetProperty("pontos").GetInt32());
            Assert.Equal(7, grafico.GetProperty("ticks").GetInt32());
        });
        Assert.Equal(10, await pagina.Locator("#publicResults .chart-zoom-button").CountAsync());
        Assert.Equal("static", await pagina.Locator("#publicSolarDuration").EvaluateAsync<string>("el => getComputedStyle(el).position"));
        Assert.True(await pagina.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"));
    }

    [Fact]
    public async Task ModoPublico_PrevisaoQuinzeDiasDeveManterOrdemGradeResponsivaESincronizacao()
    {
        await using var contexto = await CriarContextoAsync(autorizarInterno: false);
        var pagina = await contexto.NewPageAsync();
        await pagina.SetViewportSizeAsync(1440, 900);
        await pagina.RouteAsync("**/api/public-weather/cep/01001000", rota => rota.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = CriarPublicWeatherComPrevisaoJson()
        }));

        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.GetByRole(AriaRole.Textbox, new() { Name = "CEP" }).FillAsync("01001000");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Buscar" }).ClickAsync();
        await pagina.WaitForSelectorAsync("#publicChartForecastUv", new() { State = WaitForSelectorState.Attached });
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "EstacaoClimatica.sln"))) raiz = raiz.Parent;
        var pastaEvidencias = Path.Combine(raiz?.FullName ?? AppContext.BaseDirectory, "outputs", "publico-consolidado-2026-10-20");
        Directory.CreateDirectory(pastaEvidencias);
        await pagina.ScreenshotAsync(new() { Path = Path.Combine(pastaEvidencias, "publico-1440x900.png"), FullPage = true });

        Assert.Equal(15, await pagina.Locator("[data-forecast-index]").CountAsync());
        Assert.Equal(8, await pagina.Locator(".public-forecast-day--extended").CountAsync());
        var paleta = await pagina.EvaluateAsync<string[]>("""
            () => {
                const temperatura = Chart.getChart('publicChartForecastTemperature');
                const chuva = Chart.getChart('publicChartForecastRain');
                const uv = Chart.getChart('publicChartForecastUv');
                return [
                    temperatura.data.datasets[0].borderColor,
                    temperatura.data.datasets[1].borderColor,
                    chuva.data.datasets[0].borderColor,
                    chuva.data.datasets[1].borderColor,
                    uv.data.datasets[0].borderColor
                ];
            }
            """);
        Assert.Equal(new[] { "#fbbf24", "#38bdf8", "#38bdf8", "#a78bfa", "#fbbf24" }, paleta);
        Assert.True(await pagina.EvaluateAsync<bool>("""
            () => {
                const curtos = document.querySelector('.public-metric-charts');
                const previsao = document.querySelector('.public-forecast');
                const solar = document.querySelector('#public-solar-container');
                return Boolean(curtos && previsao && solar &&
                    (curtos.compareDocumentPosition(previsao) & Node.DOCUMENT_POSITION_FOLLOWING) &&
                    (previsao.compareDocumentPosition(solar) & Node.DOCUMENT_POSITION_FOLLOWING));
            }
            """));

        var gradeLarga = await pagina.EvaluateAsync<int[]>("""
            () => [...document.querySelectorAll('.public-metric-charts > .chart-card')]
                .map(item => Math.round(item.getBoundingClientRect().top))
            """);
        Assert.Equal(gradeLarga[0], gradeLarga[1]);
        Assert.Equal(gradeLarga[1], gradeLarga[2]);
        Assert.Equal(gradeLarga[3], gradeLarga[4]);
        Assert.Equal(6, gradeLarga.Length);
        Assert.Equal(gradeLarga[4], gradeLarga[5]);
        Assert.True(gradeLarga[3] > gradeLarga[0]);
        Assert.True(await pagina.Locator(".public-metric-charts").EvaluateAsync<bool>("el => new Set([...el.children].map(card => Math.round(card.getBoundingClientRect().height))).size === 1"));
        Assert.Equal(3, await pagina.Locator(".public-forecast-charts").EvaluateAsync<int>("el => getComputedStyle(el).gridTemplateColumns.split(' ').length"));

        await pagina.Locator("[data-forecast-index='7']").FocusAsync();
        var indicesAtivos = await pagina.EvaluateAsync<int[]>("""
            () => ['publicChartForecastTemperature', 'publicChartForecastRain', 'publicChartForecastUv']
                .map(id => Chart.getChart(id).getActiveElements()[0]?.index ?? -1)
            """);
        Assert.Equal(new[] { 7, 7, 7 }, indicesAtivos);
        Assert.True(await pagina.Locator("[data-forecast-index='7']").EvaluateAsync<bool>("el => el.classList.contains('is-active')"));

        await pagina.Locator("[data-forecast-index='7']").PressAsync("ArrowRight");
        Assert.True(await pagina.Locator("[data-forecast-index='8']").EvaluateAsync<bool>("el => document.activeElement === el && el.classList.contains('is-active')"));

        await pagina.SetViewportSizeAsync(1024, 768);
        Assert.Equal(2, await pagina.Locator(".public-metric-charts").EvaluateAsync<int>("el => getComputedStyle(el).gridTemplateColumns.split(' ').length"));
        Assert.True(await pagina.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
        await pagina.EvaluateAsync("() => scrollTo(0, 0)");
        await pagina.ScreenshotAsync(new() { Path = Path.Combine(pastaEvidencias, "publico-1024x768.png"), FullPage = true });
        await pagina.SetViewportSizeAsync(390, 844);
        Assert.Equal("x mandatory", await pagina.Locator(".public-forecast-charts").EvaluateAsync<string>("el => getComputedStyle(el).scrollSnapType"));
        Assert.True(await pagina.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
        await pagina.EvaluateAsync("() => scrollTo(0, 0)");
        await pagina.ScreenshotAsync(new() { Path = Path.Combine(pastaEvidencias, "publico-390x844.png"), FullPage = true });
        await pagina.SetViewportSizeAsync(320, 568);
        Assert.True(await pagina.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
        await pagina.EvaluateAsync("() => scrollTo(0, 0)");
        await pagina.ScreenshotAsync(new() { Path = Path.Combine(pastaEvidencias, "publico-320x568.png"), FullPage = true });
    }

    [Fact]
    public async Task ModoPublico_PrevisaoAusenteDevePreservarDadosAtuaisEPermitirNovaTentativa()
    {
        await using var contexto = await CriarContextoAsync(autorizarInterno: false);
        var pagina = await contexto.NewPageAsync();
        var consultas = 0;
        await pagina.RouteAsync("**/api/public-weather/cep/01001000", rota => rota.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = ++consultas == 1 ? JsonClimaPublico : CriarPublicWeatherComPrevisaoJson()
        }));

        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.GetByRole(AriaRole.Textbox, new() { Name = "CEP" }).FillAsync("01001000");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Buscar" }).ClickAsync();
        await pagina.WaitForSelectorAsync("[data-retry-forecast]", new() { State = WaitForSelectorState.Visible });

        Assert.Contains("21.40°C", await pagina.Locator(".public-stats-grid").InnerTextAsync());
        Assert.True(await pagina.Locator("#publicChartSolar").IsVisibleAsync());
        Assert.Contains("continuam disponíveis", await pagina.Locator(".public-forecast__state").InnerTextAsync());

        await pagina.GetByRole(AriaRole.Button, new() { Name = "Tentar novamente" }).ClickAsync();
        await pagina.WaitForSelectorAsync("#publicChartForecastUv", new() { State = WaitForSelectorState.Attached });
        Assert.Equal(2, consultas);
        Assert.Contains("21.40°C", await pagina.Locator(".public-stats-grid").InnerTextAsync());
    }

    [Theory]
    [InlineData(1440)]
    [InlineData(1024)]
    [InlineData(390)]
    [InlineData(360)]
    [InlineData(320)]
    public async Task ModoPublico_SeisGraficosDevemSincronizarEPreservarSeriesNoZoom(int largura)
    {
        await using var contexto = await CriarContextoAsync(autorizarInterno: false);
        var pagina = await contexto.NewPageAsync();
        await pagina.SetViewportSizeAsync(largura, 844);
        await pagina.RouteAsync("**/api/public-weather/cep/01001000", rota => rota.FulfillAsync(new()
        {
            Status = 200, ContentType = "application/json", Body = CriarPublicWeatherComPrevisaoJson()
        }));
        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.GetByRole(AriaRole.Textbox, new() { Name = "CEP" }).FillAsync("01001000");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Buscar" }).ClickAsync();
        await pagina.WaitForFunctionAsync("() => !!Chart.getChart('publicChartRainProbability')");
        Assert.Equal(6, await pagina.Locator(".public-metric-charts canvas").CountAsync());
        Assert.Equal(0, await pagina.Locator("#publicResults .rain-chart-toggle").CountAsync());
        Assert.True(await pagina.EvaluateAsync<bool>("""
            () => {
                const chuva = Chart.getChart('publicChartRain');
                const chance = Chart.getChart('publicChartRainProbability');
                const ids = ['publicChartTemperature', 'publicChartFeelsLike', 'publicChartHumidity',
                    'publicChartPressure', 'publicChartRain', 'publicChartRainProbability'];
                SincronizadorGraficos.sincronizarIndice('publico', 26);
                return ids.every(id => Chart.getChart(id).getActiveElements()[0]?.index === 26)
                    && ids.every(id => Chart.getChart(id).$marcadorAgora.indice === 24)
                    && chuva.data.datasets.length === 2 && chance.data.datasets.length === 1
                    && chuva.options.scales.yMilimetros.display && !chuva.options.scales.yProbabilidade.display
                    && chance.options.scales.yProbabilidade.display && !chance.options.scales.yMilimetros.display
                    && chance.data.datasets[0].data.length === 37
                    && chance.data.datasets[0].data[0] === 20 && chance.data.datasets[0].data[5] === null;
            }
            """));

        foreach (var id in new[] { "publicChartRain", "publicChartRainProbability", "publicChartForecastTemperature", "publicChartForecastRain", "publicChartForecastUv" })
        {
            var botao = pagina.Locator($"canvas#{id}").Locator("..").Locator(".chart-zoom-button");
            await botao.ScrollIntoViewIfNeededAsync();
            await pagina.EvaluateAsync("""
                id => {
                    const grafico = Chart.getChart(id);
                    if (grafico.data.datasets.length > 1) {
                        grafico.setDatasetVisibility(0, false);
                        grafico.update('none');
                    }
                    window.posicaoAntesZoom = { x: scrollX, y: scrollY,
                        carrossel: document.querySelector('.public-metric-charts').scrollLeft };
                }
                """, id);
            await botao.ClickAsync();
            await pagina.WaitForSelectorAsync(".plot-zoom-overlay");
            Assert.True(await pagina.EvaluateAsync<bool>("""
                id => {
                    const origem = Chart.getChart(id);
                    const zoom = Chart.getChart(document.querySelector('.plot--zoom'));
                    return zoom.data.datasets.every((serie, indice) =>
                        zoom.isDatasetVisible(indice) === origem.isDatasetVisible(indice)
                        && serie.segment === origem.data.datasets[indice].segment
                        && serie.borderColor === origem.data.datasets[indice].borderColor
                        && JSON.stringify(serie.data) === JSON.stringify(origem.data.datasets[indice].data))
                        && Object.keys(zoom.options.scales).join() === Object.keys(origem.options.scales).join()
                        && zoom.$chavesSincronizacao.length === origem.$chavesSincronizacao.length
                        && Math.abs(scrollY - window.posicaoAntesZoom.y) <= 1
                        && document.querySelector('.public-metric-charts').scrollLeft === window.posicaoAntesZoom.carrossel;
                }
                """, id));
            await pagina.Keyboard.PressAsync("Escape");
            await pagina.WaitForSelectorAsync(".plot-zoom-overlay", new() { State = WaitForSelectorState.Detached });
        }
        Assert.True(await pagina.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    }

    [Theory]
    [InlineData("precipitacao", "publicChartRain", "publicChartRainProbability", "precipitação")]
    [InlineData("probabilidadeChuva", "publicChartRainProbability", "publicChartRain", "chance de chuva")]
    public async Task ModoPublico_ChuvaSemDadosDeveTerEstadoVazioIndependente(string campo, string vazio, string disponivel, string rotulo)
    {
        await using var contexto = await CriarContextoAsync(autorizarInterno: false);
        var pagina = await contexto.NewPageAsync();
        var dados = JsonNode.Parse(CriarPublicWeatherComPrevisaoJson())!.AsObject();
        dados["climaAtual"]!["temperatura"] = null;
        dados["aqi"]!["valor"] = null;
        foreach (var ponto in dados["previsaoCurtoPrazo"]!.AsArray()) ponto![campo] = null;
        await pagina.RouteAsync("**/api/public-weather/cep/01001000", rota => rota.FulfillAsync(new()
        {
            Status = 200, ContentType = "application/json", Body = dados.ToJsonString()
        }));
        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.GetByRole(AriaRole.Textbox, new() { Name = "CEP" }).FillAsync("01001000");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Buscar" }).ClickAsync();
        await pagina.WaitForFunctionAsync("id => !!Chart.getChart(id)", disponivel);
        Assert.False(await pagina.EvaluateAsync<bool>("id => !!Chart.getChart(id)", vazio));
        Assert.Contains($"Sem dados de {rotulo}", await pagina.Locator(".public-metric-charts").InnerTextAsync());
        Assert.Equal("--", await pagina.Locator(".public-stats-grid .stats-card__value").First.InnerTextAsync());
        Assert.Equal("--", await pagina.Locator(".public-card--aqi .stats-card__value").InnerTextAsync());
    }

    [Theory]
    [InlineData(320)]
    [InlineData(360)]
    [InlineData(390)]
    public async Task CardsCompartilhados_TextoLongoDeveFicarAbaixoDoTituloSemSobreporValor(int largura)
    {
        await using var contexto = await CriarContextoAsync();
        var pagina = await CriarPaginaComDashboardMockadoAsync(contexto, []);
        await pagina.SetViewportSizeAsync(largura, 844);
        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.WaitForSelectorAsync(".stats-card__header .stats-card__trend", new() { State = WaitForSelectorState.Attached });
        Assert.True(await pagina.EvaluateAsync<bool>("""
            () => [...document.querySelectorAll('.stats-card__header')].filter(header => header.getBoundingClientRect().width > 0).every(header => {
                const titulo = header.querySelector('.stats-card__label');
                const status = header.querySelector('.stats-card__trend');
                if (!status || !titulo) return true;
                status.textContent = 'Insalubre para grupos sensíveis com exposição prolongada';
                const valor = header.closest('.stats-card').querySelector('.stats-card__value');
                const t = titulo.getBoundingClientRect(), s = status.getBoundingClientRect(), v = valor.getBoundingClientRect();
                return s.top >= t.bottom && v.top >= s.bottom
                    && status.scrollWidth <= status.clientWidth + 1
                    && s.right <= header.getBoundingClientRect().right + 1;
            })
            """));
        Assert.True(await pagina.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    }

    [Fact]
    public async Task ModoPublico_DeveExibirErroDeGeolocalizacaoEReabilitarControles()
    {
        await using var contexto = await CriarContextoAsync(autorizarInterno: false);
        await contexto.AddInitScriptAsync("""
            Object.defineProperty(navigator, 'geolocation', {
                configurable: true,
                value: { getCurrentPosition: (_, rejeitar) => rejeitar({ code: 1 }) }
            });
        """);
        var pagina = await contexto.NewPageAsync();

        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Usar localização" }).ClickAsync();

        await pagina.WaitForSelectorAsync("#publicResults >> text=Permissão de localização negada.");
        Assert.False(await pagina.Locator("#publicLocationButton").IsDisabledAsync());
        Assert.False(await pagina.Locator("#publicSearchInput").IsDisabledAsync());
        Assert.False(await pagina.GetByRole(AriaRole.Button, new() { Name = "Buscar" }).IsDisabledAsync());
    }

    [Fact]
    public async Task LocalizacaoPublicaEPrivada_DevemCarregarInsightsSomenteAposAcaoDoUsuario()
    {
        await using var contextoPublico = await CriarContextoAsync(autorizarInterno: false);
        await contextoPublico.GrantPermissionsAsync(["geolocation"], new() { Origin = _servidor.UrlBase.TrimEnd('/') });
        await contextoPublico.SetGeolocationAsync(new() { Latitude = -23.55f, Longitude = -46.63f, Accuracy = 20 });
        var paginaPublica = await contextoPublico.NewPageAsync();
        var consultasPublicas = 0;
        await paginaPublica.RouteAsync("**/api/public-weather/location**", rota =>
        {
            consultasPublicas++;
            return rota.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = JsonClimaPublico });
        });
        await paginaPublica.GotoAsync(_servidor.UrlBase);
        await paginaPublica.WaitForSelectorAsync("#publicApp", new() { State = WaitForSelectorState.Visible });
        Assert.Equal(0, consultasPublicas);
        await paginaPublica.Locator("#publicLocationButton").ClickAsync();
        await paginaPublica.WaitForSelectorAsync("#publicResults .environment-insight");
        Assert.Equal(1, consultasPublicas);

        await using var contextoPrivado = await CriarContextoAsync();
        await contextoPrivado.GrantPermissionsAsync(["geolocation"], new() { Origin = _servidor.UrlBase.TrimEnd('/') });
        await contextoPrivado.SetGeolocationAsync(new() { Latitude = -23.55f, Longitude = -46.63f, Accuracy = 20 });
        var paginaPrivada = await CriarPaginaComDashboardMockadoAsync(contextoPrivado, []);
        var consultasPrivadas = 0;
        await paginaPrivada.RouteAsync("**/api/public-weather/location**", rota =>
        {
            consultasPrivadas++;
            return rota.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = JsonClimaPublico });
        });
        await paginaPrivada.GotoAsync(_servidor.UrlBase);
        await paginaPrivada.WaitForSelectorAsync("#environmentLocationButton");
        Assert.Equal(0, consultasPrivadas);
        Assert.True(await paginaPrivada.Locator("#chart-container-rain").IsVisibleAsync());
        Assert.Equal(0, await paginaPrivada.Locator("#plotRain").EvaluateAsync<double>("el => el.getBoundingClientRect().height"));
        Assert.Null(await paginaPrivada.EvaluateAsync<object?>("() => montarClimaExternoJson()"));
        Assert.Contains("Sensores internos", await paginaPrivada.Locator("#environmentInsights").InnerTextAsync());
        await paginaPrivada.Locator("#environmentLocationButton").ClickAsync();
        await paginaPrivada.WaitForSelectorAsync("#environmentInsights >> text=Clima externo atualizado");
        await paginaPrivada.WaitForFunctionAsync("() => Boolean(window.Chart.getChart('plotRain'))");
        Assert.Equal(1, consultasPrivadas);
        Assert.Contains("Interior + exterior", await paginaPrivada.Locator("#environmentInsights").InnerTextAsync());
        Assert.Equal("Sem chuva agora", await paginaPrivada.Locator("#rainCurrentCondition").InnerTextAsync());
        var climaExportado = await paginaPrivada.EvaluateAsync<string>("() => JSON.stringify(montarClimaExternoJson())");
        using (var documentoClima = JsonDocument.Parse(climaExportado))
        {
            var origem = documentoClima.RootElement.GetProperty("origem");
            Assert.False(origem.TryGetProperty("latitude", out _));
            Assert.False(origem.TryGetProperty("longitude", out _));
            Assert.False(origem.TryGetProperty("precisao", out _));
            Assert.False(origem.TryGetProperty("cep", out _));
            Assert.True(documentoClima.RootElement.TryGetProperty("previsaoCurtoPrazo", out _));
        }
        Assert.Contains("Chuva · 24h + previsão 12h", await paginaPrivada.EvaluateAsync<string>("() => montarRelatorioPdf().cartoesGraficos.map(item => item.label).join('|')"));
    }

    [Fact]
    public async Task UsuarioForaDaAllowlist_DevePermanecerNoModoPublico()
    {
        await using var contexto = await CriarContextoAsync(autorizarInterno: false, emailTeste: "visitante@example.com");
        var pagina = await contexto.NewPageAsync();
        var requisicoesDashboard = 0;
        pagina.Request += (_, requisicao) =>
        {
            if (requisicao.Url.Contains("/api/dashboard", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref requisicoesDashboard);
            }
        };

        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.WaitForSelectorAsync("#publicApp", new() { State = WaitForSelectorState.Visible });

        Assert.Contains("visitante@example.com · acesso público", await pagina.Locator("#publicUserStatus").InnerTextAsync());
        Assert.True(await pagina.Locator("#privateApp").IsHiddenAsync());
        Assert.True(await pagina.Locator("#aiChat").IsHiddenAsync());
        Assert.Equal(0, requisicoesDashboard);
    }

    [Fact]
    public async Task ModoInterno_DeveSeguirAutorizacaoDaApiSemListaNoCliente()
    {
        await using var contexto = await CriarContextoAsync(autorizarInterno: false);
        var pagina = await CriarPaginaComDashboardMockadoAsync(contexto, []);
        var status = 200;
        var tokenRecebido = "";
        await pagina.RouteAsync("**/firebase-app.js", rota => rota.FulfillAsync(new()
        {
            ContentType = "text/javascript",
            Headers = new Dictionary<string, string> { ["Access-Control-Allow-Origin"] = "*" },
            Body = "export function initializeApp() { return {}; }"
        }));
        await pagina.RouteAsync("**/firebase-auth.js", rota => rota.FulfillAsync(new()
        {
            ContentType = "text/javascript",
            Headers = new Dictionary<string, string> { ["Access-Control-Allow-Origin"] = "*" },
            Body = """
                export function getAuth() { return {}; }
                export function onAuthStateChanged(auth, callback) {
                    window.__authCallback = callback;
                    callback({ email: 'usuario@example.test', getIdToken: async () => 'token-teste' });
                }
                """
        }));
        await pagina.RouteAsync("**/api/internal-access", rota =>
        {
            tokenRecebido = rota.Request.Headers.GetValueOrDefault("authorization") ?? "";
            return rota.FulfillAsync(new() { Status = status });
        });
        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.WaitForSelectorAsync("#privateApp", new() { State = WaitForSelectorState.Visible });
        Assert.True(await pagina.EvaluateAsync<bool>("() => ClimateAuth.ehUsuarioInterno()"));
        Assert.Equal("Bearer token-teste", tokenRecebido);

        status = 401;
        await pagina.EvaluateAsync("() => window.__authCallback({ email: 'usuario@example.test', getIdToken: async () => 'token-teste' })");
        await pagina.WaitForSelectorAsync("#publicApp", new() { State = WaitForSelectorState.Visible });
        Assert.False(await pagina.EvaluateAsync<bool>("() => ClimateAuth.ehUsuarioInterno()"));
        Assert.True(await pagina.Locator("#privateApp").IsHiddenAsync());
    }

    [Fact]
    public async Task Logout_DeveLimparDashboardGraficosERetornarAoModoPublico()
    {
        await using var contexto = await CriarContextoAsync();
        var pagina = await CriarPaginaComDashboardMockadoAsync(contexto, []);

        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.WaitForSelectorAsync("#statsEstacao .stats-card", new() { State = WaitForSelectorState.Attached, Timeout = 15_000 });
        Assert.True(await pagina.Locator("#authLogoutButton").IsVisibleAsync());

        await pagina.Locator("#authLogoutButton").ClickAsync();
        await pagina.WaitForSelectorAsync("#publicApp", new() { State = WaitForSelectorState.Visible });

        Assert.True(await pagina.Locator("#privateApp").IsHiddenAsync());
        Assert.True(await pagina.Locator("#aiChat").IsHiddenAsync());
        Assert.True(await pagina.Locator("#authLogoutButton").IsHiddenAsync());
        Assert.True(await pagina.EvaluateAsync<bool>("() => dashboardAtual === null && Object.keys(graficosInstancias).length === 0"));
    }

    [Fact]
    public async Task ControlesInternos_DevemAplicarDataAbasColapsaveisEZoom()
    {
        await using var contexto = await CriarContextoAsync();
        var pagina = await CriarPaginaComDashboardMockadoAsync(contexto, []);
        var urlsDashboard = new List<string>();
        pagina.Request += (_, requisicao) =>
        {
            if (requisicao.Url.Contains("/api/dashboard", StringComparison.OrdinalIgnoreCase))
            {
                lock (urlsDashboard) urlsDashboard.Add(requisicao.Url);
            }
        };

        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.WaitForSelectorAsync("#statsEstacao .stats-card", new() { State = WaitForSelectorState.Attached, Timeout = 15_000 });
        await pagina.Locator("#selectedDate").FillAsync("2026-06-03");
        await pagina.Locator("#selectedDate").DispatchEventAsync("change");
        await pagina.WaitForFunctionAsync("() => dataSelecionada === '03-06-2026'");

        Assert.Contains(urlsDashboard, url => url.Contains("date=03-06-2026", StringComparison.Ordinal));
        await pagina.Locator("button[data-tab-target=\"Tab1\"]").ClickAsync();
        Assert.Equal("Tab1", await pagina.EvaluateAsync<string>("() => localStorage.getItem('activeTab')"));
        var abaSala = pagina.Locator("button[data-tab-target=\"Tab1\"]");
        await abaSala.PressAsync("ArrowRight");
        Assert.Equal("Tab2", await pagina.EvaluateAsync<string>("() => obterAbaAtiva()"));
        await pagina.Locator("button[data-tab-target=\"Tab2\"]").PressAsync("Home");
        Assert.Equal("Tab0", await pagina.EvaluateAsync<string>("() => obterAbaAtiva()"));
        await pagina.Locator("button[data-tab-target=\"Tab0\"]").PressAsync("End");
        Assert.Equal("Tab3", await pagina.EvaluateAsync<string>("() => obterAbaAtiva()"));
        await abaSala.ClickAsync();

        var secao = pagina.Locator("#Tab1 .collapsible-section").First;
        var gatilho = secao.Locator(".collapsible-trigger");
        Assert.True(await secao.EvaluateAsync<bool>("el => el.classList.contains('is-collapsed')"));
        await gatilho.ClickAsync();
        Assert.False(await secao.EvaluateAsync<bool>("el => el.classList.contains('is-collapsed')"));
        Assert.Equal("true", await gatilho.GetAttributeAsync("aria-expanded"));

        await pagina.Locator("#chart-container-temp-sala .chart-zoom-button").ClickAsync();
        Assert.True(await pagina.Locator(".plot-zoom-overlay").IsVisibleAsync());
        await pagina.Keyboard.PressAsync("Escape");
        Assert.Equal(0, await pagina.Locator(".plot-zoom-overlay").CountAsync());
    }

    [Fact]
    public async Task ModulosGraficos_DevemPreservarJanelaChuvaENullAoSincronizar()
    {
        await using var contexto = await CriarContextoAsync();
        var pagina = await contexto.NewPageAsync();
        await pagina.GotoAsync(_servidor.UrlBase);

        var resultado = await pagina.EvaluateAsync<JsonElement>("""
            () => {
                const previsao = [
                    { horario: '2026-06-02T12:30:00-03:00', precipitacao: 1.2, probabilidadeChuva: null },
                    { horario: '2026-06-03T12:00:00-03:00', precipitacao: null, probabilidadeChuva: 20 },
                    { horario: '2026-06-03T13:00:00-03:00', precipitacao: 0.4, probabilidadeChuva: 70 },
                    { horario: '2026-06-04T01:00:00-03:00', precipitacao: 9, probabilidadeChuva: 100 }
                ];
                const janela = window.ClimaChuva.montarJanela(previsao, '2026-06-03T12:30:00-03:00');

                function criarGrafico(chaves, dados) {
                    const canvas = document.createElement('canvas');
                    document.body.appendChild(canvas);
                    return {
                        canvas,
                        data: { datasets: [{ data: dados }] },
                        $chavesSincronizacao: chaves,
                        setActiveElements(elementos) { this.ativos = elementos; },
                        tooltip: { setActiveElements(elementos) { this.ativos = elementos; } },
                        getDatasetMeta() { return { data: dados.map(() => ({ tooltipPosition: () => ({ x: 1, y: 1 }) })) }; },
                        scales: { x: { getPixelForValue: indice => indice } },
                        chartArea: { left: 0, top: 0 },
                        update() {}
                    };
                }

                const origem = criarGrafico(['2026-06-03T09:00:00', '2026-06-03T10:00:00'], [1, 2]);
                const proximo = criarGrafico(['2026-06-03T08:00:00', '2026-06-03T10:30:00'], [3, 4]);
                const distante = criarGrafico(['2026-06-03T12:00:00'], [null]);
                window.SincronizadorGraficos.registrar(origem, 'teste');
                window.SincronizadorGraficos.registrar(proximo, 'teste');
                window.SincronizadorGraficos.registrar(distante, 'teste');
                window.SincronizadorGraficos.tratarInteracao(origem, { type: 'mousemove' }, [{ index: 1 }]);

                return {
                    quantidadeJanela: janela.horarios.length,
                    tipos: janela.tipos,
                    nuloPreservado: janela.precipitacao[1] === null,
                    origemAtivada: Array.isArray(origem.ativos),
                    indiceProximo: proximo.ativos?.[0]?.index ?? -1,
                    ativosDistantes: distante.ativos?.length ?? -1
                };
            }
            """);

        Assert.Equal(3, resultado.GetProperty("quantidadeJanela").GetInt32());
        Assert.Equal(new[] { "observado", "observado", "previsao" }, resultado.GetProperty("tipos").EnumerateArray().Select(item => item.GetString()));
        Assert.True(resultado.GetProperty("nuloPreservado").GetBoolean());
        Assert.False(resultado.GetProperty("origemAtivada").GetBoolean());
        Assert.Equal(1, resultado.GetProperty("indiceProximo").GetInt32());
        Assert.Equal(0, resultado.GetProperty("ativosDistantes").GetInt32());
    }

    [Fact]
    public async Task GraficoChuvaMovel_DeveAlternarSeriesERestaurarDesktopSemDuplicarControle()
    {
        await using var contexto = await CriarContextoAsync();
        var pagina = await contexto.NewPageAsync();
        await pagina.SetViewportSizeAsync(390, 844);
        await pagina.GotoAsync(_servidor.UrlBase);

        var estadoInicial = await pagina.EvaluateAsync<JsonElement>("""
            () => {
                const recipiente = document.createElement('div');
                const canvas = document.createElement('canvas');
                canvas.id = 'chuvaTesteResponsivo';
                recipiente.style.width = '360px';
                recipiente.appendChild(canvas);
                document.body.appendChild(recipiente);
                const janela = window.ClimaChuva.montarJanela([
                    { horario: '2026-06-03T10:00:00-03:00', precipitacao: 1.2, probabilidadeChuva: null },
                    { horario: '2026-06-03T11:00:00-03:00', precipitacao: 0.4, probabilidadeChuva: null },
                    { horario: '2026-06-03T13:00:00-03:00', precipitacao: 0.8, probabilidadeChuva: 70 }
                ], '2026-06-03T12:00:00-03:00');
                window.graficoChuvaTeste = window.ClimaChuva.criarGrafico({ canvas, janela });
                return {
                    controles: recipiente.querySelectorAll('.rain-chart-toggle').length,
                    modo: recipiente.querySelector('[aria-pressed="true"]')?.dataset.rainMode,
                    ocultos: window.graficoChuvaTeste.data.datasets.map(serie => serie.hidden === true),
                    milimetros: window.graficoChuvaTeste.options.scales.yMilimetros.display,
                    probabilidade: window.graficoChuvaTeste.options.scales.yProbabilidade.display,
                    legenda: window.graficoChuvaTeste.options.plugins.legend.display,
                    ticks: window.graficoChuvaTeste.options.scales.x.ticks.maxTicksLimit
                };
            }
            """);

        Assert.Equal(1, estadoInicial.GetProperty("controles").GetInt32());
        Assert.Equal("precipitacao", estadoInicial.GetProperty("modo").GetString());
        Assert.Equal(new[] { false, false, true }, estadoInicial.GetProperty("ocultos").EnumerateArray().Select(item => item.GetBoolean()));
        Assert.True(estadoInicial.GetProperty("milimetros").GetBoolean());
        Assert.False(estadoInicial.GetProperty("probabilidade").GetBoolean());
        Assert.False(estadoInicial.GetProperty("legenda").GetBoolean());
        Assert.Equal(5, estadoInicial.GetProperty("ticks").GetInt32());

        await pagina.Locator("[data-rain-chart='chuvaTesteResponsivo'] [data-rain-mode='probabilidade']").ClickAsync();
        Assert.Equal(new[] { true, true, false }, await pagina.EvaluateAsync<bool[]>("() => window.graficoChuvaTeste.data.datasets.map(serie => serie.hidden === true)"));
        Assert.True(await pagina.EvaluateAsync<bool>("() => window.graficoChuvaTeste.options.scales.yProbabilidade.display"));

        await pagina.SetViewportSizeAsync(900, 900);
        await pagina.WaitForFunctionAsync("() => !document.querySelector('[data-rain-chart=\"chuvaTesteResponsivo\"]')");
        var estadoDesktop = await pagina.EvaluateAsync<JsonElement>("""
            () => ({
                ocultos: window.graficoChuvaTeste.data.datasets.map(serie => serie.hidden === true),
                milimetros: window.graficoChuvaTeste.options.scales.yMilimetros.display,
                probabilidade: window.graficoChuvaTeste.options.scales.yProbabilidade.display,
                legenda: window.graficoChuvaTeste.options.plugins.legend.display,
                ticks: window.graficoChuvaTeste.options.scales.x.ticks.maxTicksLimit
            })
            """);
        Assert.Equal(new[] { false, false, false }, estadoDesktop.GetProperty("ocultos").EnumerateArray().Select(item => item.GetBoolean()));
        Assert.True(estadoDesktop.GetProperty("milimetros").GetBoolean());
        Assert.True(estadoDesktop.GetProperty("probabilidade").GetBoolean());
        Assert.True(estadoDesktop.GetProperty("legenda").GetBoolean());
        Assert.Equal(13, estadoDesktop.GetProperty("ticks").GetInt32());
    }

    [Fact]
    public async Task ResponsividadeTabletECabecalhoMovel_DevemPreservarLayoutEFoco()
    {
        await using var contexto = await CriarContextoAsync();
        var pagina = await CriarPaginaComDashboardMockadoAsync(contexto, []);
        await pagina.SetViewportSizeAsync(768, 1024);
        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.WaitForSelectorAsync("#statsEstacao .stats-card", new() { State = WaitForSelectorState.Attached, Timeout = 15_000 });

        var tablet = await pagina.EvaluateAsync<JsonElement>("""
            () => {
                const abas = document.querySelector('.tab-nav').getBoundingClientRect();
                const toolbar = document.querySelector('.toolbar').getBoundingClientRect();
                return {
                    colunas: getComputedStyle(document.querySelector('.tab-nav')).gridTemplateColumns.split(' ').length,
                    toolbarAbaixo: abas.bottom <= toolbar.top + 1,
                    semOverflow: document.documentElement.scrollWidth <= innerWidth + 1,
                    alturaMinima: Math.min(...[...document.querySelectorAll('#privateApp .tablink, #privateApp .date-picker, #privateApp .toolbar-button')]
                        .filter(item => item.getClientRects().length > 0)
                        .map(item => item.getBoundingClientRect().height))
                };
            }
            """);
        Assert.Equal(4, tablet.GetProperty("colunas").GetInt32());
        Assert.True(tablet.GetProperty("toolbarAbaixo").GetBoolean());
        Assert.True(tablet.GetProperty("semOverflow").GetBoolean());
        Assert.True(tablet.GetProperty("alturaMinima").GetDouble() >= 44);

        await pagina.SetViewportSizeAsync(390, 844);
        await pagina.EvaluateAsync("window.scrollTo(0, 700)");
        await pagina.WaitForFunctionAsync("() => document.querySelector('.app-header').classList.contains('is-hidden-on-scroll')");
        await pagina.EvaluateAsync("window.scrollTo(0, 450)");
        await pagina.WaitForFunctionAsync("() => !document.querySelector('.app-header').classList.contains('is-hidden-on-scroll')");
        await pagina.Locator("#aqiIndicator").ClickAsync();
        await pagina.EvaluateAsync("window.scrollTo(0, 800)");
        await pagina.WaitForTimeoutAsync(100);
        Assert.False(await pagina.Locator(".app-header").EvaluateAsync<bool>("el => el.classList.contains('is-hidden-on-scroll')"));

        await pagina.Locator("#aiChatToggle").ClickAsync();
        var chat = await pagina.Locator("#aiChatPanel").EvaluateAsync<JsonElement>("""
            el => {
                const caixa = el.getBoundingClientRect();
                return { posicao: getComputedStyle(el).position, esquerda: caixa.left, direita: caixa.right, topo: caixa.top, base: caixa.bottom };
            }
            """);
        Assert.Equal("fixed", chat.GetProperty("posicao").GetString());
        Assert.True(chat.GetProperty("esquerda").GetDouble() >= 0);
        Assert.True(chat.GetProperty("direita").GetDouble() <= 390);
        Assert.True(chat.GetProperty("topo").GetDouble() >= 0);
        Assert.True(chat.GetProperty("base").GetDouble() <= 844);
    }

    [Fact]
    public async Task RefinamentosMoveis_DevemManterAqiChuvaSolarEHeatmapLegiveis()
    {
        await using var contexto = await CriarContextoAsync();
        var pagina = await CriarPaginaComDashboardMockadoAsync(contexto, []);
        await pagina.SetViewportSizeAsync(390, 844);
        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.WaitForSelectorAsync("#statsEstacao .stats-card", new() { State = WaitForSelectorState.Attached, Timeout = 15_000 });
        await pagina.WaitForFunctionAsync("() => Boolean(Chart.getChart('plotSolarToday'))");

        var medidas = await pagina.EvaluateAsync<JsonElement>("""
            () => {
                const indicador = document.querySelector('#aqiIndicator');
                const valor = indicador.querySelector('.aqi-indicator__value');
                const prefixo = indicador.querySelector('.aqi-indicator__prefix');
                valor.textContent = '123';
                const caixaIndicador = indicador.getBoundingClientRect();
                const caixaValor = valor.getBoundingClientRect();
                document.querySelector('#plotRain')?.removeAttribute('id');
                const canvasInterno = document.createElement('canvas');
                canvasInterno.id = 'plotRain';
                document.body.appendChild(canvasInterno);
                const canvasPublico = document.createElement('canvas');
                canvasPublico.id = 'publicChartRain';
                document.body.appendChild(canvasPublico);
                const estiloValor = getComputedStyle(valor);
                return {
                    prefixoVisivel: getComputedStyle(prefixo).display !== 'none',
                    valorVisivel: estiloValor.display !== 'none',
                    valorDentro: caixaValor.left >= caixaIndicador.left && caixaValor.right <= caixaIndicador.right
                        && caixaValor.top >= caixaIndicador.top && caixaValor.bottom <= caixaIndicador.bottom,
                    fundoValor: estiloValor.backgroundColor,
                    camadaValor: Number(estiloValor.zIndex),
                    chuvaInterna: canvasInterno.getBoundingClientRect().height,
                    chuvaPublica: canvasPublico.getBoundingClientRect().height,
                    pontosSolar: (() => {
                        const serie = Chart.getChart('plotSolarToday').data.datasets.find(item => item.label === 'Eventos solares');
                        return { raio: serie.pointRadius, toque: serie.pointHitRadius, hover: serie.pointHoverRadius };
                    })()
                };
            }
            """);

        Assert.True(medidas.GetProperty("prefixoVisivel").GetBoolean());
        Assert.True(medidas.GetProperty("valorVisivel").GetBoolean());
        Assert.True(medidas.GetProperty("valorDentro").GetBoolean());
        Assert.NotEqual("rgba(0, 0, 0, 0)", medidas.GetProperty("fundoValor").GetString());
        Assert.True(medidas.GetProperty("camadaValor").GetDouble() >= 2);
        Assert.Equal(200, medidas.GetProperty("chuvaInterna").GetDouble(), 1);
        Assert.Equal(200, medidas.GetProperty("chuvaPublica").GetDouble(), 1);
        Assert.Equal(7, medidas.GetProperty("pontosSolar").GetProperty("raio").GetDouble());
        Assert.Equal(24, medidas.GetProperty("pontosSolar").GetProperty("toque").GetDouble());
        Assert.Equal(9, medidas.GetProperty("pontosSolar").GetProperty("hover").GetDouble());

        await pagina.Locator("#plotSolarToday").ScrollIntoViewIfNeededAsync();
        var eventoSolar = await pagina.EvaluateAsync<string>("""
            async () => {
                const grafico = Chart.getChart('plotSolarToday');
                const elemento = grafico.getDatasetMeta(1).data[2];
                const caixa = grafico.canvas.getBoundingClientRect();
                grafico.canvas.dispatchEvent(new MouseEvent('mousemove', {
                    bubbles: true,
                    clientX: caixa.left + elemento.x + 5,
                    clientY: caixa.top + elemento.y
                }));
                await new Promise(resolve => setTimeout(resolve, 100));
                return grafico.tooltip?.dataPoints?.[0]?.raw?.label || '';
            }
            """);
        Assert.Equal("Zenite", eventoSolar);

        await pagina.Locator("#tabSala").ClickAsync();
        var mapa = pagina.Locator("#weeklyHeatmapSala");
        await mapa.EvaluateAsync("el => el.closest('.collapsible-section')?.classList.remove('is-collapsed')");
        await mapa.EvaluateAsync("el => { el.scrollLeft = el.scrollWidth; }");
        var estadoMapa = await mapa.EvaluateAsync<JsonElement>("""
            el => {
                const dia = el.querySelector('.weekly-heatmap__day');
                const canto = el.querySelector('.weekly-heatmap__axis--corner');
                const caixaMapa = el.getBoundingClientRect();
                const caixaDia = dia.getBoundingClientRect();
                const estiloDia = getComputedStyle(dia);
                return {
                    alinhado: Math.abs(caixaDia.left - caixaMapa.left) <= 1,
                    largura: caixaDia.width,
                    fundo: estiloDia.backgroundColor,
                    camadaDia: Number(estiloDia.zIndex),
                    camadaCanto: Number(getComputedStyle(canto).zIndex)
                };
            }
            """);
        Assert.True(estadoMapa.GetProperty("alinhado").GetBoolean());
        Assert.Equal(48, estadoMapa.GetProperty("largura").GetDouble(), 1);
        Assert.StartsWith("rgb(", estadoMapa.GetProperty("fundo").GetString());
        Assert.True(estadoMapa.GetProperty("camadaDia").GetDouble() >= 3);
        Assert.True(estadoMapa.GetProperty("camadaCanto").GetDouble() > estadoMapa.GetProperty("camadaDia").GetDouble());
    }

    [Fact]
    public async Task BotaoHoje_DeveAplicar03DeJunhoDe2026()
    {
        await using var contexto = await CriarContextoAsync();
        await contexto.AddInitScriptAsync("""
            {
                const DataReal = Date;
                const instante = new DataReal('2026-06-03T12:00:00-03:00').getTime();
                window.Date = class extends DataReal {
                    constructor(...args) { super(...(args.length ? args : [instante])); }
                    static now() { return instante; }
                };
            }
        """);
        var pagina = await CriarPaginaComDashboardMockadoAsync(contexto, []);
        var urlsDashboard = new List<string>();
        pagina.Request += (_, requisicao) =>
        {
            if (requisicao.Url.Contains("/api/dashboard", StringComparison.OrdinalIgnoreCase))
            {
                lock (urlsDashboard) urlsDashboard.Add(requisicao.Url);
            }
        };

        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.WaitForSelectorAsync("#statsEstacao .stats-card", new() { State = WaitForSelectorState.Attached, Timeout = 15_000 });
        await pagina.Locator("#btnToday").ClickAsync();
        await pagina.WaitForFunctionAsync("() => dataSelecionada === '03-06-2026'");

        Assert.Equal("2026-06-03", await pagina.Locator("#selectedDate").InputValueAsync());
        Assert.Contains(urlsDashboard, url => url.Contains("date=03-06-2026", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DashboardSemDados_DeveExibirMensagensEspecificasDoOriginal()
    {
        await using var contexto = await CriarContextoAsync();
        var pagina = await contexto.NewPageAsync();
        await pagina.RouteAsync("**/api/dashboard**", rota => rota.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = CriarDashboardSemDadosJson()
        }));

        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.Locator("#selectedDate").FillAsync("2026-06-03");
        await pagina.Locator("#selectedDate").DispatchEventAsync("change");
        await pagina.WaitForFunctionAsync("() => dataSelecionada === '03-06-2026'");
        await pagina.WaitForSelectorAsync("#dataSala >> text=Sem registros da sala em 03/06/2026.", new() { State = WaitForSelectorState.Attached });

        Assert.Equal("Sem registros da sala em 03/06/2026.", (await pagina.Locator("#dataSala").InnerTextAsync()).Trim());
        Assert.Equal("Sem registros de temperatura em 03/06/2026.", (await pagina.Locator("#data").InnerTextAsync()).Trim());
        Assert.Equal("Sem registros do aquário em 03/06/2026.", (await pagina.Locator("#dataAquario").InnerTextAsync()).Trim());
        Assert.Contains("Sem dados de temperatura da sala em 03/06/2026.", await pagina.Locator("#chartsSala").InnerTextAsync());
        Assert.Contains("Sem dados de sensação térmica em 03/06/2026.", await pagina.Locator("#chartsQuarto").InnerTextAsync());
        Assert.Contains("Sem dados de temperatura do aquário em 03/06/2026.", await pagina.Locator("#chartsAquario").InnerTextAsync());
    }

    [Fact]
    public async Task FalhaDaApi_DeveExibirEstadoDeErroDoOriginalELimparCarregamento()
    {
        await using var contexto = await CriarContextoAsync();
        var pagina = await contexto.NewPageAsync();
        await pagina.RouteAsync("**/api/dashboard**", rota => rota.FulfillAsync(new()
        {
            Status = 503,
            ContentType = "application/json",
            Body = "{\"error\":\"indisponível\"}"
        }));

        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.WaitForSelectorAsync("#dataSala >> text=Falha ao carregar o Firebase.", new() { State = WaitForSelectorState.Attached, Timeout = 15_000 });

        Assert.Equal(3, await pagina.Locator("#dataSala, #data, #dataAquario").CountAsync());
        Assert.All(await pagina.Locator("#dataSala, #data, #dataAquario").AllInnerTextsAsync(),
            texto => Assert.Contains("Falha ao carregar o Firebase. Verifique a conexão com a internet.", texto));
        Assert.Contains("Falha ao carregar o Firebase.", await pagina.Locator("#chart-container-sun").InnerTextAsync());
        Assert.Contains("Falha ao carregar o Firebase.", await pagina.Locator("#chart-container-sun-today").InnerTextAsync());
        Assert.False(await pagina.Locator("#loadingBar").EvaluateAsync<bool>("el => el.classList.contains('is-active')"));
    }

    [Fact]
    public async Task ModoInternoMobile_NaoDeveCriarRolagemHorizontal()
    {
        await using var contexto = await CriarContextoAsync();
        var pagina = await CriarPaginaComDashboardMockadoAsync(contexto, []);
        await pagina.SetViewportSizeAsync(390, 844);

        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.Locator("#selectedDate").FillAsync("2026-06-03");
        await pagina.Locator("#selectedDate").DispatchEventAsync("change");
        await pagina.WaitForFunctionAsync("() => dataSelecionada === '03-06-2026'");

        foreach (var aba in new[] { "Tab0", "Tab1", "Tab2", "Tab3" })
        {
            await pagina.Locator($"[data-tab-target='{aba}']").ClickAsync();
            Assert.True(await pagina.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"));
        }
        Assert.True(await pagina.Locator("#authLogoutButton").IsVisibleAsync());
        Assert.True(await pagina.Locator("#selectedDate").IsVisibleAsync());
        Assert.True(await pagina.Locator("#btnToday").IsVisibleAsync());
    }

    [Fact]
    public async Task ModoInternoCelularPaisagem_DeveSepararAbasEToolbarSemPerderInteracoes()
    {
        await using var contexto = await CriarContextoAsync();
        var pagina = await CriarPaginaComDashboardMockadoAsync(contexto, []);
        await pagina.SetViewportSizeAsync(720, 360);

        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.WaitForSelectorAsync("#statsEstacao .stats-card", new() { State = WaitForSelectorState.Attached, Timeout = 15_000 });
        await pagina.Locator("#selectedDate").FillAsync("2026-06-03");
        await pagina.Locator("#selectedDate").DispatchEventAsync("change");
        await pagina.WaitForFunctionAsync("() => dataSelecionada === '03-06-2026'");

        var geometria = await pagina.EvaluateAsync<string>("""
            () => {
                const retangulo = seletor => {
                    const caixa = document.querySelector(seletor).getBoundingClientRect();
                    return { left: caixa.left, right: caixa.right, top: caixa.top, bottom: caixa.bottom, width: caixa.width };
                };
                const sobrepoe = (a, b) => a.left < b.right - 1 && a.right > b.left + 1
                    && a.top < b.bottom - 1 && a.bottom > b.top + 1;
                const toolbar = retangulo('.toolbar');
                const navegacao = retangulo('.tab-nav');
                const abas = [...document.querySelectorAll('.tablink')].map(elemento => {
                    const caixa = elemento.getBoundingClientRect();
                    return { left: caixa.left, right: caixa.right, top: caixa.top, bottom: caixa.bottom, width: caixa.width };
                });
                const controles = [...document.querySelectorAll('.toolbar-group > *')].map(elemento => {
                    const caixa = elemento.getBoundingClientRect();
                    return { left: caixa.left, right: caixa.right, top: caixa.top, bottom: caixa.bottom, width: caixa.width };
                });
                return JSON.stringify({
                    media: matchMedia('(orientation: landscape) and (max-height: 540px) and (max-width: 900px)').matches,
                    colunas: getComputedStyle(document.querySelector('.tab-nav')).gridTemplateColumns.split(' ').length,
                    navegacaoAntesToolbar: navegacao.bottom <= toolbar.top + 1,
                    navegacaoLarguraTotal: Math.abs(navegacao.width - document.querySelector('.tab-toolbar').getBoundingClientRect().width) <= 1,
                    abasEquivalentes: Math.max(...abas.map(aba => aba.width)) - Math.min(...abas.map(aba => aba.width)) <= 1,
                    abasSemSobreposicao: abas.every((aba, indice) => abas.slice(indice + 1).every(outra => !sobrepoe(aba, outra))),
                    controlesSemSobreposicao: controles.every((controle, indice) => controles.slice(indice + 1).every(outro => !sobrepoe(controle, outro))),
                    toolbarSemOverflow: document.querySelector('.toolbar-group').scrollWidth <= document.querySelector('.toolbar-group').clientWidth + 1,
                    paginaSemOverflow: document.documentElement.scrollWidth <= window.innerWidth + 1
                });
            }
            """);
        using (var documento = JsonDocument.Parse(geometria))
        {
            var raiz = documento.RootElement;
            Assert.True(raiz.GetProperty("media").GetBoolean());
            Assert.Equal(4, raiz.GetProperty("colunas").GetInt32());
            Assert.True(raiz.GetProperty("navegacaoAntesToolbar").GetBoolean());
            Assert.True(raiz.GetProperty("navegacaoLarguraTotal").GetBoolean());
            Assert.True(raiz.GetProperty("abasEquivalentes").GetBoolean());
            Assert.True(raiz.GetProperty("abasSemSobreposicao").GetBoolean());
            Assert.True(raiz.GetProperty("controlesSemSobreposicao").GetBoolean());
            Assert.True(raiz.GetProperty("toolbarSemOverflow").GetBoolean());
            Assert.True(raiz.GetProperty("paginaSemOverflow").GetBoolean());
        }

        var abaEstacao = pagina.Locator("#tabEstacao");
        await abaEstacao.PressAsync("ArrowRight");
        Assert.Equal("Tab1", await pagina.EvaluateAsync<string>("() => obterAbaAtiva()"));
        Assert.Equal("true", await pagina.Locator("#tabSala").GetAttributeAsync("aria-selected"));
        Assert.Equal("tabSala", await pagina.EvaluateAsync<string>("() => document.activeElement?.id"));
        await pagina.Locator("#tabSala").PressAsync("ArrowLeft");
        await pagina.Locator("#tabEstacao").PressAsync("End");
        Assert.Equal("Tab3", await pagina.EvaluateAsync<string>("() => obterAbaAtiva()"));
        await pagina.Locator("#tabAquario").PressAsync("Home");
        Assert.Equal("Tab0", await pagina.EvaluateAsync<string>("() => obterAbaAtiva()"));

        await pagina.EvaluateAsync("""
            () => {
                const container = document.querySelector('.container');
                const disparar = (tipo, x) => {
                    const evento = new Event(tipo, { bubbles: true, cancelable: true });
                    const toque = [{ clientX: x, clientY: 160 }];
                    Object.defineProperty(evento, tipo === 'touchstart' ? 'touches' : 'changedTouches', { value: toque });
                    container.dispatchEvent(evento);
                };
                disparar('touchstart', 600);
                disparar('touchend', 400);
            }
            """);
        Assert.Equal("Tab1", await pagina.EvaluateAsync<string>("() => obterAbaAtiva()"));

        await pagina.Locator("label[for='exportFormatJson']").ClickAsync();
        Assert.Equal("Exportar JSON", await pagina.Locator("#btnExportData").InnerTextAsync());
        await pagina.Locator("label[for='exportFormatPdf']").ClickAsync();
        Assert.Equal("Exportar PDF", await pagina.Locator("#btnExportData").InnerTextAsync());

        await pagina.Locator("#aiChatToggle").ClickAsync();
        Assert.Equal("true", await pagina.Locator("#aiChatToggle").GetAttributeAsync("aria-expanded"));
        await pagina.WaitForFunctionAsync("() => document.activeElement?.id === 'aiChatInput'");
        Assert.Equal("aiChatInput", await pagina.EvaluateAsync<string>("() => document.activeElement?.id"));
        await pagina.Locator("#aiChatToggle").ClickAsync();

        await pagina.Locator("#astroIndicator").ClickAsync();
        Assert.True(await pagina.Locator("#astroPopover").IsVisibleAsync());
        await pagina.Locator("#aqiIndicator").ClickAsync();
        Assert.True(await pagina.Locator("#aqiPopover").IsVisibleAsync());
        Assert.True(await pagina.Locator("#astroPopover").IsHiddenAsync());

        Assert.Equal("2026-06-03", await pagina.Locator("#selectedDate").InputValueAsync());
        Assert.True(await pagina.Locator("#tabSala").EvaluateAsync<bool>("el => el.classList.contains('active')"));
    }

    [Fact]
    public async Task RegressaoVisualSolar_DeveGerarEvidenciasSemOverflowNasLargurasObrigatorias()
    {
        await using var contexto = await CriarContextoAsync();
        var pagina = await CriarPaginaComDashboardMockadoAsync(contexto, []);
        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.Locator("#selectedDate").FillAsync("2026-06-03");
        await pagina.Locator("#selectedDate").DispatchEventAsync("change");
        await pagina.WaitForFunctionAsync("() => dataSelecionada === '03-06-2026'");

        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "EstacaoClimatica.sln"))) raiz = raiz.Parent;
        var pasta = Path.Combine(raiz?.FullName ?? AppContext.BaseDirectory, "outputs", "ui-ux-responsiva-2026-09-22", "estacao");
        Directory.CreateDirectory(pasta);

        foreach (var (largura, altura, nome) in new[]
        {
            (1440, 1000, "desktop-1440x1000"),
            (960, 1024, "tablet-960x1024"),
            (768, 1024, "tablet-768x1024"),
            (390, 844, "mobile-390x844"),
            (360, 800, "mobile-360x800"),
            (320, 700, "mobile-320x700"),
            (720, 360, "paisagem-720x360")
        })
        {
            await pagina.SetViewportSizeAsync(largura, altura);
            await pagina.WaitForTimeoutAsync(100);
            Assert.True(await pagina.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"));
            if (largura <= 640 || (largura <= 900 && altura <= 540))
            {
                Assert.Equal("static", await pagina.Locator("#solarDayDuration").EvaluateAsync<string>("el => getComputedStyle(el).position"));
            }
            await pagina.ScreenshotAsync(new() { Path = Path.Combine(pasta, $"estacao-{nome}.png"), FullPage = true });
        }
    }

    [Fact]
    public async Task ModoPublicoMobile_NaoDeveCriarRolagemHorizontal()
    {
        await using var contexto = await CriarContextoAsync(autorizarInterno: false);
        var pagina = await contexto.NewPageAsync();
        await pagina.SetViewportSizeAsync(390, 844);

        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.WaitForSelectorAsync("#publicApp", new() { State = WaitForSelectorState.Visible });

        var estouro = await pagina.EvaluateAsync<string>("""
            () => JSON.stringify([...document.querySelectorAll('body *')]
                .map(elemento => ({
                    seletor: elemento.id ? `#${elemento.id}` : elemento.className,
                    esquerda: Math.round(elemento.getBoundingClientRect().left),
                    direita: Math.round(elemento.getBoundingClientRect().right)
                }))
                .filter(item => item.esquerda < -1 || item.direita > window.innerWidth + 1))
            """);
        Assert.Equal("[]", estouro);
        Assert.True(await pagina.Locator("#publicLocationButton").IsVisibleAsync());
        Assert.True(await pagina.Locator("#publicSearchInput").IsVisibleAsync());
        Assert.True(await pagina.GetByRole(AriaRole.Button, new() { Name = "Buscar" }).IsVisibleAsync());
    }

    [Fact]
    public async Task ModoPublicoMobile_AposConsultaDeveCompactarHeroEPermitirAlterarLocal()
    {
        await using var contexto = await CriarContextoAsync(autorizarInterno: false);
        var pagina = await contexto.NewPageAsync();
        await pagina.SetViewportSizeAsync(390, 844);
        await pagina.RouteAsync("**/api/public-weather/cep/01001000", rota => rota.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = JsonClimaPublico
        }));

        await pagina.GotoAsync(_servidor.UrlBase);
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "EstacaoClimatica.sln"))) raiz = raiz.Parent;
        var pasta = Path.Combine(raiz?.FullName ?? AppContext.BaseDirectory, "outputs", "ui-ux-responsiva-2026-09-22", "publico");
        Directory.CreateDirectory(pasta);
        var viewports = new[]
        {
            (1440, 1000, "1440x1000"), (960, 1024, "960x1024"), (768, 1024, "768x1024"),
            (390, 844, "390x844"), (360, 800, "360x800"), (720, 360, "720x360")
        };
        foreach (var (largura, altura, nome) in viewports)
        {
            await pagina.SetViewportSizeAsync(largura, altura);
            await pagina.ScreenshotAsync(new() { Path = Path.Combine(pasta, $"publico-inicial-{nome}.png"), FullPage = true });
        }
        await pagina.SetViewportSizeAsync(390, 844);
        await pagina.GetByRole(AriaRole.Textbox, new() { Name = "CEP" }).FillAsync("01001000");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Buscar" }).ClickAsync();
        await pagina.WaitForSelectorAsync("#publicChartTemperature", new() { State = WaitForSelectorState.Attached });

        foreach (var (largura, altura, nome) in viewports)
        {
            await pagina.SetViewportSizeAsync(largura, altura);
            await pagina.WaitForTimeoutAsync(100);
            Assert.True(await pagina.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
            await pagina.ScreenshotAsync(new() { Path = Path.Combine(pasta, $"publico-preenchido-{nome}.png"), FullPage = true });
        }
        await pagina.SetViewportSizeAsync(390, 844);

        Assert.True(await pagina.Locator("#publicApp").EvaluateAsync<bool>("el => el.classList.contains('has-results')"));
        Assert.True(await pagina.Locator(".public-hero").IsHiddenAsync());
        Assert.Equal(2, await pagina.Locator(".public-stats-grid").EvaluateAsync<int>("el => getComputedStyle(el).gridTemplateColumns.split(' ').length"));
        Assert.Equal("1 / -1", await pagina.Locator(".public-card--aqi").EvaluateAsync<string>("el => getComputedStyle(el).gridColumn"));
        Assert.Equal("x mandatory", await pagina.Locator(".public-metric-charts").EvaluateAsync<string>("el => getComputedStyle(el).scrollSnapType"));
        Assert.True(await pagina.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
        Assert.Equal(0, await pagina.Locator("#publicResults .rain-chart-toggle").CountAsync());
        Assert.Equal(200, await pagina.Locator("#publicChartRain").EvaluateAsync<double>("el => el.getBoundingClientRect().height"), 1);
        Assert.Equal("42", await pagina.Locator("#aqiIndicator .aqi-indicator__value").InnerTextAsync());
        var pontosSolaresPublicos = await pagina.EvaluateAsync<double[]>("""
            () => {
                const serie = Chart.getChart('publicChartSolar').data.datasets.find(item => item.label === 'Eventos solares');
                return [serie.pointRadius, serie.pointHitRadius, serie.pointHoverRadius];
            }
            """);
        Assert.Equal(new[] { 7d, 24d, 9d }, pontosSolaresPublicos);

        await pagina.GetByRole(AriaRole.Button, new() { Name = "Alterar local" }).ClickAsync();
        await pagina.WaitForFunctionAsync("() => document.activeElement?.id === 'publicSearchInput'");
        Assert.False(await pagina.Locator("#publicApp").EvaluateAsync<bool>("el => el.classList.contains('has-results')"));
        Assert.True(await pagina.Locator(".public-hero").IsVisibleAsync());
    }

    [Fact]
    public async Task RelatorioPdf_DevePreservarContratoPorAbaEGerarCicloSolarOffscreen()
    {
        await using var contexto = await CriarContextoAsync();
        var pagina = await CriarPaginaComDashboardMockadoAsync(contexto, []);

        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.WaitForSelectorAsync("#statsEstacao .stats-card", new() { State = WaitForSelectorState.Attached, Timeout = 15_000 });

        var contrato = await pagina.EvaluateAsync<string>(
            @"() => {
                const montar = aba => {
                    window.abrirAba(aba);
                    const relatorio = window.montarRelatorioPdf();
                    const graficos = relatorio.cartoesGraficos.map(cartao => ({
                        titulo: cartao.label,
                        possuiImagem: Boolean(cartao.image)
                    }));

                    return {
                        aba: relatorio.configuracao.rotulo,
                        incluirSolar: relatorio.configuracao.incluirSolar,
                        resumo: relatorio.cartoesResumo.map(cartao => cartao.label),
                        graficos,
                        tabela: relatorio.metricasTabela.map(metrica => metrica.label)
                    };
                };

                return JSON.stringify({
                    estacao: montar('Tab0'),
                    sala: montar('Tab1'),
                    quarto: montar('Tab2'),
                    aquario: montar('Tab3')
                });
            }");

        using var documento = JsonDocument.Parse(contrato);
        var estacao = documento.RootElement.GetProperty("estacao");
        var sala = documento.RootElement.GetProperty("sala");
        var quarto = documento.RootElement.GetProperty("quarto");
        var aquario = documento.RootElement.GetProperty("aquario");

        static string[] LerStrings(JsonElement elemento, string propriedade) =>
            elemento.GetProperty(propriedade).EnumerateArray().Select(item => item.GetString() ?? "").ToArray();

        static string[] LerTitulosGraficos(JsonElement elemento) =>
            elemento.GetProperty("graficos").EnumerateArray().Select(item => item.GetProperty("titulo").GetString() ?? "").ToArray();

        static bool GraficoTemImagem(JsonElement elemento, string titulo) =>
            elemento.GetProperty("graficos").EnumerateArray()
                .First(item => item.GetProperty("titulo").GetString() == titulo)
                .GetProperty("possuiImagem")
                .GetBoolean();

        Assert.Equal("Estação", estacao.GetProperty("aba").GetString());
        Assert.True(estacao.GetProperty("incluirSolar").GetBoolean());
        Assert.Contains("Estação do ano", LerStrings(estacao, "resumo"));
        Assert.Contains("Fase da lua", LerStrings(estacao, "resumo"));
        Assert.Contains("AQI estimado", LerStrings(estacao, "resumo"));
        Assert.Contains("Temp. Sala", LerStrings(estacao, "resumo"));
        Assert.Contains("Temp. Quarto", LerStrings(estacao, "resumo"));
        Assert.Contains("Temp. Aquário", LerStrings(estacao, "resumo"));
        Assert.Contains("Temperatura por Ambiente", LerTitulosGraficos(estacao));
        Assert.Contains("Umidade por Ambiente", LerTitulosGraficos(estacao));
        Assert.Contains("Ciclo solar", LerTitulosGraficos(estacao));
        Assert.True(GraficoTemImagem(estacao, "Ciclo solar"));
        Assert.Empty(LerStrings(estacao, "tabela"));

        // Sala usa somente gráficos ambientais e tabela MQ135 com Tolueno; solar fica na aba Estação.
        Assert.False(sala.GetProperty("incluirSolar").GetBoolean());
        Assert.Contains("Temperatura x Sensação Térmica", LerTitulosGraficos(sala));
        Assert.Contains(LerTitulosGraficos(sala), titulo => titulo.StartsWith("Pressão", StringComparison.Ordinal));
        Assert.DoesNotContain("Ciclo solar", LerTitulosGraficos(sala));
        Assert.Equal(["CO", "CO2", "Acetona", "Álcool", "Amônia", "Tolueno"], LerStrings(sala, "tabela"));

        Assert.False(quarto.GetProperty("incluirSolar").GetBoolean());
        Assert.DoesNotContain("Ciclo solar", LerTitulosGraficos(quarto));
        Assert.Equal(["Temperatura", "Sensação térmica", "Umidade"], LerStrings(quarto, "tabela"));

        Assert.False(aquario.GetProperty("incluirSolar").GetBoolean());
        Assert.Equal(["Temperatura", "PH", "TDS", "Turbidez"], LerStrings(aquario, "resumo"));
        Assert.Equal(["Temperatura", "PH", "TDS", "Turbidez"], LerTitulosGraficos(aquario));
        Assert.Equal(["Temperatura", "PH", "TDS", "Turbidez"], LerStrings(aquario, "tabela"));
    }

    [Fact]
    public async Task RelatorioPdf_DeveIndicarAusenciaDeMedicaoSemChamarDeEstavel()
    {
        await using var contexto = await CriarContextoAsync();
        var pagina = await CriarPaginaComDashboardMockadoAsync(contexto, []);
        await pagina.GotoAsync(_servidor.UrlBase);

        var resultado = await pagina.EvaluateAsync<string>("""
            () => {
                const metricas = [{ index: 2, key: 'co', label: 'CO' }, { index: 3, key: 'co2', label: 'CO2' }];
                const linhas = montarLinhasTabelaPdf({ table: { rows: [['03/06/2026', '09:30', '--', '450ppm']] } }, metricas);
                const painel = criarPainelAlertasPdf([], [{ status: 'Sem dados' }]);
                return JSON.stringify({
                    status: linhas[0].status,
                    painel: painel.textContent,
                    statusNulo: obterStatusMetricaPdf('CO', null),
                    resumoNulo: montarResumoMetricaPdf({ key: 'co', label: 'CO' }, { label: 'CO', average: null }).current,
                    pontos: obterPontosGraficoPdf({ points: [
                        { label: 'ausente', value: null }, { label: 'vazio', value: '' }, { label: 'zero', value: 0 }
                    ] }).map(ponto => ponto.label)
                });
            }
            """);

        using var documento = JsonDocument.Parse(resultado);
        Assert.Equal("Sem dados", documento.RootElement.GetProperty("status").GetString());
        Assert.Equal("Sem dados", documento.RootElement.GetProperty("statusNulo").GetString());
        Assert.Equal("--", documento.RootElement.GetProperty("resumoNulo").GetString());
        Assert.Equal(["zero"], documento.RootElement.GetProperty("pontos").EnumerateArray().Select(ponto => ponto.GetString()));
        Assert.Contains("Sem medições", documento.RootElement.GetProperty("painel").GetString());
        Assert.DoesNotContain("Estável", documento.RootElement.GetProperty("painel").GetString());
    }

    [Fact]
    public async Task RelatorioPdf_DevePaginarTabelaLongaSemCortarLinhas()
    {
        await using var contexto = await CriarContextoAsync();
        var pagina = await CriarPaginaComDashboardMockadoAsync(contexto, []);
        await pagina.GotoAsync(_servidor.UrlBase);

        var resultado = await pagina.EvaluateAsync<string>("""
            async () => {
                await carregarDependenciasPdf();
                const raiz = document.createElement('div');
                raiz.className = 'pdf-render-root';
                raiz.innerHTML = '<table class="pdf-table"><thead><tr><th>Horário</th><th>Valor</th></tr></thead><tbody></tbody></table>';
                document.body.appendChild(raiz);
                const tabela = raiz.querySelector('table');
                for (let i = 0; i < 100; i++) {
                    const linha = document.createElement('tr');
                    linha.innerHTML = `<td>linha-${i}</td><td>valor-${i}</td>`;
                    tabela.tBodies[0].appendChild(linha);
                }
                const capturas = [];
                const imagens = [];
                const original = window.html2canvas;
                window.html2canvas = async (elemento, opcoes) => {
                    capturas.push({
                        cabecalho: elemento.tHead?.textContent.includes('Horário'),
                        linhas: [...elemento.tBodies[0].rows].map(linha => linha.cells[0].textContent)
                    });
                    return original(elemento, opcoes);
                };
                let paginas = 1;
                const pdf = {
                    addPage() { paginas++; }, setFillColor() {}, rect() {},
                    addImage(_, __, x, y, largura, altura) { imagens.push({ x, y, largura, altura }); }
                };
                try {
                    await adicionarTabelaPdf(pdf, tabela, criarLeiautePdf(), 16);
                    return JSON.stringify({ capturas, imagens, paginas });
                } finally {
                    window.html2canvas = original;
                    raiz.remove();
                }
            }
            """);

        using var documento = JsonDocument.Parse(resultado);
        var capturas = documento.RootElement.GetProperty("capturas").EnumerateArray().ToArray();
        var imagens = documento.RootElement.GetProperty("imagens").EnumerateArray().ToArray();
        Assert.True(capturas.Length > 1);
        Assert.All(capturas, captura => Assert.True(captura.GetProperty("cabecalho").GetBoolean()));
        Assert.Equal(Enumerable.Range(0, 100).Select(indice => $"linha-{indice}"),
            capturas.SelectMany(captura => captura.GetProperty("linhas").EnumerateArray().Select(linha => linha.GetString())));
        Assert.Equal(capturas.Length, imagens.Length);
        Assert.Equal(capturas.Length, documento.RootElement.GetProperty("paginas").GetInt32());
        Assert.All(imagens, imagem => Assert.True(imagem.GetProperty("y").GetDouble() + imagem.GetProperty("altura").GetDouble() <= 281.01));
    }

    [Fact]
    public async Task RelatorioPdf_DeveFalharQuandoCssNaoCarrega()
    {
        await using var contexto = await CriarContextoAsync();
        var pagina = await CriarPaginaComDashboardMockadoAsync(contexto, []);
        await pagina.RouteAsync("**/pdf-report.css*", rota => rota.AbortAsync());
        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.WaitForSelectorAsync("#statsEstacao .stats-card", new() { State = WaitForSelectorState.Attached });

        await pagina.Locator("#btnExportData").ClickAsync();
        await pagina.WaitForSelectorAsync("#exportFeedback.is-error");

        Assert.Contains("Falha ao exportar", await pagina.Locator("#exportStatus").InnerTextAsync());
        Assert.Equal("undefined", await pagina.EvaluateAsync<string>("() => typeof window.html2canvas"));
    }

    [Fact]
    public async Task ExportacaoJson_DeveBaixarArquivoComMetadadosResumoTabelaEDadosDaAba()
    {
        await using var contexto = await CriarContextoAsync();
        var pagina = await CriarPaginaComDashboardMockadoAsync(contexto, []);

        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.WaitForSelectorAsync("#statsEstacao .stats-card", new() { State = WaitForSelectorState.Attached, Timeout = 15_000 });
        await pagina.Locator("label[for=\"exportFormatJson\"]").ClickAsync();

        // Valida o download real disparado pelo navegador, não apenas a função JavaScript isolada.
        var download = await pagina.RunAndWaitForDownloadAsync(
            () => pagina.Locator("#btnExportData").ClickAsync(),
            new() { Timeout = 15_000 });

        Assert.EndsWith(".json", download.SuggestedFilename, StringComparison.OrdinalIgnoreCase);
        var caminho = await download.PathAsync();
        Assert.False(string.IsNullOrWhiteSpace(caminho));

        using var documento = JsonDocument.Parse(await File.ReadAllTextAsync(caminho));
        var raiz = documento.RootElement;

        // O JSON precisa conter o relatório amigável e também os dados processados da aba ativa.
        Assert.Equal("Estação Climática", raiz.GetProperty("relatorio").GetString());
        Assert.Equal("Estação", raiz.GetProperty("aba").GetString());
        Assert.True(raiz.GetProperty("resumo").GetArrayLength() > 0);
        Assert.Equal(0, raiz.GetProperty("tabela").GetArrayLength());
        Assert.Equal(0, raiz.GetProperty("tabelaResumida").GetArrayLength());
        Assert.Equal(0, raiz.GetProperty("tabelaDetalhada").GetArrayLength());
        Assert.True(raiz.GetProperty("dadosDaAba").GetProperty("sala").GetProperty("charts").GetArrayLength() > 0);
        Assert.True(raiz.GetProperty("dadosDaAba").GetProperty("quarto").GetProperty("charts").GetArrayLength() > 0);
        Assert.True(raiz.GetProperty("dadosDaAba").GetProperty("aquario").GetProperty("charts").GetArrayLength() > 0);
        Assert.True(raiz.GetProperty("dadosDaAba").TryGetProperty("solar", out _));
        Assert.True(raiz.TryGetProperty("dadosBrutos", out _));
    }

    [Fact]
    public async Task ExportacaoPdf_DeveBaixarArquivoPdf()
    {
        var contexto = await CriarContextoAsync();
        try
        {
            var pagina = await CriarPaginaComDashboardMockadoAsync(contexto, []);

            await pagina.GotoAsync(_servidor.UrlBase);
            await pagina.WaitForSelectorAsync("#statsEstacao .stats-card", new() { State = WaitForSelectorState.Attached, Timeout = 15_000 });

            Assert.Equal("undefined", await pagina.EvaluateAsync<string>("() => typeof window.html2canvas"));
            Assert.Equal("undefined", await pagina.EvaluateAsync<string>("() => typeof window.jspdf"));

            // PDF envolve canvas, html2canvas e jsPDF; por isso o timeout é maior que o do JSON.
            var download = await pagina.RunAndWaitForDownloadAsync(
                () => pagina.Locator("#btnExportData").ClickAsync(),
                new() { Timeout = 45_000 });

            Assert.EndsWith(".pdf", download.SuggestedFilename, StringComparison.OrdinalIgnoreCase);
            var caminho = await download.PathAsync();
            Assert.False(string.IsNullOrWhiteSpace(caminho));

            var bytes = await File.ReadAllBytesAsync(caminho);
            Assert.True(bytes.Length > 1_000, "PDF gerado ficou pequeno demais para um relatorio valido.");
            Assert.Equal("%PDF"u8.ToArray(), bytes.Take(4).ToArray());
            Assert.Equal("function", await pagina.EvaluateAsync<string>("() => typeof window.html2canvas"));
            Assert.Equal("object", await pagina.EvaluateAsync<string>("() => typeof window.jspdf"));
        }
        finally
        {
            try
            {
                await contexto.DisposeAsync();
            }
            catch (PlaywrightException)
            {
                await ReiniciarNavegadorCompartilhadoAsync();
            }
        }
    }

    [Fact]
    public async Task Chat_DeveEnviarPerguntaParaEndpointCSharpERenderizarResposta()
    {
        await using var contexto = await CriarContextoAsync();
        var pagina = await CriarPaginaComDashboardMockadoAsync(contexto, []);
        string? corpoRequisicao = null;

        await pagina.RouteAsync("**/api/chat", rota =>
        {
            corpoRequisicao = rota.Request.PostData;
            return rota.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = """
                {
                  "answer": "Resposta mockada do endpoint C#.",
                  "source": "local",
                  "intent": { "environments": ["Sala"], "metrics": ["Temperatura"], "operation": "media", "period": "03/06/2026", "samples": 2 }
                }
                """
            });
        });

        await pagina.GotoAsync(_servidor.UrlBase);
        await pagina.WaitForSelectorAsync("#statsEstacao .stats-card", new() { State = WaitForSelectorState.Attached, Timeout = 15_000 });
        await pagina.Locator("#selectedDate").FillAsync("2026-06-03");
        await pagina.Locator("#selectedDate").DispatchEventAsync("change");
        await pagina.WaitForFunctionAsync("() => dataSelecionada === '03-06-2026'");
        await pagina.Locator("#aiChatToggle").ClickAsync();
        await pagina.Locator("#aiChatInput").FillAsync("Qual foi a temperatura média?");
        await pagina.Locator("#aiChatSubmit").ClickAsync();
        await pagina.WaitForSelectorAsync("#aiChatMessages >> text=Resposta mockada do endpoint C#.", new() { Timeout = 15_000 });

        Assert.NotNull(corpoRequisicao);
        using var documento = JsonDocument.Parse(corpoRequisicao!);
        var raiz = documento.RootElement;
        Assert.Equal("Qual foi a temperatura média?", raiz.GetProperty("question").GetString());
        Assert.Equal("Tab0", raiz.GetProperty("activeTab").GetString());
        Assert.Equal("03-06-2026", raiz.GetProperty("selectedDate").GetString());
    }

    private static string CriarPublicWeatherCidadeJson(string rotulo)
    {
        var raiz = JsonNode.Parse(JsonClimaPublico)?.AsObject()
            ?? throw new InvalidOperationException("Clima público de teste inválido.");
        raiz["origem"] = new JsonObject
        {
            ["tipo"] = "cidade",
            ["rotulo"] = rotulo,
            ["latitude"] = -29.3788,
            ["longitude"] = -50.8726,
            ["cep"] = null,
            ["precisao"] = null
        };
        return raiz.ToJsonString();
    }

    private static string CriarPublicWeatherComPrevisaoJson()
    {
        var raiz = JsonNode.Parse(JsonClimaPublico)?.AsObject()
            ?? throw new InvalidOperationException("Clima público de teste inválido.");
        raiz["atualizadoEm"] = "2026-06-03T16:00:00";

        var horarios = new JsonArray();
        var temperatura = new JsonArray();
        var sensacao = new JsonArray();
        var umidade = new JsonArray();
        var pressao = new JsonArray();
        var chuva = new JsonArray();
        var inicio = new DateTime(2026, 6, 2, 16, 0, 0, DateTimeKind.Unspecified);
        for (var indice = 0; indice < 37; indice++)
        {
            horarios.Add(inicio.AddHours(indice).ToString("yyyy-MM-dd'T'HH:mm"));
            temperatura.Add(18 + (indice * 0.2));
            sensacao.Add(17.5 + (indice * 0.2));
            umidade.Add(72 - (indice * 0.5));
            pressao.Add(1019 - (indice * 0.1));
            chuva.Add(new JsonObject
            {
                ["horario"] = inicio.AddHours(indice).ToString("yyyy-MM-dd'T'HH:mm"),
                ["precipitacao"] = indice == 5 ? null : indice % 4 * 0.5,
                ["probabilidadeChuva"] = indice == 5 ? null : 20 + indice,
                ["codigoTempo"] = 3
            });
        }

        raiz["seriesHorarias"] = new JsonObject
        {
            ["horarios"] = horarios,
            ["temperatura"] = temperatura,
            ["sensacaoTermica"] = sensacao,
            ["umidade"] = umidade,
            ["pressao"] = pressao
        };
        var previsaoQuinzeDias = new JsonArray();
        for (var indice = 0; indice < 15; indice++)
        {
            previsaoQuinzeDias.Add(new JsonObject
            {
                ["data"] = new DateOnly(2026, 6, 3).AddDays(indice).ToString("yyyy-MM-dd"),
                ["temperaturaMinima"] = 13.0 + indice * 0.2,
                ["temperaturaMaxima"] = 22.0 + indice * 0.25,
                ["precipitacao"] = indice % 4 == 0 ? 2.5 : 0.2,
                ["probabilidadeChuva"] = 20 + indice * 4,
                ["indiceUvMaximo"] = 4.5 + indice * 0.1,
                ["codigoClimatico"] = indice % 4 == 0 ? 61 : 2,
                ["maiorIncerteza"] = indice >= 7
            });
        }
        raiz["previsaoQuinzeDias"] = previsaoQuinzeDias;
        raiz["previsaoCurtoPrazo"] = chuva;
        return raiz.ToJsonString();
    }

    private static string CriarDashboardSemDadosJson()
    {
        var dashboard = JsonNode.Parse(JsonDashboard)?.AsObject()
            ?? throw new InvalidOperationException("Dashboard de teste inválido.");

        foreach (var nomeAmbiente in new[] { "sala", "quarto", "aquario" })
        {
            var ambiente = dashboard[nomeAmbiente]?.AsObject()
                ?? throw new InvalidOperationException($"Ambiente {nomeAmbiente} ausente no dashboard de teste.");
            ambiente["stats"] = new JsonArray();
            ambiente["table"]!["rows"] = new JsonArray();
            foreach (var grafico in ambiente["charts"]!.AsArray())
            {
                grafico!["points"] = new JsonArray();
            }
        }

        return dashboard.ToJsonString();
    }

    private static async Task<IBrowserContext> CriarContextoAsync(bool autorizarInterno = true, string? emailTeste = null)
    {
        var navegador = await ObterNavegadorCompartilhadoAsync();

        // Downloads precisam estar habilitados para inspecionar PDF/JSON gerados pelo frontend.
        IBrowserContext contexto;
        try
        {
            contexto = await navegador.NewContextAsync(new() { AcceptDownloads = true });
        }
        catch (PlaywrightException)
        {
            await ReiniciarNavegadorCompartilhadoAsync();
            navegador = await ObterNavegadorCompartilhadoAsync();
            contexto = await navegador.NewContextAsync(new() { AcceptDownloads = true });
        }
        if (!string.IsNullOrWhiteSpace(emailTeste))
        {
            await contexto.AddInitScriptAsync($"window.__climateTestUserEmail = {JsonSerializer.Serialize(emailTeste)};");
        }
        else if (autorizarInterno)
        {
            await contexto.AddInitScriptAsync("window.__climateTestAuthorized = true;");
        }
        return contexto;
    }

    private static async Task<IBrowser> ObterNavegadorCompartilhadoAsync()
    {
        await TravaNavegador.WaitAsync();
        try
        {
            playwrightCompartilhado ??= await Playwright.CreateAsync();
            if (navegadorCompartilhado is null || !navegadorCompartilhado.IsConnected)
            {
                navegadorCompartilhado = await AbrirNavegadorAsync(playwrightCompartilhado);
            }
            return navegadorCompartilhado;
        }
        finally
        {
            TravaNavegador.Release();
        }
    }

    private static async Task ReiniciarNavegadorCompartilhadoAsync()
    {
        await TravaNavegador.WaitAsync();
        try
        {
            navegadorCompartilhado = null;
            playwrightCompartilhado?.Dispose();
            playwrightCompartilhado = null;
        }
        finally
        {
            TravaNavegador.Release();
        }
    }

    private static async Task<IPage> CriarPaginaComDashboardMockadoAsync(IBrowserContext contexto, List<string> errosConsole)
    {
        var pagina = await contexto.NewPageAsync();

        // Filtra ruído de recursos auxiliares para falhar apenas em erros reais do JavaScript da aplicação.
        pagina.Console += (_, mensagem) =>
        {
            if (mensagem.Type == "error" && !EhErroDeRecursoAuxiliar(mensagem.Text))
            {
                errosConsole.Add(mensagem.Text);
            }
        };

        // O browser renderiza a aplicação real, mas a API é mockada para o teste não depender do Firebase.
        await pagina.RouteAsync("**/api/dashboard**", rota => rota.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = JsonDashboard
        }));

        return pagina;
    }

    private static bool EhErroDeRecursoAuxiliar(string mensagem)
    {
        // Favicon e recursos auxiliares podem falhar em ambiente de teste sem indicar regressão da aplicação.
        return mensagem.Contains("favicon", StringComparison.OrdinalIgnoreCase) ||
            mensagem.StartsWith("Failed to load resource:", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizarTexto(IEnumerable<string> textos)
    {
        return string.Join(" ", textos.Select(texto => texto.ReplaceLineEndings(" ")));
    }

    private static async Task<IBrowser> AbrirNavegadorAsync(IPlaywright playwright)
    {
        try
        {
            return await playwright.Chromium.LaunchAsync(new()
            {
                Headless = true
            });
        }
        catch (PlaywrightException)
        {
            // Usa um navegador instalado quando o Chromium gerenciado não estiver disponível.
            try
            {
                return await playwright.Chromium.LaunchAsync(new()
                {
                    Channel = "chrome",
                    Headless = true
                });
            }
            catch (PlaywrightException)
            {
                return await playwright.Chromium.LaunchAsync(new()
                {
                    Channel = "msedge",
                    Headless = true
                });
            }
        }
    }

    private const string JsonDashboard = """
    {
      "date": "03-06-2026",
      "quarto": {
        "stats": [
          { "key": "Temperatura", "label": "Temperatura", "average": 24.1, "min": 22.5, "max": 25.2, "delta": 1.1, "suffix": "°C", "trend": { "label": "Subindo", "className": "up", "symbol": "↗" } },
          { "key": "Sensacao termica", "label": "Sensação", "average": 24.4, "min": 22.8, "max": 25.6, "delta": 0.8, "suffix": "°C", "trend": { "label": "Subindo", "className": "up", "symbol": "↗" } },
          { "key": "Umidade", "label": "Umidade", "average": 61, "min": 58, "max": 64, "delta": -1, "suffix": "%", "trend": { "label": "Caindo", "className": "down", "symbol": "↘" } }
        ],
        "charts": [
          { "id": "plotsTemp", "label": "Temperatura", "color": "#38bdf8", "suffix": "°", "points": [{ "label": "08:00", "value": 22.5 }, { "label": "09:00", "value": 24.1 }] },
          { "id": "plotsST", "label": "Sensação Térmica", "color": "#34d399", "suffix": "°", "points": [{ "label": "08:00", "value": 22.8 }, { "label": "09:00", "value": 24.4 }] },
          { "id": "plotsUmidade", "label": "Umidade", "color": "#a78bfa", "suffix": "%", "points": [{ "label": "08:00", "value": 64 }, { "label": "09:00", "value": 61 }] }
        ],
        "table": { "headers": ["Data", "Hora", "Temperatura", "Sensacao termica", "Umidade"], "rows": [["03/06/2026", "09:00", "24.10", "24.40", "61.00"], ["03/06/2026", "08:00", "22.50", "22.80", "64.00"]] },
        "advanced": {
          "monthlyCalendar": [{ "label": "1", "value": 23.5 }, { "label": "2", "value": 24.1 }],
          "hourlyHeatmap": [{ "label": "08h", "value": 22.5 }, { "label": "09h", "value": 24.1 }],
          "weeklyHeatmap": [
            { "weekday": 0, "hour": 0, "value": null }, { "weekday": 0, "hour": 1, "value": null }, { "weekday": 0, "hour": 2, "value": null }, { "weekday": 0, "hour": 3, "value": null }, { "weekday": 0, "hour": 4, "value": null }, { "weekday": 0, "hour": 5, "value": null }, { "weekday": 0, "hour": 6, "value": null }, { "weekday": 0, "hour": 7, "value": null }, { "weekday": 0, "hour": 8, "value": 22.5 }, { "weekday": 0, "hour": 9, "value": 24.1 }, { "weekday": 0, "hour": 10, "value": null }, { "weekday": 0, "hour": 11, "value": null }, { "weekday": 0, "hour": 12, "value": null }, { "weekday": 0, "hour": 13, "value": null }, { "weekday": 0, "hour": 14, "value": null }, { "weekday": 0, "hour": 15, "value": null }, { "weekday": 0, "hour": 16, "value": null }, { "weekday": 0, "hour": 17, "value": null }, { "weekday": 0, "hour": 18, "value": null }, { "weekday": 0, "hour": 19, "value": null }, { "weekday": 0, "hour": 20, "value": null }, { "weekday": 0, "hour": 21, "value": null }, { "weekday": 0, "hour": 22, "value": null }, { "weekday": 0, "hour": 23, "value": null }
          ]
        }
      },
      "sala": {
        "stats": [
          { "key": "temperatura", "label": "Temperatura", "average": 25.1, "min": 24.2, "max": 26.2, "delta": -0.2, "suffix": "°C", "trend": { "label": "Caindo", "className": "down", "symbol": "↘" } },
          { "key": "sensacaoTermica", "label": "Sensação", "average": 25.6, "min": 24.5, "max": 26.8, "delta": -0.1, "suffix": "°C", "trend": { "label": "Caindo", "className": "down", "symbol": "↘" } },
          { "key": "umidade", "label": "Umidade", "average": 55, "min": 52, "max": 58, "delta": 1, "suffix": "%", "trend": { "label": "Subindo", "className": "up", "symbol": "↗" } },
          { "key": "pressao", "label": "Pressão", "average": 1012, "min": 1010, "max": 1013, "delta": 0, "suffix": " hPa", "trend": { "label": "Estável", "className": "stable", "symbol": "→" } }
        ],
        "charts": [
          { "id": "plotsTempSala", "label": "Temperatura", "color": "#38bdf8", "suffix": "°", "points": [{ "label": "08:00", "value": 26.2 }, { "label": "09:00", "value": 25.1 }] },
          { "id": "plotsSTSala", "label": "Sensação Térmica", "color": "#34d399", "suffix": "°", "points": [{ "label": "08:00", "value": 26.8 }, { "label": "09:00", "value": 25.6 }] },
          { "id": "plotsUmidadeSala", "label": "Umidade", "color": "#a78bfa", "suffix": "%", "points": [{ "label": "08:00", "value": 52 }, { "label": "09:00", "value": 55 }] },
          { "id": "plotsPressaoSala", "label": "Pressão (hPa)", "color": "#fbbf24", "suffix": "", "points": [{ "label": "08:00", "value": 1010 }, { "label": "09:00", "value": 1012 }] }
        ],
        "table": { "headers": ["Data", "Hora", "CO", "CO2", "Acetona", "Álcool", "Amônia", "Tolueno"], "rows": [["03/06/2026", "09:00", "1.00", "410.00", "0.20", "0.10", "0.30", "0.40"], ["03/06/2026", "08:00", "0.90", "405.00", "0.18", "0.09", "0.25", "0.35"]] },
        "advanced": {
          "monthlyCalendar": [{ "label": "1", "value": 24.2 }, { "label": "2", "value": 25.1 }],
          "hourlyHeatmap": [{ "label": "08h", "value": 26.2 }, { "label": "09h", "value": 25.1 }],
          "weeklyHeatmap": [
            { "weekday": 0, "hour": 0, "value": null }, { "weekday": 0, "hour": 1, "value": null }, { "weekday": 0, "hour": 2, "value": null }, { "weekday": 0, "hour": 3, "value": null }, { "weekday": 0, "hour": 4, "value": null }, { "weekday": 0, "hour": 5, "value": null }, { "weekday": 0, "hour": 6, "value": null }, { "weekday": 0, "hour": 7, "value": null }, { "weekday": 0, "hour": 8, "value": 26.2 }, { "weekday": 0, "hour": 9, "value": 25.1 }, { "weekday": 0, "hour": 10, "value": null }, { "weekday": 0, "hour": 11, "value": null }, { "weekday": 0, "hour": 12, "value": null }, { "weekday": 0, "hour": 13, "value": null }, { "weekday": 0, "hour": 14, "value": null }, { "weekday": 0, "hour": 15, "value": null }, { "weekday": 0, "hour": 16, "value": null }, { "weekday": 0, "hour": 17, "value": null }, { "weekday": 0, "hour": 18, "value": null }, { "weekday": 0, "hour": 19, "value": null }, { "weekday": 0, "hour": 20, "value": null }, { "weekday": 0, "hour": 21, "value": null }, { "weekday": 0, "hour": 22, "value": null }, { "weekday": 0, "hour": 23, "value": null }
          ]
        }
      },
      "aquario": {
        "stats": [
          { "key": "temperaturaDS18B20", "label": "Temperatura", "average": 26.5, "min": 26.1, "max": 26.7, "delta": 0.2, "suffix": "°C", "trend": { "label": "Subindo", "className": "up", "symbol": "↗" } },
          { "key": "PH", "label": "PH", "average": null, "min": null, "max": null, "delta": null, "suffix": "", "trend": { "label": "--", "className": "stable", "symbol": "--" } },
          { "key": "TDS", "label": "TDS", "average": null, "min": null, "max": null, "delta": null, "suffix": "", "trend": { "label": "--", "className": "stable", "symbol": "--" } },
          { "key": "Turbidez", "label": "Turbidez", "average": null, "min": null, "max": null, "delta": null, "suffix": "", "trend": { "label": "--", "className": "stable", "symbol": "--" } }
        ],
        "charts": [
          { "id": "plotsTempAquario", "label": "Temperatura", "color": "#38bdf8", "suffix": "°", "points": [{ "label": "08:00", "value": 26.1 }, { "label": "09:00", "value": 26.5 }] },
          { "id": "plotsPH", "label": "PH", "color": "#2dd4bf", "suffix": "", "points": [{ "label": "08:00", "value": null }, { "label": "09:00", "value": null }] },
          { "id": "plotsTDS", "label": "TDS", "color": "#fbbf24", "suffix": "", "points": [{ "label": "08:00", "value": null }, { "label": "09:00", "value": null }] },
          { "id": "plotsTurbidez", "label": "Turbidez", "color": "#fb7185", "suffix": "", "points": [{ "label": "08:00", "value": null }, { "label": "09:00", "value": null }] }
        ],
        "table": { "headers": ["Data", "Hora", "Temperatura", "PH", "TDS", "Turbidez"], "rows": [["03/06/2026", "09:00", "26.50", "7.10", "320.00", "4.00"]] },
        "advanced": null
      },
      "solar": {
        "sunHistoryDawn": { "id": "sunHistoryDawn", "label": "Amanhecer", "color": "#fde68a", "suffix": "", "points": [{ "label": "01/06/2026", "value": 5.8 }, { "label": "02/06/2026", "value": 5.9 }] },
        "sunHistorySunrise": { "id": "sunHistorySunrise", "label": "Nascer do sol", "color": "#fb923c", "suffix": "", "points": [{ "label": "01/06/2026", "value": 6.4 }, { "label": "02/06/2026", "value": 6.5 }] },
        "sunHistorySunset": { "id": "sunHistorySunset", "label": "Pôr do sol", "color": "#f87171", "suffix": "", "points": [{ "label": "01/06/2026", "value": 17.7 }, { "label": "02/06/2026", "value": 17.8 }] },
        "sunHistoryDusk": { "id": "sunHistoryDusk", "label": "Anoitecer", "color": "#818cf8", "suffix": "", "points": [{ "label": "01/06/2026", "value": 18.2 }, { "label": "02/06/2026", "value": 18.3 }] },
        "day": { "date": "03-06-2026", "dawn": 5.9, "sunrise": 6.5, "zenith": 12.1, "sunset": 17.8, "dusk": 18.3 }
      },
      "insights": {
        "ventilacao": { "rotulo": "Ambiente equilibrado", "classe": "favoravel", "descricao": "Os sensores internos não indicam necessidade urgente de ventilação." },
        "riscoSala": { "nivel": "muito_baixo", "rotulo": "Risco baixo", "classe": "favoravel", "descricao": "O ar está distante da condensação e da faixa mais favorável ao mofo.", "pontoOrvalho": 15.2, "margemCondensacao": 9.9 },
        "riscoQuarto": { "nivel": "baixo", "rotulo": "Monitorar", "classe": "atencao", "descricao": "Risco ainda baixo, mas a umidade já pede acompanhamento.", "pontoOrvalho": 16.1, "margemCondensacao": 8.0 }
      }
    }
    """;

    private const string JsonClimaPublico = """
    {
      "origem": { "tipo": "cep", "rotulo": "São Paulo - SP", "latitude": -23.55, "longitude": -46.63, "cep": "01001000", "precisao": null },
      "atualizadoEm": "2026-06-03T12:00:00",
      "climaAtual": { "temperatura": 21.4, "sensacaoTermica": 20.8, "umidade": 63, "pressao": 1017.2, "pontoOrvalho": 14.1, "precipitacao": 0, "chuva": 0, "codigoTempo": 3, "rajadaVento": 16, "indiceUv": 4.5 },
      "seriesHorarias": {
        "horarios": ["2026-06-02T13:00:00", "2026-06-03T12:00:00"],
        "temperatura": [19.2, 21.4], "sensacaoTermica": [18.9, 20.8], "umidade": [71, 63], "pressao": [1019.1, 1017.2]
      },
      "aqi": { "valor": 42, "horarios": ["2026-06-03T12:00:00"], "serie": [42] },
      "cicloSolar": { "dawn": 5.5, "sunrise": 6.5, "zenith": 12.125, "sunset": 17.75, "dusk": 18.75, "daylightDuration": 40500 },
      "previsaoCurtoPrazo": [
        { "horario": "2026-06-02T12:00:00", "probabilidadeChuva": 10, "precipitacao": 0.2, "chuva": 0.2, "pancadas": 0, "codigoTempo": 51 },
        { "horario": "2026-06-03T12:00:00", "probabilidadeChuva": 20, "precipitacao": 0, "chuva": 0, "pancadas": 0, "codigoTempo": 3 },
        { "horario": "2026-06-03T13:00:00", "probabilidadeChuva": 70, "precipitacao": 1.5, "chuva": 1.5, "pancadas": 0, "codigoTempo": 61 },
        { "horario": "2026-06-03T14:00:00", "probabilidadeChuva": 80, "precipitacao": 3.0, "chuva": 3.0, "pancadas": 0, "codigoTempo": 63 }
      ],
      "insights": {
        "ventilacao": { "rotulo": "Boa hora para ventilar", "classe": "favoravel", "descricao": "As condições externas estão favoráveis à renovação do ar." },
        "chuva": { "rotulo": "Chuva moderada", "classe": "alerta", "descricao": "Maior intensidade prevista perto de 14:00.", "probabilidade": 70, "intensidadeMaxima": 3.0, "acumulado": 3.2, "periodoHoras": 6, "horarioPico": "14:00" },
        "indiceUv": { "rotulo": "Moderado", "classe": "atencao", "descricao": "Use protetor solar e prefira sombra perto do meio-dia.", "valor": 4.5, "maximo": 5.2, "usaMaximoComoFallback": false },
        "riscoMofo": { "nivel": "baixo", "rotulo": "Monitorar", "classe": "atencao", "descricao": "Risco ainda baixo, mas a umidade já pede acompanhamento.", "pontoOrvalho": 14.1, "margemCondensacao": 7.3 }
      }
    }
    """;
}

public sealed class ApiAuthorizationTests : IClassFixture<AmbienteTestesInterfaceNavegador>
{
    private readonly AmbienteTestesInterfaceNavegador _servidor;

    public ApiAuthorizationTests(AmbienteTestesInterfaceNavegador servidor)
    {
        _servidor = servidor;
    }

    [Fact]
    public async Task EndpointsInternos_DevemRejeitarRequisicoesSemToken()
    {
        using var cliente = new HttpClient();
        var caminhosGet = new[]
        {
            "api/internal-access",
            "api/dashboard?date=03-06-2026",
            "api/environments",
            "api/environments/sala/latest",
            "api/environments/sala/measurements?from=03-06-2026&to=03-06-2026",
            "api/metrics/temperatura/series?date=03-06-2026",
            "api/dashboard/summary?date=03-06-2026",
            "api/air-quality?date=03-06-2026",
            "api/solar?date=03-06-2026",
            "api/alerts?date=03-06-2026",
            "api/exports/json?date=03-06-2026&tab=Tab0"
        };
        var respostasGet = new List<HttpResponseMessage>();
        foreach (var caminho in caminhosGet)
        {
            respostasGet.Add(await cliente.GetAsync($"{_servidor.UrlBase}{caminho}"));
        }
        using var chat = await cliente.PostAsync(
            $"{_servidor.UrlBase}api/chat",
            new StringContent("""{"question":"Temperatura média?","activeTab":"Tab1","selectedDate":"03-06-2026"}""", Encoding.UTF8, "application/json"));
        using var comparacao = await cliente.PostAsync(
            $"{_servidor.UrlBase}api/comparisons",
            new StringContent("""{"dates":["02-06-2026","03-06-2026"]}""", Encoding.UTF8, "application/json"));

        Assert.All(respostasGet, resposta => Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode));
        Assert.Equal(HttpStatusCode.Unauthorized, chat.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, comparacao.StatusCode);
        foreach (var resposta in respostasGet)
        {
            Assert.Contains("Acesso interno não autorizado.", await resposta.Content.ReadAsStringAsync());
        }
        Assert.Contains("Acesso interno não autorizado.", await chat.Content.ReadAsStringAsync());
        Assert.Contains("Acesso interno não autorizado.", await comparacao.Content.ReadAsStringAsync());
        foreach (var resposta in respostasGet) resposta.Dispose();
    }

    [Fact]
    public async Task EndpointsPublicos_DevemPermanecerAcessiveisSemToken()
    {
        using var cliente = new HttpClient();

        using var saude = await cliente.GetAsync($"{_servidor.UrlBase}api/health");
        using var clima = await cliente.GetAsync($"{_servidor.UrlBase}api/public-weather/cep/123");

        Assert.Equal(HttpStatusCode.OK, saude.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, clima.StatusCode);
        Assert.Contains("Informe um CEP com 8 dígitos.", await clima.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ScriptPublico_DeveOmitirListaDeEmailsAutorizados()
    {
        using var cliente = new HttpClient();
        using var resposta = await cliente.GetAsync($"{_servidor.UrlBase}scripts/auth.js");
        var script = await resposta.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.DoesNotContain("usuario@example.com", script);
        Assert.DoesNotContain("outra@example.com", script);
        Assert.Contains("/api/internal-access", script);
    }
}

public sealed class PublicWeatherRateLimitTests
{
    [Fact]
    public async Task RotasPublicas_DevemRetornar429AposLimitePorOrigem()
    {
        var servidor = new AmbienteTestesInterfaceNavegador();
        await servidor.InitializeAsync();
        try
        {
            using var cliente = new HttpClient();
            for (var tentativa = 0; tentativa < 60; tentativa++)
            {
                using var resposta = await cliente.GetAsync($"{servidor.UrlBase}api/public-weather/cities?term=x");
                Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
            }

            using var excedente = await cliente.GetAsync($"{servidor.UrlBase}api/public-weather/cities?term=x");
            Assert.Equal((HttpStatusCode)429, excedente.StatusCode);
            using var saude = await cliente.GetAsync($"{servidor.UrlBase}api/health");
            Assert.Equal(HttpStatusCode.OK, saude.StatusCode);
        }
        finally
        {
            await servidor.DisposeAsync();
        }
    }
}

public sealed class AmbienteTestesInterfaceNavegador : IAsyncLifetime
{
    private Process? _processo;
    public string UrlBase { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var raiz = LocalizarRaizProjeto();
        var porta = ObterPortaLivre();
        UrlBase = $"http://127.0.0.1:{porta}/";
        var projetoWeb = Path.Combine(raiz, "src", "EstacaoClimatica.Web", "EstacaoClimatica.Web.csproj");

        // Sobe o host ASP.NET real para validar DOM, scripts e downloads de ponta a ponta.
        _processo = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --no-build --project \"{projetoWeb}\" --urls {UrlBase.TrimEnd('/')}",
            WorkingDirectory = raiz,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        });

        if (_processo is null)
        {
            throw new InvalidOperationException("Nao foi possivel iniciar o servidor de teste.");
        }

        await AguardarServidorAsync();
    }

    public Task DisposeAsync()
    {
        if (_processo is { HasExited: false })
        {
            _processo.Kill(entireProcessTree: true);
            _processo.Dispose();
        }

        return Task.CompletedTask;
    }

    private async Task AguardarServidorAsync()
    {
        using var cliente = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var limite = DateTime.UtcNow.AddSeconds(45);

        // Aguarda o Kestrel aceitar conexões antes de entregar a URL aos testes de browser.
        while (DateTime.UtcNow < limite)
        {
            if (_processo is { HasExited: true })
            {
                throw new InvalidOperationException("Servidor de teste encerrou antes de responder.");
            }

            try
            {
                using var resposta = await cliente.GetAsync(UrlBase);
                if (resposta.StatusCode == HttpStatusCode.OK) return;
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            await Task.Delay(500);
        }

        throw new TimeoutException("Servidor de teste nao respondeu dentro do tempo esperado.");
    }

    private static int ObterPortaLivre()
    {
        // Porta dinâmica evita conflito com o site aberto manualmente em 5288.
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }

    private static string LocalizarRaizProjeto()
    {
        var diretorio = new DirectoryInfo(AppContext.BaseDirectory);
        while (diretorio is not null && !File.Exists(Path.Combine(diretorio.FullName, "EstacaoClimatica.sln")))
        {
            diretorio = diretorio.Parent;
        }

        return diretorio?.FullName ?? throw new InvalidOperationException("Nao foi possivel localizar EstacaoClimatica.sln.");
    }
}

