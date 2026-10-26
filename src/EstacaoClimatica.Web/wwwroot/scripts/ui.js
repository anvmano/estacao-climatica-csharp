let ultimoEstadoAstronomico = null;

function configurarRelogio() {
    const elementoDom = document.getElementById('headerTime');
    const atualizar = () => {
        if (elementoDom) elementoDom.textContent = new Date().toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
    };
    atualizar();
    setInterval(atualizar, 1000);
}

function configurarIndicadorAstronomico() {
    const indicador = document.getElementById('astroIndicator');
    const janelaDetalhes = document.getElementById('astroPopover');

    if (indicador && janelaDetalhes) {
        indicador.setAttribute('role', 'button');
        indicador.setAttribute('tabindex', '0');
        indicador.setAttribute('aria-controls', 'astroPopover');
        indicador.setAttribute('aria-expanded', 'false');

        indicador.addEventListener('click', evento => {
            evento.stopPropagation();
            alternarDetalhesAstronomicos();
        });

        indicador.addEventListener('keydown', evento => {
            if (evento.key !== 'Enter' && evento.key !== ' ') return;
            evento.preventDefault();
            alternarDetalhesAstronomicos();
        });

        document.addEventListener('click', evento => {
            if (janelaDetalhes.hidden) return;
            if (janelaDetalhes.contains(evento.target) || indicador.contains(evento.target)) return;
            fecharDetalhesAstronomicos();
        });

        document.addEventListener('keydown', evento => {
            if (evento.key === 'Escape') fecharDetalhesAstronomicos();
        });

        window.addEventListener('header-popover-open', evento => {
            if (evento.detail?.source !== 'astro') fecharDetalhesAstronomicos();
        });
    }

    atualizarIndicadorAstronomico();
    if (temporizadorIndicadorAstronomico) clearInterval(temporizadorIndicadorAstronomico);
    temporizadorIndicadorAstronomico = setInterval(atualizarIndicadorAstronomico, 60000);
}

function configurarModulo() {
    const indicador = document.getElementById('aqiIndicator');
    const janelaDetalhes = document.getElementById('aqiPopover');
    if (!indicador || !janelaDetalhes) return;

    indicador.addEventListener('click', evento => {
        evento.stopPropagation();
        alternarDetalhes();
    });

    document.addEventListener('click', evento => {
        if (janelaDetalhes.hidden) return;
        if (janelaDetalhes.contains(evento.target) || indicador.contains(evento.target)) return;
        fecharDetalhes();
    });

    document.addEventListener('keydown', evento => {
        if (evento.key === 'Escape') fecharDetalhes();
    });

    window.addEventListener('header-popover-open', evento => {
        if (evento.detail?.source !== 'aqi') fecharDetalhes();
    });

    atualizarModulo(null);
}

function atualizarModulo(aqi) {
    const indicador = document.getElementById('aqiIndicator');
    const janelaDetalhes = document.getElementById('aqiPopover');
    if (!indicador || !janelaDetalhes) return;

    if (!aqi) {
        renderizarAqiIndisponivel(indicador, janelaDetalhes);
        return;
    }

    const valor = indicador.querySelector('.aqi-indicator__value');
    const status = indicador.querySelector('.aqi-indicator__status');
    const titulo = `AQI estimado da Sala: ${aqi.value} ${aqi.category}. Dominante: ${aqi.dominant}.`;

    indicador.className = `aqi-indicator aqi-indicator--${aqi.categoryClass}`;
    indicador.title = titulo;
    indicador.setAttribute('aria-label', titulo);
    if (valor) valor.textContent = aqi.value;
    if (status) status.textContent = aqi.category;

    renderizarDetalhes(janelaDetalhes, aqi);
}

function renderizarAqiIndisponivel(indicador, janelaDetalhes) {
    indicador.className = 'aqi-indicator aqi-indicator--unknown';
    indicador.title = 'AQI estimado indisponível';
    indicador.setAttribute('aria-label', indicador.title);
    indicador.setAttribute('aria-expanded', 'false');

    const valor = indicador.querySelector('.aqi-indicator__value');
    const status = indicador.querySelector('.aqi-indicator__status');
    if (valor) valor.textContent = '--';
    if (status) status.textContent = '--';

    janelaDetalhes.hidden = true;
    janelaDetalhes.innerHTML = `
        <div class="aqi-popover__header">
            <span>AQI estimado da Sala</span>
            <strong>--</strong>
        </div>
        <p class="aqi-popover__text">Sem dados suficientes do MQ135.</p>
    `;
}

