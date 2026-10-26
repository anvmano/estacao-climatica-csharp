
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
let geracaoDashboard = 0;
let consultaDashboard = null;
let painelInternoAtivo = false;
const abasPendentes = new Set();
const avancadosPendentes = new Set();
let conexaoAtualizacoes = null;
let reconexaoAtualizacoes = null;
let atualizacaoAgendada = null;
const falhasAtualizacoes = new Set();

window.inicializarPainelInterno = function () {
    painelInternoAtivo = true;
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
    window.RecursosClimaticos.prepararControlesInternos();
    carregarDashboard();
};

window.pararPainelInterno = function () {
    painelInternoAtivo = false;
    geracaoDashboard++;
    consultaDashboard?.abort();
    conexaoAtualizacoes?.abort();
    conexaoAtualizacoes = null;
    clearTimeout(reconexaoAtualizacoes);
    clearTimeout(atualizacaoAgendada);
    reconexaoAtualizacoes = null;
    falhasAtualizacoes.clear();
    abasPendentes.clear();
    avancadosPendentes.clear();
    definirCarregando(false);
};

function renderizarAbaPendente() {
    if (!painelInternoAtivo || !dashboardAtual) return;
    const aba = obterNomeAbaAtiva() || 'Tab0';
    const dashboard = dashboardAtual;
    if (abasPendentes.delete(aba)) {
        if (aba === 'Tab0') { renderizarEstacao(dashboard); renderizarSolar(dashboard.solar); }
        else {
            const ambiente = { Tab1: 'sala', Tab2: 'quarto', Tab3: 'aquario' }[aba];
            if (ambiente) renderizarAmbiente(idsTela[ambiente], dashboard[ambiente], ambiente);
        }
    }
    const ambiente = { Tab1: 'sala', Tab2: 'quarto' }[aba];
    if (ambiente && avancadosPendentes.has(ambiente) && document.querySelector(`#${aba} .advanced-section:not(.table-section):not(.is-collapsed)`)) {
        renderizarAvancado(dashboard[ambiente].advanced, idsAvancados[ambiente]);
        avancadosPendentes.delete(ambiente);
    }
}

function atualizarAvisoSensores(falhas) {
    falhas = [...new Set([...falhas, ...falhasAtualizacoes])];
    const aviso = document.getElementById('sensorConnectionAlert');
    if (!aviso) return;
    aviso.hidden = !falhas.length;
    aviso.textContent = falhas.length ? `Falha ao atualizar ${falhas.join(', ')}. Últimas leituras podem estar desatualizadas; confira o horário em cada card.` : '';
}

async function iniciarAtualizacoes() {
    if (!painelInternoAtivo || conexaoAtualizacoes || reconexaoAtualizacoes) return;
    const controle = conexaoAtualizacoes = new AbortController();
    try {
        const resposta = await window.ClimateAuth.fetchInterno('/api/dashboard/updates', { signal: controle.signal });
        if (!resposta.ok || !resposta.body) throw new Error('Atualizacao em tempo real indisponivel.');
        const leitor = resposta.body.getReader();
        const decodificador = new TextDecoder();
        let pendente = '';
        while (!controle.signal.aborted) {
            const { value, done } = await leitor.read();
            if (done) throw new Error('Conexao de atualizacoes encerrada.');
            pendente += decodificador.decode(value, { stream: true });
            let fim;
            while ((fim = pendente.indexOf('\n\n')) >= 0) {
                const mensagem = pendente.slice(0, fim);
                pendente = pendente.slice(fim + 2);
                const linha = mensagem.split('\n').find(item => item.startsWith('data: '));
                if (!linha || !painelInternoAtivo || conexaoAtualizacoes !== controle) continue;
                const evento = JSON.parse(linha.slice(6));
                falhasAtualizacoes.delete('conexão');
                if (evento.failed) falhasAtualizacoes.add(evento.branch);
                else falhasAtualizacoes.delete(evento.branch);
                atualizarAvisoSensores(dashboardAtual?.failedBranches || []);
                if (!evento.failed) {
                    clearTimeout(atualizacaoAgendada);
                    atualizacaoAgendada = setTimeout(() => { if (painelInternoAtivo) carregarDashboard(true); }, 150);
                }
            }
        }
    } catch (erro) {
        if (!controle.signal.aborted) {
            falhasAtualizacoes.add('conexão');
            atualizarAvisoSensores(dashboardAtual?.failedBranches || []);
        }
    } finally {
        if (conexaoAtualizacoes === controle) {
            conexaoAtualizacoes = null;
            if (painelInternoAtivo) reconexaoAtualizacoes = setTimeout(() => {
                reconexaoAtualizacoes = null;
                iniciarAtualizacoes();
            }, 5000);
        }
    }
}

