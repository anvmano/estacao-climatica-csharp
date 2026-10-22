
let ultimaTentativaExportacao = null;

function configurarExportacaoDados() {
    const botao = document.getElementById('btnExportData');
    if (!botao) return;

    const sincronizarRotulo = () => {
        botao.innerText = obterFormatoExportacao() === 'json' ? 'Exportar JSON' : 'Exportar PDF';
    };

    document.querySelectorAll('input[name="exportFormat"]').forEach(entrada => {
        entrada.addEventListener('change', sincronizarRotulo);
    });
    sincronizarRotulo();

    botao.addEventListener('click', () => exportarAbaAtiva(botao));
    document.getElementById('btnRetryExport')?.addEventListener('click', () => ultimaTentativaExportacao?.());
}

async function exportarAbaAtiva(botao) {
    const formato = obterFormatoExportacao();
    if (!dashboardAtual) {
        atualizarProgresso('Aguarde o carregamento dos dados antes de exportar.', 'error', true);
        return;
    }

    const textoOriginal = botao.innerText;
    botao.disabled = true;
    botao.innerText = formato === 'json' ? 'Gerando JSON...' : 'Gerando PDF...';
    ultimaTentativaExportacao = () => exportarAbaAtiva(botao);
    atualizarProgresso('Preparando exportação.', 'loading');

    // O relatório é montado fora da tela para capturar HTML estilizado sem alterar a aba atual.
    const raizRenderizacao = document.createElement('div');
    raizRenderizacao.className = 'pdf-render-root';

    try {
        await executarComTimeout(async controle => {
            atualizarProgresso('Coletando dados da aba.', 'loading');
            const relatorio = montarRelatorioPdf();
            if (controle.cancelado) return;
            if (formato === 'json') {
                exportarRelatorioJson(relatorio);
                atualizarProgresso('Download iniciado.', 'success');
                return;
            }

            atualizarProgresso('Carregando recursos do PDF.', 'loading');
            await carregarDependenciasPdf();
            if (controle.cancelado) return;
            raizRenderizacao.appendChild(relatorio.elemento);
            document.body.appendChild(raizRenderizacao);
            atualizarProgresso('Montando PDF.', 'loading');
            await gerarPdf(relatorio.elemento, relatorio.nomeArquivo);
            if (!controle.cancelado) atualizarProgresso('Download iniciado.', 'success');
        });
    } catch (erroLocal) {
        console.error('Erro ao exportar dados:', erroLocal);
        const mensagem = erroLocal?.name === 'TimeoutError'
            ? 'A exportação demorou além do esperado. Verifique a conexão e tente novamente.'
            : 'Falha ao exportar. Verifique a conexão e tente novamente.';
        atualizarProgresso(mensagem, 'error', true);
    } finally {
        raizRenderizacao.remove();
        botao.disabled = false;
        botao.innerText = textoOriginal;
    }
}

function obterFormatoExportacao() {
    const selecionado = document.querySelector('input[name="exportFormat"]:checked');
    return selecionado?.value === 'json' ? 'json' : 'pdf';
}

function montarRelatorioPdf() {
    const configuracao = obterConfiguracaoAbaPdf(obterNomeAbaAtiva());
    const secao = configuracao.chaveDashboard ? dashboardAtual[configuracao.chaveDashboard] : null;
    const geradoEm = new Date();
    const nomeBase = `relatorio-estacao-${gerarIdentificadorUrl(configuracao.rotulo)}-${dataSelecionada}`;
    const metricasGraficos = configuracao.tipo === 'estacao' ? obterMetricasGraficosEstacaoPdf() : obterMetricasGraficosPdf(secao);
    const metricasTabela = configuracao.tipo === 'estacao' ? [] : obterMetricasTabelaPdf(secao);
    const cartoesResumo = configuracao.tipo === 'estacao'
        ? montarCartoesResumoEstacaoPdf()
        : montarCartoesResumoPdf(secao, configuracao, metricasGraficos);
    const cartoesGraficos = configuracao.tipo === 'estacao'
        ? montarCartoesGraficosEstacaoPdf()
        : montarCartoesGraficosPdf(secao, configuracao, metricasGraficos);
    const linhasTabela = configuracao.tipo === 'estacao' ? [] : montarLinhasTabelaPdf(secao, metricasTabela);
    const alertas = configuracao.tipo === 'estacao' ? [] : montarAlertasDiaPdf(secao, metricasGraficos);

    // Mantém a estrutura visual do projeto original: cabeçalho, indicadores, alertas, gráficos e tabela compacta.
    const relatorio = document.createElement('article');
    relatorio.className = 'pdf-report';
    relatorio.appendChild(criarCabecalho(configuracao.rotulo, dataSelecionada, geradoEm));
    relatorio.appendChild(criarSecaoResumo(cartoesResumo, alertas));
    relatorio.appendChild(criarSecaoGraficos(cartoesGraficos));
    if (!configuracao.semTabela) relatorio.appendChild(criarSecaoTabela(linhasTabela, metricasTabela));

    return {
        elemento: relatorio,
        nomeArquivo: `${nomeBase}.pdf`,
        nomeArquivoJson: `${nomeBase}.json`,
        configuracao,
        secao,
        dataConsulta: dataSelecionada,
        geradoEm,
        metricasGraficos,
        metricasTabela,
        alertas,
        cartoesResumo,
        cartoesGraficos,
        linhasTabela
    };
}

function exportarRelatorioJson(relatorio) {
    // JSON expõe dados processados da aba ativa e metadados suficientes para auditoria offline.
    const climaExterno = relatorio.configuracao.tipo === 'estacao' ? montarClimaExternoJson() : null;
    const conteudoSerializado = {
        relatorio: 'Estação Climática',
        aba: relatorio.configuracao.rotulo,
        dataConsultada: formatarDataFirebase(relatorio.dataConsulta),
        dataConsultadaFirebase: relatorio.dataConsulta,
        geradoEm: relatorio.geradoEm.toISOString(),
        resumo: relatorio.cartoesResumo.map(cartao => ({
            indicador: cartao.label,
            atual: cartao.current,
            minimo: cartao.min,
            maximo: cartao.max,
            delta: cartao.delta,
            status: cartao.status
        })),
        graficos: relatorio.cartoesGraficos.map(cartao => ({
            titulo: cartao.label,
            unidade: cartao.unit || '',
            possuiImagemRenderizada: Boolean(cartao.image)
        })),
        metricasTabela: relatorio.metricasTabela.map(metrica => ({
            chave: metrica.key,
            rotulo: metrica.label,
            unidade: metrica.unit || ''
        })),
        alertas: relatorio.alertas,
        tabela: relatorio.linhasTabela.map(linha => ({
            horario: linha.horario,
            valores: linha.valores,
            status: linha.status
        })),
        tabelaResumida: relatorio.linhasTabela.map(linha => ({
            horario: linha.horario,
            valores: linha.valores,
            status: linha.status
        })),
        tabelaDetalhada: montarTabelaDetalhadaJson(relatorio),
        dadosBrutos: montarDadosDaAbaJson(relatorio),
        dadosDaAba: montarDadosDaAbaJson(relatorio),
        ...(climaExterno ? { climaExterno } : {})
    };

    baixarJson(conteudoSerializado, relatorio.nomeArquivoJson);
}

function montarDadosDaAbaJson(relatorio) {
    if (relatorio.configuracao.tipo === 'estacao') {
        const climaExterno = montarClimaExternoJson();
        return {
            sala: dashboardAtual?.sala || null,
            quarto: dashboardAtual?.quarto || null,
            aquario: dashboardAtual?.aquario || null,
            solar: dashboardAtual?.solar || null,
            aqi: dashboardAtual?.aqi || null,
            ...(climaExterno ? { climaExterno } : {})
        };
    }

    const secao = relatorio.secao || {};

    // Mantém o contrato bruto da aba junto com o resumo amigável do relatório.
    const dados = {
        stats: secao.stats || [],
        charts: (secao.charts || []).map(grafico => ({
            id: grafico.id,
            label: grafico.label,
            suffix: grafico.suffix,
            series: grafico.series || []
        })),
        table: secao.table || { headers: [], rows: [] },
        advanced: secao.advanced || null
    };

    if (relatorio.configuracao.incluirSolar) {
        dados.solar = dashboardAtual?.solar || null;
    }

    return dados;
}

