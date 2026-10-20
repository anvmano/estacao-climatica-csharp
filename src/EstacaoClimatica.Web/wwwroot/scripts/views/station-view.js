let dadosExternosAtuais = null;
let estadoConsultaExterna = 'inicial';
let mensagemConsultaExterna = '';

function renderizarEstacao(dadosMaisRecentes) {
    if (!dadosMaisRecentes) return;

    renderizarResumoGlobal(dadosMaisRecentes);
    renderizarLinhaEstacoes();
    renderizarResumoLua(dataSelecionada);
    renderizarInsightsAmbientais(dadosMaisRecentes);
    renderizarGraficoChuvaExterna();
    renderizarGraficoComparativo({
        canvasId: 'plotGlobalTemperature',
        containerId: 'chart-container-global-temp',
        titulo: 'Temperatura',
        unidade: '°',
        eixoY: '(°C)',
        series: [
            criarSerieComparativa('Sala', obterGraficoPorRotulo(dadosMaisRecentes.sala, 'Temperatura'), '#38bdf8'),
            criarSerieComparativa('Quarto', obterGraficoPorRotulo(dadosMaisRecentes.quarto, 'Temperatura'), '#34d399'),
            criarSerieComparativa('Aquário', obterGraficoPorRotulo(dadosMaisRecentes.aquario, 'Temperatura'), '#f59e0b')
        ],
        mensagemVazia: `Sem dados comparativos de temperatura em ${formatarDataFirebase(dataSelecionada)}.`
    });
    renderizarGraficoComparativo({
        canvasId: 'plotGlobalHumidity',
        containerId: 'chart-container-global-umidade',
        titulo: 'Umidade',
        unidade: '%',
        eixoY: '%',
        series: [
            criarSerieComparativa('Sala', obterGraficoPorRotulo(dadosMaisRecentes.sala, 'Umidade'), '#a78bfa'),
            criarSerieComparativa('Quarto', obterGraficoPorRotulo(dadosMaisRecentes.quarto, 'Umidade'), '#fb7185')
        ],
        mensagemVazia: `Sem dados comparativos de umidade em ${formatarDataFirebase(dataSelecionada)}.`
    });
}

function renderizarInsightsAmbientais(dadosMaisRecentes) {
    const recipiente = document.getElementById('environmentInsights');
    if (!recipiente) return;

    const internos = dadosMaisRecentes?.insights;
    const riscoSala = internos?.riscoSala || criarResultadoIndisponivel('Sem dados suficientes para estimar o risco de mofo.');
    const riscoQuarto = internos?.riscoQuarto || criarResultadoIndisponivel('Sem dados suficientes para estimar o risco de mofo.');
    const chuva = dadosExternosAtuais?.insights?.chuva || null;
    const indiceUv = dadosExternosAtuais?.insights?.indiceUv || null;
    const ventilacaoExterna = dadosExternosAtuais?.insights?.ventilacao || null;
    const ventilacao = recomendarVentilacaoCombinada(internos?.ventilacao, ventilacaoExterna);

    recipiente.innerHTML = `
        <div class="environment-insights__grid">
            ${montarCardInsight({
                titulo: 'Ventilação da Sala',
                valor: ventilacao.rotulo,
                status: dadosExternosAtuais ? 'Interior + exterior' : 'Sensores internos',
                classe: ventilacao.classe,
                descricao: ventilacao.descricao,
                detalhe: dadosExternosAtuais ? `Clima externo: ${dadosExternosAtuais.origem?.rotulo || 'Localização atual'}` : 'Consulte a localização para combinar o clima externo'
            })}
            ${montarCardPrevisaoChuva(chuva)}
            ${montarCardIndiceUv(indiceUv)}
            ${montarCardOrvalho('Sala', riscoSala)}
            ${montarCardOrvalho('Quarto', riscoQuarto)}
        </div>`;

    document.getElementById('environmentLocationButton')?.addEventListener('click', consultarClimaExterno);
    document.getElementById('rainLocationButton')?.addEventListener('click', consultarClimaExterno);
}

