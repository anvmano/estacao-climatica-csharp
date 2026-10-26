
let disparadorZoom = null;
let sequenciaZoom = 0;

function ehDispositivoToque() {
    return window.matchMedia('(pointer: coarse)').matches || navigator.maxTouchPoints > 0;
}

function criarBotaoAmpliacao(cartao) {
    if (cartao.querySelector('.chart-zoom-button')) return;

    const botao = document.createElement('button');
    botao.type = 'button';
    botao.className = 'chart-zoom-button';
    botao.setAttribute('aria-label', 'Ampliar gráfico');
    botao.innerHTML = `
        <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
            <path d="M15 3h6v6"/>
            <path d="M21 3l-7 7"/>
            <path d="M9 21H3v-6"/>
            <path d="M3 21l7-7"/>
        </svg>
    `;
    botao.addEventListener('click', eventoLocal => {
        eventoLocal.stopPropagation();
        tratarAmpliacao(cartao, botao);
    });
    cartao.appendChild(botao);
}

function garantirCartaoComZoom(cartao) {
    cartao.classList.add('chart-card--zoomable');
    cartao.removeAttribute('title');
    criarBotaoAmpliacao(cartao);
    if (!cartao.dataset.zoomBound) {
        cartao.addEventListener('dblclick', () => {
            const botaoZoom = cartao.querySelector('.chart-zoom-button');
            tratarAmpliacao(cartao, botaoZoom || cartao);
        });
        cartao.dataset.zoomBound = 'true';
    }
}

function conterFocoNoDialogo(evento, dialogo) {
    if (evento.key !== 'Tab') return;

    const focaveis = Array.from(dialogo.querySelectorAll(
        'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
    )).filter(elemento => !elemento.hasAttribute('hidden'));
    if (!focaveis.length) {
        evento.preventDefault();
        dialogo.focus();
        return;
    }

    const primeiro = focaveis[0];
    const ultimo = focaveis[focaveis.length - 1];
    const focoAtual = document.activeElement;
    if (evento.shiftKey && (focoAtual === primeiro || !dialogo.contains(focoAtual))) {
        evento.preventDefault();
        ultimo.focus();
    } else if (!evento.shiftKey && (focoAtual === ultimo || !dialogo.contains(focoAtual))) {
        evento.preventDefault();
        primeiro.focus();
    }
}