function montarClimaExternoJson() {
    const dados = window.obterDadosClimaExternoAtual?.();
    if (!dados) return null;
    return {
        origem: {
            tipo: dados.origem?.tipo || null,
            rotulo: dados.origem?.rotulo || null
        },
        atualizadoEm: dados.atualizadoEm || null,
        climaAtual: dados.climaAtual || null,
        previsaoCurtoPrazo: dados.previsaoCurtoPrazo || [],
        previsaoDiaria: dados.previsaoDiaria || null,
        insights: dados.insights || null
    };
}

function executarComTimeout(acao) {
    const limite = Number(window.AppConfig?.exports?.timeoutMs) || 45000;
    const controle = { cancelado: false };
    let temporizador;
    return Promise.race([
        Promise.resolve().then(() => acao(controle)),
        new Promise((resolver, rejeitar) => {
            temporizador = setTimeout(() => {
                controle.cancelado = true;
                const erro = new Error('Tempo limite da exportação excedido.');
                erro.name = 'TimeoutError';
                rejeitar(erro);
            }, limite);
        })
    ]).finally(() => clearTimeout(temporizador));
}

function atualizarProgresso(mensagem, tipo, permitirNovaTentativa = false) {
    const retornoVisual = document.getElementById('exportFeedback');
    const estado = document.getElementById('exportStatus');
    const repetir = document.getElementById('btnRetryExport');
    if (!retornoVisual || !estado) return;
    retornoVisual.hidden = false;
    retornoVisual.classList.toggle('is-error', tipo === 'error');
    retornoVisual.setAttribute('role', tipo === 'error' ? 'alert' : 'status');
    estado.textContent = mensagem;
    if (repetir) repetir.hidden = !permitirNovaTentativa;
}

function montarTabelaDetalhadaJson(relatorio) {
    if (!relatorio.metricasTabela.length) return [];

    return relatorio.linhasTabela.flatMap(linha => relatorio.metricasTabela.map(metrica => ({
        horario: linha.horario,
        indicador: metrica.label,
        valor: linha.valores[metrica.key] || '--',
        status: linha.status
    })));
}