function recomendarVentilacaoCombinada(interna, externa) {
    if (!interna || interna.classe === 'indisponivel') return externa || criarResultadoIndisponivel('Sem leituras suficientes para avaliar ventilação.');
    if (!externa || externa.classe === 'indisponivel') return interna;
    if (['alerta', 'critico'].includes(externa.classe)) return externa;
    if (interna.rotulo === 'Renovação recomendada' && externa.classe === 'favoravel') {
        return { rotulo: 'Ventile agora', classe: 'favoravel', descricao: 'O ambiente pede renovação e as condições externas estão favoráveis.' };
    }
    if (interna.rotulo === 'Renovação recomendada') {
        return { rotulo: 'Ventile brevemente', classe: 'atencao', descricao: 'O ambiente pede renovação, mas há ressalvas no clima externo.' };
    }
    return externa.classe === 'favoravel' ? interna : externa;
}

async function consultarClimaExterno() {
    if (estadoConsultaExterna === 'carregando') return;
    estadoConsultaExterna = 'carregando';
    mensagemConsultaExterna = 'Consultando clima da localização do navegador...';
    renderizarInsightsAmbientais(dashboardAtual);

    try {
        const posicao = await obterLocalizacaoInsights();
        const url = `/api/public-weather/location?latitude=${encodeURIComponent(posicao.coords.latitude)}&longitude=${encodeURIComponent(posicao.coords.longitude)}&precisao=${encodeURIComponent(posicao.coords.accuracy)}`;
        const resposta = await fetch(url);
        const dados = await resposta.json().catch(() => null);
        if (!resposta.ok) throw new Error(dados?.message || 'Não foi possível consultar o clima externo.');
        dadosExternosAtuais = dados;
        estadoConsultaExterna = 'sucesso';
        mensagemConsultaExterna = `Clima externo atualizado às ${formatarHorarioInsight(dados.atualizadoEm)}.`;
    } catch (erro) {
        estadoConsultaExterna = 'erro';
        mensagemConsultaExterna = erro?.message || 'Não foi possível consultar o clima externo.';
    }

    renderizarInsightsAmbientais(dashboardAtual);
    renderizarGraficoChuvaExterna();
}

function renderizarGraficoChuvaExterna() {
    const cartao = document.getElementById('chart-container-rain');
    const canvas = document.getElementById('plotRain');
    const estado = document.getElementById('rainChartState');
    const condicao = document.getElementById('rainCurrentCondition');
    if (!cartao || !canvas || !estado) return;

    const existente = graficosInstancias.plotRain;
    if (existente) {
        window.SincronizadorGraficos?.desregistrar(existente);
        existente.destroy();
        delete graficosInstancias.plotRain;
    }

    const analiseAtual = window.ClimaChuva?.analisarAgora(dadosExternosAtuais?.climaAtual || {});
    if (condicao) condicao.textContent = analiseAtual?.rotulo || 'Condição atual indisponível';

    if (!dadosExternosAtuais) {
        canvas.hidden = true;
        estado.hidden = false;
        const texto = estado.querySelector('span');
        if (texto) texto.textContent = estadoConsultaExterna === 'erro'
            ? mensagemConsultaExterna
            : estadoConsultaExterna === 'carregando'
                ? 'Consultando precipitação da localização...'
                : 'Use sua localização para carregar precipitação observada e previsão.';
        const botao = estado.querySelector('button');
        if (botao) {
            botao.disabled = estadoConsultaExterna === 'carregando';
            botao.textContent = estadoConsultaExterna === 'carregando' ? 'Consultando...' : 'Usar localização';
            botao.addEventListener('click', consultarClimaExterno);
        }
        return;
    }

    const janela = window.ClimaChuva?.montarJanela(dadosExternosAtuais.previsaoCurtoPrazo, dadosExternosAtuais.atualizadoEm);
    const grafico = window.ClimaChuva?.criarGrafico({ canvas, janela, cores, grupoSincronizacao: 'estacao' });
    if (!grafico) {
        canvas.hidden = true;
        estado.hidden = false;
        const texto = estado.querySelector('span');
        if (texto) texto.textContent = 'Sem dados de precipitação para a janela consultada.';
        return;
    }

    canvas.hidden = false;
    estado.hidden = true;
    grafico.$zoomKind = 'rain';
    graficosInstancias.plotRain = grafico;
    garantirCartaoComZoom(cartao);
}

