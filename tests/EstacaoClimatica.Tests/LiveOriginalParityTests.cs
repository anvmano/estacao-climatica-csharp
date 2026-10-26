using System.Text.Json;
using Microsoft.Playwright;

namespace EstacaoClimatica.Tests;

public sealed class TestesParidadeOriginalAoVivo
{
    [ParidadeAoVivo]
    [Trait("Category", "LiveParity")]
    public async Task ProjetosDevemManterParidadeObservavelEm03DeJunhoDe2026()
    {
        var urlOriginal = Environment.GetEnvironmentVariable("ORIGINAL_WEB_URL") ?? "http://127.0.0.1:8086/";
        var urlDotnet = Environment.GetEnvironmentVariable("DOTNET_WEB_URL") ?? "http://127.0.0.1:5082/";
        var pastaEvidencias = Environment.GetEnvironmentVariable("LIVE_PARITY_OUTPUT")
            ?? Path.Combine(LocalizarRaizProjeto(), "outputs", "comparacao-comportamental-03-06-2026-nova", "interno");
        Directory.CreateDirectory(pastaEvidencias);

        using var playwright = await Playwright.CreateAsync();
        await using var navegador = await AbrirNavegadorAsync(playwright);
        await using var contextoOriginal = await navegador.NewContextAsync(new()
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 }
        });
        await using var contextoDotnet = await navegador.NewContextAsync(new()
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 }
        });
        await contextoDotnet.AddInitScriptAsync("window.__climateTestAuthorized = true;");

        var original = await contextoOriginal.NewPageAsync();
        var dotnet = await contextoDotnet.NewPageAsync();
        var errosOriginal = CapturarErros(original);
        var errosDotnet = CapturarErros(dotnet);

        await original.RouteAsync("**/scripts/auth/auth-service.js*", rota => rota.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/javascript; charset=utf-8",
            Body = AuthOriginalAutorizada
        }));

        await Task.WhenAll(original.GotoAsync(urlOriginal), dotnet.GotoAsync(urlDotnet));
        await Task.WhenAll(
            original.WaitForSelectorAsync("#privateApp:not([hidden])", new() { Timeout = 30_000 }),
            dotnet.WaitForSelectorAsync("#privateApp:not([hidden])", new() { Timeout = 30_000 }));
        await Task.WhenAll(
            AguardarCardsAsync(original, "Projeto Web original"),
            AguardarCardsAsync(dotnet, "Projeto C#/.NET"));

        await SelecionarDataAsync(original);
        await SelecionarDataAsync(dotnet);
        await Task.WhenAll(
            original.WaitForFunctionAsync("() => document.querySelector('#selectedDate')?.value === '2026-06-03' && document.querySelectorAll('#dataSala tbody tr').length > 0"),
            dotnet.WaitForFunctionAsync("() => dataSelecionada === '03-06-2026' && document.querySelectorAll('#dataSala tbody tr').length > 0"));
        await Task.Delay(1_500);

        await ExpandirVisualizacoesAsync(original);
        await ExpandirVisualizacoesAsync(dotnet);

        var estadoOriginal = await ColetarEstadoAsync(original);
        var estadoDotnet = await ColetarEstadoAsync(dotnet);
        await File.WriteAllTextAsync(
            Path.Combine(pastaEvidencias, "estado-original.json"),
            JsonSerializer.Serialize(estadoOriginal, OpcoesJson));
        await File.WriteAllTextAsync(
            Path.Combine(pastaEvidencias, "estado-dotnet.json"),
            JsonSerializer.Serialize(estadoDotnet, OpcoesJson));
        await original.ScreenshotAsync(new() { Path = Path.Combine(pastaEvidencias, "original-desktop.png"), FullPage = true });
        await dotnet.ScreenshotAsync(new() { Path = Path.Combine(pastaEvidencias, "dotnet-desktop.png"), FullPage = true });

        await Task.WhenAll(
            original.SetViewportSizeAsync(390, 844),
            dotnet.SetViewportSizeAsync(390, 844));
        await Task.WhenAll(
            original.Locator("[data-tab-target='Tab1']").ClickAsync(),
            dotnet.Locator("[data-tab-target='Tab1']").ClickAsync());
        await Task.Delay(1_000);
        var estadoMobileOriginal = await ColetarEstadoMobileAsync(original);
        var estadoMobileDotnet = await ColetarEstadoMobileAsync(dotnet);
        await File.WriteAllTextAsync(
            Path.Combine(pastaEvidencias, "estado-mobile-original.json"),
            JsonSerializer.Serialize(estadoMobileOriginal, OpcoesJson));
        await File.WriteAllTextAsync(
            Path.Combine(pastaEvidencias, "estado-mobile-dotnet.json"),
            JsonSerializer.Serialize(estadoMobileDotnet, OpcoesJson));
        await original.ScreenshotAsync(new() { Path = Path.Combine(pastaEvidencias, "original-mobile-sala.png"), FullPage = true });
        await dotnet.ScreenshotAsync(new() { Path = Path.Combine(pastaEvidencias, "dotnet-mobile-sala.png"), FullPage = true });

        Assert.Equal(estadoOriginal.Date, estadoDotnet.Date);
        Assert.Equal(estadoOriginal.Tabs, estadoDotnet.Tabs);
        Assert.Equal(estadoOriginal.StatsCounts, estadoDotnet.StatsCounts);
        Assert.Equal(estadoOriginal.StatsTexts, estadoDotnet.StatsTexts);
        Assert.Equal(estadoOriginal.EnvironmentStatsTexts, estadoDotnet.EnvironmentStatsTexts);
        Assert.Equal(estadoOriginal.TableTools, estadoDotnet.TableTools);
        Assert.Equal(estadoOriginal.QualityBadges, estadoDotnet.QualityBadges);
        Assert.Equal(estadoOriginal.AstroState, estadoDotnet.AstroState);
        Assert.Equal(estadoOriginal.InsightAction, estadoDotnet.InsightAction);
        Assert.Equal(estadoOriginal.ChartXRotations, estadoDotnet.ChartXRotations);
        Assert.Equal(estadoOriginal.ChartLabels, estadoDotnet.ChartLabels);
        Assert.Equal(estadoOriginal.ChartAxes, estadoDotnet.ChartAxes);
        Assert.Equal(estadoOriginal.ChartData, estadoDotnet.ChartData);
        Assert.Equal(estadoOriginal.TableHeaders, estadoDotnet.TableHeaders);
        Assert.Equal(estadoOriginal.TableRows, estadoDotnet.TableRows);
        Assert.Equal(estadoOriginal.AdvancedCells, estadoDotnet.AdvancedCells);
        Assert.Equal(estadoOriginal.SolarLabels, estadoDotnet.SolarLabels);
        Assert.Equal(estadoOriginal.InsightTexts, estadoDotnet.InsightTexts);
        Assert.Equal(estadoOriginal.SolarDuration, estadoDotnet.SolarDuration);
        Assert.Equal(estadoOriginal.ActiveTab, estadoDotnet.ActiveTab);
        Assert.Equal(estadoMobileOriginal, estadoMobileDotnet);
        Assert.True(estadoMobileOriginal.SemOverflowHorizontal);
        Assert.True(estadoMobileOriginal.ControlesVisiveis);
        Assert.Empty(errosOriginal);
        Assert.Empty(errosDotnet);
    }

    private static async Task SelecionarDataAsync(IPage pagina)
    {
        await pagina.Locator("#selectedDate").FillAsync("2026-06-03");
        await pagina.Locator("#selectedDate").DispatchEventAsync("change");
    }

    private static async Task AguardarCardsAsync(IPage pagina, string projeto)
    {
        try
        {
            await pagina.Locator("[data-tab-target='Tab1']").ClickAsync();
            await pagina.WaitForSelectorAsync("#statsSala .stats-card", new() { State = WaitForSelectorState.Attached, Timeout = 45_000 });
        }
        catch (TimeoutException erro)
        {
            throw new TimeoutException($"{projeto} não renderizou os cards da Sala dentro de 45 segundos. URL: {pagina.Url}", erro);
        }
    }

    private static async Task ExpandirVisualizacoesAsync(IPage pagina)
    {
        foreach (var aba in new[] { "Tab1", "Tab2" })
        {
            await pagina.Locator($"[data-tab-target='{aba}']").ClickAsync();
            var gatilho = pagina.Locator($"#{aba} .advanced-section .collapsible-trigger").First;
            if (await gatilho.GetAttributeAsync("aria-expanded") != "true")
            {
                await gatilho.ClickAsync();
            }
        }

        await pagina.Locator("[data-tab-target='Tab0']").ClickAsync();
    }

    private static async Task<EstadoParidade> ColetarEstadoAsync(IPage pagina)
    {
        var estado = await pagina.EvaluateAsync<JsonElement>("""
        () => {
            const textos = seletor => [...document.querySelectorAll(seletor)]
                .map(item => (item.textContent || '').replace(/\s+/g, ' ').trim())
                .filter(Boolean);
            const cabecalhos = seletor => textos(`${seletor} th`);
            const linhas = seletor => {
                const tabela = document.querySelector(`${seletor} table`);
                if (!tabela) return 0;
                const quantidade = tabela.querySelectorAll('tbody tr').length;
                return tabela.querySelector('thead') ? quantidade : Math.max(0, quantidade - 1);
            };
            const eixos = [
                'plotGlobalTemperature', 'plotGlobalHumidity',
                'plotsTempSala', 'plotsSTSala', 'plotsUmidadeSala', 'plotsPressaoSala',
                'plotsTemp', 'plotsST', 'plotsUmidade',
                'plotsTempAquario', 'plotsPH', 'plotsTDS', 'plotsTurbidez'
            ].map(id => {
                const grafico = window.Chart?.getChart(id);
                const eixo = grafico?.options?.scales?.y;
                const callback = eixo?.ticks?.callback;
                return {
                    id,
                    title: eixo?.title?.display ? String(eixo.title.text || '') : '',
                    sampleTick: typeof callback === 'function' ? String(callback(20)) : '20'
                };
            });
            const dadosGraficos = [
                'plotGlobalTemperature', 'plotGlobalHumidity',
                'plotsTempSala', 'plotsSTSala', 'plotsUmidadeSala', 'plotsPressaoSala',
                'plotsTemp', 'plotsST', 'plotsUmidade',
                'plotsTempAquario', 'plotsPH', 'plotsTDS', 'plotsTurbidez',
                'plotSolarToday', 'plotSunriseSunset'
            ].map(id => {
                const grafico = window.Chart?.getChart(id);
                return JSON.stringify({
                    id,
                    labels: grafico?.data?.labels || [],
                    datasets: (grafico?.data?.datasets || []).map(serie => ({
                        label: serie.label || '',
                        data: serie.data || []
                    }))
                });
            });
            return {
                date: document.querySelector('#selectedDate')?.value || '',
                activeTab: document.querySelector('.tablink.active')?.getAttribute('data-tab-target') || '',
                tabs: textos('.tablink'),
                statsCounts: [
                    document.querySelectorAll('#statsEstacao .stats-card').length,
                    document.querySelectorAll('#statsSala .stats-card').length,
                    document.querySelectorAll('#statsQuarto .stats-card').length,
                    document.querySelectorAll('#statsAquario .stats-card').length
                ],
                statsTexts: textos('#statsEstacao .stats-card'),
                environmentStatsTexts: textos('#statsSala .stats-card, #statsQuarto .stats-card, #statsAquario .stats-card'),
                tableTools: textos('#dataSala .table-tools, #data .table-tools, #dataAquario .table-tools'),
                qualityBadges: textos('.chart-quality-badge'),
                astroState: `${document.querySelector('#astroIndicator')?.className || ''}|${document.querySelector('#astroIndicator')?.getAttribute('title') || ''}`,
                insightAction: (document.querySelector('#environmentLocationButton')?.textContent || '').replace(/\s+/g, ' ').trim(),
                chartXRotations: [
                    'plotGlobalTemperature', 'plotGlobalHumidity',
                    'plotsTempSala', 'plotsSTSala', 'plotsUmidadeSala', 'plotsPressaoSala',
                    'plotsTemp', 'plotsST', 'plotsUmidade',
                    'plotsTempAquario', 'plotsPH', 'plotsTDS', 'plotsTurbidez'
                ].map(id => {
                    const ticks = window.Chart?.getChart(id)?.options?.scales?.x?.ticks;
                    return `${id}|${ticks?.minRotation ?? ''}|${ticks?.maxRotation ?? ''}`;
                }),
                chartLabels: textos('#Tab0 .chart-label, #Tab1 .chart-label, #Tab2 .chart-label, #Tab3 .chart-label')
                    .filter((item, index, array) => array.indexOf(item) === index),
                chartAxes: eixos,
                chartData: dadosGraficos,
                tableHeaders: [cabecalhos('#dataSala'), cabecalhos('#data'), cabecalhos('#dataAquario')],
                tableRows: [linhas('#dataSala'), linhas('#data'), linhas('#dataAquario')],
                advancedCells: [
                    document.querySelectorAll('#monthlyClimateCalendarSala .heatmap-cell').length,
                    document.querySelectorAll('#hourlyHeatmapSala .heatmap-cell').length,
                    document.querySelectorAll('#weeklyHeatmapSala .weekly-heatmap__cell').length,
                    document.querySelectorAll('#monthlyClimateCalendar .heatmap-cell').length,
                    document.querySelectorAll('#hourlyHeatmap .heatmap-cell').length,
                    document.querySelectorAll('#weeklyHeatmap .weekly-heatmap__cell').length
                ],
                solarLabels: textos('#astroPopover dt, #Tab0 #chart-container-sun .chart-label, #Tab0 #chart-container-sun-today .chart-label')
                ,insightTexts: [...document.querySelectorAll('#environmentInsights .environment-insight')].map(card => [
                    card.querySelector('.environment-insight__header > span')?.textContent || '',
                    card.querySelector('.environment-insight__header > small')?.textContent || '',
                    card.querySelector(':scope > strong')?.textContent || '',
                    card.querySelector(':scope > p')?.textContent || '',
                    card.querySelector('.environment-insight__detail')?.textContent || ''
                ].map(item => item.replace(/\s+/g, ' ').trim()).join('|')),
                solarDuration: (document.querySelector('#solarTodayDuration, #solarDayDuration')?.textContent || '').trim()
            };
        }
        """);
        return JsonSerializer.Deserialize<EstadoParidade>(estado.GetRawText(), OpcoesJson)
            ?? throw new InvalidOperationException("Estado de paridade não pôde ser desserializado.");
    }

    private static List<string> CapturarErros(IPage pagina)
    {
        var erros = new List<string>();
        pagina.Console += (_, mensagem) =>
        {
            if (mensagem.Type == "error" && !mensagem.Text.StartsWith("Failed to load resource:", StringComparison.OrdinalIgnoreCase))
            {
                erros.Add(mensagem.Text);
            }
        };
        pagina.PageError += (_, erro) => erros.Add(erro);
        return erros;
    }

    private static async Task<EstadoParidadeCelular> ColetarEstadoMobileAsync(IPage pagina)
    {
        var estado = await pagina.EvaluateAsync<JsonElement>("""
            () => ({
                semOverflowHorizontal: document.documentElement.scrollWidth <= window.innerWidth + 1,
                controlesVisiveis: ['selectedDate', 'btnToday', 'authLogoutButton'].every(id => {
                    const elemento = document.getElementById(id);
                    if (!elemento) return false;
                    const estilo = getComputedStyle(elemento);
                    const caixa = elemento.getBoundingClientRect();
                    return estilo.display !== 'none' && estilo.visibility !== 'hidden' && caixa.width > 0 && caixa.height > 0;
                }),
                abaAtiva: document.querySelector('.tablink.active')?.getAttribute('data-tab-target') || ''
            })
            """);
        return JsonSerializer.Deserialize<EstadoParidadeCelular>(estado.GetRawText(), OpcoesJson)
            ?? throw new InvalidOperationException("Estado móvel de paridade não pôde ser desserializado.");
    }

    private static async Task<IBrowser> AbrirNavegadorAsync(IPlaywright playwright)
    {
        try
        {
            return await playwright.Chromium.LaunchAsync(new() { Headless = true });
        }
        catch (PlaywrightException)
        {
            try
            {
                return await playwright.Chromium.LaunchAsync(new() { Channel = "chrome", Headless = true });
            }
            catch (PlaywrightException)
            {
                return await playwright.Chromium.LaunchAsync(new() { Channel = "msedge", Headless = true });
            }
        }
    }

    private static string LocalizarRaizProjeto()
    {
        var atual = new DirectoryInfo(AppContext.BaseDirectory);
        while (atual is not null && !File.Exists(Path.Combine(atual.FullName, "EstacaoClimatica.sln")))
        {
            atual = atual.Parent;
        }
        return atual?.FullName ?? throw new DirectoryNotFoundException("Raiz do projeto não encontrada.");
    }

    private sealed record EstadoParidade(
        string Date,
        string ActiveTab,
        IReadOnlyList<string> Tabs,
        IReadOnlyList<int> StatsCounts,
        IReadOnlyList<string> StatsTexts,
        IReadOnlyList<string> EnvironmentStatsTexts,
        IReadOnlyList<string> TableTools,
        IReadOnlyList<string> QualityBadges,
        string AstroState,
        string InsightAction,
        IReadOnlyList<string> ChartXRotations,
        IReadOnlyList<string> ChartLabels,
        IReadOnlyList<EstadoEixoGrafico> ChartAxes,
        IReadOnlyList<string> ChartData,
        IReadOnlyList<IReadOnlyList<string>> TableHeaders,
        IReadOnlyList<int> TableRows,
        IReadOnlyList<int> AdvancedCells,
        IReadOnlyList<string> SolarLabels,
        IReadOnlyList<string> InsightTexts,
        string SolarDuration);

    private sealed record EstadoEixoGrafico(string Id, string Title, string SampleTick);

    private sealed record EstadoParidadeCelular(bool SemOverflowHorizontal, bool ControlesVisiveis, string AbaAtiva);

    private static readonly JsonSerializerOptions OpcoesJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private const string AuthOriginalAutorizada = """
        'use strict';
        (() => {
            const usuario = { email: 'usuario@example.com' };
            window.ClimateAuthService = {
                inicializar: async () => ({}),
                entrarComGoogle: async () => ({ user: usuario }),
                sair: async () => {},
                observarEstado: async callback => { callback(usuario); return () => {}; },
                obterUsuarioAtual: () => usuario,
                ehUsuarioInterno: () => true,
            };
        })();
        """;
}