function renderizarDetalhes(janelaDetalhes, aqi) {
    const subindices = (aqi.subIndexes || []).slice(0, 3).map(item => `
        <li>
            <span>${item.label}</span>
            <strong>${formatarNumero(item.concentration)}${item.unit}</strong>
            <em>AQI ${item.value}</em>
        </li>
    `).join('');

    janelaDetalhes.innerHTML = `
        <div class="aqi-popover__header">
            <span>AQI estimado da Sala</span>
            <strong>${aqi.value}</strong>
        </div>
        <div class="aqi-popover__badge aqi-popover__badge--${aqi.categoryClass}">
            ${aqi.category}
        </div>
        <p class="aqi-popover__text">${aqi.impact}</p>
        <dl class="aqi-popover__meta">
            <div><dt>Dominante</dt><dd>${aqi.dominant}</dd></div>
            <div><dt>Atualizado</dt><dd>${aqi.timestamp}</dd></div>
        </dl>
        <ul class="aqi-popover__list">${subindices}</ul>
        <p class="aqi-popover__note">Estimativa pelo MQ135; categorias visuais seguem as faixas AQI.</p>
    `;
}

function alternarDetalhes() {
    const indicador = document.getElementById('aqiIndicator');
    const janelaDetalhes = document.getElementById('aqiPopover');
    if (!indicador || !janelaDetalhes) return;

    if (janelaDetalhes.hidden) {
        window.dispatchEvent(new CustomEvent('header-popover-open', { detail: { source: 'aqi' } }));
        janelaDetalhes.hidden = false;
        indicador.setAttribute('aria-expanded', 'true');
    } else {
        fecharDetalhes();
    }
}

function fecharDetalhes() {
    const indicador = document.getElementById('aqiIndicator');
    const janelaDetalhes = document.getElementById('aqiPopover');
    if (!indicador || !janelaDetalhes) return;
    janelaDetalhes.hidden = true;
    indicador.setAttribute('aria-expanded', 'false');
}

function atualizarIndicadorAstronomico() {
    const indicador = document.getElementById('astroIndicator');
    if (!indicador) return;
    if (document.getElementById('privateApp')?.hasAttribute('hidden')) return;

    const estado = obterEstadoAstronomico();
    ultimoEstadoAstronomico = estado;
    indicador.classList.remove('astro-indicator--day', 'astro-indicator--twilight', 'astro-indicator--night', 'astro-indicator--unknown');
    indicador.classList.add(`astro-indicator--${estado.modo}`);
    indicador.style.setProperty('--astro-progress', estado.progresso.toFixed(3));
    const trilho = indicador.querySelector('.astro-indicator__track');
    if (trilho) trilho.style.setProperty('--astro-x', estado.x);
    indicador.style.setProperty('--astro-y', estado.y);
    indicador.setAttribute('aria-label', estado.titulo);
    indicador.title = estado.titulo;

    const rotulo = indicador.querySelector('.astro-indicator__label');
    if (rotulo) rotulo.textContent = '';

    atualizarDetalhesAstronomicos(estado);
}