async function carregarDashboard(silencioso = false) {
    const geracao = ++geracaoDashboard;
    consultaDashboard?.abort();
    const controle = consultaDashboard = new AbortController();
    const dataConsulta = dataSelecionada;
    if (!silencioso) definirCarregando(true);
    try {
        // A API espera a data no mesmo formato do Firebase; o controle HTML converte antes de atualizar este estado.
        const respostaLocal = await window.ClimateAuth.fetchInterno(`/api/dashboard?date=${encodeURIComponent(dataConsulta)}`, { signal: controle.signal });
        if (!respostaLocal.ok) throw new Error(`HTTP ${respostaLocal.status}`);
        const dashboard = await respostaLocal.json();
        await window.RecursosClimaticos.carregarChart();
        if (geracao !== geracaoDashboard || dataConsulta !== dataSelecionada || !painelInternoAtivo) return;
        const falhas = dashboard.failedBranches || [];
        if (dashboardAtual?.date === dashboard.date) {
            falhas.forEach(ramo => { dashboard[ramo] = dashboardAtual[ramo]; });
            if (falhas.includes('sala')) dashboard.aqi = dashboardAtual.aqi;
            if (dashboard.insights && dashboardAtual.insights) {
                if (falhas.includes('sala')) {
                    dashboard.insights.riscoSala = dashboardAtual.insights.riscoSala;
                    dashboard.insights.ventilacao = dashboardAtual.insights.ventilacao;
                }
                if (falhas.includes('quarto')) dashboard.insights.riscoQuarto = dashboardAtual.insights.riscoQuarto;
            }
            if (dashboard.latest && dashboardAtual.latest) {
                Object.keys(dashboard.latest).forEach(chave => {
                    if (falhas.some(ramo => chave.startsWith(ramo))) dashboard.latest[chave] = dashboardAtual.latest[chave];
                });
            }
        }
        dashboardAtual = dashboard;

        // Renderização acontece por domínio para manter Sala, Quarto, Aquário e Solar independentes na tela.
        ['Tab0', 'Tab1', 'Tab2', 'Tab3'].forEach(aba => abasPendentes.add(aba));
        ['sala', 'quarto'].forEach(ambiente => avancadosPendentes.add(ambiente));
        renderizarAbaPendente();
        atualizarAvisoSensores(falhas);
        if (respostaLocal.headers?.get('X-Climate-Updates') === 'enabled') iniciarAtualizacoes();
        atualizarModulo(dashboard.aqi);
        window.ClimateSeason?.update?.();
        window.ClimateMoon?.update?.();
        atualizarIndicadorAstronomico();
    } catch (erro) {
        if (controle.signal.aborted || geracao !== geracaoDashboard || !painelInternoAtivo) return;
        console.error(erro);
        atualizarAvisoSensores(['Sala', 'Quarto', 'Aquário', 'Solar']);
        if (!dashboardAtual) renderizarErroDashboard();
    } finally {
        if (geracao === geracaoDashboard) definirCarregando(false);
    }
}