function obterLocalizacaoInsights() {
    if (!navigator.geolocation) return Promise.reject(new Error('Geolocalização não está disponível neste navegador.'));
    return new Promise((resolver, rejeitar) => navigator.geolocation.getCurrentPosition(
        resolver,
        erro => rejeitar(new Error({ 1: 'Permissão de localização negada.', 2: 'Localização indisponível no momento.', 3: 'Tempo esgotado ao obter localização.' }[erro.code] || 'Não foi possível obter a localização.')),
        { enableHighAccuracy: false, timeout: 25000, maximumAge: 900000 }
    ));
}

function montarCardPrevisaoChuva(chuva) {
    const indisponivel = !chuva || chuva.classe === 'indisponivel';
    const detalhe = indisponivel
        ? 'Nenhuma localização é armazenada'
        : `${formatarDecimalInsight(chuva.acumulado)} mm acumulados · pico ${formatarDecimalInsight(chuva.intensidadeMaxima)} mm/h`;
    return montarCardInsight({
        titulo: 'Chuva · próximas 6h',
        valor: indisponivel ? '--' : `${Math.round(chuva.probabilidade)}%`,
        status: chuva?.rotulo || 'Localização necessária',
        classe: chuva?.classe || 'indisponivel',
        descricao: chuva?.descricao || 'Use a localização para carregar probabilidade e intensidade de chuva.',
        detalhe,
        acao: montarAcaoLocalizacao()
    });
}

function montarCardIndiceUv(indiceUv) {
    const indisponivel = !indiceUv || indiceUv.classe === 'indisponivel';
    let detalhe = 'Nenhuma localização é armazenada';
    if (!indisponivel && Number.isFinite(indiceUv.maximo)) detalhe = `Máxima do dia: ${indiceUv.maximo.toFixed(1)}`;
    if (indiceUv?.usaMaximoComoFallback) detalhe += ' · usando máxima diária';
    return montarCardInsight({
        titulo: 'Índice UV',
        valor: indisponivel ? '--' : Number(indiceUv.valor).toFixed(1),
        status: indiceUv?.rotulo || 'Localização necessária',
        classe: indiceUv?.classe || 'indisponivel',
        descricao: indiceUv?.descricao || 'Use a localização para carregar o índice UV e a recomendação solar.',
        detalhe
    });
}

function montarCardOrvalho(ambiente, risco) {
    return montarCardInsight({
        titulo: `Ponto de orvalho · ${ambiente}`,
        valor: Number.isFinite(risco.pontoOrvalho) ? `${risco.pontoOrvalho.toFixed(1)}°C` : '--',
        status: risco.rotulo,
        classe: risco.classe,
        descricao: risco.descricao,
        detalhe: 'Risco estimado de condensação e mofo'
    });
}

function montarCardInsight({ titulo, valor, status, classe, descricao, detalhe, acao = '' }) {
    return `<article class="environment-insight environment-insight--${escaparHtml(classe)}">
        <div class="environment-insight__header"><span>${escaparHtml(titulo)}</span><small>${escaparHtml(status)}</small></div>
        <strong>${escaparHtml(valor)}</strong>
        <p>${escaparHtml(descricao)}</p>
        <span class="environment-insight__detail">${escaparHtml(detalhe)}</span>
        ${acao}
    </article>`;
}

function montarAcaoLocalizacao() {
    const mensagem = mensagemConsultaExterna
        ? `<span class="environment-insight__feedback environment-insight__feedback--${escaparHtml(estadoConsultaExterna)}" role="status">${escaparHtml(mensagemConsultaExterna)}</span>`
        : '';
    const texto = estadoConsultaExterna === 'carregando'
        ? 'Consultando...'
        : dadosExternosAtuais ? 'Atualizar clima externo' : 'Usar localização para chuva e UV';
    return `<div class="environment-insight__action">
        ${mensagem}
        <button class="environment-insights__location" id="environmentLocationButton" type="button" ${estadoConsultaExterna === 'carregando' ? 'disabled' : ''}>${texto}</button>
    </div>`;
}