function atualizarIndicadorAstronomicoPublico(solar, origem) {
    const indicador = document.getElementById('astroIndicator');
    const janelaDetalhes = document.getElementById('astroPopover');
    if (!indicador || !janelaDetalhes) return;

    const eventos = solar ? {
        dawn: Number(solar.dawn),
        sunrise: Number(solar.sunrise),
        zenith: Number(solar.zenith),
        sunset: Number(solar.sunset),
        dusk: Number(solar.dusk)
    } : null;

    if (!eventos || !Object.values(eventos).every(Number.isFinite)) {
        ultimoEstadoAstronomico = null;
        indicador.className = 'astro-indicator astro-indicator--unknown';
        indicador.title = 'Informe CEP ou permita a localização para consultar o ciclo solar.';
        indicador.setAttribute('aria-label', indicador.title);
        indicador.setAttribute('aria-expanded', 'false');
        janelaDetalhes.hidden = true;
        janelaDetalhes.innerHTML = `
            <div class="astro-popover__header"><span>Ciclo solar de hoje</span><strong>--</strong></div>
            <dl class="astro-popover__list"><div><dt>Status</dt><dd>Informe CEP ou localização</dd></div><div><dt>Origem</dt><dd>Modo público</dd></div></dl>
        `;
        return;
    }

    ultimoEstadoAstronomico = obterEstadoAstronomico(new Date(), eventos, origem || null);
    const estado = ultimoEstadoAstronomico;
    indicador.className = `astro-indicator astro-indicator--${estado.modo}`;
    indicador.style.setProperty('--astro-progress', estado.progresso.toFixed(3));
    const trilho = indicador.querySelector('.astro-indicator__track');
    if (trilho) trilho.style.setProperty('--astro-x', estado.x);
    indicador.style.setProperty('--astro-y', estado.y);
    indicador.title = estado.titulo;
    indicador.setAttribute('aria-label', estado.titulo);
    indicador.setAttribute('aria-expanded', 'false');
    janelaDetalhes.hidden = true;
    atualizarDetalhesAstronomicos(estado);
}

function obterEstadoAstronomico(agora = new Date(), eventosInformados = null, origem = null) {
    const eventos = eventosInformados || obterEventosSolaresDataSelecionada();
    const hora = agora.getHours() + (agora.getMinutes() / 60) + (agora.getSeconds() / 3600);

    // Sem dados solares do dia atual, mantém o indicador útil com o ciclo aproximado 06:00-18:00.
    const eventosEfetivos = eventos || {
        dawn: 5.5,
        sunrise: 6,
        zenith: 12,
        sunset: 18,
        dusk: 18.5
    };

    const estaEmTransicao = (hora >= eventosEfetivos.dawn && hora < eventosEfetivos.sunrise)
        || (hora > eventosEfetivos.sunset && hora <= eventosEfetivos.dusk);
    const estaDeDia = hora >= eventosEfetivos.sunrise && hora <= eventosEfetivos.sunset;
    const estaNoPercursoSolar = hora >= eventosEfetivos.dawn && hora <= eventosEfetivos.dusk;
    const modo = estaEmTransicao ? 'twilight' : estaDeDia ? 'day' : 'night';
    const inicioFaixa = estaNoPercursoSolar ? eventosEfetivos.dawn : eventosEfetivos.dusk;
    const fimFaixa = estaNoPercursoSolar ? eventosEfetivos.dusk : eventosEfetivos.sunrise + 24;
    const horaAjustada = !estaNoPercursoSolar && hora < eventosEfetivos.sunrise ? hora + 24 : hora;
    const progresso = limitar((horaAjustada - inicioFaixa) / (fimFaixa - inicioFaixa || 1), 0, 1);
    const rotulo = modo === 'night' ? 'Noite' : modo === 'twilight' ? 'Transição' : 'Dia';

    return {
        ...montarEstadoAstronomico(modo, rotulo, progresso, eventosEfetivos, eventos ? 'dados solares' : 'fallback 06:00-18:00'),
        origem
    };
}

function obterEventosSolaresDataSelecionada() {
    const dia = dashboardAtual?.solar?.currentDay;

    // Usa dados solares somente quando pertencem ao dia real atual; outras datas abertas não dirigem o header.
    if (!dia || dia.date !== paraDataFirebase(new Date())) return null;

    const eventos = {
        dawn: Number(dia.dawn),
        sunrise: Number(dia.sunrise),
        zenith: Number(dia.zenith),
        sunset: Number(dia.sunset),
        dusk: Number(dia.dusk)
    };

    return Object.values(eventos).every(Number.isFinite) ? eventos : null;
}