function baixarJson(conteudoSerializado, nomeArquivo) {
    const blob = new Blob([JSON.stringify(conteudoSerializado, null, 2)], { type: 'application/json;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = nomeArquivo;
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
}

async function carregarDependenciasPdf() {
    await carregarCssUmaVez('pdf-report.css?v=20260623-station');
    if (typeof html2canvas !== 'function') {
        await carregarScriptUnico('vendor/html2canvas.min.js', 'html2canvas');
    }
    if (typeof window.jspdf?.jsPDF !== 'function') {
        await carregarScriptUnico('vendor/jspdf.umd.min.js', 'jspdf');
    }
}

function carregarCssUmaVez(enderecoDestino) {
    return new Promise((resolver, rejeitar) => {
        const caminho = enderecoDestino.split('?')[0];
        if ([...document.styleSheets].some(folha => folha.href?.includes(caminho))) {
            resolver();
            return;
        }

        const link = document.createElement('link');
        link.rel = 'stylesheet';
        link.href = enderecoDestino;
        link.onload = () => resolver();
        link.onerror = () => {
            link.remove();
            rejeitar(new Error(`Falha ao carregar ${enderecoDestino}`));
        };
        document.head.appendChild(link);
    });
}

function carregarScriptUnico(enderecoOrigem, nomeGlobal) {
    return new Promise((resolver, rejeitar) => {
        if (window[nomeGlobal]) {
            resolver();
            return;
        }

        const existente = document.querySelector(`script[data-lazy-lib="${nomeGlobal}"]`);
        if (existente) {
            existente.addEventListener('load', () => resolver(), { once: true });
            existente.addEventListener('error', rejeitar, { once: true });
            return;
        }

        const script = document.createElement('script');
        script.src = enderecoOrigem;
        script.defer = true;
        script.dataset.lazyLib = nomeGlobal;
        script.onload = () => resolver();
        script.onerror = () => rejeitar(new Error(`Falha ao carregar ${enderecoOrigem}`));
        document.head.appendChild(script);
    });
}

function obterConfiguracaoAbaPdf(aba) {
    // O contrato atual replica o projeto Web: a aba Estação centraliza o contexto solar/global.
    const configuracoes = {
        Tab0: { rotulo: 'Estação', chaveDashboard: null, incluirSolar: true, tipo: 'estacao', semTabela: true },
        Tab1: { rotulo: 'Sala', chaveDashboard: 'sala', incluirSolar: false },
        Tab2: { rotulo: 'Quarto', chaveDashboard: 'quarto', incluirSolar: false },
        Tab3: { rotulo: 'Aquário', chaveDashboard: 'aquario', incluirSolar: false }
    };
    return configuracoes[aba] || configuracoes.Tab0;
}

function criarCabecalho(rotuloAba, dataConsulta, geradoEm) {
    const cabecalho = document.createElement('header');
    cabecalho.className = 'pdf-report__header';
    cabecalho.innerHTML = `
        <div>
            <span class="pdf-report__eyebrow">Resumo executivo</span>
            <h2 class="pdf-report__title">Relatório da Estação Climática</h2>
            <p class="pdf-report__subtitle">Leitura consolidada da aba ativa e da data selecionada.</p>
        </div>
        <div class="pdf-report__meta">
            <div class="pdf-report__meta-item">
                <span class="pdf-report__label">Aba selecionada</span>
                <span class="pdf-report__value">${escaparHtml(rotuloAba)}</span>
            </div>
            <div class="pdf-report__meta-item">
                <span class="pdf-report__label">Data consultada</span>
                <span class="pdf-report__value">${escaparHtml(formatarDataFirebase(dataConsulta))}</span>
            </div>
            <div class="pdf-report__meta-item">
                <span class="pdf-report__label">Gerado em</span>
                <span class="pdf-report__value">${escaparHtml(formatarDataHoraRelatorio(geradoEm))}</span>
            </div>
        </div>
    `;
    return cabecalho;
}

function criarIndicePdf() {
    const secao = document.createElement('section');
    secao.className = 'pdf-report__index';
    secao.innerHTML = `
        <span class="pdf-report__section-title">Índice</span>
        <ol class="pdf-index-list">
            <li><span>Resumo geral</span><strong>1</strong></li>
            <li><span>Gráficos</span><strong>2</strong></li>
            <li><span>Tabela</span><strong>3</strong></li>
        </ol>
    `;
    return secao;
}

function criarSecaoResumo(cartoes, alertas) {
    const secao = document.createElement('section');
    secao.className = 'pdf-report__section pdf-report__summary-section';
    secao.innerHTML = '<span class="pdf-report__section-title">Indicadores principais</span>';

    const grade = document.createElement('div');
    grade.className = 'pdf-summary-grid';
    cartoes.forEach(cartao => grade.appendChild(criarCardResumoRelatorio(cartao)));
    secao.appendChild(grade);
    secao.appendChild(criarPainelAlertas(alertas, cartoes));
    return secao;
}

function criarPainelAlertas(alertas, cartoes = []) {
    const painel = document.createElement('aside');
    const possuiAlertas = alertas.length > 0;
    const semDados = !cartoes.some(cartao => !['Sem dados', 'Atual'].includes(cartao.status));
    const dadosParciais = cartoes.some(cartao => cartao.status === 'Sem dados');
    painel.className = `pdf-alert-panel ${possuiAlertas ? 'pdf-alert-panel--alert' : 'pdf-alert-panel--stable'}`;

    const itens = possuiAlertas
        ? alertas.map(alerta => `<li>${escaparHtml(alerta)}</li>`).join('')
        : semDados ? '<li>Sem medições para avaliar alertas neste período.</li>'
            : dadosParciais ? '<li>Sem alertas nos indicadores com dados; há medições ausentes.</li>'
                : '<li>Nenhum indicador fora da faixa de conforto no período analisado.</li>';

    painel.innerHTML = `
        <div class="pdf-alert-panel__heading">
            <span>Alertas do dia</span>
            <strong>${possuiAlertas ? `${alertas.length} atenção` : semDados ? 'Sem dados' : dadosParciais ? 'Dados parciais' : 'Estável'}</strong>
        </div>
        <ul class="pdf-alert-list">${itens}</ul>
    `;
    return painel;
}

function criarCardResumoRelatorio(cartao) {
    const elemento = document.createElement('article');
    elemento.className = 'pdf-summary-card';
    elemento.innerHTML = `
        <div class="pdf-summary-card__top">
            <span class="pdf-summary-card__name">${escaparHtml(cartao.label)}</span>
            <span class="pdf-status pdf-status--${obterClasseEstado(cartao.status)}">${escaparHtml(cartao.status)}</span>
        </div>
        <strong class="pdf-summary-card__current">${escaparHtml(cartao.current)}</strong>
        <dl class="pdf-summary-card__details">
            <div><dt>Mín</dt><dd>${escaparHtml(cartao.min)}</dd></div>
            <div><dt>Máx</dt><dd>${escaparHtml(cartao.max)}</dd></div>
            <div><dt>Delta</dt><dd>${escaparHtml(cartao.delta)}</dd></div>
        </dl>
    `;
    return elemento;
}

function criarSecaoGraficos(cartoes) {
    const secao = document.createElement('section');
    secao.className = 'pdf-report__section pdf-report__charts-section';
    secao.innerHTML = '<span class="pdf-report__section-title">Gráficos</span>';

    const grade = document.createElement('div');
    grade.className = 'pdf-chart-grid';
    cartoes.forEach(cartao => grade.appendChild(criarCardGrafico(cartao)));
    secao.appendChild(grade);
    return secao;
}

function criarCardGrafico(cartao) {
    const elemento = document.createElement('article');
    elemento.className = [
        'pdf-chart-card',
        cartao.wide ? 'pdf-chart-card--wide' : '',
        cartao.compact ? 'pdf-chart-card--compact' : ''
    ].filter(Boolean).join(' ');
    elemento.innerHTML = `
        <div class="pdf-chart-card__title">
            <span class="pdf-chart-card__name">${escaparHtml(cartao.label)}</span>
            <span class="pdf-chart-card__unit">${escaparHtml(cartao.unit || '')}</span>
        </div>
    `;

    if (cartao.image) {
        const imagem = document.createElement('img');
        imagem.src = cartao.image;
        imagem.alt = cartao.label;
        elemento.appendChild(imagem);
    } else {
        const vazio = document.createElement('div');
        vazio.className = 'pdf-empty';
        vazio.innerText = 'Sem dados disponíveis';
        elemento.appendChild(vazio);
    }

    if (cartao.stats?.length) {
        const estatisticas = document.createElement('div');
        estatisticas.className = 'pdf-chart-stats';
        estatisticas.innerHTML = cartao.stats
            .map(item => `<span>${escaparHtml(item.label)} <strong>${escaparHtml(item.value)}</strong></span>`)
            .join('');
        elemento.appendChild(estatisticas);
    }

    return elemento;
}

function criarSecaoTabela(linhas, metricas) {
    const secao = document.createElement('section');
    secao.className = 'pdf-report__section pdf-report__table-section';
    secao.innerHTML = '<span class="pdf-report__section-title">Tabela resumida</span>';

    if (!linhas.length) {
        const vazio = document.createElement('div');
        vazio.className = 'pdf-empty';
        vazio.innerText = 'Sem dados disponíveis';
        secao.appendChild(vazio);
        return secao;
    }

    const tabela = document.createElement('table');
    tabela.className = 'pdf-table';
    tabela.innerHTML = `
        <thead>
            <tr>
                <th>Horário</th>
                ${metricas.map(metrica => `<th>${escaparHtml(metrica.label)}</th>`).join('')}
                <th>Status geral</th>
            </tr>
        </thead>
    `;

    const corpo = document.createElement('tbody');
    linhas.forEach(linha => {
        const tr = document.createElement('tr');
        tr.innerHTML = `
            <td>${escaparHtml(linha.horario)}</td>
            ${metricas.map(metrica => `<td>${escaparHtml(linha.valores[metrica.key] || '--')}</td>`).join('')}
            <td><span class="pdf-status pdf-status--${obterClasseEstado(linha.status)}">${escaparHtml(linha.status)}</span></td>
        `;
        corpo.appendChild(tr);
    });
    tabela.appendChild(corpo);
    secao.appendChild(tabela);
    return secao;
}

function montarCartoesResumoPdf(secao, configuracao, metricas) {
    const estatisticas = secao?.stats || [];
    const cartoes = metricas
        .map(metrica => montarResumoMetricaPdf(metrica, estatisticas.find(estatistica => obterChaveMetricaPdf(estatistica) === metrica.key)))
        .filter(Boolean);

    if (configuracao.incluirSolar) {
        cartoes.push(montarResumoSolarPdf());
    }

    return cartoes.length ? cartoes : [montarResumoVazioPdf('Sem dados')];
}

function montarCartoesResumoEstacaoPdf() {
    const cartoes = [
        montarCardEstacaoDoAno(),
        montarResumoLuaPdf(),
        montarResumoAqiEstacaoPdf(),
        montarResumoUltimaMedicaoEstacaoPdf('Temp. Sala', dashboardAtual?.sala, 'Temperatura', '°C'),
        montarResumoUltimaMedicaoEstacaoPdf('Temp. Quarto', dashboardAtual?.quarto, 'Temperatura', '°C'),
        montarResumoUltimaMedicaoEstacaoPdf('Temp. Aquário', dashboardAtual?.aquario, 'Temperatura', '°C'),
        montarResumoUltimaMedicaoEstacaoPdf('Umidade Sala', dashboardAtual?.sala, 'Umidade', '%'),
        montarResumoUltimaMedicaoEstacaoPdf('Umidade Quarto', dashboardAtual?.quarto, 'Umidade', '%')
    ];
    const chuva = montarResumoChuvaPdf();
    if (chuva) cartoes.push(chuva);
    return cartoes;
}

function montarResumoChuvaPdf() {
    const dados = window.obterDadosClimaExternoAtual?.();
    if (!dados) return null;
    const atual = window.ClimaChuva?.analisarAgora(dados.climaAtual || {});
    const resumo = dados.insights?.chuva;
    return {
        label: 'Chuva externa',
        current: atual?.rotulo || 'Condição atual indisponível',
        min: Number.isFinite(Number(resumo?.acumulado)) ? `${Number(resumo.acumulado).toFixed(1)} mm / 6h` : '--',
        max: Number.isFinite(Number(resumo?.probabilidade)) ? `${Math.round(Number(resumo.probabilidade))}% prevista` : '--',
        delta: dados.origem?.rotulo || 'Localização consultada',
        status: !atual ? 'Sem dados' : atual.chovendo ? 'Alerta' : 'Estável'
    };
}

function montarCardEstacaoDoAno() {
    const estado = window.ClimateSeason?.getState?.();
    if (!estado) return montarResumoVazioPdf('Estação do ano');

    const indiceAtual = estado.estacoes.findIndex(estacao => estacao.chave === estado.estacao.chave);
    const proxima = estado.estacoes[indiceAtual + 1] || estado.estacoes[0];
    return {
        label: 'Estação do ano',
        current: estado.estacao.nome,
        min: `Início ${formatarDataCompletaPdf(estado.estacao.inicio)}`,
        max: `Próxima ${proxima.nome}`,
        delta: `${Number(estado.progressoEstacao ?? 0).toFixed(1)}%`,
        status: 'Atual'
    };
}

function montarResumoLuaPdf() {
    const estado = window.ClimateMoon?.getState?.(dataSelecionada);
    if (!estado) return montarResumoVazioPdf('Fase da lua');

    return {
        label: 'Fase da lua',
        current: `${estado.iluminacao}% iluminada`,
        min: estado.fase.nome,
        max: `Cheia ${formatarDataCompletaPdf(estado.proximaCheia)}`,
        delta: `Nova ${formatarDataCompletaPdf(estado.proximaNova)}`,
        status: 'Atual'
    };
}

function montarResumoAqiEstacaoPdf() {
    const aqi = dashboardAtual?.aqi;
    if (!aqi) return montarResumoVazioPdf('AQI estimado');

    return {
        label: 'AQI estimado',
        current: String(aqi.value),
        min: aqi.category,
        max: `Dominante ${aqi.dominant}`,
        delta: aqi.timestamp || '--',
        status: aqi.category
    };
}

function montarResumoUltimaMedicaoEstacaoPdf(rotulo, secao, metrica, unidade) {
    const registro = obterUltimoValorTabelaPdf(secao?.table, metrica);
    if (!registro) return montarResumoVazioPdf(rotulo);

    return {
        label: rotulo,
        current: valorJaPossuiUnidade(registro.valor, unidade) ? registro.valor : `${registro.valor}${unidade}`,
        min: registro.data,
        max: registro.hora,
        delta: 'Última medição',
        status: 'Estável'
    };
}

function montarResumoMetricaPdf(metrica, estatistica) {
    if (!estatistica) return null;

    const unidade = metrica.unit || obterUnidadeIndicadorPdf(estatistica.label);
    return {
        label: estatistica.label,
        current: formatarValorPdf(estatistica.average, unidade),
        min: formatarValorPdf(estatistica.min, unidade),
        max: formatarValorPdf(estatistica.max, unidade),
        delta: formatarDiferenca(estatistica.delta, unidade),
        status: obterStatusMetricaPdf(metrica, estatistica.average)
    };
}

function montarResumoSolarPdf() {
    const tempos = lerSegundosEventosSolares();
    if (!tempos) return montarResumoVazioPdf('Ciclo solar');

    // Usa os horários já processados pela API/tela, evitando recalcular eventos solares no relatório.
    const duracaoDia = tempos.sunset - tempos.sunrise;
    return {
        label: 'Ciclo solar',
        current: `Zênite ${formatarTempo(tempos.zenith)}`,
        min: `Nascer ${formatarTempo(tempos.sunrise)}`,
        max: `Pôr ${formatarTempo(tempos.sunset)}`,
        delta: `${duracaoDia.toFixed(2)}h`,
        status: 'Estável'
    };
}

function montarResumoVazioPdf(rotulo) {
    return { label: rotulo, current: '--', min: '--', max: '--', delta: '--', status: 'Sem dados' };
}

function montarCartoesGraficosPdf(secao, configuracao, metricas) {
    const porChave = Object.fromEntries(metricas.map(metrica => [metrica.key, metrica]));
    const cartoes = [];

    if (porChave.temperature && porChave.feelsLike) {
        const metricasTemperatura = [porChave.temperature, porChave.feelsLike];
        cartoes.push({
            label: 'Temperatura x Sensação Térmica',
            unit: '°C',
            image: criarImagemGraficoMetrica('Temperatura x Sensação Térmica', '°C', metricasTemperatura),
            wide: true,
            stats: montarStatsMetricasPdf(metricasTemperatura)
        });
    } else if (porChave.temperature) {
        cartoes.push(criarCartaoMetricaPdf(porChave.temperature, true));
    }

    if (porChave.humidity) {
        cartoes.push(criarCartaoMetricaPdf(porChave.humidity, false));
    }

    metricas
        .filter(metrica => !['temperature', 'feelsLike', 'humidity'].includes(metrica.key))
        .forEach(metrica => cartoes.push(criarCartaoMetricaPdf(metrica, cartoes.length === 0)));

    if (configuracao.incluirSolar) {
        cartoes.push({
            label: 'Ciclo solar',
            unit: 'h',
            image: criarImagemSolarCompacta(),
            compact: true,
            stats: montarStatsGraficoSolarPdf()
        });
    }

    return cartoes;
}

function montarCartoesGraficosEstacaoPdf() {
    const cartoes = [
        {
            label: 'Temperatura por Ambiente',
            unit: '°C',
            image: capturarImagemGrafico('plotGlobalTemperature'),
            wide: true,
            stats: []
        },
        {
            label: 'Umidade por Ambiente',
            unit: '%',
            image: capturarImagemGrafico('plotGlobalHumidity'),
            wide: true,
            stats: []
        },
        {
            label: 'Ciclo solar',
            unit: 'h',
            image: criarImagemSolarCompacta(),
            compact: true,
            stats: montarStatsGraficoSolarPdf()
        }
    ];
    if (window.obterDadosClimaExternoAtual?.()) {
        cartoes.splice(2, 0, {
            label: 'Chuva · 24h + previsão 12h',
            unit: 'mm / %',
            image: criarImagemChuvaPdf(),
            wide: true,
            stats: []
        });
    }
    return cartoes;
}

function criarImagemChuvaPdf() {
    const dados = window.obterDadosClimaExternoAtual?.();
    if (!dados || !window.ClimaChuva || typeof Chart === 'undefined') return null;

    const janela = window.ClimaChuva.montarJanela(dados.previsaoCurtoPrazo, dados.atualizadoEm);
    const recipiente = document.createElement('div');
    recipiente.style.cssText = 'position:fixed;left:-10000px;top:0;width:1200px;height:520px;visibility:hidden;';
    const canvas = document.createElement('canvas');
    canvas.width = 1200;
    canvas.height = 520;
    recipiente.appendChild(canvas);
    document.body.appendChild(recipiente);

    const grafico = window.ClimaChuva.criarGrafico({ canvas, janela, cores });
    if (!grafico) {
        recipiente.remove();
        return null;
    }

    grafico.options.animation = false;
    grafico.options.responsive = false;
    grafico.options.plugins.tooltip.enabled = false;
    grafico.resize(1200, 520);
    grafico.update('none');
    const imagem = canvas.toDataURL('image/png', 1);
    grafico.destroy();
    recipiente.remove();
    return imagem;
}

function criarCartaoMetricaPdf(metrica, amplo) {
    return {
        label: metrica.label,
        unit: metrica.unit,
        image: criarImagemGraficoMetrica(metrica.label, metrica.unit, [metrica]),
        wide: amplo,
        stats: montarStatsMetricasPdf([metrica])
    };
}

function capturarImagemGrafico(idGrafico) {
    const grafico = graficosInstancias[idGrafico];
    const canvas = grafico?.canvas || document.getElementById(idGrafico);
    if (!canvas || typeof canvas.toDataURL !== 'function') return null;

    try {
        // Canvas pode falhar se algum recurso externo violar CORS; nesse caso o PDF mostra "sem dados".
        return canvas.toDataURL('image/png', 1);
    } catch {
        return null;
    }
}

function montarLinhasTabelaPdf(secao, metricas) {
    if (!metricas.length) return [];

    const linhas = secao?.table?.rows || [];
    if (!linhas.length) return [];

    // A tabela do PDF segue a tabela da aba ativa, preservando MQ135 na Sala e sensores no Aquário.
    return linhas.map(linha => {
        const valores = {};
        const alertas = [];
        let possuiValorAusente = false;

        metricas.forEach(metrica => {
            const valorOriginal = linha[metrica.index] ?? '--';
            valores[metrica.key] = formatarValorTabela(valorOriginal, metrica.label);

            const status = obterStatusMetricaPdf(metrica, extrairNumeroPdf(valorOriginal));
            if (status === 'Alerta') {
                alertas.push(metrica.label);
            }
            if (status === 'Sem dados') possuiValorAusente = true;
        });

        return {
            horario: linha[1] || '--',
            valores,
            status: alertas.length ? 'Alerta' : possuiValorAusente ? 'Sem dados' : 'Estável'
        };
    });
}

function obterMetricasGraficosPdf(secao) {
    return (secao?.charts || []).map(grafico => ({
        key: obterChaveMetricaPdf(grafico),
        label: grafico.label,
        unit: obterUnidadeGraficoPdf(grafico),
        chartId: grafico.id,
        chart: grafico
    }));
}

function obterMetricasTabelaPdf(secao) {
    const cabecalhos = secao?.table?.headers || [];

    return cabecalhos.slice(2).map((cabecalho, indice) => ({
        key: gerarIdentificadorUrl(cabecalho),
        label: obterRotuloTabelaPdf(cabecalho),
        unit: obterUnidadeIndicadorPdf(cabecalho),
        index: indice + 2
    }));
}

function obterMetricasGraficosEstacaoPdf() {
    const metricas = [
        { key: 'globalTemperature', label: 'Temperatura por Ambiente', unit: '°C', chartId: 'plotGlobalTemperature' },
        { key: 'globalHumidity', label: 'Umidade por Ambiente', unit: '%', chartId: 'plotGlobalHumidity' },
        { key: 'solarToday', label: 'Ciclo solar', unit: 'h', chartId: 'plotSolarToday' }
    ];
    if (window.obterDadosClimaExternoAtual?.()) metricas.splice(2, 0, { key: 'rain', label: 'Chuva', unit: 'mm / %', chartId: 'plotRain' });
    return metricas;
}

function obterUltimoValorTabelaPdf(tabela, metrica) {
    const cabecalhosLocal = tabela?.headers || [];
    const linhas = tabela?.rows || [];
    const indice = cabecalhosLocal.findIndex(cabecalhoLocal => normalizarTextoTabela(cabecalhoLocal) === normalizarTextoTabela(metrica));
    if (indice < 0 || !linhas.length) return null;

    const primeira = linhas[0];
    const valor = primeira[indice];
    if (valor == null || valor === '--') return null;

    return {
        data: primeira[0] || formatarDataFirebase(dataSelecionada),
        hora: primeira[1] || '--',
        valor: formatarCelulaTabela(valor, cabecalhosLocal[indice], 0)
    };
}

function formatarDataCompletaPdf(data) {
    if (!(data instanceof Date) || Number.isNaN(data.getTime())) return '--';
    return `${String(data.getDate()).padStart(2, '0')}/${String(data.getMonth() + 1).padStart(2, '0')}/${data.getFullYear()}`;
}

function obterRotuloTabelaPdf(cabecalho) {
    const chave = String(cabecalho || '').toLowerCase();
    if (chave === 'sensacao termica' || chave === 'sensação termica' || chave === 'sensacao térmica') {
        return 'Sensação térmica';
    }

    return cabecalho;
}

function montarAlertasDiaPdf(secao, metricas) {
    return metricas
        .filter(metrica => obterFaixaConfortoPdf(metrica))
        .map(metrica => {
            const pontosEmAlerta = obterPontosGraficoPdf(metrica.chart)
                .filter(ponto => obterStatusMetricaPdf(metrica, ponto.value) === 'Alerta');
            if (!pontosEmAlerta.length) return null;

            const primeiro = pontosEmAlerta[0]?.label || '--';
            const ultimo = pontosEmAlerta[pontosEmAlerta.length - 1]?.label || primeiro;
            const faixa = obterFaixaConfortoPdf(metrica);
            return `${metrica.label} fora da faixa ideal (${faixa.min}${metrica.unit} a ${faixa.max}${metrica.unit}) entre ${primeiro} e ${ultimo}.`;
        })
        .filter(Boolean);
}

function obterChaveMetricaPdf(metrica) {
    const rotulo = String(metrica?.label || metrica?.key || metrica?.id || '').toLowerCase();
    if (rotulo.includes('sensação') || rotulo.includes('sensacao')) return 'feelsLike';
    if (rotulo.includes('temperatura')) return 'temperature';
    if (rotulo.includes('umidade')) return 'humidity';
    if (rotulo.includes('pressão') || rotulo.includes('pressao')) return 'pressure';
    if (rotulo === 'ph' || rotulo.includes('ph')) return 'ph';
    if (rotulo.includes('tds')) return 'tds';
    if (rotulo.includes('turbidez')) return 'turbidity';
    return gerarIdentificadorUrl(rotulo || 'metrica');
}

function obterUnidadeGraficoPdf(grafico) {
    const chave = obterChaveMetricaPdf(grafico);
    if (['temperature', 'feelsLike'].includes(chave)) return '°C';
    if (chave === 'humidity') return '%';
    if (chave === 'pressure') return 'hPa';
    if (chave === 'tds') return 'ppm';
    if (chave === 'turbidity') return 'NTU';
    return grafico?.suffix || '';
}

function obterPontosGraficoPdf(grafico) {
    return (grafico?.points || grafico?.series || [])
        .map(ponto => {
            const valor = ponto.value ?? ponto.valor;
            return {
                label: ponto.label || ponto.horario || ponto.time || '',
                value: valor == null || String(valor).trim() === '' ? NaN : Number(valor)
            };
        })
        .filter(ponto => ponto.label && Number.isFinite(ponto.value));
}

function criarImagemGraficoMetrica(titulo, unidade, metricas) {
    if (typeof Chart === 'undefined' || !metricas.length) {
        return capturarImagemGrafico(metricas[0]?.chartId);
    }

    const rotulos = obterPontosGraficoPdf(metricas[0].chart).map(ponto => ponto.label);
    if (!rotulos.length) return capturarImagemGrafico(metricas[0]?.chartId);

    const canvas = document.createElement('canvas');
    canvas.width = 1200;
    canvas.height = 520;
    const contexto = canvas.getContext('2d');
    contexto.fillStyle = '#0f172a';
    contexto.fillRect(0, 0, canvas.width, canvas.height);

    const conjuntosDados = metricas.flatMap((metrica, indice) => {
        const valores = obterPontosGraficoPdf(metrica.chart).map(ponto => ponto.value);
        const cor = obterCorGraficoPdf(metrica.chart, indice);
        const media = calcularMediaPdf(valores);
        return [
            {
                label: metrica.label,
                data: valores,
                borderColor: cor,
                backgroundColor: comAlpha(cor, 0.18),
                fill: true,
                borderWidth: 3,
                pointRadius: 0,
                tension: 0.35
            },
            {
                label: `Média ${metrica.label}`,
                data: valores.map(() => media),
                borderColor: comAlpha(cor, 0.7),
                borderDash: [8, 6],
                borderWidth: 2,
                pointRadius: 0,
                fill: false,
                tension: 0
            }
        ];
    });

    const faixa = obterFaixaConfortoPdf(metricas[0]);
    const graficoLocal = new Chart(contexto, {
        type: 'line',
        data: { labels: rotulos, datasets: conjuntosDados },
        options: criarOpcoesGraficoPdf(titulo, unidade),
        plugins: [pluginFundoGraficoPdf(), pluginFaixaConfortoPdf(faixa)]
    });
    graficoLocal.update('none');
    const imagem = canvas.toDataURL('image/png', 1);
    graficoLocal.destroy();
    return imagem;
}

function criarOpcoesGraficoPdf(titulo, unidade) {
    return {
        responsive: false,
        animation: false,
        plugins: {
            legend: {
                labels: { color: '#e5eefb', boxWidth: 18, padding: 14 }
            },
            title: {
                display: true,
                text: titulo,
                color: '#e5eefb',
                font: { size: 22, weight: 'bold' }
            }
        },
        scales: {
            x: {
                ticks: { color: '#cbd5e1', maxRotation: 0, autoSkip: true, maxTicksLimit: 10 },
                grid: { color: 'rgba(148, 163, 184, 0.16)' }
            },
            y: {
                title: {
                    display: Boolean(unidade),
                    text: unidade || '',
                    color: '#cbd5e1',
                    font: { size: 13, weight: 'bold' }
                },
                ticks: {
                    color: '#cbd5e1',
                    callback: valorLocal => `${valorLocal}${unidade || ''}`
                },
                grid: { color: 'rgba(148, 163, 184, 0.16)' }
            }
        }
    };
}

function pluginFaixaConfortoPdf(faixa) {
    return {
        id: 'pdfComfortBand',
        beforeDatasetsDraw(graficoLocal) {
            const escalaY = graficoLocal.scales.y;
            const area = graficoLocal.chartArea;
            if (!faixa || !escalaY || !area) return;

            const minimoVisivel = Math.min(escalaY.min, escalaY.max);
            const maximoVisivel = Math.max(escalaY.min, escalaY.max);
            const faixaMin = Math.max(faixa.min, minimoVisivel);
            const faixaMax = Math.min(faixa.max, maximoVisivel);
            if (faixaMin >= faixaMax) return;

            const minimoY = escalaY.getPixelForValue(faixaMin);
            const maximoY = escalaY.getPixelForValue(faixaMax);
            const topo = Math.max(area.top, Math.min(minimoY, maximoY));
            const base = Math.min(area.bottom, Math.max(minimoY, maximoY));
            const alturaLocal = base - topo;
            if (alturaLocal <= 0) return;

            const contextoDesenho = graficoLocal.ctx;

            // No PDF a faixa tambem nao participa do zoom: so o trecho visivel e desenhado.
            contextoDesenho.save();
            contextoDesenho.fillStyle = 'rgba(52, 211, 153, 0.08)';
            contextoDesenho.fillRect(area.left, topo, area.right - area.left, alturaLocal);
            contextoDesenho.strokeStyle = 'rgba(52, 211, 153, 0.20)';
            contextoDesenho.setLineDash([8, 6]);
            contextoDesenho.beginPath();
            if (faixa.min >= minimoVisivel && faixa.min <= maximoVisivel) {
                const linhaMin = escalaY.getPixelForValue(faixa.min);
                contextoDesenho.moveTo(area.left, linhaMin);
                contextoDesenho.lineTo(area.right, linhaMin);
            }
            if (faixa.max >= minimoVisivel && faixa.max <= maximoVisivel) {
                const linhaMax = escalaY.getPixelForValue(faixa.max);
                contextoDesenho.moveTo(area.left, linhaMax);
                contextoDesenho.lineTo(area.right, linhaMax);
            }
            contextoDesenho.stroke();
            contextoDesenho.restore();
        }
    };
}

function pluginFundoGraficoPdf() {
    return {
        id: 'pdfChartBackground',
        beforeDraw(graficoLocal) {
            const { ctx: contextoDesenho, width: largura, height: alturaLocal } = graficoLocal;
            contextoDesenho.save();
            contextoDesenho.fillStyle = '#0f172a';
            contextoDesenho.fillRect(0, 0, largura, alturaLocal);
            contextoDesenho.restore();
        }
    };
}

function criarImagemSolarCompacta() {
    const tempos = lerSegundosEventosSolares();
    if (!tempos || typeof Chart === 'undefined' || typeof obterOpcoesSolarDia !== 'function') return null;

    const canvas = document.createElement('canvas');
    canvas.width = 1200;
    canvas.height = 520;
    const contexto = canvas.getContext('2d');
    contexto.fillStyle = '#0f172a';
    contexto.fillRect(0, 0, canvas.width, canvas.height);

    const pontos = [
        { x: 0, y: 0 },
        { x: tempos.dawn, y: 0.08, label: 'Amanhecer', timeLabel: formatarTempo(tempos.dawn) },
        { x: tempos.sunrise, y: 0.52, label: 'Nascer do sol', timeLabel: formatarTempo(tempos.sunrise) },
        { x: tempos.zenith, y: 1, label: 'Zenite', timeLabel: formatarTempo(tempos.zenith) },
        { x: tempos.sunset, y: 0.52, label: 'Pôr do sol', timeLabel: formatarTempo(tempos.sunset) },
        { x: tempos.dusk, y: 0.08, label: 'Anoitecer', timeLabel: formatarTempo(tempos.dusk) },
        { x: 24, y: 0 }
    ];

    // Reaproveita as opções do gráfico da tela para manter curva, escalas, legenda e fundo por períodos solares.
    const opcoes = obterOpcoesSolarDia({ tickSize: 17, labelSize: 17 });
    opcoes.responsive = false;
    opcoes.maintainAspectRatio = false;
    opcoes.animation = false;
    opcoes.layout = { padding: { top: 20, right: 24, bottom: 8, left: 10 } };
    if (opcoes.plugins?.tooltip) {
        opcoes.plugins.tooltip.enabled = false;
    }

    const graficoLocal = new Chart(contexto, {
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
                pointRadius: 6,
                pointHitRadius: 18,
                pointHoverRadius: 7,
                order: 1
            }]
        },
        options: opcoes,
        plugins: [pluginFundoGraficoPdf(), pluginFundoDiaSolar]
    });

    graficoLocal.$solarDayTimes = tempos;
    graficoLocal.update('none');
    const imagem = canvas.toDataURL('image/png', 1);
    graficoLocal.destroy();
    return imagem;
}

