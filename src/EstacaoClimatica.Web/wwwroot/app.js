
document.addEventListener('DOMContentLoaded', () => {
    configurarRelogio();
    configurarCabecalhoMovel();
    window.ClimateSeason?.setup?.();
    window.ClimateMoon?.setup?.();
    configurarIndicadorAstronomico();
    configurarModulo();
    window.PublicWeatherApp.setup();
});

let painelInternoInicializado = false;

window.inicializarPainelInterno = function () {
    if (painelInternoInicializado) {
        carregarDashboard();
        return;
    }

    painelInternoInicializado = true;
    configurarAbas();
    configurarDeslizeAbas();
    configurarControlesData();
    configurarSecoesRecolhaveis();
    configurarEventosZoom();
    configurarExportacaoDados();
    configurar();
    carregarDashboard();
};

async function carregarDashboard() {
    definirCarregando(true);
    try {
        // A API espera a data no mesmo formato do Firebase; o controle HTML converte antes de atualizar este estado.
        const respostaLocal = await window.ClimateAuth.fetchInterno(`/api/dashboard?date=${encodeURIComponent(dataSelecionada)}`);
        if (!respostaLocal.ok) throw new Error(`HTTP ${respostaLocal.status}`);
        const dashboard = await respostaLocal.json();
        dashboardAtual = dashboard;

        // Renderização acontece por domínio para manter Sala, Quarto, Aquário e Solar independentes na tela.
        renderizarEstacao(dashboard);
        renderizarAmbiente(idsTela.sala, dashboard.sala, 'sala');
        renderizarAmbiente(idsTela.quarto, dashboard.quarto, 'quarto');
        renderizarAmbiente(idsTela.aquario, dashboard.aquario, 'aquario');
        renderizarSolar(dashboard.solar);
        atualizarModulo(dashboard.aqi);
        window.ClimateSeason?.update?.();
        window.ClimateMoon?.update?.();
        atualizarIndicadorAstronomico();
        renderizarAvancado(dashboard.sala.advanced, idsAvancados.sala);
        renderizarAvancado(dashboard.quarto.advanced, idsAvancados.quarto);
    } catch (erro) {
        console.error(erro);
        renderizarErroDashboard();
    } finally {
        definirCarregando(false);
    }
}
