
var graficosInstancias = {};
var sobreposicaoZoom = null;
var dataSelecionada = paraDataFirebase(new Date());
var dashboardAtual = null;
var temporizadorIndicadorAstronomico = null;

var cores = {
    blue: '#38bdf8',
    green: '#34d399',
    purple: '#a78bfa',
    amber: '#fbbf24',
    teal: '#2dd4bf',
    rose: '#fb7185',
    grid: 'rgba(99,132,200,0.1)',
    text: '#94a3b8'
};

var faixaConforto = {
    min: 20,
    max: 26,
    label: 'Faixa de conforto'
};

var faixaConfortoUmidade = {
    min: 40,
    max: 60,
    label: 'Faixa de conforto da umidade'
};

var faixaConfortoAquario = {
    min: 25,
    max: 27,
    label: 'Faixa de conforto do aquário'
};

var idsTela = {
    sala: { stats: 'statsSala', graficosInstancias: 'chartsSala', table: 'dataSala' },
    quarto: { stats: 'statsQuarto', graficosInstancias: 'chartsQuarto', table: 'data' },
    aquario: { stats: 'statsAquario', graficosInstancias: 'chartsAquario', table: 'dataAquario' }
};

var idsContainersGraficos = {
    plotsTemp: 'chart-container-temp',
    plotsST: 'chart-container-st',
    plotsUmidade: 'chart-container-umidade',
    plotsTempSala: 'chart-container-temp-sala',
    plotsSTSala: 'chart-container-st-sala',
    plotsUmidadeSala: 'chart-container-umidade-sala',
    plotsPressaoSala: 'chart-container-pressao-sala',
    plotsTempAquario: 'chart-container-temp-aquario',
    plotsPH: 'chart-container-ph',
    plotsTDS: 'chart-container-tds',
    plotsTurbidez: 'chart-container-turbidez'
};

var idsAvancados = {
    sala: {
        monthlyCalendar: 'monthlyClimateCalendarSala',
        hourlyHeatmap: 'hourlyHeatmapSala',
        weeklyHeatmap: 'weeklyHeatmapSala'
    },
    quarto: {
        monthlyCalendar: 'monthlyClimateCalendar',
        hourlyHeatmap: 'hourlyHeatmap',
        weeklyHeatmap: 'weeklyHeatmap'
    }
};

var idsChat = {
    root: 'aiChat',
    toggle: 'aiChatToggle',
    panel: 'aiChatPanel',
    close: 'aiChatClose',
    form: 'aiChatForm',
    input: 'aiChatInput',
    submit: 'aiChatSubmit',
    messages: 'aiChatMessages',
    quickActions: '[data-chat-question]'
};

var mensagensChat = {
    inicial: 'Olá! Posso responder perguntas sobre a data e aba selecionadas.',
    analisando: 'Analisando os dados...',
    semResposta: 'Não consegui montar uma resposta para essa pergunta.',
    falha: 'Não consegui consultar o assistente agora. Tente novamente em instantes.'
};

var mensagensAvancadas = {
    calendario: 'Falha ao renderizar calendário climático.',
    heatmapHorario: 'Falha ao renderizar heatmap horário.',
    mapaSemanal: 'Falha ao renderizar mapa semanal.'
};

var padroesGrafico = {
    responsive: true,
    maintainAspectRatio: false,
    animation: { duration: 600, easing: 'easeInOutQuart' },
    interaction: { mode: 'index', intersect: false, axis: 'x' },
    hover: { mode: 'index', intersect: false },
    onHover: (evento, elementosAtivos, grafico) => window.SincronizadorGraficos?.tratarInteracao(
        grafico,
        evento?.native || evento,
        elementosAtivos
    ),
    onClick: (evento, elementosAtivos, grafico) => window.SincronizadorGraficos?.tratarInteracao(
        grafico,
        evento?.native || evento,
        elementosAtivos
    ),
    plugins: {
        legend: { display: false },
        tooltip: {
            mode: 'index',
            intersect: false,
            displayColors: false,
            backgroundColor: '#1a2234',
            borderColor: 'rgba(99,132,200,0.3)',
            borderWidth: 1,
            titleColor: cores.text,
            bodyColor: '#f1f5f9',
            padding: 10,
            cornerRadius: 8
        }
    },
    scales: {
        x: { grid: { color: cores.grid, drawBorder: false }, ticks: { color: cores.text, font: { size: 11 }, minRotation: 45, maxRotation: 45 } },
        y: { grid: { color: cores.grid, drawBorder: false }, ticks: { color: cores.text, font: { size: 11 } } }
    }
};

