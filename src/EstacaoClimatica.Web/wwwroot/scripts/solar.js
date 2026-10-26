
function renderizarSolar(solar) {
    renderizarHistoricoSolar(solar);
    criarGraficoSolarDia(solar.day);
}

function renderizarHistoricoSolar(solar) {
    const cartao = document.getElementById('chart-container-sun');
    const canvas = document.getElementById('plotSunriseSunset');
    if (!canvas || !cartao) return;
    const id = canvas.id;
    limparMensagemGrafico(cartao);
    garantirCartaoComZoom(cartao);

    const seriesClimaticas = [solar.sunHistoryDawn, solar.sunHistorySunrise, solar.sunHistorySunset, solar.sunHistoryDusk];
    const possuiPontos = seriesClimaticas.some(item => item.points.some(ponto => Number.isFinite(ponto.value)));
    if (!possuiPontos) {
        graficosInstancias[id]?.destroy();
        delete graficosInstancias[id];
        renderizarMensagemGrafico(cartao, 'Sem histórico solar recente.');
        return;
    }

    // Amanhecer/nascer e pôr/anoitecer vivem em faixas horárias diferentes; remapeia para dois eixos legíveis.
    const remapear = (pontos, minimoEntrada, maximoEntrada, minimoSaida, maximoSaida) => pontos.map(ponto => ({
        ...ponto,
        value: Number.isFinite(ponto.value) ? mapearIntervalo(ponto.value * 3600, minimoEntrada, maximoEntrada, minimoSaida, maximoSaida) : null,
        rawValue: ponto.value
    }));

    const amanhecer = remapear(solar.sunHistoryDawn.points, 14400, 25200, 4, 7);
    const nascerDoSol = remapear(solar.sunHistorySunrise.points, 14400, 25200, 4, 7);
    const porDoSol = remapear(solar.sunHistorySunset.points, 61200, 75600, 17, 21);
    const anoitecer = remapear(solar.sunHistoryDusk.points, 61200, 75600, 17, 21);

    const grafico = criarOuAtualizarGrafico(canvas.getContext('2d'), {
        type: 'line',
        data: {
            labels: solar.sunHistorySunrise.points.map(ponto => ponto.label),
            datasets: [
                criarDatasetSolar('Amanhecer', amanhecer, 'yLeft', '#fde68a', 1),
                criarDatasetSolar('Nascer do sol', nascerDoSol, 'yLeft', '#fb923c', 2),
                criarDatasetSolar('Pôr do sol', porDoSol, 'yRight', '#f87171', 3),
                criarDatasetSolar('Anoitecer', anoitecer, 'yRight', '#818cf8', 4)
            ]
        },
        options: obterOpcoesHistoricoSolar()
    });

    grafico.$zoomKind = 'solarHistory';
    graficosInstancias[id] = grafico;
}

function criarDatasetSolar(rotulo, pontos, idEixo, cor, ordem) {
    return {
        label: rotulo,
        yAxisID: idEixo,
        data: pontos.map(ponto => ponto.value),
        rawValues: pontos.map(ponto => ponto.rawValue),
        borderColor: cor,
        backgroundColor: 'transparent',
        tension: 0.4,
        borderWidth: 2,
        pointRadius: 0,
        pointHitRadius: 18,
        pointHoverRadius: 6,
        order: ordem
    };
}

function obterOpcoesHistoricoSolar({ legend: legenda = true, tickSize: tamanhoMarcacao = 11, labelSize: tamanhoRotulo = 11 } = {}) {
    return {
        responsive: true,
        maintainAspectRatio: false,
        animation: { duration: 600 },
        interaction: { mode: 'index', intersect: false, axis: 'x' },
        hover: { mode: 'index', intersect: false },
        plugins: {
            legend: {
                display: legenda,
                position: 'top',
                labels: { color: cores.text, font: { size: tamanhoRotulo }, boxWidth: 12, padding: 16 }
            },
            tooltip: {
                ...padroesGrafico.plugins.tooltip,
                mode: 'index',
                intersect: false,
                itemSort: ordenarTooltipSolarPorPosicao,
                callbacks: {
                    label: contexto => {
                        const bruto = contexto.dataset.rawValues?.[contexto.dataIndex];
                        return `${contexto.dataset.label}: ${formatarTempo(bruto ?? contexto.parsed.y)}`;
                    }
                }
            }
        },
        scales: {
            x: {
                grid: { color: cores.grid, drawBorder: false },
                ticks: { color: cores.text, font: { size: tamanhoMarcacao }, maxRotation: 45 }
            },
            yLeft: {
                type: 'linear',
                position: 'right',
                min: 4,
                max: 7,
                grid: { color: cores.grid, drawBorder: false },
                ticks: { callback: formatarTempo, color: cores.text, font: { size: tamanhoMarcacao } }
            },
            yRight: {
                type: 'linear',
                position: 'left',
                min: 17,
                max: 21,
                grid: { drawOnChartArea: false },
                ticks: { callback: formatarTempo, color: cores.text, font: { size: tamanhoMarcacao } }
            }
        }
    };
}

function ordenarTooltipSolarPorPosicao(primeiro, segundo) {
    const pixelPrimeiro = obterPixelTooltipSolar(primeiro);
    const pixelSegundo = obterPixelTooltipSolar(segundo);
    if (Number.isFinite(pixelPrimeiro) && Number.isFinite(pixelSegundo)) return pixelPrimeiro - pixelSegundo;

    const valorPrimeiro = Number(primeiro.parsed?.y);
    const valorSegundo = Number(segundo.parsed?.y);
    if (!Number.isFinite(valorPrimeiro)) return 1;
    if (!Number.isFinite(valorSegundo)) return -1;
    return valorSegundo - valorPrimeiro;
}