function criarResultadoIndisponivel(descricao) {
    return { rotulo: 'Indisponível', classe: 'indisponivel', descricao, pontoOrvalho: null };
}

function formatarDecimalInsight(valor) {
    return Number.isFinite(Number(valor)) ? Number(valor).toFixed(1) : '--';
}

function formatarHorarioInsight(valor) {
    const dados = valor ? new Date(valor) : null;
    return dados && !Number.isNaN(dados.getTime()) ? `${String(dados.getHours()).padStart(2, '0')}:${String(dados.getMinutes()).padStart(2, '0')}` : '--:--';
}

function renderizarResumoGlobal(dadosMaisRecentes) {
    const recipiente = document.getElementById('statsEstacao');
    if (!recipiente) return;

    const cartoes = [
        montarCardAqi(dadosMaisRecentes.aqi),
        montarCardUltimaMedicao('Temp. Sala', dadosMaisRecentes.latest?.salaTemperatura, dadosMaisRecentes.sala, 'Temperatura', '°C'),
        montarCardUltimaMedicao('Temp. Quarto', dadosMaisRecentes.latest?.quartoTemperatura, dadosMaisRecentes.quarto, 'Temperatura', '°C'),
        montarCardUltimaMedicao('Temp. Aquário', dadosMaisRecentes.latest?.aquarioTemperatura, dadosMaisRecentes.aquario, 'Temperatura', '°C'),
        montarCardUltimaMedicao('Umidade Sala', dadosMaisRecentes.latest?.salaUmidade, dadosMaisRecentes.sala, 'Umidade', '%'),
        montarCardUltimaMedicao('Umidade Quarto', dadosMaisRecentes.latest?.quartoUmidade, dadosMaisRecentes.quarto, 'Umidade', '%')
    ];

    recipiente.innerHTML = '';
    cartoes.forEach(cartao => recipiente.appendChild(criarCardResumo(cartao)));

    if (!cartoes.some(cartao => cartao.temValor)) {
        recipiente.innerHTML = `<p class="state-message">Sem resumo global disponível para ${formatarDataFirebase(dataSelecionada)}.</p>`;
    }
}

function montarCardAqi(aqi) {
    if (!aqi) {
        return {
            titulo: 'AQI estimado',
            valor: '--',
            detalhePrincipal: 'Sem dados suficientes',
            detalheSecundario: 'Dominante: --',
            tendencia: '--',
            classe: 'stable',
            temValor: false
        };
    }

    return {
        titulo: 'AQI estimado',
        valor: String(aqi.value),
        detalhePrincipal: aqi.category,
        detalheSecundario: `Dominante: ${aqi.dominant}`,
        tendencia: aqi.category,
        classe: aqi.categoryClass === 'good' ? 'stable' : 'down',
        temValor: true
    };
}

function montarCardUltimaMedicao(titulo, ultimaMedicao, secao, metrica, unidade) {
    const registro = ultimaMedicao ? {
        valor: Number(ultimaMedicao.value).toFixed(2),
        data: ultimaMedicao.date,
        hora: ultimaMedicao.time
    } : obterUltimoValorTabela(secao?.table, metrica);
    if (!registro) {
        return {
            titulo,
            valor: '--',
            detalhePrincipal: 'Sem medição disponível',
            detalheSecundario: '--',
            tendencia: '--',
            classe: 'stable',
            temValor: false
        };
    }

    return {
        titulo,
        valor: valorJaPossuiUnidade(registro.valor, unidade) ? registro.valor : `${registro.valor}${unidade}`,
        detalhePrincipal: `${registro.data} · ${registro.hora}`,
        detalheSecundario: montarDetalheQualidadeAtual(ultimaMedicao?.quality),
        tendencia: '',
        classe: 'stable',
        temValor: true
    };
}

