'use strict';

(function () {
    const HORAS_OBSERVADAS = 24;
    const HORAS_PREVISTAS = 12;
    const CONSULTA_MOVEL = '(max-width: 640px)';

    const pluginMarcadorAgoraChuva = {
        id: 'marcadorAgoraChuva',
        afterDraw(grafico) {
            const indice = grafico.$indiceAgora ?? grafico.$marcadorAgora?.indice;
            const escalaX = grafico.scales?.x;
            const area = grafico.chartArea;
            if (!Number.isInteger(indice) || indice < 0 || !escalaX || !area) return;
            const x = escalaX.getPixelForValue(indice);
            if (!Number.isFinite(x) || x < area.left || x > area.right) return;

            const contexto = grafico.ctx;
            contexto.save();
            contexto.strokeStyle = 'rgba(148, 163, 184, 0.65)';
            contexto.lineWidth = 1;
            contexto.setLineDash([4, 4]);
            contexto.beginPath();
            contexto.moveTo(x, area.top);
            contexto.lineTo(x, area.bottom);
            contexto.stroke();
            contexto.restore();
        }
    };

    const pluginControleResponsivoChuva = {
        id: 'controleResponsivoChuva',
        beforeDestroy(grafico) {
            removerControleMovel(grafico);
        }
    };

    function montarJanela(previsao = [], atualizadoEm = new Date(), horasObservadas = HORAS_OBSERVADAS, horasPrevistas = HORAS_PREVISTAS) {
        const agora = dataValida(atualizadoEm) || new Date();
        const inicio = new Date(agora.getTime() - horasObservadas * 60 * 60 * 1000);
        const inicioPrevisao = new Date(agora);
        inicioPrevisao.setMinutes(0, 0, 0);
        inicioPrevisao.setHours(inicioPrevisao.getHours() + 1);
        const fimPrevisao = new Date(inicioPrevisao.getTime() + horasPrevistas * 60 * 60 * 1000);
        const resultado = { horarios: [], tipos: [], precipitacao: [], probabilidade: [], indiceAgora: -1 };

        previsao.forEach(item => {
            const horario = dataValida(item?.horario);
            if (!horario) return;
            const observado = horario >= inicio && horario <= agora;
            const previsto = horario >= inicioPrevisao && horario < fimPrevisao;
            if (!observado && !previsto) return;
            resultado.horarios.push(item.horario);
            resultado.tipos.push(observado ? 'observado' : 'previsao');
            resultado.precipitacao.push(numeroOuNulo(item.precipitacao));
            resultado.probabilidade.push(numeroOuNulo(item.probabilidadeChuva));
            if (observado) resultado.indiceAgora = resultado.horarios.length - 1;
        });
        return resultado;
    }

    function analisarAgora(climaAtual = {}) {
        const precipitacao = numeroOuNulo(climaAtual.precipitacao);
        const chuva = numeroOuNulo(climaAtual.chuva);
        const codigoTempo = numeroOuNulo(climaAtual.codigoTempo);
        const codigoChuva = codigoTempo !== null && ((codigoTempo >= 51 && codigoTempo <= 67) || (codigoTempo >= 80 && codigoTempo <= 82) || (codigoTempo >= 95 && codigoTempo <= 99));
        const disponivel = precipitacao !== null || chuva !== null || codigoTempo !== null;
        const chovendo = (precipitacao ?? 0) > 0 || (chuva ?? 0) > 0 || codigoChuva;
        return { disponivel, chovendo, rotulo: !disponivel ? 'Condição atual indisponível' : chovendo ? 'Chovendo agora' : 'Sem chuva agora', precipitacao };
    }

    function criarGrafico({ canvas, janela, graficoExistente, cores: paleta = {}, grupoSincronizacao = null, modo = null }) {
        if (graficoExistente) {
            window.SincronizadorGraficos?.desregistrar(graficoExistente);
            graficoExistente.destroy();
        }
        if (!canvas || !window.Chart || !janela?.horarios?.length) return null;

        const observada = janela.precipitacao.map((valor, indice) => janela.tipos[indice] === 'observado' ? valor : null);
        const prevista = janela.precipitacao.map((valor, indice) => janela.tipos[indice] === 'previsao' ? valor : null);
        const modoFixo = modo === 'precipitacao' || modo === 'probabilidade' ? modo : null;
        const probabilidade = janela.probabilidade.map((valor, indice) => modoFixo || janela.tipos[indice] === 'previsao' ? valor : null);
        const valores = modoFixo === 'precipitacao' ? [...observada, ...prevista]
            : modoFixo === 'probabilidade' ? probabilidade : [...observada, ...prevista, ...probabilidade];
        if (!valores.some(valor => numeroOuNulo(valor) !== null)) return null;

        const corChuva = paleta.blue || '#38bdf8';
        const corPrevisao = 'rgba(125, 211, 252, 0.48)';
        const corProbabilidade = paleta.purple || '#a78bfa';
        const movel = estaEmTelaMovel();
        const probabilidadeObservada = probabilidade.map((valor, indice) => janela.tipos[indice] === 'observado' ? valor : null);
        const probabilidadePrevista = probabilidade.map((valor, indice) => janela.tipos[indice] === 'previsao' ? valor : null);
        const valorTransicao = probabilidadeObservada[janela.indiceAgora];
        if (numeroOuNulo(valorTransicao) !== null && probabilidadePrevista.some(valor => numeroOuNulo(valor) !== null)) {
            probabilidadePrevista[janela.indiceAgora] = valorTransicao;
        }
        const serieProbabilidadePublica = {
            type: 'line', yAxisID: 'yProbabilidade', backgroundColor: 'transparent',
            borderWidth: 2, tension: 0.3, pointRadius: 0, pointHitRadius: 18, spanGaps: false
        };
        const grafico = new Chart(canvas.getContext('2d'), {
            type: 'bar',
            data: {
                labels: janela.horarios.map(formatarHora),
                datasets: modoFixo === 'probabilidade' ? [
                    { ...serieProbabilidadePublica, label: 'Medido', data: probabilidadeObservada, borderColor: corProbabilidade, tipoDado: 'observado' },
                    { ...serieProbabilidadePublica, label: 'Previsão', data: probabilidadePrevista, borderColor: 'rgba(196, 181, 253, 0.58)', borderDash: [6, 4], tipoDado: 'previsao' }
                ] : [
                    { label: 'Precipitação observada', data: observada, yAxisID: 'yMilimetros', backgroundColor: `${corChuva}aa`, borderColor: corChuva, borderWidth: 1, borderRadius: 3, tipoDado: 'observado', order: 2 },
                    { label: 'Precipitação prevista', data: prevista, yAxisID: 'yMilimetros', backgroundColor: corPrevisao, borderColor: corPrevisao, borderWidth: 1, borderRadius: 3, tipoDado: 'previsao', order: 2 },
                    { type: 'line', label: 'Chance de chuva', data: probabilidade, yAxisID: 'yProbabilidade', borderColor: corProbabilidade, backgroundColor: 'transparent', borderDash: modoFixo ? undefined : [6, 4], segment: modoFixo ? { borderDash: contexto => janela.tipos[contexto.p1DataIndex] === 'previsao' ? [6, 4] : undefined } : undefined, borderWidth: 2, tension: 0.3, pointRadius: 0, pointHitRadius: 18, spanGaps: false, tipoDado: modoFixo ? 'probabilidade' : 'previsao', order: 1, hidden: !modoFixo && movel }
                ].filter(serie => !modoFixo || (modoFixo === 'probabilidade' ? serie.yAxisID === 'yProbabilidade' : serie.yAxisID === 'yMilimetros'))
            },
            options: obterOpcoes({ janela, cores: paleta, modo: modoFixo }),
            plugins: [pluginMarcadorAgoraChuva, pluginControleResponsivoChuva]
        });
        grafico.$indiceAgora = janela.indiceAgora;
        grafico.$marcadorAgora = { indice: janela.indiceAgora };
        grafico.$janelaChuva = janela;
        grafico.$modoChuvaFixo = modoFixo;
        grafico.$zoomPlugins = [pluginMarcadorAgoraChuva];
        grafico.$chavesSincronizacao = [...janela.horarios];
        grafico.$modoChuvaMovel = 'precipitacao';
        sincronizarModoResponsivo(grafico);
        if (grupoSincronizacao) window.SincronizadorGraficos?.registrar(grafico, grupoSincronizacao);
        grafico.update('none');
        return grafico;
    }

    function obterOpcoes({ janela = null, cores: paleta = window.cores || {}, modo = null } = {}) {
        const movel = estaEmTelaMovel();
        const limiteTicks = movel ? 5 : 13;
        const opcoes = mesclarRecursivamente(padroesGrafico, {
            onResize: grafico => sincronizarModoResponsivo(grafico, modo),
            interaction: { mode: 'index', intersect: false, axis: 'x' },
            plugins: {
                legend: { display: modo === 'probabilidade' || !movel, labels: { color: paleta.text || cores.text, boxWidth: 12, padding: 12 } },
                tooltip: {
                    mode: 'index', intersect: false,
                    filter: contexto => numeroOuNulo(contexto.raw) !== null
                        && !(modo === 'probabilidade' && contexto.dataset.tipoDado === 'previsao' && contexto.dataIndex <= janela.indiceAgora),
                    callbacks: {
                        title: itens => janela?.horarios?.[itens[0]?.dataIndex] ? formatarDataHora(janela.horarios[itens[0].dataIndex]) : itens[0]?.label || '--',
                        label: contexto => contexto.dataset.yAxisID === 'yProbabilidade' ? `${contexto.dataset.label}: ${Number(contexto.parsed.y).toFixed(0)}%` : `${contexto.dataset.label}: ${Number(contexto.parsed.y).toFixed(1)} mm`
                    }
                }
            },
            scales: {
                x: { stacked: true, ticks: { maxTicksLimit: limiteTicks } },
                yMilimetros: { type: 'linear', position: 'left', beginAtZero: true, display: true, title: { display: !movel, text: 'mm', color: paleta.text || cores.text }, ticks: { color: paleta.text || cores.text, callback: valor => `${valor} mm` }, grid: { color: paleta.grid || cores.grid } },
                yProbabilidade: { type: 'linear', position: modo === 'probabilidade' ? 'left' : 'right', min: 0, max: 100, display: !movel, title: { display: !movel, text: '%', color: paleta.text || cores.text }, ticks: { color: paleta.text || cores.text, callback: valor => `${valor}%` }, grid: { drawOnChartArea: false } }
            }
        });
        delete opcoes.scales.y;
        return opcoes;
    }

    function estaEmTelaMovel() {
        return !!window.matchMedia?.(CONSULTA_MOVEL)?.matches;
    }

    function sincronizarModoResponsivo(grafico, modo = grafico?.$modoChuvaFixo) {
        if (!grafico?.data?.datasets || !grafico.options?.scales) return;
        const movel = estaEmTelaMovel();
        grafico.options.plugins.legend.display = modo === 'probabilidade' || !movel;
        grafico.options.scales.x.ticks.maxTicksLimit = movel ? 5 : 13;
        grafico.options.scales.yMilimetros.title.display = !movel;
        grafico.options.scales.yProbabilidade.title.display = !movel;

        if (modo) {
            grafico.options.scales.yMilimetros.display = modo === 'precipitacao';
            grafico.options.scales.yProbabilidade.display = modo === 'probabilidade';
            removerControleMovel(grafico);
            return;
        }

        if (!movel) {
            grafico.data.datasets.forEach(serie => { serie.hidden = false; });
            grafico.options.scales.yMilimetros.display = true;
            grafico.options.scales.yProbabilidade.display = true;
            removerControleMovel(grafico);
            return;
        }

        criarControleMovel(grafico);
        aplicarModoMovel(grafico, grafico.$modoChuvaMovel || 'precipitacao', false);
    }

    function criarControleMovel(grafico) {
        const canvas = grafico?.canvas;
        const recipiente = canvas?.parentElement;
        if (!canvas || !recipiente) return;

        const seletor = `.rain-chart-toggle[data-rain-chart="${canvas.id}"]`;
        if (recipiente.querySelector(seletor)) return;

        const controle = document.createElement('div');
        controle.className = 'rain-chart-toggle';
        controle.dataset.rainChart = canvas.id;
        controle.setAttribute('role', 'group');
        controle.setAttribute('aria-label', 'Dado exibido no gráfico de chuva');
        controle.innerHTML = `
            <button type="button" data-rain-mode="precipitacao" aria-pressed="true">Precipitação</button>
            <button type="button" data-rain-mode="probabilidade" aria-pressed="false">Chance</button>
        `;
        controle.addEventListener('click', evento => {
            const botao = evento.target.closest?.('[data-rain-mode]');
            if (!botao) return;
            aplicarModoMovel(grafico, botao.dataset.rainMode);
        });
        recipiente.insertBefore(controle, canvas);
    }

    function aplicarModoMovel(grafico, modo, atualizar = true) {
        if (!grafico?.data?.datasets || !estaEmTelaMovel()) return;
        const mostrarProbabilidade = modo === 'probabilidade';
        grafico.$modoChuvaMovel = mostrarProbabilidade ? 'probabilidade' : 'precipitacao';
        grafico.data.datasets.forEach(serie => {
            serie.hidden = mostrarProbabilidade
                ? serie.yAxisID !== 'yProbabilidade'
                : serie.yAxisID === 'yProbabilidade';
        });
        grafico.options.scales.yMilimetros.display = !mostrarProbabilidade;
        grafico.options.scales.yProbabilidade.display = mostrarProbabilidade;

        const controle = grafico.canvas?.parentElement?.querySelector(
            `.rain-chart-toggle[data-rain-chart="${grafico.canvas.id}"]`
        );
        controle?.querySelectorAll('[data-rain-mode]').forEach(botao => {
            botao.setAttribute('aria-pressed', String(botao.dataset.rainMode === grafico.$modoChuvaMovel));
        });
        if (atualizar) grafico.update('none');
    }

    function removerControleMovel(grafico) {
        const canvas = grafico?.canvas;
        if (!canvas?.parentElement) return;
        canvas.parentElement
            .querySelector(`.rain-chart-toggle[data-rain-chart="${canvas.id}"]`)
            ?.remove();
    }

    function dataValida(valor) { const data = valor instanceof Date ? valor : new Date(valor); return Number.isNaN(data.getTime()) ? null : data; }
    function numeroOuNulo(valor) { if (valor === null || valor === undefined || valor === '') return null; const numero = Number(valor); return Number.isFinite(numero) ? numero : null; }
    function formatarHora(valor) { const data = dataValida(valor); return data ? `${String(data.getHours()).padStart(2, '0')}:${String(data.getMinutes()).padStart(2, '0')}` : '--'; }
    function formatarDataHora(valor) { const data = dataValida(valor); return data ? `${String(data.getDate()).padStart(2, '0')}/${String(data.getMonth() + 1).padStart(2, '0')} ${formatarHora(data)}` : '--'; }

    window.ClimaChuva = { montarJanela, analisarAgora, criarGrafico, obterOpcoes };
})();
