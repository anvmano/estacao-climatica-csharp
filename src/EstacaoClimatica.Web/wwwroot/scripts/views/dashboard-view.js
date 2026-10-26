
function renderizarErroDashboard() {
    Object.values(idsTela).forEach(alvo => {
        renderizarErro(alvo.table, 'Falha ao carregar o Firebase. Verifique a conexão com a internet.');
    });
    renderizarMensagemGrafico(document.getElementById('chart-container-sun'), 'Falha ao carregar o Firebase.', 'error');
    renderizarMensagemGrafico(document.getElementById('chart-container-sun-today'), 'Falha ao carregar o Firebase.', 'error');
}

function renderizarAmbiente(alvos, secao, nomeSecao) {
    renderizarEstatisticas(alvos.stats, secao.stats);
    renderizarGradeGraficos(alvos.graficosInstancias, secao.charts, nomeSecao);
    renderizarTabela(alvos.table, secao.table);
}

function renderizarEstatisticas(id, estatisticas) {
    const elementoDom = document.getElementById(id);
    if (!elementoDom) return;
    elementoDom.innerHTML = '';

    estatisticas.forEach(metricaLocal => {
        const cartao = document.createElement('article');
        cartao.className = 'stats-card';
        const qualidade = metricaLocal.quality;
        const tendencia = qualidade?.level === 'critica'
            ? { label: 'Leitura crítica', className: 'critical', symbol: '!' }
            : qualidade?.level === 'suspeita'
                ? { label: 'Verificar sensor', className: 'suspicious', symbol: '!' }
                : metricaLocal.trend;
        cartao.innerHTML = `
            <div class="stats-card__header">
                <span class="stats-card__label">${escaparHtml(metricaLocal.label)}</span>
                <span class="stats-card__trend stats-card__trend--${tendencia.className}">${escaparHtml(formatarTendenciaCard(tendencia))}</span>
            </div>
            <strong class="stats-card__value">${formatarMetrica(metricaLocal.average, metricaLocal.suffix)}</strong>
            <dl class="stats-card__details">
                <div><dt>Mín</dt><dd>${formatarMetrica(metricaLocal.min, metricaLocal.suffix)}</dd></div>
                <div><dt>Máx</dt><dd>${formatarMetrica(metricaLocal.max, metricaLocal.suffix)}</dd></div>
                <div><dt>Delta</dt><dd>${formatarVariacao(metricaLocal.delta, metricaLocal.suffix)}</dd></div>
            </dl>
            ${montarEstadoOperacional(metricaLocal.quality)}
        `;
        elementoDom.appendChild(cartao);
    });
}

function renderizarGradeGraficos(id, serieGrafico, nomeSecao) {
    const elementoDom = document.getElementById(id);
    if (!elementoDom) return;
    const ids = new Set(serieGrafico.map(serie => serie.id));
    elementoDom.querySelectorAll('canvas').forEach(canvas => {
        if (ids.has(canvas.id)) return;
        graficosInstancias[canvas.id]?.destroy();
        delete graficosInstancias[canvas.id];
        canvas.closest('.chart-card')?.remove();
    });

    serieGrafico.forEach(seriesClimaticas => {
        const rotuloCartao = seriesClimaticas.id === 'plotsPressaoSala' ? 'Pressão' : seriesClimaticas.label;
        const cartao = document.getElementById(seriesClimaticas.id)?.closest('.chart-card')
            || criarCartaoGrafico(seriesClimaticas.id, rotuloCartao, `${seriesClimaticas.label} - ${nomeSecao}`);
        if (cartao.parentElement !== elementoDom) elementoDom.appendChild(cartao);
        aplicarAoGrafico(cartao, seriesClimaticas.quality);
        criarGraficoLinha(seriesClimaticas.id, seriesClimaticas, cartao, nomeSecao);
    });
}

function criarCartaoGrafico(idTelaGrafico, rotulo, rotuloAcessivel) {
    const cartao = document.createElement('div');
    const idRecipiente = idsContainersGraficos[idTelaGrafico];
    if (idRecipiente) cartao.id = idRecipiente;
    cartao.className = 'chart-card chart-card--zoomable';
    cartao.title = 'Dê duplo clique ou use o botão para ampliar';
    cartao.innerHTML = `
        <span class="chart-label">${escaparHtml(rotulo)}</span>
        <canvas class="plot" id="${idTelaGrafico}" aria-label="${escaparHtml(rotuloAcessivel)}" role="img"></canvas>
    `;
    criarBotaoAmpliacao(cartao);
    cartao.addEventListener('dblclick', () => tratarAmpliacao(cartao));
    return cartao;
}