function montarEstadoAstronomico(modo, rotulo, progresso, eventos, origem) {
    const progressoLimitado = limitar(progresso, 0, 1);
    const x = `${(progressoLimitado * 100).toFixed(1)}%`;
    const arco = Math.sin(progressoLimitado * Math.PI);
    const y = `${(3 + arco * 12).toFixed(1)}px`;
    const titulo = `Nascer do sol: ${formatarTempo(eventos.sunrise)} · Pôr do sol: ${formatarTempo(eventos.sunset)}`;

    return { modo, rotulo, progresso: progressoLimitado, x, y, titulo, eventos, origem };
}

function alternarDetalhesAstronomicos() {
    const indicador = document.getElementById('astroIndicator');
    const janelaDetalhes = document.getElementById('astroPopover');
    if (!indicador || !janelaDetalhes) return;

    if (janelaDetalhes.hidden) {
        window.dispatchEvent(new CustomEvent('header-popover-open', { detail: { source: 'astro' } }));
        atualizarDetalhesAstronomicos(ultimoEstadoAstronomico || obterEstadoAstronomico());
        janelaDetalhes.hidden = false;
        indicador.setAttribute('aria-expanded', 'true');
    } else {
        fecharDetalhesAstronomicos();
    }
}

function fecharDetalhesAstronomicos() {
    const indicador = document.getElementById('astroIndicator');
    const janelaDetalhes = document.getElementById('astroPopover');
    if (!indicador || !janelaDetalhes) return;

    janelaDetalhes.hidden = true;
    indicador.setAttribute('aria-expanded', 'false');
}

function atualizarDetalhesAstronomicos(estado) {
    const janelaDetalhes = document.getElementById('astroPopover');
    if (!janelaDetalhes || !estado) return;

    const destaque = janelaDetalhes.querySelector('.astro-popover__header strong');
    const lista = janelaDetalhes.querySelector('.astro-popover__list');
    if (!destaque || !lista) return;

    const eventos = estado.eventos;
    destaque.textContent = obterRotuloModoAstronomico(estado.modo);
    const origem = estado.origem ? [['Origem', estado.origem]] : [];
    lista.innerHTML = [
        ...origem,
        ['Amanhecer', formatarTempo(eventos.dawn)],
        ['Nascer do sol', formatarTempo(eventos.sunrise)],
        ['Zênite', formatarTempo(eventos.zenith)],
        ['Pôr do sol', formatarTempo(eventos.sunset)],
        ['Anoitecer', formatarTempo(eventos.dusk)],
        ['Duração do dia', formatarDuracaoAstronomica(eventos.sunrise, eventos.sunset)]
    ].map(([rotulo, valor]) => `<div><dt>${rotulo}</dt><dd>${valor}</dd></div>`).join('');
}

function obterRotuloModoAstronomico(modo) {
    if (modo === 'day') return 'Dia';
    if (modo === 'twilight') return 'Transição';
    return 'Noite';
}

function formatarDuracaoAstronomica(inicio, fim) {
    if (!Number.isFinite(inicio) || !Number.isFinite(fim)) return '--';

    const totalMinutosSolar = Math.max(0, Math.round((fim - inicio) * 60));
    const horas = Math.floor(totalMinutosSolar / 60);
    const minutos = totalMinutosSolar % 60;
    return `${horas}h${String(minutos).padStart(2, '0')}`;
}

function configurarAbas() {
    document.querySelectorAll('.tablink[data-tab-target]').forEach(botao => {
        botao.addEventListener('click', () => abrirAba(botao.dataset.tabTarget, botao));
        botao.addEventListener('keydown', evento => {
            const botoes = Array.from(document.querySelectorAll('.tablink[data-tab-target]'));
            const indice = botoes.indexOf(botao);
            let proximoIndice = null;
            if (evento.key === 'ArrowLeft') proximoIndice = (indice - 1 + botoes.length) % botoes.length;
            if (evento.key === 'ArrowRight') proximoIndice = (indice + 1) % botoes.length;
            if (evento.key === 'Home') proximoIndice = 0;
            if (evento.key === 'End') proximoIndice = botoes.length - 1;
            if (proximoIndice === null) return;
            evento.preventDefault();
            const proximo = botoes[proximoIndice];
            abrirAba(proximo.dataset.tabTarget, proximo);
            proximo.focus();
        });
    });
    const abaSalva = localStorage.getItem('activeTab');
    const abaInicial = document.getElementById(abaSalva) ? abaSalva : 'Tab0';
    abrirAba(abaInicial);
}