function criarOpcoesCicloSolarPdf() {
    return {
        responsive: false,
        animation: false,
        interaction: { mode: 'nearest', intersect: false, axis: 'x' },
        plugins: {
            legend: {
                display: true,
                position: 'top',
                labels: { color: '#e5eefb', font: { size: 15 }, boxWidth: 14, padding: 18 }
            },
            title: { display: false },
            tooltip: { enabled: false }
        },
        scales: {
            x: {
                type: 'linear',
                min: 0,
                max: 24,
                grid: { color: 'rgba(99,132,200,0.13)', drawBorder: false },
                ticks: {
                    stepSize: 2,
                    color: '#cbd5e1',
                    font: { size: 13 },
                    callback: valorLocal => `${valorLocal}h`
                }
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

function pluginFundoDiaSolarPdf(tempos) {
    return {
        id: 'pdfSolarDayBackground',
        beforeDatasetsDraw(graficoLocal) {
            const escalaX = graficoLocal.scales.x;
            const area = graficoLocal.chartArea;
            if (!escalaX || !area) return;

            const contextoDesenho = graficoLocal.ctx;
            const posicaoAmanhecerX = escalaX.getPixelForValue(tempos.dawn);
            const posicaoNascerSolX = escalaX.getPixelForValue(tempos.sunrise);
            const posicaoZeniteX = escalaX.getPixelForValue(tempos.zenith);
            const posicaoPorSolX = escalaX.getPixelForValue(tempos.sunset);
            const posicaoAnoitecerX = escalaX.getPixelForValue(tempos.dusk);

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
            contextoDesenho.lineWidth = 2;
            contextoDesenho.beginPath();
            contextoDesenho.moveTo(posicaoZeniteX, area.top);
            contextoDesenho.lineTo(posicaoZeniteX, area.bottom);
            contextoDesenho.stroke();
            contextoDesenho.restore();
        }
    };
}

function lerSegundosEventosSolares() {
    const tempos = graficosInstancias.plotSolarToday?.$solarDayTimes || dashboardAtual?.solar?.day;
    if (!tempos) return null;

    const amanhecer = Number(tempos.dawn);
    const nascerDoSol = Number(tempos.sunrise);
    const porDoSol = Number(tempos.sunset);
    const anoitecer = Number(tempos.dusk);
    const zenite = Number.isFinite(Number(tempos.zenith)) ? Number(tempos.zenith) : (nascerDoSol + porDoSol) / 2;

    if (![amanhecer, nascerDoSol, zenite, porDoSol, anoitecer].every(Number.isFinite)) return null;
    return { dawn: amanhecer, sunrise: nascerDoSol, zenith: zenite, sunset: porDoSol, dusk: anoitecer };
}

function montarStatsMetricasPdf(metricas) {
    return metricas.flatMap(metrica => {
        const valores = obterPontosGraficoPdf(metrica.chart).map(ponto => ponto.value);
        if (!valores.length) return [];
        return [
            { label: `${metrica.label} média`, value: formatarValorPdf(calcularMediaPdf(valores), metrica.unit) },
            { label: `${metrica.label} máx`, value: formatarValorPdf(Math.max(...valores), metrica.unit) }
        ];
    });
}

function montarStatsGraficoSolarPdf() {
    const tempos = lerSegundosEventosSolares();
    if (!tempos) return [];

    return [
        { label: 'Amanhecer', value: formatarTempo(tempos.dawn) },
        { label: 'Nascer', value: formatarTempo(tempos.sunrise) },
        { label: 'Zênite', value: formatarTempo(tempos.zenith) },
        { label: 'Pôr', value: formatarTempo(tempos.sunset) },
        { label: 'Anoitecer', value: formatarTempo(tempos.dusk) }
    ];
}

function obterCorGraficoPdf(grafico, indice) {
    const paleta = ['#38bdf8', '#fb7185', '#34d399', '#f59e0b', '#a78bfa'];
    return grafico?.color || paleta[indice % paleta.length];
}

function comAlpha(cor, opacidade) {
    if (!cor?.startsWith('#') || ![4, 7].includes(cor.length)) return cor;

    const normalizada = cor.length === 4
        ? `#${cor[1]}${cor[1]}${cor[2]}${cor[2]}${cor[3]}${cor[3]}`
        : cor;
    const r = parseInt(normalizada.slice(1, 3), 16);
    const g = parseInt(normalizada.slice(3, 5), 16);
    const b = parseInt(normalizada.slice(5, 7), 16);
    return `rgba(${r}, ${g}, ${b}, ${opacidade})`;
}

function calcularMediaPdf(valores) {
    const validos = valores.filter(Number.isFinite);
    if (!validos.length) return NaN;
    return validos.reduce((total, valor) => total + valor, 0) / validos.length;
}

function formatarValorTabela(valor, indicador) {
    if (valor === '--') return valor;
    const unidade = obterUnidadeIndicadorPdf(indicador);
    return unidade && !String(valor).endsWith(unidade) ? `${valor}${unidade}` : String(valor);
}

function extrairNumeroPdf(valor) {
    if (valor === null || valor === undefined || valor === '--') return Number.NaN;
    return Number(String(valor).replace(/[^\d,.-]/g, '').replace(',', '.'));
}

function obterUnidadeIndicadorPdf(indicador) {
    const chave = indicador.toLowerCase();
    if (chave.includes('temperatura') || chave.includes('sensacao') || chave.includes('sensação')) return '°C';
    if (chave.includes('umidade')) return '%';
    if (chave.includes('pressão') || chave.includes('pressao')) return ' hPa';
    if (['co', 'co2', 'acetona', 'alcool', 'álcool', 'amonia', 'amônia', 'tolueno'].includes(chave)) return 'ppm';
    if (chave.includes('tds')) return 'ppm';
    if (chave.includes('turbidez')) return 'NTU';
    return '';
}

function obterStatusTabelaPdf(indicador, valor) {
    const numero = Number(String(valor).replace(',', '.'));
    return obterStatusMetricaPdf(indicador, numero);
}

function formatarValorPdf(valor, unidade) {
    if (valor == null || String(valor).trim() === '' || !Number.isFinite(Number(valor))) return '--';
    const casas = Math.abs(Number(valor)) >= 100 ? 0 : 1;
    return `${Number(valor).toFixed(casas)}${unidade || ''}`;
}

function formatarDiferenca(valor, unidade) {
    if (valor == null || String(valor).trim() === '' || !Number.isFinite(Number(valor))) return '--';
    const sinal = Number(valor) > 0 ? '+' : '';
    return `${sinal}${formatarValorPdf(valor, unidade)}`;
}

function obterStatusMetricaPdf(metricaOuRotulo, valor) {
    if (valor == null || String(valor).trim() === '' || !Number.isFinite(Number(valor))) return 'Sem dados';
    const faixa = obterFaixaConfortoPdf(metricaOuRotulo);

    if (faixa && (Number(valor) < faixa.min || Number(valor) > faixa.max)) {
        return 'Alerta';
    }
    return 'Estável';
}

function obterFaixaConfortoPdf(metricaOuRotulo) {
    const texto = typeof metricaOuRotulo === 'object'
        ? `${metricaOuRotulo.key || ''} ${metricaOuRotulo.label || ''} ${metricaOuRotulo.chartId || ''}`
        : String(metricaOuRotulo || '');
    const chave = texto.toLowerCase();

    if (chave.includes('umidade') || chave.includes('humidity')) return faixaConfortoUmidade;
    if (chave.includes('aquario') && (chave.includes('temperatura') || chave.includes('temperature'))) return faixaConfortoAquario;
    if (chave.includes('temperatura') || chave.includes('temperature') || chave.includes('sensação') || chave.includes('sensacao') || chave.includes('feelslike')) return faixaConforto;
    return null;
}

function obterClasseEstado(estado) {
    if (estado === 'Alerta') return 'alert';
    if (estado === 'Sem dados') return 'empty';
    return 'stable';
}

async function gerarPdf(elemento, nomeArquivo) {
    await aguardarImagens(elemento);

    const { jsPDF } = window.jspdf;
    const pdf = new jsPDF({ unit: 'mm', format: 'a4', orientation: 'portrait', compress: true });
    await adicionarBlocosRelatorio(pdf, elemento);
    adicionarRodapesPdf(pdf);
    pdf.save(nomeArquivo);
}

async function adicionarBlocosRelatorio(pdf, elemento) {
    const leiaute = criarLeiautePdf();
    const cabecalho = elemento.querySelector('.pdf-report__header');
    const resumo = elemento.querySelector('.pdf-report__summary-section');
    const graficos = elemento.querySelector('.pdf-report__charts-section');
    const tabela = elemento.querySelector('.pdf-report__table-section');
    let posicaoAtualY = leiaute.margin.top;

    // O layout original mantém cabeçalho e indicadores na primeira página, gráficos na segunda e tabela na terceira.
    pintarPaginaPdf(pdf, leiaute);
    posicaoAtualY = await adicionarBlocoElemento(pdf, cabecalho, leiaute, posicaoAtualY, { gap: 6 });
    await adicionarBlocoElemento(pdf, resumo, leiaute, posicaoAtualY, { gap: 0 });

    posicaoAtualY = adicionarPaginaPdf(pdf, leiaute);
    posicaoAtualY = adicionarTituloSecao(pdf, 'Gráficos', leiaute, posicaoAtualY);
    for (const cartaoGrafico of graficos?.querySelectorAll('.pdf-chart-card') || []) {
        posicaoAtualY = await adicionarBlocoElemento(pdf, cartaoGrafico, leiaute, posicaoAtualY, { gap: 4 });
    }

    if (tabela) {
        posicaoAtualY = adicionarPaginaPdf(pdf, leiaute);
        posicaoAtualY = adicionarTituloSecao(pdf, 'Tabela resumida', leiaute, posicaoAtualY);
        const conteudoTabela = tabela.querySelector('.pdf-table, .pdf-empty');
        if (conteudoTabela?.matches('.pdf-table')) {
            await adicionarTabelaPdf(pdf, conteudoTabela, leiaute, posicaoAtualY);
        } else {
            await adicionarBlocoElemento(pdf, conteudoTabela, leiaute, posicaoAtualY, { gap: 0 });
        }
    }
}

function criarLeiautePdf() {
    return {
        pageWidth: 210,
        pageHeight: 297,
        contentWidth: 194,
        contentBottom: 281,
        margin: { top: 8, right: 8, bottom: 16, left: 8 }
    };
}

async function adicionarTabelaPdf(pdf, tabela, leiaute, posicaoAtualY) {
    const linhas = [...(tabela.tBodies[0]?.rows || [])];
    if (!linhas.length) return adicionarBlocoElemento(pdf, tabela, leiaute, posicaoAtualY, { gap: 0 });

    const paginaTabela = tabela.cloneNode(false);
    if (tabela.tHead) paginaTabela.appendChild(tabela.tHead.cloneNode(true));
    const corpo = paginaTabela.appendChild(document.createElement('tbody'));
    tabela.parentElement.appendChild(paginaTabela);

    const alturaEmMm = () => paginaTabela.getBoundingClientRect().height * leiaute.contentWidth / paginaTabela.getBoundingClientRect().width;
    const desenharPagina = async () => {
        const canvas = await capturarElemento(paginaTabela);
        const altura = obterAlturaCanvasMm(canvas, leiaute);
        if (altura > obterAlturaConteudo(leiaute)) throw new Error('Uma linha da tabela excede a altura da página.');
        if (posicaoAtualY + altura > leiaute.contentBottom) posicaoAtualY = adicionarPaginaPdf(pdf, leiaute);
        adicionarImagemCanvas(pdf, canvas, leiaute, posicaoAtualY, altura);
        posicaoAtualY += altura;
    };

    try {
        for (const linha of linhas) {
            corpo.appendChild(linha.cloneNode(true));
            if (posicaoAtualY + alturaEmMm() <= leiaute.contentBottom) continue;

            corpo.lastChild.remove();
            if (corpo.rows.length) {
                await desenharPagina();
                posicaoAtualY = adicionarPaginaPdf(pdf, leiaute);
                corpo.replaceChildren();
            } else if (posicaoAtualY > leiaute.margin.top) {
                posicaoAtualY = adicionarPaginaPdf(pdf, leiaute);
            }
            corpo.appendChild(linha.cloneNode(true));
            if (alturaEmMm() > obterAlturaConteudo(leiaute)) throw new Error('Uma linha da tabela excede a altura da página.');
        }
        await desenharPagina();
        return posicaoAtualY;
    } finally {
        paginaTabela.remove();
    }
}

async function adicionarBlocoElemento(pdf, elemento, leiaute, posicaoAtualY, opcoes = {}) {
    if (!elemento) return posicaoAtualY;

    const intervaloVisual = opcoes.gap ?? 4;
    const canvas = await capturarElemento(elemento);
    const alturaMm = obterAlturaCanvasMm(canvas, leiaute);

    // Blocos grandes, como tabelas, são fatiados para atravessar páginas sem perder conteúdo.
    if (opcoes.allowSplit || alturaMm > obterAlturaConteudo(leiaute)) {
        return adicionarFatiasCanvas(pdf, canvas, leiaute, posicaoAtualY, intervaloVisual);
    }

    if (posicaoAtualY + alturaMm > leiaute.contentBottom) {
        posicaoAtualY = adicionarPaginaPdf(pdf, leiaute);
    }

    adicionarImagemCanvas(pdf, canvas, leiaute, posicaoAtualY, alturaMm);
    return posicaoAtualY + alturaMm + intervaloVisual;
}

async function capturarElemento(elemento) {
    return html2canvas(elemento, {
        scale: 2,
        backgroundColor: '#0b1120',
        useCORS: true,
        scrollX: 0,
        scrollY: 0,
        windowWidth: Math.max(elemento.scrollWidth, elemento.offsetWidth),
        windowHeight: Math.max(elemento.scrollHeight, elemento.offsetHeight)
    });
}

function adicionarFatiasCanvas(pdf, canvas, leiaute, posicaoAtualY, intervaloVisual) {
    const pixelsPorMm = canvas.width / leiaute.contentWidth;
    let posicaoOrigemY = 0;

    // Divide o canvas em partes proporcionais ao espaço restante de cada página A4.
    while (posicaoOrigemY < canvas.height) {
        const alturaDisponivel = leiaute.contentBottom - posicaoAtualY;
        if (alturaDisponivel < 35) {
            posicaoAtualY = adicionarPaginaPdf(pdf, leiaute);
        }

        const alturaFatia = Math.min(
            Math.floor((leiaute.contentBottom - posicaoAtualY) * pixelsPorMm),
            canvas.height - posicaoOrigemY
        );
        const canvasPagina = criarFatiaCanvas(canvas, posicaoOrigemY, alturaFatia);
        const alturaFatiaMm = alturaFatia / pixelsPorMm;

        adicionarImagemCanvas(pdf, canvasPagina, leiaute, posicaoAtualY, alturaFatiaMm);
        posicaoOrigemY += alturaFatia;
        posicaoAtualY += alturaFatiaMm;

        if (posicaoOrigemY < canvas.height) {
            posicaoAtualY = adicionarPaginaPdf(pdf, leiaute);
        }
    }

    return posicaoAtualY + intervaloVisual;
}

function criarFatiaCanvas(canvas, posicaoOrigemY, alturaFatia) {
    const canvasPagina = document.createElement('canvas');
    canvasPagina.width = canvas.width;
    canvasPagina.height = alturaFatia;

    const contexto = canvasPagina.getContext('2d');
    contexto.fillStyle = '#0b1120';
    contexto.fillRect(0, 0, canvasPagina.width, canvasPagina.height);
    contexto.drawImage(canvas, 0, posicaoOrigemY, canvas.width, alturaFatia, 0, 0, canvasPagina.width, canvasPagina.height);

    return canvasPagina;
}

function adicionarImagemCanvas(pdf, canvas, leiaute, y, alturaMm) {
    pdf.addImage(canvas.toDataURL('image/jpeg', 0.96), 'JPEG', leiaute.margin.left, y, leiaute.contentWidth, alturaMm);
}

function obterAlturaCanvasMm(canvas, leiaute) {
    return canvas.height * leiaute.contentWidth / canvas.width;
}

function obterAlturaConteudo(leiaute) {
    return leiaute.contentBottom - leiaute.margin.top;
}

function adicionarTituloSecao(pdf, titulo, leiaute, posicaoAtualY) {
    const altura = 8;
    if (posicaoAtualY + altura > leiaute.contentBottom) {
        posicaoAtualY = adicionarPaginaPdf(pdf, leiaute);
    }

    pdf.setFont('helvetica', 'bold');
    pdf.setFontSize(8);
    pdf.setTextColor(148, 163, 184);
    pdf.text(titulo.toUpperCase(), leiaute.margin.left, posicaoAtualY + 4);
    return posicaoAtualY + altura;
}

function adicionarPaginaPdf(pdf, leiaute) {
    pdf.addPage();
    pintarPaginaPdf(pdf, leiaute);
    return leiaute.margin.top;
}

function pintarPaginaPdf(pdf, leiaute) {
    pdf.setFillColor(11, 17, 32);
    pdf.rect(0, 0, leiaute.pageWidth, leiaute.pageHeight, 'F');
}

function aguardarImagens(elemento) {
    const imagens = Array.from(elemento.querySelectorAll('img'));
    const pendente = imagens
        .filter(imagemElemento => !imagemElemento.complete)
        .map(imagemElemento => new Promise(resolver => {
            imagemElemento.addEventListener('load', resolver, { once: true });
            imagemElemento.addEventListener('error', resolver, { once: true });
        }));

    return Promise.all(pendente);
}

function adicionarRodapesPdf(pdf) {
    const quantidadePaginas = pdf.internal.getNumberOfPages();
    const geradoEm = formatarDataHoraRelatorio(new Date());

    for (let pagina = 1; pagina <= quantidadePaginas; pagina++) {
        pdf.setPage(pagina);
        pdf.setFontSize(8);
        pdf.setTextColor(148, 163, 184);
        pdf.text('Estação Climática', 8, 290);
        pdf.text(`Página ${pagina} / ${quantidadePaginas}`, 105, 290, { align: 'center' });
        pdf.text(geradoEm, 202, 290, { align: 'right' });
    }
}


Object.assign(window, {
    configurarExportacaoDados,
    exportarAbaAtiva,
    executarExportacaoComTimeout: executarComTimeout,
    atualizarProgressoExportacao: atualizarProgresso,
    obterFormatoExportacao,
    montarRelatorioPdf,
    exportarRelatorioJson,
    montarDadosDaAbaJson,
    montarClimaExternoJson,
    baixarJson,
    obterConfiguracaoAbaPdf,
    criarCabecalhoPdf: criarCabecalho,
    criarIndicePdf,
    criarSecaoResumoPdf: criarSecaoResumo,
    criarCartaoResumoPdf: criarCardResumoRelatorio,
    criarSecaoGraficosPdf: criarSecaoGraficos,
    criarCartaoGraficoPdf: criarCardGrafico,
    criarSecaoTabelaPdf: criarSecaoTabela,
    criarPainelAlertasPdf: criarPainelAlertas,
    montarCartoesResumoPdf,
    montarResumoChuvaPdf,
    montarResumoMetricaPdf,
    montarResumoSolarPdf,
    montarResumoVazioPdf,
    montarCartoesGraficosPdf,
    criarImagemChuvaPdf,
    criarCartaoMetricaPdf,
    capturarImagemGraficoPdf: capturarImagemGrafico,
    montarLinhasTabelaPdf,
    obterMetricasGraficosPdf,
    obterMetricasTabelaPdf,
    obterRotuloTabelaPdf,
    montarAlertasDiaPdf,
    obterChaveMetricaPdf,
    obterUnidadeGraficoPdf,
    obterPontosGraficoPdf,
    criarImagemGraficoMetricasPdf: criarImagemGraficoMetrica,
    criarImagemCicloSolarPdf: criarImagemSolarCompacta,
    criarOpcoesGraficoPdf,
    pluginFundoGraficoPdf,
    pluginFaixaConfortoPdf,
    montarStatsMetricasPdf,
    montarStatsGraficoSolarPdf,
    obterCorGraficoPdf,
    comAlpha,
    calcularMediaPdf,
    formatarValorTabelaPdf: formatarValorTabela,
    extrairNumeroPdf,
    obterUnidadeIndicadorPdf,
    obterStatusTabelaPdf,
    formatarValorPdf,
    formatarDeltaPdf: formatarDiferenca,
    obterStatusMetricaPdf,
    obterFaixaConfortoPdf,
    obterClasseStatusPdf: obterClasseEstado,
    gerarPdf,
    adicionarBlocosRelatorioPdf: adicionarBlocosRelatorio,
    criarLayoutPdf: criarLeiautePdf,
    adicionarBlocoElementoPdf: adicionarBlocoElemento,
    capturarElementoPdf: capturarElemento,
    adicionarFatiasCanvasPdf: adicionarFatiasCanvas,
    criarFatiaCanvasPdf: criarFatiaCanvas,
    adicionarImagemCanvasPdf: adicionarImagemCanvas,
    obterAlturaCanvasMm,
    obterAlturaConteudoPdf: obterAlturaConteudo,
    adicionarTituloSecaoPdf: adicionarTituloSecao,
    adicionarPaginaPdf,
    pintarPaginaPdf,
    aguardarImagensPdf: aguardarImagens,
    adicionarRodapesPdf
});