function obterPixelTooltipSolar(contexto) {
    const serie = contexto.chart?.data?.datasets?.[contexto.datasetIndex];
    const escala = contexto.chart?.scales?.[serie?.yAxisID || 'y'];
    const valor = Number(contexto.parsed?.y);
    return escala && Number.isFinite(valor) ? escala.getPixelForValue(valor) : null;
}

function criarGraficoSolarDia(dia) {
    const cartao = document.getElementById('chart-container-sun-today');
    const canvas = document.getElementById('plotSolarToday');
    const chipDuracao = document.getElementById('solarDayDuration');
    if (!canvas || !cartao) return;
    const id = canvas.id;
    limparMensagemGrafico(cartao);
    garantirCartaoComZoom(cartao);

    if (!dia) {
        graficosInstancias[id]?.destroy();
        if (chipDuracao) chipDuracao.hidden = true;
        delete graficosInstancias[id];
        renderizarMensagemGrafico(cartao, `Sem dados suficientes para o ciclo solar em ${dataSelecionada.replaceAll('-', '/')}.`);
        return;
    }

    atualizarChipDuracaoDia(chipDuracao, dia);

    // A curva representa a intensidade visual do ciclo diário, enquanto os pontos destacam eventos solares.
    const pontos = [
        { x: 0, y: 0 },
        { x: dia.dawn, y: 0.08, label: 'Amanhecer', timeLabel: formatarTempo(dia.dawn) },
        { x: dia.sunrise, y: 0.52, label: 'Nascer do sol', timeLabel: formatarTempo(dia.sunrise) },
        { x: dia.zenith, y: 1, label: 'Zenite', timeLabel: formatarTempo(dia.zenith) },
        { x: dia.sunset, y: 0.52, label: 'Pôr do sol', timeLabel: formatarTempo(dia.sunset) },
        { x: dia.dusk, y: 0.08, label: 'Anoitecer', timeLabel: formatarTempo(dia.dusk) },
        { x: 24, y: 0 }
    ];

    const grafico = criarOuAtualizarGrafico(canvas.getContext('2d'), {
        type: 'line',
        data: {
            datasets: [{
                label: 'Luz do dia',
                data: pontos,
                borderColor: '#facc15',
                backgroundColor: 'rgba(250, 204, 21, 0.22)',
                fill: true,
                tension: 0.42,
                pointRadius: 0,
                pointHitRadius: 18,
                pointHoverRadius: 0,
                order: 2
            }, {
                type: 'scatter',
                label: 'Eventos solares',
                data: pontos.slice(1, 6),
                borderColor: '#f8fafc',
                backgroundColor: ['#fde68a', '#fb923c', '#facc15', '#f87171', '#818cf8'],
                pointBorderColor: '#0b1120',
                pointBorderWidth: 2,
                ...obterOpcoesPontosEventosSolares(),
                order: 1
            }]
        },
        options: obterOpcoesSolarDia(),
        plugins: [pluginFundoDiaSolar]
    });

    grafico.$solarDayTimes = dia;
    grafico.$zoomKind = 'solarToday';
    grafico.update('none');
    graficosInstancias[id] = grafico;
}

function obterOpcoesPontosEventosSolares() {
    const movel = !!window.matchMedia?.('(max-width: 640px)')?.matches;
    return {
        pointRadius: movel ? 7 : 5,
        pointHitRadius: movel ? 24 : 18,
        pointHoverRadius: movel ? 9 : 7
    };
}

function atualizarChipDuracaoDia(chip, dia) {
    if (!chip) return;
    const nascer = Number(dia?.sunrise);
    const por = Number(dia?.sunset);
    if (!Number.isFinite(nascer) || !Number.isFinite(por) || por < nascer) {
        chip.hidden = true;
        chip.textContent = '';
        return;
    }

    const minutos = Math.round((por - nascer) * 60);
    chip.textContent = `Duração do dia: ${Math.floor(minutos / 60)}h${String(minutos % 60).padStart(2, '0')}`;
    chip.hidden = false;
}

function obterOpcoesSolarDia({ tickSize: tamanhoMarcacao = 11, labelSize: tamanhoRotulo = 11 } = {}) {
    return {
        responsive: true,
        maintainAspectRatio: false,
        animation: { duration: 600 },
        interaction: { mode: 'nearest', intersect: false, axis: 'x' },
        hover: { mode: 'nearest', intersect: false },
        plugins: {
            legend: {
                display: true,
                position: 'top',
                labels: { color: cores.text, font: { size: tamanhoRotulo }, boxWidth: 12, padding: 16 }
            },
            tooltip: {
                ...padroesGrafico.plugins.tooltip,
                filter: contexto => contexto.dataset.label === 'Eventos solares',
                callbacks: {
                    title: itens => itens[0]?.raw?.label || '',
                    label: contexto => contexto.raw?.timeLabel || formatarTempo(contexto.parsed.x)
                }
            }
        },
        scales: {
            x: {
                type: 'linear',
                min: 0,
                max: 24,
                grid: { color: 'rgba(99,132,200,0.13)', drawBorder: false },
                ticks: { stepSize: 2, color: cores.text, font: { size: tamanhoMarcacao }, callback: valor => `${valor}h` }
            },
            y: {
                min: 0,
                max: 1.12,
                grid: { color: 'rgba(99,132,200,0.10)', drawBorder: false },
                ticks: { display: false }
            }
        }
    };
}


Object.assign(window, {
    renderizarSolar,
    renderizarHistoricoSolar,
    criarDatasetSolar,
    obterOpcoesHistoricoSolar,
    renderizarDiaSolar: criarGraficoSolarDia,
    obterOpcoesSolarHoje: obterOpcoesSolarDia,
    obterOpcoesPontosEventosSolares
});