function montarDetalheQualidadeAtual(qualidade) {
    if (!qualidade || (qualidade.level === 'adequada' && qualidade.coveragePercent >= 99.5)) return '';
    const cobertura = qualidade.expectedReadings > 0 ? `${qualidade.coveragePercent.toFixed(0)}%` : '0%';
    return `${qualidade.stateLabel} · ${qualidade.validReadings}/${qualidade.expectedReadings} · ${cobertura}`;
}

function obterUltimoValorTabela(tabela, metrica) {
    const cabecalhos = tabela?.headers || [];
    const linhas = tabela?.rows || [];
    const indice = cabecalhos.findIndex(cabecalho => normalizarTextoTabela(cabecalho) === normalizarTextoTabela(metrica));
    if (indice < 0 || !linhas.length) return null;

    const primeira = linhas[0];
    const valor = primeira[indice];
    if (valor == null || valor === '--') return null;

    return {
        data: primeira[0] || formatarDataFirebase(dataSelecionada),
        hora: primeira[1] || '--',
        valor: formatarCelulaTabela(valor, cabecalhos[indice], 0)
    };
}

function criarCardResumo(cartao) {
    const elemento = document.createElement('article');
    elemento.className = 'stats-card station-summary-card';
    elemento.innerHTML = `
        <div class="stats-card__header">
            <span class="stats-card__label">${escaparHtml(cartao.titulo)}</span>
            ${cartao.tendencia ? `<span class="stats-card__trend stats-card__trend--${escaparHtml(cartao.classe)}">${escaparHtml(cartao.tendencia)}</span>` : ''}
        </div>
        <strong class="stats-card__value">${escaparHtml(cartao.valor)}</strong>
        <div class="station-summary-card__meta">
            <span>${escaparHtml(cartao.detalhePrincipal)}</span>
            ${cartao.detalheSecundario ? `<span>${escaparHtml(cartao.detalheSecundario)}</span>` : ''}
        </div>
    `;
    return elemento;
}

function renderizarLinhaEstacoes() {
    const recipiente = document.getElementById('seasonTimeline');
    if (!recipiente) return;

    const estado = window.ClimateSeason?.getState?.();
    if (!estado) {
        recipiente.innerHTML = '';
        return;
    }

    recipiente.innerHTML = `
        <div class="season-timeline__track" aria-label="Progresso anual das estações">
            ${estado.estacoes.map(estacao => `
                <span class="season-timeline__segment season-timeline__segment--${estacao.chave}">
                    <i>${estacao.nome}</i>
                </span>
            `).join('')}
            <span class="season-timeline__marker" style="left: ${estado.progressoAno}%"></span>
        </div>
    `;
}

function renderizarResumoLua(dataFirebase) {
    const recipiente = document.getElementById('moonSummary');
    if (!recipiente) return;

    const estado = window.ClimateMoon?.getState?.(dataFirebase);
    if (!estado) {
        recipiente.innerHTML = '';
        return;
    }

    recipiente.innerHTML = `
        <div class="moon-summary__scene moon-summary__scene--${estado.fase.chave}" style="--moon-shadow: ${estado.sombra}%">
            <span class="moon-summary__orb" aria-hidden="true"></span>
        </div>
        <div class="moon-summary__content">
            <span>${estado.fase.nome}</span>
            <strong>${estado.iluminacao}% iluminada</strong>
        </div>
        <dl class="moon-summary__details">
            <div><dt>Idade</dt><dd>${estado.idade.toFixed(1)} dias</dd></div>
            <div><dt>Próx. cheia</dt><dd>${formatarDataCompletaEstacao(estado.proximaCheia)}</dd></div>
            <div><dt>Próx. nova</dt><dd>${formatarDataCompletaEstacao(estado.proximaNova)}</dd></div>
        </dl>
    `;
}