function criarGraficoLinha(idTelaGrafico, seriesClimaticas, cartao, nomeSecao) {
    const canvas = document.getElementById(idTelaGrafico);
    if (!canvas) return;

    limparMensagemGrafico(cartao);
    const possuiPontos = seriesClimaticas.points.some(ponto => Number.isFinite(ponto.value));
    if (!possuiPontos) {
        window.SincronizadorGraficos?.desregistrar(graficosInstancias[idTelaGrafico]);
        graficosInstancias[idTelaGrafico]?.destroy();
        // Mantém o card visível, mas troca o canvas por uma mensagem contextual da métrica.
        delete graficosInstancias[idTelaGrafico];
        renderizarMensagemGrafico(cartao, obterMensagemGraficoVazio(idTelaGrafico, seriesClimaticas, nomeSecao));
        return;
    }

    const contextoDesenho = canvas.getContext('2d');
    const gradiente = contextoDesenho.createLinearGradient(0, 0, 0, 220);
    gradiente.addColorStop(0, `${seriesClimaticas.color}33`);
    gradiente.addColorStop(1, `${seriesClimaticas.color}00`);
    const sufixoEixo = obterSufixoGraficoTela(seriesClimaticas);
    const tituloEixo = obterTituloEixoGrafico(seriesClimaticas);
    const faixa = obterFaixaConfortoSerie(seriesClimaticas);

    const grafico = criarOuAtualizarGrafico(contextoDesenho, {
        type: 'line',
        data: {
            labels: seriesClimaticas.points.map(ponto => ponto.label),
            datasets: [{
                label: seriesClimaticas.label,
                data: seriesClimaticas.points.map(ponto => ponto.value),
                borderColor: seriesClimaticas.color,
                borderWidth: 2,
                backgroundColor: gradiente,
                fill: true,
                tension: 0.4,
                pointRadius: 0,
                pointHitRadius: 18,
                pointHoverRadius: 6,
                pointHoverBackgroundColor: seriesClimaticas.color,
                pointHoverBorderColor: '#0b1120',
                pointHoverBorderWidth: 2
            }]
        },
        options: mesclarRecursivamente(padroesGrafico, {
            plugins: {
                tooltip: {
                    callbacks: {
                        label: contexto => `${seriesClimaticas.label}: ${formatarNumero(contexto.parsed.y)}${sufixoEixo}`
                    }
                }
            },
            scales: {
                y: {
                    title: {
                        display: Boolean(tituloEixo),
                        text: tituloEixo,
                        color: cores.text,
                        font: { size: 11, weight: '600' }
                    },
                    ticks: {
                        precision: 1,
                        callback: valorLocal => formatarMarcadorEixo(valorLocal, sufixoEixo)
                    }
                }
            }
        })
    });

    if (faixa) {
        // A faixa de conforto e apenas visual; ela nao altera a escala calculada pelo Chart.js.
        grafico.$faixaConforto = faixa;
        grafico.update();
    }
    grafico.$zoomKind = 'line';
    grafico.$sourceSeries = seriesClimaticas;
    grafico.$chavesSincronizacao = seriesClimaticas.points.map(ponto => ponto.instante || null);
    window.SincronizadorGraficos?.registrar(grafico, nomeSecao);
    graficosInstancias[idTelaGrafico] = grafico;
}

function renderizarTabela(id, tabelaLocal) {
    const elementoDom = document.getElementById(id);
    if (!elementoDom) return;
    elementoDom.innerHTML = '';

    if (!tabelaLocal.rows.length) {
        renderizarErro(id, obterMensagemTabelaVazia(id), 'empty');
        return;
    }

    const nomeExportacao = `estacao-climatica-${dataSelecionada}`;
    elementoDom.innerHTML = `
        <div class="table-tools">
            <span class="table-tools__count">${tabelaLocal.rows.length} de 24 horários</span>
            <div class="table-tools__actions">
                <button type="button" data-table-sort aria-label="Inverter ordem temporal">Mais antigos primeiro</button>
                <button type="button" data-table-csv aria-label="Baixar tabela visível em CSV">CSV</button>
            </div>
        </div>
        <table data-export-name="${nomeExportacao}">
            <thead><tr>${tabelaLocal.headers.map(cabecalhoLocal => `<th>${escaparHtml(cabecalhoLocal)}</th>`).join('')}</tr></thead>
            <tbody>${tabelaLocal.rows.map((linhaLocal, indiceLinha) => `<tr data-timestamp="${criarTimestampTabela(linhaLocal)}">${linhaLocal.map((celulaLocal, indice) => {
                const qualidade = tabelaLocal.cellQuality?.[indiceLinha]?.[indice];
                const classe = qualidade ? ` class="table-cell--${escaparHtml(qualidade)}"` : '';
                return `<td${classe}>${escaparHtml(formatarCelulaTabela(celulaLocal, tabelaLocal.headers[indice], indiceLinha))}</td>`;
            }).join('')}</tr>`).join('')}</tbody>
        </table>`;

    const tabela = elementoDom.querySelector('table');
    elementoDom.querySelector('[data-table-sort]')?.addEventListener('click', evento => {
        const ascendente = evento.currentTarget.dataset.order !== 'asc';
        evento.currentTarget.dataset.order = ascendente ? 'asc' : 'desc';
        evento.currentTarget.textContent = ascendente ? 'Mais recentes primeiro' : 'Mais antigos primeiro';
        ordenarTabelaPorHorario(tabela, ascendente);
    });
    elementoDom.querySelector('[data-table-csv]')?.addEventListener('click', () => baixarTabelaCsv(tabela));
}

