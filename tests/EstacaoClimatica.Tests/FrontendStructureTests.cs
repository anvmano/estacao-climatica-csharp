using System.Text.RegularExpressions;

namespace EstacaoClimatica.Tests;

public sealed partial class TestesEstruturaInterface
{
    [Fact]
    public void Index_DeveReferenciarArquivosLocaisExistentes()
    {
        var html = LerIndex();

        // Protege contra links quebrados quando scripts/CSS são movidos ou renomeados.
        var referencias = ExtrairReferenciasLocais(html, ExpressaoRegularOrigemScript())
            .Concat(ExtrairReferenciasLocais(html, ExpressaoRegularEnderecoEstilo()))
            .ToArray();

        Assert.NotEmpty(referencias);
        foreach (var referencia in referencias)
        {
            var caminho = Path.Combine(RaizArquivosWeb, referencia.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(caminho), $"Arquivo referenciado nao encontrado: {referencia}");
        }
    }

    [Fact]
    public void Style_DeveImportarArquivosCssLocaisExistentes()
    {
        var css = File.ReadAllText(Path.Combine(RaizArquivosWeb, "style.css"));
        var referencias = ExtrairReferenciasLocais(css, ExpressaoRegularImportacaoCss()).ToArray();

        Assert.NotEmpty(referencias);
        foreach (var referencia in referencias)
        {
            var caminho = Path.Combine(RaizArquivosWeb, referencia.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(caminho), $"Arquivo CSS importado nao encontrado: {referencia}");
        }
    }

    [Fact]
    public void Index_DeveConterIdsPrincipaisDaInterface()
    {
        var html = LerIndex();

        // Estes IDs são consumidos pelos scripts clássicos; alterar qualquer um quebra renderização ou eventos.
        var idsObrigatorios = new[]
        {
            "headerTime",
            "seasonIndicator",
            "seasonPopover",
            "astroIndicator",
            "astroPopover",
            "aqiIndicator",
            "aqiPopover",
            "moonIndicator",
            "moonPopover",
            "selectedDate",
            "btnToday",
            "exportFormatPdf",
            "exportFormatJson",
            "btnExportData",
            "loadingBar",
            "Tab0",
            "statsEstacao",
            "environmentInsights",
            "seasonTimeline",
            "moonSummary",
            "plotGlobalTemperature",
            "plotGlobalHumidity",
            "plotRain",
            "rainCurrentCondition",
            "statsSala",
            "chartsSala",
            "dataSala",
            "statsQuarto",
            "chartsQuarto",
            "data",
            "statsAquario",
            "chartsAquario",
            "dataAquario",
            "monthlyClimateCalendarSala",
            "hourlyHeatmapSala",
            "weeklyHeatmapSala",
            "monthlyClimateCalendar",
            "hourlyHeatmap",
            "weeklyHeatmap",
            "plotSolarToday",
            "solarDayDuration",
            "plotSunriseSunset",
            "aiChat",
            "aiChatToggle",
            "aiChatPanel",
            "aiChatClose",
            "aiChatMessages",
            "aiChatForm",
            "aiChatInput",
            "aiChatSubmit"
        };

        foreach (var id in idsObrigatorios)
        {
            Assert.Contains($"id=\"{id}\"", html);
        }
    }

    [Fact]
    public void Index_DeveConterControleDeExportacaoPdfEJson()
    {
        var html = LerIndex();

        Assert.Contains("id=\"exportFormatPdf\"", html);
        Assert.Contains("value=\"pdf\"", html);
        Assert.Contains("id=\"exportFormatJson\"", html);
        Assert.Contains("value=\"json\"", html);
        Assert.Contains("name=\"exportFormat\"", html);
        Assert.Contains("id=\"btnExportData\"", html);
        Assert.Contains("Exportar PDF", html);
        Assert.Contains("id=\"exportFeedback\"", html);
        Assert.Contains("id=\"exportStatus\"", html);
        Assert.Contains("id=\"btnRetryExport\"", html);
    }

