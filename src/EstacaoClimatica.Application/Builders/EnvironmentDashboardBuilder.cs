using System.Text.Json;
using EstacaoClimatica.Application.Configuration;
using EstacaoClimatica.Application.Models;
using EstacaoClimatica.Application.Utilities;
using EstacaoClimatica.Domain;

namespace EstacaoClimatica.Application.Builders;

internal sealed class MontadorDashboardAmbiente
{
    private readonly CoresAplicacao _cores;
    private readonly Func<DateTime> _obterAgora;

    public MontadorDashboardAmbiente(CoresAplicacao cores)
        : this(cores, () => RelogioClimatico.Agora)
    {
    }

    internal MontadorDashboardAmbiente(CoresAplicacao cores, Func<DateTime> obterAgora)
    {
        _cores = cores;
        _obterAgora = obterAgora;
    }

    public EnvironmentDashboard MontarQuarto(JsonElement? raiz, string dataSelecionada)
    {
        // Estatísticas e tabela usam o dia escolhido; gráficos comuns usam janela móvel de 24h.
        var dados = LeitorRegistrosFirebase.FiltrarDataSelecionada(raiz, dataSelecionada);
        var dadosGraficos = LeitorRegistrosFirebase.FiltrarDadosPorJanelaHoras(raiz, dataSelecionada, 24, _obterAgora());

        return Montar(
            raiz,
            dados,
            dadosGraficos,
            dataSelecionada,
            metricas: new[]
            {
                new DefinicaoMetrica(CamposFirebase.Quarto.Temperatura, "Temperatura", "°C"),
                new DefinicaoMetrica(CamposFirebase.Quarto.SensacaoTermica, "Sensação térmica", "°C"),
                new DefinicaoMetrica(CamposFirebase.Quarto.Umidade, "Umidade", "%")
            },
            graficos: new[]
            {
                new DefinicaoGrafico("plotsTemp", "Temperatura", CamposFirebase.Quarto.Temperatura, "°", _cores.Blue),
                new DefinicaoGrafico("plotsST", "Sensação Térmica", CamposFirebase.Quarto.SensacaoTermica, "°", _cores.Green),
                new DefinicaoGrafico("plotsUmidade", "Umidade", CamposFirebase.Quarto.Umidade, "%", _cores.Purple)
            },
            cabecalhosTabela: new[] { "Data", "Hora", CamposFirebase.Quarto.Temperatura, CamposFirebase.Quarto.SensacaoTermica, CamposFirebase.Quarto.Umidade },
            chaveMetricaHeatmap: CamposFirebase.Quarto.Temperatura);
    }

    public EnvironmentDashboard MontarSala(JsonElement? raiz, string dataSelecionada)
    {
        // Sala combina dados climáticos e qualidade do ar, preservando os nomes originais dos campos Firebase.
        var dados = LeitorRegistrosFirebase.FiltrarDataSelecionada(raiz, dataSelecionada);
        var dadosGraficos = LeitorRegistrosFirebase.FiltrarDadosPorJanelaHoras(raiz, dataSelecionada, 24, _obterAgora());

        return Montar(
            raiz,
            dados,
            dadosGraficos,
            dataSelecionada,
            metricas: new[]
            {
                new DefinicaoMetrica(CamposFirebase.Sala.Temperatura, "Temperatura", "°C"),
                new DefinicaoMetrica(CamposFirebase.Sala.SensacaoTermica, "Sensação térmica", "°C"),
                new DefinicaoMetrica(CamposFirebase.Sala.Umidade, "Umidade", "%"),
                new DefinicaoMetrica(CamposFirebase.Sala.Pressao, "Pressão", " hPa")
            },
            graficos: new[]
            {
                new DefinicaoGrafico("plotsTempSala", "Temperatura", CamposFirebase.Sala.Temperatura, "°", _cores.Blue),
                new DefinicaoGrafico("plotsSTSala", "Sensação Térmica", CamposFirebase.Sala.SensacaoTermica, "°", _cores.Green),
                new DefinicaoGrafico("plotsUmidadeSala", "Umidade", CamposFirebase.Sala.Umidade, "%", _cores.Purple),
                new DefinicaoGrafico("plotsPressaoSala", "Pressão (hPa)", CamposFirebase.Sala.Pressao, "", _cores.Amber)
            },
            cabecalhosTabela: new[] { "Data", "Hora", CamposFirebase.Sala.Co, CamposFirebase.Sala.Co2, CamposFirebase.Sala.Acetona, CamposFirebase.Sala.Alcool, CamposFirebase.Sala.Amonia, CamposFirebase.Sala.Tolueno },
            chaveMetricaHeatmap: CamposFirebase.Sala.Temperatura);
    }

    public EnvironmentDashboard MontarAquario(JsonElement? raiz, string dataSelecionada)
    {
        // O aquário não recebe heatmap avançado na regra atual; apenas resumo, gráficos e tabela.
        var dados = LeitorRegistrosFirebase.FiltrarDataSelecionada(raiz, dataSelecionada);
        var dadosGraficos = LeitorRegistrosFirebase.FiltrarDadosPorJanelaHoras(raiz, dataSelecionada, 24, _obterAgora());

        return Montar(
            raiz,
            dados,
            dadosGraficos,
            dataSelecionada,
            metricas: new[]
            {
                new DefinicaoMetrica(CamposFirebase.Aquario.Temperatura, "Temperatura", "°C"),
                new DefinicaoMetrica(CamposFirebase.Aquario.Ph, "pH", ""),
                new DefinicaoMetrica(CamposFirebase.Aquario.Tds, "TDS", "ppm"),
                new DefinicaoMetrica(CamposFirebase.Aquario.Turbidez, "Turbidez", "NTU")
            },
            graficos: new[]
            {
                new DefinicaoGrafico("plotsTempAquario", "Temperatura", CamposFirebase.Aquario.Temperatura, "°", _cores.Blue),
                new DefinicaoGrafico("plotsPH", "PH", CamposFirebase.Aquario.Ph, "", _cores.Teal),
                new DefinicaoGrafico("plotsTDS", "TDS", CamposFirebase.Aquario.Tds, "ppm", _cores.Amber),
                new DefinicaoGrafico("plotsTurbidez", "Turbidez", CamposFirebase.Aquario.Turbidez, "NTU", _cores.Rose)
            },
            cabecalhosTabela: new[] { "Data", "Hora", CamposFirebase.Aquario.Temperatura, CamposFirebase.Aquario.Ph, CamposFirebase.Aquario.Tds, CamposFirebase.Aquario.Turbidez },
            chaveMetricaHeatmap: null);
    }

    private static EnvironmentDashboard Montar(
        JsonElement? raiz,
        JsonElement? dados,
        JsonElement? dadosGraficos,
        string dataSelecionada,
        IReadOnlyList<DefinicaoMetrica> metricas,
        IReadOnlyList<DefinicaoGrafico> graficos,
        IReadOnlyList<string> cabecalhosTabela,
        string? chaveMetricaHeatmap)
    {
        // Composição comum dos ambientes para evitar lógica de negócio espalhada no controller.
        return new EnvironmentDashboard(
            MontadorEstatisticas.Montar(dados, metricas),
            graficos.Select(grafico => MontadorSeriesGraficos.Montar(dadosGraficos, grafico, dados)).ToList(),
            MontadorDadosTabela.Montar(dados, cabecalhosTabela),
            chaveMetricaHeatmap is null ? null : MontadorMapaCalor.Montar(raiz, dataSelecionada, chaveMetricaHeatmap));
    }
}