function montarEstadoOperacional(qualidade) {
    if (!qualidade || (qualidade.level === 'adequada' && qualidade.coveragePercent >= 99.5)) return '';
    const cobertura = qualidade.expectedReadings > 0
        ? `${qualidade.coveragePercent.toFixed(0)}% · ${qualidade.validReadings}/${qualidade.expectedReadings} amostras`
        : '0 amostras';
    const ultima = qualidade.latestReading
        ? `${qualidade.latestReading.date.replaceAll('-', '/')} · ${qualidade.latestReading.time}`
        : 'sem leitura';
    const titulo = (qualidade.warnings || []).join(' ') || qualidade.label;
    return `
        <p class="stats-card__quality stats-card__quality--${escaparHtml(qualidade.level)}" title="${escaparHtml(titulo)}">
            <strong>${escaparHtml(qualidade.stateLabel)}</strong>
            <span>${escaparHtml(cobertura)}</span>
            <span>Última: ${escaparHtml(ultima)}</span>
        </p>
    `;
}

function aplicarAoGrafico(cartao, qualidade) {
    if (!cartao || !qualidade || ['adequada', 'sem_dados'].includes(qualidade.level)) return;
    const selo = document.createElement('span');
    selo.className = `chart-quality-badge chart-quality-badge--${qualidade.level}`;
    selo.textContent = qualidade.label;
    selo.title = (qualidade.warnings || []).join(' ');
    cartao.appendChild(selo);
}

function criarTimestampTabela(linhaLocal) {
    const [dia, mes, ano] = String(linhaLocal?.[0] || '').split('/');
    const horario = String(linhaLocal?.[1] || '00:00').padStart(5, '0');
    return ano && mes && dia ? `${ano}-${mes}-${dia}T${horario}` : String(linhaLocal?.[1] || '');
}

function ordenarTabelaPorHorario(tabela, ascendente) {
    const corpo = tabela?.tBodies?.[0];
    if (!corpo) return;
    const linhas = Array.from(corpo.rows).sort((a, b) => {
        const comparacao = String(a.dataset.timestamp || '').localeCompare(String(b.dataset.timestamp || ''));
        return ascendente ? comparacao : -comparacao;
    });
    linhas.forEach(linha => corpo.appendChild(linha));
    preencherDatasOcultas(corpo.rows);
}

function preencherDatasOcultas(linhas) {
    let dataAnterior = '';
    Array.from(linhas).forEach(linha => {
        const [dataIso] = String(linha.dataset.timestamp || '').split('T');
        const [ano, mes, dia] = dataIso.split('-');
        const data = ano && mes && dia ? `${dia}/${mes}/${ano}` : '';
        linha.cells[0].textContent = data !== dataAnterior ? data : '';
        dataAnterior = data;
    });
}