    [Fact]
    public void Frontend_DevePreservarContratosRecentesDoOriginal()
    {
        var html = LerIndex();
        var publico = File.ReadAllText(Path.Combine(RaizArquivosWeb, "scripts", "public-weather.js"));
        var dashboard = File.ReadAllText(Path.Combine(RaizArquivosWeb, "scripts", "views", "dashboard-view.js"));
        var ui = File.ReadAllText(Path.Combine(RaizArquivosWeb, "scripts", "ui.js"));
        var exportacao = File.ReadAllText(Path.Combine(RaizArquivosWeb, "scripts", "pdf-report.js"));
        var zoom = File.ReadAllText(Path.Combine(RaizArquivosWeb, "scripts", "zoom.js"));
        var chuva = File.ReadAllText(Path.Combine(RaizArquivosWeb, "scripts", "chuva.js"));
        var sincronizacao = File.ReadAllText(Path.Combine(RaizArquivosWeb, "scripts", "sincronizacao-graficos.js"));

        Assert.Contains("id=\"publicSearchStatus\"", html);
        Assert.Contains("id=\"publicSearchModeCep\"", html);
        Assert.Contains("id=\"publicSearchModeCity\"", html);
        Assert.Contains("id=\"publicSearchInput\"", html);
        Assert.Contains("id=\"publicCityResults\"", html);
        Assert.Contains("aria-busy=\"false\"", html);
        Assert.Contains("tabindex=\"-1\"", html);
        Assert.Contains("aria-labelledby=\"tabEstacao\"", html);
        Assert.Contains("const idBusca = ++sequenciaBusca", publico);
        Assert.Contains("aria-invalid", publico);
        Assert.Contains("function pesquisarPorCidade", publico);
        Assert.Contains("/api/public-weather/cities?term=", publico);
        Assert.Contains("/api/public-weather/city", publico);
        Assert.Contains("function alterarModoBusca", publico);
        Assert.Contains("function renderizarOpcoesCidades", publico);
        Assert.Contains("sessionStorage.setItem", publico);
        Assert.Contains("['latitude', 'longitude', 'precisao', 'cep']", publico);
        Assert.Contains("Última consulta desta sessão restaurada.", publico);
        Assert.Contains("Math.floor", publico);
        Assert.Contains("min · desatualizado", publico);
        Assert.Contains("garantirCartaoComZoom(cartao)", publico);
        Assert.Contains("HORAS_PREVISAO_GRAFICOS = 12", publico);
        Assert.Contains("montarJanelaObservadaEPrevista", publico);
        Assert.Contains("label: 'Medido'", publico);
        Assert.Contains("label: 'Previsão'", publico);
        Assert.Contains("borderDash: [6, 4]", publico);
        Assert.Contains("marcadorAgoraPlugin", publico);
        Assert.Contains("data-table-sort", dashboard);
        Assert.Contains("data-table-csv", dashboard);
        Assert.Contains("evento.key === 'Home'", ui);
        Assert.Contains("evento.key === 'End'", ui);
        Assert.Contains("function executarComTimeout", exportacao);
        Assert.Contains("function atualizarProgresso", exportacao);
        Assert.Contains("plot-zoom-overlay--touch", zoom);
        Assert.Contains("botaoFechar.focus({ preventScroll: true })", zoom);
        Assert.Contains("borderDash: conjuntoDados.borderDash", zoom);
        Assert.Contains("tipoDado: conjuntoDados.tipoDado", zoom);
        Assert.Contains("graficoOrigem.$zoomPlugins", zoom);
        Assert.Contains("graficoAmpliado.$marcadorAgora", zoom);
        Assert.Contains("['rain', 'publicWeather', 'publicForecast'].includes(graficoOrigem.$zoomKind)", zoom);
        Assert.Contains("HORAS_OBSERVADAS = 24", chuva);
        Assert.Contains("HORAS_PREVISTAS = 12", chuva);
        Assert.Contains("label: 'Chance de chuva'", chuva);
        Assert.Contains("window.SincronizadorGraficos", sincronizacao);
        Assert.Contains("45 * 60 * 1000", sincronizacao);
        Assert.Contains("window.ClimaChuva", chuva);
        Assert.Contains("montarClimaExternoJson", exportacao);
        Assert.Contains("criarImagemChuvaPdf", exportacao);
        Assert.DoesNotContain("window.scrollTo({ top: 0", zoom);
    }