function configurarDeslizeAbas({ ordemAbas = ['Tab0', 'Tab1', 'Tab2', 'Tab3'], distanciaMinima = 60, desvioVerticalMaximo = 80 } = {}) {
    const recipiente = document.querySelector('.container');
    if (!recipiente || ordemAbas.length < 2) return;

    let inicioX = 0;
    let inicioY = 0;
    let ignorarSwipe = false;

    recipiente.addEventListener('touchstart', evento => {
        const toque = evento.touches[0];
        if (!toque) return;
        ignorarSwipe = deveIgnorarDeslizeAbas(evento.target);
        inicioX = toque.clientX;
        inicioY = toque.clientY;
    }, { passive: true });

    recipiente.addEventListener('touchend', evento => {
        if (ignorarSwipe) {
            ignorarSwipe = false;
            return;
        }

        const toque = evento.changedTouches[0];
        if (!toque) return;

        const deltaX = toque.clientX - inicioX;
        const deltaY = toque.clientY - inicioY;

        // Ignora gestos curtos ou muito verticais para não confundir scroll com troca de aba.
        if (Math.abs(deltaX) < distanciaMinima) return;
        if (Math.abs(deltaY) > desvioVerticalMaximo) return;

        const abaAtual = obterNomeAbaAtiva();
        const indiceAtual = ordemAbas.indexOf(abaAtual);
        if (indiceAtual === -1) return;

        const proximoIndice = deltaX < 0 ? indiceAtual + 1 : indiceAtual - 1;
        const proximaAba = ordemAbas[proximoIndice];
        if (!proximaAba) return;

        abrirAba(proximaAba);
    }, { passive: true });
}

function deveIgnorarDeslizeAbas(destino) {
    const areaInterativaExplicita = destino?.closest?.('.table-wrapper, .weekly-heatmap, .hourly-heatmap, .calendar-heatmap');
    if (areaInterativaExplicita) return true;

    let elemento = destino instanceof Element ? destino : null;
    while (elemento && !elemento.classList.contains('container')) {
        const estilo = window.getComputedStyle(elemento);
        const podeRolarHorizontalmente = /(auto|scroll)/.test(estilo.overflowX) && elemento.scrollWidth > elemento.clientWidth;
        if (podeRolarHorizontalmente) return true;
        elemento = elemento.parentElement;
    }

    return false;
}

function obterNomeAbaAtiva() {
    const botaoAtivo = document.querySelector('.tablink.active[data-tab-target]');
    if (botaoAtivo) return botaoAtivo.dataset.tabTarget;

    const abaVisivel = Array.from(document.querySelectorAll('.tabcontent')).find(aba => !aba.hasAttribute('hidden'));
    return abaVisivel?.id || null;
}

function abrirAba(nomeAba, acionador) {
    document.querySelectorAll('.tabcontent').forEach(elementoDom => {
        elementoDom.style.display = 'none';
        elementoDom.setAttribute('hidden', '');
    });
    document.querySelectorAll('.tablink').forEach(elementoDom => {
        elementoDom.classList.remove('active');
        elementoDom.setAttribute('aria-selected', 'false');
        elementoDom.setAttribute('tabindex', '-1');
    });

    const aba = document.getElementById(nomeAba);
    if (aba) {
        aba.style.display = 'block';
        aba.removeAttribute('hidden');
    }

    const botaoSelecionado = acionador || document.querySelector(`.tablink[data-tab-target="${nomeAba}"]`);
    if (botaoSelecionado) {
        botaoSelecionado.classList.add('active');
        botaoSelecionado.setAttribute('aria-selected', 'true');
        botaoSelecionado.setAttribute('tabindex', '0');
    }
    localStorage.setItem('activeTab', nomeAba);
    window.renderizarAbaPendente?.();
}