function baixarTabelaCsv(tabela) {
    if (!tabela) return;
    const linhas = Array.from(tabela.rows).map(linha => Array.from(linha.cells).map(celula => {
        const valor = celula.textContent.replace(/\s*⚠\s*$/, '').trim().replaceAll('"', '""');
        return `"${valor}"`;
    }).join(';'));
    const blob = new Blob(['\uFEFF', linhas.join('\r\n')], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `${tabela.dataset.exportName || 'tabela'}.csv`;
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
}

function obterMensagemGraficoVazio(idTelaGrafico, seriesClimaticas, nomeSecao) {
    const metricaPorGrafico = {
        plotsTempSala: 'temperatura da sala',
        plotsSTSala: 'sensação térmica da sala',
        plotsUmidadeSala: 'umidade da sala',
        plotsPressaoSala: 'pressão',
        plotsTemp: 'temperatura',
        plotsST: 'sensação térmica',
        plotsUmidade: 'umidade',
        plotsTempAquario: 'temperatura do aquário',
        plotsPH: 'PH',
        plotsTDS: 'TDS',
        plotsTurbidez: 'turbidez'
    };
    const metrica = metricaPorGrafico[idTelaGrafico] || seriesClimaticas.label || nomeSecao;
    return `Sem dados de ${metrica} em ${dataSelecionada.replaceAll('-', '/')}.`;
}

function obterMensagemTabelaVazia(id) {
    const data = dataSelecionada.replaceAll('-', '/');
    if (id === 'dataSala') return `Sem registros da sala em ${data}.`;
    if (id === 'data') return `Sem registros de temperatura em ${data}.`;
    if (id === 'dataAquario') return `Sem registros do aquário em ${data}.`;
    return 'Sem registros recentes.';
}

function formatarCelulaTabela(valor, cabecalho, indiceLinha = 0) {
    if (valor == null || valor === '--') return valor ?? '--';
    if (normalizarTextoTabela(cabecalho) === 'data' && indiceLinha > 0) return '';

    const unidade = obterUnidadeTabela(cabecalho);
    if (!unidade || valorJaPossuiUnidade(valor, unidade)) return String(valor);

    return `${valor}${unidade}`;
}

function obterUnidadeTabela(cabecalho) {
    const chave = normalizarTextoTabela(cabecalho);
    if (!chave || chave === 'data' || chave === 'hora') return '';
    if (chave.includes('temperatura') || chave.includes('sensacao')) return '°C';
    if (chave.includes('umidade')) return '%';
    if (chave.includes('pressao')) return 'hPa';
    if (['co', 'co2', 'acetona', 'alcool', 'amonia', 'tolueno', 'tds'].includes(chave)) return 'ppm';
    if (chave.includes('turbidez')) return 'NTU';
    return '';
}

function obterSufixoGraficoTela(seriesClimaticas) {
    const chave = normalizarTextoTabela(`${seriesClimaticas?.id || ''} ${seriesClimaticas?.label || ''}`);
    if (seriesClimaticas?.suffix === '°' || seriesClimaticas?.suffix === '°C' || chave.includes('temperatura') || chave.includes('sensacao')) return '°';
    if (seriesClimaticas?.suffix === '%' || chave.includes('umidade')) return '%';
    if (chave.includes('pressao')) return 'hPa';
    if (chave.includes('tds')) return 'ppm';
    if (chave.includes('turbidez')) return 'NTU';
    return seriesClimaticas?.suffix || '';
}

function obterTituloEixoGrafico(seriesClimaticas) {
    const chave = normalizarTextoTabela(`${seriesClimaticas?.id || ''} ${seriesClimaticas?.label || ''}`);
    if (seriesClimaticas?.suffix === '°' || seriesClimaticas?.suffix === '°C' || chave.includes('temperatura') || chave.includes('sensacao')) return '(°C)';
    return obterSufixoGraficoTela(seriesClimaticas);
}

function formatarMarcadorEixo(valor, sufixo) {
    const numero = Number(valor);
    if (!Number.isFinite(numero)) return valor;

    const casas = Math.abs(numero) < 10 ? 2 : 1;
    const formatado = numero.toFixed(casas).replace(/\.?0+$/, '');
    return `${formatado}${sufixo || ''}`;
}

function formatarTendenciaCard(tendenciaLocal) {
    if (!tendenciaLocal) return '--';
    if (tendenciaLocal.symbol === tendenciaLocal.label) return tendenciaLocal.symbol || '--';
    return `${tendenciaLocal.symbol || ''} ${tendenciaLocal.label || ''}`.trim() || '--';
}

function valorJaPossuiUnidade(valor, unidade) {
    return String(valor).trim().toLowerCase().endsWith(unidade.toLowerCase());
}

function normalizarTextoTabela(texto) {
    return String(texto || '')
        .normalize('NFD')
        .replace(/[\u0300-\u036f]/g, '')
        .trim()
        .toLowerCase();
}


Object.assign(window, {
    renderizarErroDashboard,
    renderizarAmbiente,
    renderizarEstatisticas,
    renderizarGradeGraficos,
    criarCartaoGrafico,
    renderizarGraficoLinha: criarGraficoLinha,
    renderizarTabela,
    montarEstadoQualidade: montarEstadoOperacional,
    aplicarQualidadeAoGrafico: aplicarAoGrafico,
    criarTimestampTabela,
    ordenarTabelaPorHorario,
    preencherDatasTabela: preencherDatasOcultas,
    baixarTabelaCsv,
    obterMensagemGraficoVazio,
    obterMensagemTabelaVazia,
    formatarCelulaTabela,
    obterUnidadeTabela,
    obterSufixoGraficoTela,
    obterTituloEixoGrafico,
    formatarRotuloEixo: formatarMarcadorEixo,
    formatarTendenciaCard,
    valorJaPossuiUnidade,
    normalizarTextoTabela
});