    [Fact]
    public void Index_DeveConterContainersDeGraficosTabelasEHeatmaps()
    {
        var html = LerIndex();

        // Valida a presença mínima das regiões onde o frontend injeta dados, gráficos e heatmaps.
        Assert.True(ContarOcorrencias(html, "class=\"charts-grid\"") >= 4);
        Assert.True(ContarOcorrencias(html, "class=\"table-wrapper\"") >= 3);
        Assert.True(ContarOcorrencias(html, "class=\"calendar-heatmap\"") >= 2);
        Assert.True(ContarOcorrencias(html, "class=\"hourly-heatmap\"") >= 2);
        Assert.True(ContarOcorrencias(html, "class=\"weekly-heatmap\"") >= 2);
        Assert.Contains("id=\"plotSolarToday\"", html);
        Assert.Contains("id=\"plotSunriseSunset\"", html);
        Assert.Contains("id=\"plotRain\"", html);
        Assert.Contains("scripts/sincronizacao-graficos.js", html);
        Assert.Contains("scripts/chuva.js", html);
        Assert.DoesNotContain("Tab2\" class=\"tab-content active", html);
    }

    [Fact]
    public void InterfaceResponsiva_DevePreservarSemanticaBreakpointsEAlvosDeToque()
    {
        var html = LerIndex();
        var baseCss = File.ReadAllText(Path.Combine(RaizArquivosWeb, "styles", "base.css"));
        var responsivo = File.ReadAllText(Path.Combine(RaizArquivosWeb, "styles", "responsive.css"));
        var chat = File.ReadAllText(Path.Combine(RaizArquivosWeb, "styles", "chat.css"));

        Assert.Contains("<main class=\"public-app\" id=\"publicApp\" aria-labelledby=\"publicPageTitle\"", html);
        Assert.Contains("<h1 id=\"publicPageTitle\">Clima por localização</h1>", html);
        Assert.Contains("<h1 class=\"visually-hidden\">Estação Climática</h1>", html);
        Assert.Contains("header-brand__title", html);
        Assert.Contains(".visually-hidden", baseCss);
        Assert.Contains("@media (min-width: 641px) and (max-width: 960px)", responsivo);
        Assert.Contains("@media (orientation: landscape) and (max-height: 540px) and (max-width: 900px)", responsivo);
        Assert.Contains("min-height: 44px", responsivo);
        Assert.Contains("width: 48px", responsivo);
        Assert.Contains(".ai-chat .ai-chat__panel", chat);
        Assert.Contains("position: fixed", chat);
        Assert.Contains("72dvh", chat);
    }

    [Fact]
    public void ChuvaMovelEModoPublicoCompacto_DevemConterContratosDeInteracao()
    {
        var chuva = File.ReadAllText(Path.Combine(RaizArquivosWeb, "scripts", "chuva.js"));
        var publico = File.ReadAllText(Path.Combine(RaizArquivosWeb, "scripts", "public-weather.js"));
        var graficos = File.ReadAllText(Path.Combine(RaizArquivosWeb, "styles", "charts.css"));
        var publicoCss = File.ReadAllText(Path.Combine(RaizArquivosWeb, "styles", "public-weather.css"));
        var responsivo = File.ReadAllText(Path.Combine(RaizArquivosWeb, "styles", "responsive.css"));
        var avancado = File.ReadAllText(Path.Combine(RaizArquivosWeb, "styles", "advanced-views.css"));
        var solar = File.ReadAllText(Path.Combine(RaizArquivosWeb, "scripts", "solar.js"));
        var sincronizacao = File.ReadAllText(Path.Combine(RaizArquivosWeb, "scripts", "sincronizacao-graficos.js"));

        Assert.Contains("HORAS_OBSERVADAS = 24", chuva);
        Assert.Contains("HORAS_PREVISTAS = 12", chuva);
        Assert.Contains("CONSULTA_MOVEL = '(max-width: 640px)'", chuva);
        Assert.Contains("onResize: grafico => sincronizarModoResponsivo(grafico, modo)", chuva);
        Assert.Contains("aria-pressed=\"true\">Precipitação", chuva);
        Assert.Contains("aria-pressed=\"false\">Chance", chuva);
        Assert.Contains("window.ClimaChuva", chuva);
        Assert.Contains(".rain-chart-toggle", graficos);
        Assert.Contains("classList.add('has-results')", publico);
        Assert.Contains("classList.remove('has-results')", publico);
        Assert.Contains("public-change-location", publico);
        Assert.Contains("public-metric-charts", publico);
        Assert.Contains("public-forecast-charts", publico);
        Assert.Contains("GRUPO_PREVISAO_QUINZE_DIAS", publico);
        Assert.Contains("previsaoEstendidaPlugin", publico);
        Assert.Contains("data-retry-forecast", publico);
        Assert.Contains("tabindex=\"0\"", publico);
        Assert.DoesNotContain(": 'Modo público'", publico);
        Assert.Contains(".public-app.has-results .public-hero", publicoCss);
        Assert.Contains("scroll-snap-type: x mandatory", publicoCss);
        Assert.Contains("#publicChartRain { height: 200px !important; }", responsivo);
        Assert.Contains("background: rgba(8, 15, 30, 0.88)", responsivo);
        Assert.Contains("function obterOpcoesPontosEventosSolares", solar);
        Assert.Contains("pointHitRadius: movel ? 24 : 18", solar);
        Assert.Contains("background: var(--bg-card)", avancado);
        Assert.Contains("weekly-heatmap__axis--corner", avancado);
        Assert.Contains("if (grafico === graficoOrigem) continue", sincronizacao);
        Assert.Contains("sincronizarIndice, limparGrupo", sincronizacao);
        Assert.Contains("repeat(3, minmax(0, 1fr))", publicoCss);
        Assert.Contains("publicChartRainProbability", publico);
    }