async function tratarAmpliacao(cartao, disparador = document.activeElement) {
    fecharAmpliacao({ restaurarFoco: false });
    const sequencia = sequenciaZoom;
    try { await window.RecursosClimaticos.carregarCssZoom(); }
    catch (erro) { console.warn(erro.message); return; }
    if (sequencia !== sequenciaZoom || !cartao.isConnected || cartao.offsetParent === null) return;

    const telaGraficoOrigem = cartao.querySelector('canvas');
    if (!telaGraficoOrigem) return;

    const graficoOrigem = graficosInstancias[telaGraficoOrigem.id] || window.Chart?.getChart?.(telaGraficoOrigem);
    if (!graficoOrigem) return;

    const sobreposicao = document.createElement('div');
    sobreposicao.className = 'plot-zoom-overlay';
    sobreposicao.setAttribute('role', 'dialog');
    sobreposicao.setAttribute('aria-modal', 'true');
    sobreposicao.setAttribute('tabindex', '-1');
    if (ehDispositivoToque()) {
        sobreposicao.classList.add('plot-zoom-overlay--touch');
    }

    const clone = cartao.cloneNode(false);
    const rotulo = cartao.querySelector('.chart-label');
    const botaoFechar = document.createElement('button');
    const telaGraficoAmpliado = document.createElement('canvas');
    botaoFechar.type = 'button';
    botaoFechar.className = 'plot-zoom-close';
    botaoFechar.setAttribute('aria-label', 'Fechar gráfico ampliado');
    botaoFechar.innerHTML = '&times;';
    telaGraficoAmpliado.className = 'plot plot--zoom';
    telaGraficoAmpliado.setAttribute('aria-label', telaGraficoOrigem.getAttribute('aria-label') || 'Gráfico ampliado');
    telaGraficoAmpliado.setAttribute('role', 'img');

    if (rotulo) {
        const rotuloAmpliado = rotulo.cloneNode(true);
        rotuloAmpliado.id = `zoom-chart-label-${telaGraficoOrigem.id}`;
        sobreposicao.setAttribute('aria-labelledby', rotuloAmpliado.id);
        clone.appendChild(rotuloAmpliado);
    } else {
        sobreposicao.setAttribute('aria-label', telaGraficoOrigem.getAttribute('aria-label') || 'Gráfico ampliado');
    }
    clone.appendChild(botaoFechar);
    clone.appendChild(telaGraficoAmpliado);
    sobreposicao.appendChild(clone);
    const fecharSeFundo = eventoLocal => {
        if (eventoLocal.target === sobreposicao) fecharAmpliacao();
    };
    sobreposicao.addEventListener('pointerdown', fecharSeFundo);
    sobreposicao.addEventListener('click', fecharSeFundo);
    clone.addEventListener('pointerdown', eventoLocal => eventoLocal.stopPropagation());
    clone.addEventListener('touchstart', eventoLocal => eventoLocal.stopPropagation(), { passive: true });
    telaGraficoAmpliado.addEventListener('pointerdown', eventoLocal => eventoLocal.stopPropagation());
    telaGraficoAmpliado.addEventListener('touchstart', eventoLocal => eventoLocal.stopPropagation(), { passive: true });
    botaoFechar.addEventListener('click', eventoLocal => {
        eventoLocal.stopPropagation();
        fecharAmpliacao();
    });
    sobreposicao.addEventListener('keydown', eventoLocal => conterFocoNoDialogo(eventoLocal, sobreposicao));
    disparadorZoom = disparador instanceof HTMLElement ? disparador : null;

    sobreposicaoZoom = sobreposicao;
    document.body.appendChild(sobreposicao);

    const plugins = [];
    if (graficoOrigem.$solarDayTimes) plugins.push(pluginFundoDiaSolar);
    if (Array.isArray(graficoOrigem.$zoomPlugins)) plugins.push(...graficoOrigem.$zoomPlugins);
    const graficoAmpliado = new Chart(telaGraficoAmpliado.getContext('2d'), {
        type: graficoOrigem.config.type || 'line',
        data: copiarDadosGrafico(graficoOrigem),
        options: obterOpcoesZoom(graficoOrigem),
        plugins
    });
    graficoAmpliado.$modoChuvaFixo = graficoOrigem.$modoChuvaFixo;
    graficoAmpliado.$modoChuvaMovel = graficoOrigem.$modoChuvaMovel;
    graficoAmpliado.$janelaChuva = graficoOrigem.$janelaChuva;
    graficoAmpliado.$indiceAgora = graficoOrigem.$indiceAgora;
    graficoAmpliado.$horariosAtivos = graficoOrigem.$horariosAtivos;
    graficoAmpliado.$tiposAtivos = graficoOrigem.$tiposAtivos;
    graficoAmpliado.$visualizacoesChuva = graficoOrigem.$visualizacoesChuva;
    graficoAmpliado.$chavesSincronizacao = [...(graficoOrigem.$chavesSincronizacao || [])];
    if (graficoOrigem.$solarDayTimes) graficoAmpliado.$solarDayTimes = graficoOrigem.$solarDayTimes;
    if (graficoOrigem.$faixaConforto) {
        graficoAmpliado.$faixaConforto = graficoOrigem.$faixaConforto;
        graficoAmpliado.update();
    }
    if (graficoOrigem.$marcadorAgora) {
        graficoAmpliado.$marcadorAgora = { ...graficoOrigem.$marcadorAgora };
        graficoAmpliado.update('none');
    }
    sobreposicaoZoom._chart = graficoAmpliado;
    botaoFechar.focus({ preventScroll: true });
}

function fecharAmpliacao({ restaurarFoco = true } = {}) {
    sequenciaZoom++;
    if (!sobreposicaoZoom) return;
    const disparadorAnterior = disparadorZoom;
    if (sobreposicaoZoom._chart) sobreposicaoZoom._chart.destroy();
    sobreposicaoZoom.remove();
    sobreposicaoZoom = null;
    disparadorZoom = null;
    if (restaurarFoco && disparadorAnterior?.isConnected) {
        requestAnimationFrame(() => disparadorAnterior.focus({ preventScroll: true }));
    }
}