function renderizarGraficoComparativo({ canvasId: idTelaGrafico, containerId: idRecipiente, titulo, unidade, eixoY, series: seriesClimaticas, mensagemVazia }) {
    const canvas = document.getElementById(idTelaGrafico);
    const cartao = document.getElementById(idRecipiente);
    if (!canvas || !cartao) return;

    if (graficosInstancias[idTelaGrafico]) {
        window.SincronizadorGraficos?.desregistrar(graficosInstancias[idTelaGrafico]);
        graficosInstancias[idTelaGrafico].destroy();
    }
    limparMensagemGrafico(cartao);
    garantirCartaoComZoom(cartao);

    const seriesValidas = seriesClimaticas.filter(serie => serie.pontos.some(ponto => Number.isFinite(ponto.value)));
    if (!seriesValidas.length) {
        delete graficosInstancias[idTelaGrafico];
        renderizarMensagemGrafico(cartao, mensagemVazia);
        return;
    }

    const chaves = [...new Set(seriesValidas.flatMap(serie => serie.pontos.map(obterChavePontoComparativo)))];
    const rotulosPorChave = new Map(seriesValidas.flatMap(serie => serie.pontos.map(ponto => [obterChavePontoComparativo(ponto), ponto.label])));
    const rotulos = chaves.map(chave => rotulosPorChave.get(chave) || chave);
    const graficoLocal = new Chart(canvas.getContext('2d'), {
        type: 'line',
        data: {
            labels: rotulos,
            datasets: seriesValidas.map(serie => ({
                label: serie.nome,
                data: chaves.map(chave => serie.porChave.get(chave) ?? null),
                borderColor: serie.cor,
                backgroundColor: 'transparent',
                tension: 0.35,
                borderWidth: 2,
                pointRadius: 0,
                pointHitRadius: 18,
                pointHoverRadius: 6
            }))
        },
        options: mesclarRecursivamente(padroesGrafico, {
            plugins: {
                legend: {
                    display: true,
                    labels: { color: cores.text, boxWidth: 10, boxHeight: 10 }
                },
                tooltip: {
                    callbacks: {
                        label: contexto => `${contexto.dataset.label}: ${formatarNumero(contexto.parsed.y)}${unidade}`
                    }
                }
            },
            scales: {
                y: {
                    title: {
                        display: true,
                        text: eixoY,
                        color: cores.text,
                        font: { size: 11, weight: '600' }
                    },
                    ticks: {
                        callback: valorLocal => `${Number(valorLocal).toFixed(unidade === '%' ? 0 : 1)}${unidade}`
                    }
                }
            }
        })
    });

    graficoLocal.$zoomKind = 'line';
    graficoLocal.$sourceSeries = { id: idTelaGrafico, label: titulo, suffix: unidade, points: chaves.map((chave, indice) => ({ label: rotulos[indice], value: null, instante: chave })) };
    graficoLocal.$chavesSincronizacao = chaves;
    window.SincronizadorGraficos?.registrar(graficoLocal, 'estacao');
    graficosInstancias[idTelaGrafico] = graficoLocal;
}

function criarSerieComparativa(nome, grafico, cor) {
    const pontos = (grafico?.points || []).filter(ponto => ponto.label);
    return {
        nome,
        cor,
        pontos,
        porChave: new Map(pontos.map(ponto => [obterChavePontoComparativo(ponto), Number.isFinite(ponto.value) ? ponto.value : null]))
    };
}

function obterChavePontoComparativo(ponto) {
    return ponto?.instante || ponto?.label || '';
}

function obterGraficoPorRotulo(secao, rotulo) {
    const alvo = normalizarTextoTabela(rotulo);
    return (secao?.charts || []).find(grafico => normalizarTextoTabela(grafico.label).includes(alvo));
}

function formatarDataCompletaEstacao(dados) {
    if (!(dados instanceof Date) || Number.isNaN(dados.getTime())) return '--';
    return `${String(dados.getDate()).padStart(2, '0')}/${String(dados.getMonth() + 1).padStart(2, '0')}/${dados.getFullYear()}`;
}

Object.assign(window, {
    renderizarEstacao,
    renderizarResumoEstacao: renderizarResumoGlobal,
    renderizarLinhaEstacoes,
    renderizarResumoLua,
    obterDadosClimaExternoAtual: () => dadosExternosAtuais
});