function configurarControlesData() {
    const entrada = document.getElementById('selectedDate');
    const hoje = document.getElementById('btnToday');
    if (entrada) {
        // O input trabalha com yyyy-MM-dd; o estado global continua em dd-MM-yyyy para o Firebase.
        entrada.value = converterDataFirebaseParaEntrada(dataSelecionada);
        entrada.addEventListener('change', () => {
            dataSelecionada = converterDataEntradaParaFirebase(entrada.value);
            carregarDashboard();
        });
    }
    if (hoje) {
        hoje.addEventListener('click', () => {
            dataSelecionada = paraDataFirebase(new Date());
            if (entrada) entrada.value = converterDataFirebaseParaEntrada(dataSelecionada);
            carregarDashboard();
        });
    }
}

function configurarSecoesRecolhaveis() {
    document.querySelectorAll('.collapsible-section').forEach(secao => {
        const acionador = secao.querySelector('.collapsible-trigger');
        if (!acionador) return;
        acionador.addEventListener('click', () => {
            const recolhido = secao.classList.toggle('is-collapsed');
            acionador.setAttribute('aria-expanded', String(!recolhido));
            if (!recolhido) window.renderizarAbaPendente?.();
        });
    });
}

function configurarCabecalhoMovel() {
    const cabecalho = document.querySelector('.app-header');
    if (!cabecalho || cabecalho.dataset.autoHideReady === 'true') return;

    const consultaMovel = window.matchMedia('(max-width: 640px)');
    const limiteMovimento = 8;
    let posicaoReferencia = Math.max(0, window.scrollY);
    let quadroPendente = false;

    cabecalho.dataset.autoHideReady = 'true';

    function possuiPopoverAberto() {
        return !!cabecalho.querySelector('.header-status [aria-expanded="true"]');
    }

    function mostrarCabecalho() {
        cabecalho.classList.remove('is-hidden-on-scroll');
    }

    function atualizarCabecalho() {
        quadroPendente = false;
        const posicaoAtual = Math.max(0, window.scrollY);

        if (!consultaMovel.matches || possuiPopoverAberto() || posicaoAtual <= cabecalho.offsetHeight) {
            mostrarCabecalho();
            posicaoReferencia = posicaoAtual;
            return;
        }

        const deslocamento = posicaoAtual - posicaoReferencia;
        if (deslocamento >= limiteMovimento) {
            cabecalho.classList.add('is-hidden-on-scroll');
            posicaoReferencia = posicaoAtual;
        } else if (deslocamento <= -limiteMovimento) {
            mostrarCabecalho();
            posicaoReferencia = posicaoAtual;
        }
    }

    window.addEventListener('scroll', () => {
        if (quadroPendente) return;
        quadroPendente = true;
        window.requestAnimationFrame(atualizarCabecalho);
    }, { passive: true });
    window.addEventListener('header-popover-open', mostrarCabecalho);
    cabecalho.addEventListener('focusin', mostrarCabecalho);
    consultaMovel.addEventListener?.('change', () => {
        mostrarCabecalho();
        posicaoReferencia = Math.max(0, window.scrollY);
    });
}

function configurarEventosZoom() {
    document.addEventListener('keydown', evento => {
        if (evento.key === 'Escape') fecharAmpliacao();
    });
}


Object.assign(window, {
    configurarRelogio,
    configurarIndicadorAstronomico,
    configurarIndicadorAqi: configurarModulo,
    atualizarIndicadorAstronomico,
    atualizarIndicadorAstronomicoPublico,
    alternarPopoverAstronomico: alternarDetalhesAstronomicos,
    fecharPopoverAstronomico: fecharDetalhesAstronomicos,
    atualizarPopoverAstronomico: atualizarDetalhesAstronomicos,
    renderizarIndicadorAqi: atualizarModulo,
    obterEstadoAstronomico,
    obterEventosSolaresIndicador: obterEventosSolaresDataSelecionada,
    montarEstadoAstronomico,
    obterRotuloModoAstronomico,
    formatarDuracaoAstronomica,
    configurarAbas,
    configurarNavegacaoTouch: configurarDeslizeAbas,
    deveIgnorarSwipeAbas: deveIgnorarDeslizeAbas,
    obterAbaAtiva: obterNomeAbaAtiva,
    abrirAba,
    configurarControlesData,
    configurarSecoesRecolhaveis,
    configurarCabecalhoMovel,
    configurarEventosZoom
});