    [Fact]
    public void Projeto_NaoDeveExporChaveSecretaDeIaNoFrontendOuAppsettings()
    {
        var raiz = LocalizarRaizProjeto();
        var arquivos = Directory.EnumerateFiles(Path.Combine(raiz, "src", "EstacaoClimatica.Web"), "*.*", SearchOption.AllDirectories)
            .Where(caminho =>
                !caminho.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !caminho.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                (caminho.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                 caminho.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                 caminho.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        foreach (var arquivo in arquivos)
        {
            var conteudo = File.ReadAllText(arquivo);
            Assert.DoesNotMatch(@"AIza[0-9A-Za-z_-]{20,}", conteudo);
            Assert.DoesNotContain("GeminiApiKey", conteudo, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("GoogleAISecret", conteudo, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Index_DeveConterChatMigradoParaEndpointCSharp()
    {
        var html = LerIndex();

        Assert.Contains("scripts/chat.js", html);
        Assert.Contains("data-chat-question", html);
        Assert.Contains("Assistente climático", html);
        Assert.DoesNotContain("scripts/ai-service.js", html);
    }

    private static string LerIndex()
    {
        return File.ReadAllText(Path.Combine(RaizArquivosWeb, "index.html"));
    }

    private static string RaizArquivosWeb => Path.Combine(LocalizarRaizProjeto(), "src", "EstacaoClimatica.Web", "wwwroot");

    private static string LocalizarRaizProjeto()
    {
        // Os testes rodam a partir de bin/Debug; sobe diretórios até encontrar a solução.
        var diretorio = new DirectoryInfo(AppContext.BaseDirectory);
        while (diretorio is not null && !File.Exists(Path.Combine(diretorio.FullName, "EstacaoClimatica.sln")))
        {
            diretorio = diretorio.Parent;
        }

        return diretorio?.FullName ?? throw new InvalidOperationException("Nao foi possivel localizar EstacaoClimatica.sln.");
    }

    private static IEnumerable<string> ExtrairReferenciasLocais(string html, Regex regex)
    {
        foreach (Match correspondencia in regex.Matches(html))
        {
            var referencia = correspondencia.Groups[1].Value.Split('?')[0];

            // Referências externas não devem ser resolvidas no disco local.
            if (string.IsNullOrWhiteSpace(referencia)) continue;
            if (referencia.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
            if (referencia.StartsWith("//", StringComparison.Ordinal)) continue;
            yield return referencia.TrimStart('/');
        }
    }

    private static int ContarOcorrencias(string texto, string trecho)
    {
        return Regex.Matches(texto, Regex.Escape(trecho)).Count;
    }

    [GeneratedRegex("<script[^>]+src=\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex ExpressaoRegularOrigemScript();

    [GeneratedRegex("<link[^>]+href=\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex ExpressaoRegularEnderecoEstilo();

    [GeneratedRegex("@import\\s+url\\(\"([^\"]+)\"\\)", RegexOptions.IgnoreCase)]
    private static partial Regex ExpressaoRegularImportacaoCss();
}