var pluginFaixaConforto = {
    id: 'faixaConforto',
    beforeDatasetsDraw(grafico) {
        const faixa = grafico.$faixaConforto;
        const escalaY = grafico.scales.y;
        const area = grafico.chartArea;
        if (!faixa || !escalaY || !area) return;

        const minimoVisivel = Math.min(escalaY.min, escalaY.max);
        const maximoVisivel = Math.max(escalaY.min, escalaY.max);
        const minimoFaixa = Math.max(faixa.min, minimoVisivel);
        const maximoFaixa = Math.min(faixa.max, maximoVisivel);
        if (minimoFaixa >= maximoFaixa) return;

        const minimoY = escalaY.getPixelForValue(minimoFaixa);
        const maximoY = escalaY.getPixelForValue(maximoFaixa);
        const topo = Math.max(area.top, Math.min(minimoY, maximoY));
        const base = Math.min(area.bottom, Math.max(minimoY, maximoY));
        const altura = base - topo;
        if (altura <= 0) return;

        const contextoDesenho = grafico.ctx;

        // A escala nasce dos dados medidos; a faixa e recortada ao trecho visivel do eixo Y.
        contextoDesenho.save();
        contextoDesenho.fillStyle = 'rgba(52, 211, 153, 0.08)';
        contextoDesenho.fillRect(area.left, topo, area.right - area.left, altura);
        contextoDesenho.strokeStyle = 'rgba(52, 211, 153, 0.20)';
        contextoDesenho.setLineDash([4, 4]);
        contextoDesenho.beginPath();
        if (faixa.min >= minimoVisivel && faixa.min <= maximoVisivel) {
            const linhaMinima = escalaY.getPixelForValue(faixa.min);
            contextoDesenho.moveTo(area.left, linhaMinima);
            contextoDesenho.lineTo(area.right, linhaMinima);
        }
        if (faixa.max >= minimoVisivel && faixa.max <= maximoVisivel) {
            const linhaMaxima = escalaY.getPixelForValue(faixa.max);
            contextoDesenho.moveTo(area.left, linhaMaxima);
            contextoDesenho.lineTo(area.right, linhaMaxima);
        }
        contextoDesenho.stroke();
        contextoDesenho.restore();
    }
};

var pluginFundoDiaSolar = {
    id: 'solarDayBackground',
    beforeDatasetsDraw(grafico) {
        const horarios = grafico.$solarDayTimes;
        const escalaX = grafico.scales.x;
        const area = grafico.chartArea;
        if (!horarios || !escalaX || !area) return;

        const contextoDesenho = grafico.ctx;
        const posicaoAmanhecerX = escalaX.getPixelForValue(horarios.dawn);
        const posicaoNascerSolX = escalaX.getPixelForValue(horarios.sunrise);
        const posicaoZeniteX = escalaX.getPixelForValue(horarios.zenith);
        const posicaoPorSolX = escalaX.getPixelForValue(horarios.sunset);
        const posicaoAnoitecerX = escalaX.getPixelForValue(horarios.dusk);

        // Fundo segmentado diferencia noite, transição, dia e anoitecer no ciclo solar diário.
        contextoDesenho.save();
        contextoDesenho.fillStyle = 'rgba(15, 23, 42, 0.72)';
        contextoDesenho.fillRect(area.left, area.top, Math.max(0, posicaoAmanhecerX - area.left), area.bottom - area.top);
        contextoDesenho.fillRect(posicaoAnoitecerX, area.top, Math.max(0, area.right - posicaoAnoitecerX), area.bottom - area.top);

        const crepusculo = contextoDesenho.createLinearGradient(posicaoAmanhecerX, 0, posicaoNascerSolX, 0);
        crepusculo.addColorStop(0, 'rgba(148, 163, 184, 0.24)');
        crepusculo.addColorStop(1, 'rgba(251, 191, 36, 0.22)');
        contextoDesenho.fillStyle = crepusculo;
        contextoDesenho.fillRect(posicaoAmanhecerX, area.top, Math.max(0, posicaoNascerSolX - posicaoAmanhecerX), area.bottom - area.top);

        const luzDiurna = contextoDesenho.createLinearGradient(posicaoNascerSolX, 0, posicaoPorSolX, 0);
        luzDiurna.addColorStop(0, 'rgba(251, 191, 36, 0.20)');
        luzDiurna.addColorStop(0.5, 'rgba(254, 240, 138, 0.38)');
        luzDiurna.addColorStop(1, 'rgba(251, 191, 36, 0.20)');
        contextoDesenho.fillStyle = luzDiurna;
        contextoDesenho.fillRect(posicaoNascerSolX, area.top, Math.max(0, posicaoPorSolX - posicaoNascerSolX), area.bottom - area.top);

        const entardecer = contextoDesenho.createLinearGradient(posicaoPorSolX, 0, posicaoAnoitecerX, 0);
        entardecer.addColorStop(0, 'rgba(251, 146, 60, 0.24)');
        entardecer.addColorStop(1, 'rgba(129, 140, 248, 0.18)');
        contextoDesenho.fillStyle = entardecer;
        contextoDesenho.fillRect(posicaoPorSolX, area.top, Math.max(0, posicaoAnoitecerX - posicaoPorSolX), area.bottom - area.top);

        contextoDesenho.strokeStyle = 'rgba(251, 191, 36, 0.35)';
        contextoDesenho.lineWidth = 1;
        contextoDesenho.beginPath();
        contextoDesenho.moveTo(posicaoZeniteX, area.top);
        contextoDesenho.lineTo(posicaoZeniteX, area.bottom);
        contextoDesenho.stroke();
        contextoDesenho.restore();
    }
};

if (window.Chart) {
    Chart.register(pluginFaixaConforto);
}

Object.assign(window, {
    idsChat,
    mensagensChat,
    mensagensAvancadas
});

window.AppConfig = window.AppConfig || {};
window.AppConfig.exports = { timeoutMs: 45000 };
window.AppConfig.publicData = {
    sessionKey: 'climatePublicLastQueryV1',
    staleAfterMinutes: 20,
    maxAgeMinutes: 60
};