document.addEventListener('keydown', eventoLocal => {
    if (eventoLocal.key === 'Escape') fecharAmpliacao();
});

function copiarDadosGrafico(grafico) {
    return {
        labels: grafico.data.labels ? [...grafico.data.labels] : undefined,
        datasets: grafico.data.datasets.map((conjuntoDados, indice) => ({
            ...conjuntoDados,
            hidden: !grafico.isDatasetVisible(indice),
            type: conjuntoDados.type,
            label: conjuntoDados.label,
            data: Array.isArray(conjuntoDados.data)
                ? conjuntoDados.data.map(item => item && typeof item === 'object' ? { ...item } : item)
                : conjuntoDados.data,
            rawValues: conjuntoDados.rawValues ? [...conjuntoDados.rawValues] : undefined,
            yAxisID: conjuntoDados.yAxisID,
            borderColor: conjuntoDados.borderColor,
            borderDash: conjuntoDados.borderDash,
            backgroundColor: Array.isArray(conjuntoDados.backgroundColor) || typeof conjuntoDados.backgroundColor === 'string' ? conjuntoDados.backgroundColor : 'transparent',
            fill: conjuntoDados.fill,
            tension: conjuntoDados.tension,
            borderWidth: conjuntoDados.borderWidth,
            borderRadius: conjuntoDados.borderRadius,
            pointRadius: conjuntoDados.pointRadius,
            pointHitRadius: conjuntoDados.pointHitRadius || 18,
            pointHoverRadius: conjuntoDados.pointHoverRadius || 6,
            pointBackgroundColor: conjuntoDados.pointBackgroundColor,
            pointBorderColor: conjuntoDados.pointBorderColor,
            pointHoverBackgroundColor: conjuntoDados.pointHoverBackgroundColor,
            pointHoverBorderColor: conjuntoDados.pointHoverBorderColor,
            pointHoverBorderWidth: conjuntoDados.pointHoverBorderWidth,
            showLine: conjuntoDados.showLine,
            spanGaps: conjuntoDados.spanGaps,
            order: conjuntoDados.order,
            parsing: conjuntoDados.parsing,
            tipoDado: conjuntoDados.tipoDado
        }))
    };
}

function obterOpcoesZoom(graficoOrigem) {
    if (graficoOrigem.$zoomKind === 'solarHistory') return obterOpcoesHistoricoSolar({ tickSize: 12, labelSize: 12 });
    if (graficoOrigem.$zoomKind === 'solarToday' || graficoOrigem.$solarDayTimes) return obterOpcoesSolarDia({ tickSize: 12, labelSize: 12 });
    if (['rain', 'publicWeather', 'publicForecast'].includes(graficoOrigem.$zoomKind)) {
        const opcoes = mesclarRecursivamente(graficoOrigem.config.options, {
            animation: false,
            maintainAspectRatio: false
        });
        // O zoom preserva a selecao de series, sem recriar o seletor responsivo.
        delete opcoes.onResize;
        delete opcoes.onHover;
        delete opcoes.onClick;
        return opcoes;
    }

    const unidade = graficoOrigem.options?.scales?.y?.title?.text || '';

    return mesclarRecursivamente(padroesGrafico, {
        animation: false,
        maintainAspectRatio: false,
        plugins: {
            tooltip: {
                callbacks: {
                    label: contextoLocal => {
                        const valor = contextoLocal.parsed.y;
                        const formatado = Number.isFinite(valor) ? valor.toFixed(2) : '--';
                        return `${contextoLocal.dataset.label || ''}: ${formatado}`;
                    }
                }
            }
        },
        scales: {
            x: { ticks: { font: { size: 12 } } },
            y: {
                ticks: { font: { size: 12 } },
                title: { display: Boolean(unidade), text: unidade, color: cores.text }
            }
        }
    });
}

Object.assign(window, {
    adicionarBotaoZoom: criarBotaoAmpliacao,
    garantirCartaoComZoom,
    dispositivoTouch: ehDispositivoToque,
    conterFocoNoDialogo,
    manipularZoom: tratarAmpliacao,
    fecharZoom: fecharAmpliacao,
    clonarDadosGrafico: copiarDadosGrafico,
    obterOpcoesZoom
});
