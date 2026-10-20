'use strict';

(function () {
    const graficos = {};
    const HORAS_PREVISAO_GRAFICOS = 12;
    const GRUPO_PREVISAO_QUINZE_DIAS = 'publico-15-dias';
    const marcadorAgoraPlugin = {
        id: 'marcadorAgoraPublico',
        afterDraw(grafico) {
            const marcador = grafico.$marcadorAgora;
            const escalaX = grafico.scales?.x;
            const areaDesenho = grafico.chartArea;
            if (!marcador || !escalaX || !areaDesenho || marcador.indice < 0) return;

            const x = escalaX.getPixelForValue(marcador.indice);
            if (!Number.isFinite(x) || x < areaDesenho.left || x > areaDesenho.right) return;

            const contexto = grafico.ctx;
            contexto.save();
            contexto.strokeStyle = 'rgba(148, 163, 184, 0.65)';
            contexto.lineWidth = 1;
            contexto.setLineDash([4, 4]);
            contexto.beginPath();
            contexto.moveTo(x, areaDesenho.top);
            contexto.lineTo(x, areaDesenho.bottom);
            contexto.stroke();

            contexto.setLineDash([]);
            contexto.font = '600 10px sans-serif';
            contexto.textAlign = 'center';
            contexto.textBaseline = 'top';
            const larguraTexto = contexto.measureText('Agora').width;
            const centroSeguro = Math.min(Math.max(x, areaDesenho.left + larguraTexto / 2 + 7), areaDesenho.right - larguraTexto / 2 - 7);
            contexto.fillStyle = 'rgba(15, 23, 42, 0.88)';
            contexto.fillRect(centroSeguro - larguraTexto / 2 - 5, areaDesenho.top + 4, larguraTexto + 10, 17);
            contexto.fillStyle = '#cbd5e1';
            contexto.fillText('Agora', centroSeguro, areaDesenho.top + 7);
            contexto.restore();
        }
    };
    const previsaoEstendidaPlugin = {
        id: 'previsaoEstendidaPublica',
        beforeDraw(grafico) {
            const escalaX = grafico.scales?.x;
            const area = grafico.chartArea;
            if (!escalaX || !area || (grafico.data?.labels?.length || 0) < 8) return;

            const inicio = (escalaX.getPixelForValue(6) + escalaX.getPixelForValue(7)) / 2;
            const contexto = grafico.ctx;
            contexto.save();
            contexto.fillStyle = 'rgba(148, 163, 184, 0.045)';
            contexto.fillRect(inicio, area.top, area.right - inicio, area.bottom - area.top);
            contexto.strokeStyle = 'rgba(148, 163, 184, 0.38)';
            contexto.setLineDash([4, 4]);
            contexto.beginPath();
            contexto.moveTo(inicio, area.top);
            contexto.lineTo(inicio, area.bottom);
            contexto.stroke();
            contexto.restore();
        }
    };
    let elementos = null;
    let sequenciaBusca = 0;
    let consultaRestaurada = false;
    let modoBusca = 'cep';
    let repetirConsultaAtual = null;
    const valoresBusca = { cep: '', cidade: '' };

    function configurarModulo() {
        elementos = {
            publicApp: document.getElementById('publicApp'),
            privateApp: document.getElementById('privateApp'),
            resultados: document.getElementById('publicResults'),
            statusBusca: document.getElementById('publicSearchStatus'),
            formularioBusca: document.getElementById('publicSearchForm'),
            entradaBusca: document.getElementById('publicSearchInput'),
            rotuloBusca: document.getElementById('publicSearchLabel'),
            botaoBusca: document.getElementById('publicSearchButton'),
            botaoModoCep: document.getElementById('publicSearchModeCep'),
            botaoModoCidade: document.getElementById('publicSearchModeCity'),
            opcoesCidades: document.getElementById('publicCityResults'),
            botaoLocalizacao: document.getElementById('publicLocationButton'),
            statusUsuario: document.getElementById('publicUserStatus'),
            botaoLogin: document.getElementById('publicLoginButton'),
            botaoLogout: document.getElementById('authLogoutButton'),
            chat: document.getElementById('aiChat')
        };

        elementos.formularioBusca?.addEventListener('submit', buscarPorEntrada);
        elementos.entradaBusca?.addEventListener('input', evento => {
            if (modoBusca === 'cep') aplicarMascaraCep(evento.currentTarget);
            limparErroBusca();
            ocultarOpcoesCidades();
        });
        elementos.botaoModoCep?.addEventListener('click', () => alterarModoBusca('cep'));
        elementos.botaoModoCidade?.addEventListener('click', () => alterarModoBusca('cidade'));
        elementos.botaoLocalizacao?.addEventListener('click', buscarPorLocalizacao);
        elementos.botaoLogin?.addEventListener('click', entrar);
        elementos.botaoLogout?.addEventListener('click', () => window.ClimateAuth.sair());
        alterarModoBusca('cep', { focar: false });

        mostrarPublico(null);
        window.ClimateAuth.observarEstado(usuario => {
            if (window.ClimateAuth.ehUsuarioInterno(usuario)) {
                mostrarInterno();
            } else {
                mostrarPublico(usuario);
            }
        });
    }

    function mostrarPublico(usuario) {
        const estavaInterno = !elementos.privateApp?.hasAttribute('hidden');
        if (typeof dashboardAtual !== 'undefined') dashboardAtual = null;
        if (typeof graficosInstancias !== 'undefined') {
            Object.values(graficosInstancias).forEach(grafico => grafico?.destroy?.());
            graficosInstancias = {};
        }
        elementos.publicApp?.removeAttribute('hidden');
        elementos.privateApp?.setAttribute('hidden', '');
        elementos.chat?.setAttribute('hidden', '');
        elementos.botaoLogout?.setAttribute('hidden', '');
        atualizarUsuarioPublico(usuario);
        if (consultaRestaurada && !estavaInterno) return;
        renderizarEstado('Informe um CEP, pesquise uma cidade ou permita a localização para ver os dados climáticos.');
        atualizarAqiPublico(null);
        atualizarSolarPublico(null);
        if (!consultaRestaurada) {
            consultaRestaurada = true;
            restaurarUltimaConsulta();
        }
    }

    function mostrarInterno() {
        elementos.publicApp?.setAttribute('hidden', '');
        elementos.privateApp?.removeAttribute('hidden');
        elementos.chat?.removeAttribute('hidden');
        elementos.botaoLogout?.removeAttribute('hidden');
        window.inicializarPainelInterno?.();
    }

    function atualizarUsuarioPublico(usuario) {
        if (!elementos.statusUsuario) return;
        elementos.statusUsuario.innerHTML = '';
        const botao = document.createElement('button');
        botao.type = 'button';
        botao.textContent = usuario ? 'Sair' : 'Entrar com Google';
        botao.addEventListener('click', () => usuario ? window.ClimateAuth.sair() : entrar());
        if (usuario) {
            const texto = document.createElement('span');
            texto.textContent = `${usuario.email || 'usuário Google'} · acesso público`;
            elementos.statusUsuario.append(texto, botao);
            return;
        }
        elementos.statusUsuario.append(botao);
    }

    async function entrar() {
        try {
            await window.ClimateAuth.entrarComGoogle();
        } catch (erro) {
            window.alert(obterMensagemErroLogin(erro));
        }
    }

    function obterMensagemErroLogin(erro) {
        const codigo = erro?.code || '';
        if (codigo === 'auth/unauthorized-domain') {
            return 'Domínio não autorizado no Firebase Auth. Adicione anvmano.github.io em Authentication > Configurações > Domínios autorizados.';
        }
        if (codigo === 'auth/popup-blocked') {
            return 'O navegador bloqueou a janela de login. Permita pop-ups para este site e tente novamente.';
        }
        if (codigo === 'auth/popup-closed-by-user' || codigo === 'auth/cancelled-popup-request') {
            return 'Login com Google cancelado antes de concluir.';
        }
        if (codigo === 'auth/operation-not-allowed') {
            return 'Login com Google não está habilitado no Firebase Authentication.';
        }
        return 'Não foi possível entrar com Google agora. Verifique as configurações do Firebase Auth.';
    }

    async function buscarPorEntrada(evento) {
        evento.preventDefault();
        if (modoBusca === 'cidade') {
            await pesquisarPorCidade();
            return;
        }

        await buscarPorCep();
    }

    async function buscarPorCep() {
        const cep = elementos.entradaBusca.value.replace(/\D/g, '');
        if (cep.length !== 8) {
            marcarErroBusca('Informe um CEP com 8 dígitos.');
            renderizarEstado('Informe um CEP com 8 dígitos.', true);
            return;
        }
        limparErroBusca();
        await executarBusca(`/api/public-weather/cep/${cep}`);
    }

    async function pesquisarPorCidade() {
        const termo = normalizarTermoCidade(elementos.entradaBusca?.value);
        if (termo.length < 2) {
            const mensagem = 'Informe pelo menos 2 caracteres para pesquisar a cidade.';
            marcarErroBusca(mensagem);
            renderizarEstado(mensagem, true);
            return;
        }

        const idBusca = iniciarBusca('Procurando cidades...');
        try {
            const cidades = await requisitarJson(`/api/public-weather/cities?term=${encodeURIComponent(termo)}`);
            if (idBusca !== sequenciaBusca) return;

            if (cidades.length === 1) {
                repetirConsultaAtual = () => requisitarClimaCidade(cidades[0]);
                const dados = await requisitarClimaCidade(cidades[0]);
                concluirBusca(dados, idBusca);
                return;
            }

            renderizarEstado('Escolha a cidade correta para continuar.');
            renderizarOpcoesCidades(cidades);
            anunciarEstado(`${cidades.length} cidades encontradas. Escolha uma opção.`);
        } catch (erro) {
            tratarErroBusca(erro, idBusca, true);
        } finally {
            if (idBusca === sequenciaBusca) definirEstadoBusca(false);
        }
    }

    async function selecionarCidade(cidade) {
        valoresBusca.cidade = cidade?.nome || cidade?.rotulo || '';
        if (elementos.entradaBusca) elementos.entradaBusca.value = valoresBusca.cidade;
        const idBusca = iniciarBusca('Consultando clima da localização...');
        try {
            repetirConsultaAtual = () => requisitarClimaCidade(cidade);
            const dados = await requisitarClimaCidade(cidade);
            concluirBusca(dados, idBusca);
        } catch (erro) {
            tratarErroBusca(erro, idBusca, true);
        } finally {
            if (idBusca === sequenciaBusca) definirEstadoBusca(false);
        }
    }

    async function buscarPorLocalizacao() {
        if (!navigator.geolocation) {
            renderizarEstado('Geolocalização não está disponível neste navegador.', true);
            return;
        }

        const idBusca = iniciarBusca('Obtendo sua localização...');
        try {
            const posicao = await obterLocalizacaoAtual();
            if (idBusca !== sequenciaBusca) return;
            const url = `/api/public-weather/location?latitude=${encodeURIComponent(posicao.coords.latitude)}&longitude=${encodeURIComponent(posicao.coords.longitude)}&precisao=${encodeURIComponent(posicao.coords.accuracy)}`;
            repetirConsultaAtual = () => requisitarJson(url);
            const dados = await requisitarJson(url);
            concluirBusca(dados, idBusca);
        } catch (erro) {
            tratarErroBusca(erro, idBusca);
        } finally {
            if (idBusca === sequenciaBusca) definirEstadoBusca(false);
        }
    }

    async function obterLocalizacaoAtual() {
        const tentativas = [
            { enableHighAccuracy: false, timeout: 3000, maximumAge: 3600000 },
            { enableHighAccuracy: false, timeout: 25000, maximumAge: 900000 },
            { enableHighAccuracy: true, timeout: 30000, maximumAge: 0 }
        ];
        let ultimoErro = null;
        for (const opcoes of tentativas) {
            try {
                return await new Promise((resolver, rejeitar) => navigator.geolocation.getCurrentPosition(resolver, rejeitar, opcoes));
            } catch (erro) {
                ultimoErro = erro;
                if (erro.code === 1) break;
            }
        }
        const mensagens = { 1: 'Permissão de localização negada.', 2: 'Localização indisponível no momento.', 3: 'Tempo esgotado ao obter localização. Tente novamente ou informe o CEP.' };
        throw new Error(mensagens[ultimoErro?.code] || 'Não foi possível obter a localização.');
    }

    async function executarBusca(url) {
        const idBusca = iniciarBusca('Consultando clima e qualidade do ar...');
        try {
            repetirConsultaAtual = () => requisitarJson(url);
            const dados = await requisitarJson(url);
            concluirBusca(dados, idBusca);
        } catch (erro) {
            tratarErroBusca(erro, idBusca, true);
        } finally {
            if (idBusca === sequenciaBusca) definirEstadoBusca(false);
        }
    }

    function iniciarBusca(mensagem) {
        const idBusca = ++sequenciaBusca;
        limparErroBusca();
        ocultarOpcoesCidades();
        definirEstadoBusca(true);
        renderizarEstado(mensagem);
        return idBusca;
    }

    async function requisitarClimaCidade(cidade) {
        return requisitarJson('/api/public-weather/city', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(cidade)
        });
    }

    async function requisitarJson(url, opcoes) {
        const resposta = await fetch(url, opcoes);
        const dados = await resposta.json().catch(() => null);
        if (!resposta.ok) {
            throw new Error(dados?.message || dados?.mensagem || 'Não foi possível consultar o clima externo.');
        }
        return dados;
    }

    function concluirBusca(dados, idBusca) {
        if (idBusca !== sequenciaBusca) return false;
        renderizarResultado(dados);
        preservarUltimaConsulta(dados);
        anunciarEstado(`Dados climáticos carregados para ${dados.origem?.rotulo || 'a localização'}.`);
        return true;
    }

    function tratarErroBusca(erro, idBusca, marcarEntradaEmErro = false) {
        if (idBusca !== sequenciaBusca) return;
        const mensagem = erro?.message || 'Não foi possível consultar o clima externo.';
        if (marcarEntradaEmErro) marcarErroBusca(mensagem);
        renderizarEstado(mensagem, true);
    }

    function definirEstadoBusca(carregando) {
        [
            elementos.botaoLocalizacao,
            elementos.botaoBusca,
            elementos.entradaBusca,
            elementos.botaoModoCep,
            elementos.botaoModoCidade
        ].forEach(controle => {
            if (controle) controle.disabled = carregando;
        });
        elementos.resultados?.setAttribute('aria-busy', String(carregando));
    }

    function renderizarEstado(mensagem, erro = false) {
        limparGraficos();
        elementos.publicApp?.classList.remove('has-results');
        elementos.resultados.innerHTML = `<p class="state-message${erro ? ' error-message' : ''}">${escapar(mensagem)}</p>`;
        atualizarAqiPublico(null);
        atualizarSolarPublico(null);
        anunciarEstado(mensagem, erro ? 'alert' : 'status');
    }

    function alterarModoBusca(novoModo, { focar = true, preservarValor = true } = {}) {
        if (!elementos.entradaBusca || !['cep', 'cidade'].includes(novoModo)) return;
        if (preservarValor) valoresBusca[modoBusca] = elementos.entradaBusca.value;
        modoBusca = novoModo;

        const buscandoCep = modoBusca === 'cep';
        elementos.botaoModoCep?.setAttribute('aria-pressed', String(buscandoCep));
        elementos.botaoModoCidade?.setAttribute('aria-pressed', String(!buscandoCep));
        elementos.botaoModoCep?.classList.toggle('active', buscandoCep);
        elementos.botaoModoCidade?.classList.toggle('active', !buscandoCep);
        elementos.rotuloBusca.textContent = buscandoCep ? 'CEP' : 'Cidade';
        elementos.entradaBusca.value = valoresBusca[modoBusca];
        elementos.entradaBusca.placeholder = buscandoCep ? '00000-000' : 'Ex.: Campinas, SP';
        elementos.entradaBusca.inputMode = buscandoCep ? 'numeric' : 'search';
        elementos.entradaBusca.autocomplete = buscandoCep ? 'postal-code' : 'address-level2';
        elementos.entradaBusca.maxLength = buscandoCep ? 9 : 80;
        elementos.entradaBusca.setAttribute('aria-label', buscandoCep ? 'CEP' : 'Cidade');

        limparErroBusca();
        ocultarOpcoesCidades();
        if (focar) elementos.entradaBusca.focus();
    }

    function renderizarOpcoesCidades(cidades) {
        const recipiente = elementos.opcoesCidades;
        if (!recipiente) return;
        recipiente.replaceChildren();

        const titulo = document.createElement('span');
        titulo.className = 'public-city-results__title';
        titulo.textContent = 'Escolha a localização';
        const lista = document.createElement('div');
        lista.className = 'public-city-results__list';
        lista.setAttribute('role', 'list');
        cidades.forEach(cidade => {
            const item = document.createElement('div');
            item.setAttribute('role', 'listitem');
            const botao = document.createElement('button');
            botao.type = 'button';
            botao.className = 'public-city-option';
            botao.textContent = cidade.rotulo;
            botao.setAttribute('aria-label', `Consultar clima de ${cidade.rotulo}`);
            botao.addEventListener('click', () => selecionarCidade(cidade));
            item.appendChild(botao);
            lista.appendChild(item);
        });
        recipiente.append(titulo, lista);
        recipiente.hidden = false;
        requestAnimationFrame(() => lista.querySelector('button')?.focus());
    }

    function ocultarOpcoesCidades() {
        if (!elementos.opcoesCidades) return;
        elementos.opcoesCidades.hidden = true;
        elementos.opcoesCidades.replaceChildren();
    }

    function anunciarEstado(mensagem, papel = 'status') {
        if (!elementos.statusBusca) return;
        elementos.statusBusca.setAttribute('role', papel === 'alert' ? 'alert' : 'status');
        elementos.statusBusca.textContent = mensagem || '';
    }

    function marcarErroBusca(mensagem) {
        elementos.entradaBusca?.setAttribute('aria-invalid', 'true');
        elementos.entradaBusca?.setAttribute('aria-describedby', 'publicSearchStatus');
        anunciarEstado(mensagem, 'alert');
    }

    function limparErroBusca() {
        elementos.entradaBusca?.removeAttribute('aria-invalid');
        elementos.entradaBusca?.removeAttribute('aria-describedby');
    }

    function normalizarTermoCidade(valor) {
        return String(valor || '').trim().replace(/\s+/g, ' ');
    }

    function renderizarResultado(dados) {
        limparGraficos();
        const atualizado = novaData(dados.atualizadoEm);
        const dataFirebase = atualizado ? `${preencherDigitos(atualizado.getDate())}-${preencherDigitos(atualizado.getMonth() + 1)}-${atualizado.getFullYear()}` : paraDataFirebase(new Date());
        const estacao = window.ClimateSeason?.getState?.();
        const lua = window.ClimateMoon?.getState?.(dataFirebase);
        const aqi = classificarAqi(dados.aqi?.valor);
        const atualidade = calcularAtualidade(dados.atualizadoEm);

        elementos.resultados.innerHTML = `
            <div class="public-location ${atualidade.desatualizado ? 'is-stale' : ''}">
                <div><span>${escapar(dados.origem?.rotulo || 'Localização atual')}</span><strong>Atualizado ${formatarDataHora(atualizado)} · ${atualidade.rotulo}</strong></div>
                <button class="public-change-location" type="button">Alterar local</button>
            </div>
            <div class="stats-grid public-stats-grid">
                ${cartao('Temperatura', dados.climaAtual?.temperatura, '°C', 'Dado externo')}
                ${cartao('Sensação', dados.climaAtual?.sensacaoTermica, '°C', 'Dado externo')}
                ${cartao('Umidade', dados.climaAtual?.umidade, '%', 'Dado externo')}
                ${cartao('Pressão', dados.climaAtual?.pressao, 'hPa', 'Dado externo')}
                ${cartaoAqi(dados.aqi?.valor, aqi.nome)}
            </div>
            <div class="station-context-row">
                <section class="season-timeline public-season" id="publicSeasonTimeline">${renderizarEstacao(estacao)}</section>
                <section class="moon-summary public-moon" id="publicMoonSummary">${renderizarLua(lua)}</section>
            </div>
            ${montarInsightsPublicos(dados.insights)}
            <div class="charts-grid public-metric-charts" tabindex="0" aria-label="Gráficos climáticos; deslize horizontalmente para alternar no celular">
                ${cartaoTelaGrafico('publicChartTemperature', 'Temperatura', 'Temperatura externa')}
                ${cartaoTelaGrafico('publicChartFeelsLike', 'Sensação Térmica', 'Sensação térmica externa')}
                ${cartaoTelaGrafico('publicChartHumidity', 'Umidade', 'Umidade externa')}
                ${cartaoTelaGrafico('publicChartPressure', 'Pressão', 'Pressão externa')}
                <div class="chart-card" id="public-rain-container"><span class="chart-label">Precipitação · 24h + previsão 12h</span><span class="chart-rain-status" id="publicRainStatus"></span><canvas id="publicChartRain" class="plot" role="img" aria-label="Precipitação pública em milímetros"></canvas></div>
                ${cartaoTelaGrafico('publicChartRainProbability', 'Chance de chuva · 24h + previsão 12h', 'Chance de chuva pública em porcentagem')}
            </div>
            ${montarSecaoPrevisaoQuinzeDias(dados.previsaoQuinzeDias)}
            <div class="chart-card chart-card--wide" id="public-solar-container"><span class="chart-label">Ciclo Solar do Dia</span><span class="chart-card__meta-chip" id="publicSolarDuration" hidden></span><canvas id="publicChartSolar" class="plot plot--solar-day" role="img" aria-label="Ciclo solar público"></canvas></div>`;

        elementos.publicApp?.classList.add('has-results');
        elementos.resultados.querySelector('.public-change-location')?.addEventListener('click', () => {
            elementos.publicApp?.classList.remove('has-results');
            document.querySelector('.public-hero')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
            window.setTimeout(() => elementos.entradaBusca?.focus(), 320);
        });

        const janelaGraficos = montarJanelaObservadaEPrevista(dados.seriesHorarias, dados.atualizadoEm);
        renderizarGraficoLinha('publicChartTemperature', janelaGraficos, 'temperatura', '°C', '#38bdf8');
        renderizarGraficoLinha('publicChartFeelsLike', janelaGraficos, 'sensacaoTermica', '°C', '#34d399');
        renderizarGraficoLinha('publicChartHumidity', janelaGraficos, 'umidade', '%', '#a78bfa');
        renderizarGraficoLinha('publicChartPressure', janelaGraficos, 'pressao', 'hPa', '#fbbf24');
        renderizarGraficoChuva(dados);
        renderizarPrevisaoQuinzeDias(dados.previsaoQuinzeDias);
        criarGraficoSolarDia(dados.cicloSolar);
        elementos.resultados.querySelectorAll('.chart-card').forEach(cartao => garantirCartaoComZoom(cartao));
        atualizarChipDuracaoSolar(dados.cicloSolar);
        atualizarAqiPublico(dados.aqi, dados.origem?.rotulo, atualizado);
        atualizarSolarPublico(dados.cicloSolar, dados.origem?.rotulo);
        window.ClimateMoon?.update?.(dataFirebase);
        if (elementos.statusBusca) elementos.statusBusca.textContent = `Dados climáticos carregados para ${dados.origem?.rotulo || 'a localização'}.`;
    }

    function aplicarMascaraCep(entrada) {
        const digitos = String(entrada?.value || '').replace(/\D/g, '').slice(0, 8);
        entrada.value = digitos.length > 5 ? `${digitos.slice(0, 5)}-${digitos.slice(5)}` : digitos;
    }

    function calcularAtualidade(atualizadoEm) {
        const data = novaData(atualizadoEm);
        if (!data) return { desatualizado: true, rotulo: 'horário indisponível' };
        const minutos = Math.max(0, Math.floor((Date.now() - data.getTime()) / 60000));
        const limite = Number(window.AppConfig?.publicData?.staleAfterMinutes) || 20;
        if (minutos < 1) return { desatualizado: false, rotulo: 'agora' };
        return {
            desatualizado: minutos > limite,
            rotulo: minutos > limite ? `há ${minutos} min · desatualizado` : `há ${minutos} min`
        };
    }

    function preservarUltimaConsulta(dados) {
        try {
            const copia = JSON.parse(JSON.stringify(dados || {}));
            if (copia.origem) {
                ['latitude', 'longitude', 'precisao', 'cep'].forEach(chave => delete copia.origem[chave]);
            }
            const chave = window.AppConfig?.publicData?.sessionKey || 'climatePublicLastQueryV1';
            sessionStorage.setItem(chave, JSON.stringify({ salvoEm: Date.now(), dados: copia }));
        } catch {
            // A consulta continua válida quando o armazenamento de sessão não está disponível.
        }
    }

    function restaurarUltimaConsulta() {
        try {
            const chave = window.AppConfig?.publicData?.sessionKey || 'climatePublicLastQueryV1';
            const salvo = JSON.parse(sessionStorage.getItem(chave) || 'null');
            const idadeMinutos = (Date.now() - Number(salvo?.salvoEm || 0)) / 60000;
            const maximo = Number(window.AppConfig?.publicData?.maxAgeMinutes) || 60;
            if (!salvo?.dados || !Number.isFinite(idadeMinutos) || idadeMinutos > maximo) {
                sessionStorage.removeItem(chave);
                return;
            }
            renderizarResultado(salvo.dados);
            if (elementos.statusBusca) {
                elementos.statusBusca.setAttribute('role', 'status');
                elementos.statusBusca.textContent = 'Última consulta desta sessão restaurada.';
            }
        } catch {
            // Cache ausente ou inválido é ignorado.
        }
    }

    function montarInsightsPublicos(analisesAmbientais) {
        const ventilacao = analisesAmbientais?.ventilacao || criarResultadoIndisponivel('Sem dados externos suficientes para recomendar ventilação.');
        const chuva = analisesAmbientais?.chuva || criarResultadoIndisponivel('Previsão de chuva indisponível para as próximas 6h.');
        const indiceUv = analisesAmbientais?.indiceUv || criarResultadoIndisponivel('Índice UV indisponível para esta localização.');
        const riscoMofo = analisesAmbientais?.riscoMofo || criarResultadoIndisponivel('Sem dados suficientes para estimar o risco de mofo.');
        const chuvaDisponivel = chuva.classe !== 'indisponivel';
        const uvDisponivel = indiceUv.classe !== 'indisponivel';
        const detalheUv = uvDisponivel && Number.isFinite(indiceUv.maximo)
            ? `Máxima do dia: ${indiceUv.maximo.toFixed(1)}${indiceUv.usaMaximoComoFallback ? ' · usando máxima diária' : ''}`
            : 'Valor UV indisponível';

        return `<section class="environment-insights" aria-label="Condições e recomendações da localização">
            <div class="environment-insights__grid" tabindex="0" aria-label="Recomendações ambientais; deslize horizontalmente para alternar no celular">
                ${cartaoAnalise('Ventilação', ventilacao.rotulo, 'Agora', ventilacao, 'Baseado em clima, chuva e AQI externos')}
                ${cartaoAnalise('Chuva · próximas 6h', chuvaDisponivel ? `${Math.round(chuva.probabilidade)}%` : '--', chuva.rotulo, chuva,
                    chuvaDisponivel ? `${decimal(chuva.acumulado)} mm acumulados · pico ${decimal(chuva.intensidadeMaxima)} mm/h` : 'Sem previsão horária')}
                ${cartaoAnalise('Índice UV', uvDisponivel ? Number(indiceUv.valor).toFixed(1) : '--', indiceUv.rotulo, indiceUv, detalheUv)}
                ${cartaoAnalise('Ponto de orvalho', Number.isFinite(riscoMofo.pontoOrvalho) ? `${riscoMofo.pontoOrvalho.toFixed(1)}°C` : '--', riscoMofo.rotulo, riscoMofo,
                    'Risco estimado de condensação e mofo')}
            </div>
        </section>`;
    }

    function cartaoAnalise(titulo, valor, status, resultado, detalhe) {
        return `<article class="environment-insight environment-insight--${escapar(resultado.classe)}">
            <div class="environment-insight__header"><span>${escapar(titulo)}</span><small>${escapar(status)}</small></div>
            <strong>${escapar(valor)}</strong>
            <p>${escapar(resultado.descricao)}</p>
            <div class="environment-insight__footer"><span class="environment-insight__detail">${escapar(detalhe)}</span></div>
        </article>`;
    }

    function criarResultadoIndisponivel(descricao) {
        return { rotulo: 'Indisponível', classe: 'indisponivel', descricao };
    }

    function decimal(valor) {
        return Number.isFinite(Number(valor)) ? Number(valor).toFixed(1) : '--';
    }

    function cartao(titulo, valor, unidade, detalhe) {
        const texto = numeroValido(valor) !== null ? `${Number(valor).toFixed(unidade ? 2 : 0)}${unidade}` : '--';
        return `<article class="stats-card public-card"><span class="stats-card__label">${titulo}</span><strong class="stats-card__value">${texto}</strong><span class="public-card__meta">${escapar(detalhe)}</span></article>`;
    }

    function cartaoAqi(valor, categoria) {
        const texto = numeroValido(valor) !== null ? Math.round(Number(valor)) : '--';
        return `<article class="stats-card public-card public-card--aqi"><span class="stats-card__label">AQI externo</span><strong class="stats-card__value">${texto}</strong><span class="stats-card__trend">${escapar(categoria)}</span></article>`;
    }

    function cartaoTelaGrafico(id, titulo, aria) {
        return `<div class="chart-card"><span class="chart-label">${titulo}</span><canvas id="${id}" class="plot" role="img" aria-label="${aria}"></canvas></div>`;
    }

    function montarSecaoPrevisaoQuinzeDias(previsao) {
        const dias = Array.isArray(previsao) ? previsao.slice(0, 15) : [];
        const descricao = dias.length === 15
            ? 'A partir do 8º dia, a previsão possui maior incerteza'
            : dias.length
                ? `Resposta parcial: ${dias.length} de 15 dias disponíveis`
                : 'Previsão de 15 dias indisponível';
        const conteudo = dias.length
            ? `<div class="public-forecast-map" id="publicForecastMap" role="group" aria-label="Resumo diário de temperatura máxima, chuva e índice UV"></div>
               <div class="charts-grid public-forecast-charts" tabindex="0" aria-label="Gráficos da previsão de 15 dias; deslize horizontalmente para alternar no celular">
                   ${cartaoTelaGrafico('publicChartForecastTemperature', 'Temperatura mínima e máxima', 'Temperaturas mínima e máxima previstas para 15 dias')}
                   ${cartaoTelaGrafico('publicChartForecastRain', 'Chuva', 'Probabilidade e volume de chuva previstos para 15 dias')}
                   ${cartaoTelaGrafico('publicChartForecastUv', 'Índice UV', 'Índice UV máximo previsto para 15 dias')}
               </div>`
            : `<div class="state-message public-forecast__state"><span>Os dados atuais continuam disponíveis. Tente consultar a previsão novamente.</span><button type="button" class="toolbar-button" data-retry-forecast>Tentar novamente</button></div>`;

        return `<section class="public-forecast" aria-labelledby="publicForecastTitle">
            <div class="public-forecast__header"><h2 id="publicForecastTitle">Previsão para os próximos 15 dias</h2><span>${descricao}</span></div>
            ${conteudo}
        </section>`;
    }

    function renderizarPrevisaoQuinzeDias(previsao) {
        const dias = Array.isArray(previsao) ? previsao.slice(0, 15) : [];
        if (!dias.length) {
            configurarNovaTentativaPrevisao();
            return;
        }

        renderizarMapaPrevisaoQuinzeDias(dias);
        renderizarGraficoTemperaturaQuinzeDias(dias);
        renderizarGraficoChuvaQuinzeDias(dias);
        renderizarGraficoUvQuinzeDias(dias);
        configurarInteracaoMapaPrevisao(dias);
    }

    function configurarNovaTentativaPrevisao() {
        const botao = elementos.resultados?.querySelector('[data-retry-forecast]');
        if (!botao || !repetirConsultaAtual) {
            if (botao) botao.disabled = true;
            return;
        }

        botao.addEventListener('click', async () => {
            botao.disabled = true;
            botao.textContent = 'Consultando...';
            try {
                const dados = await repetirConsultaAtual();
                renderizarResultado(dados);
                preservarUltimaConsulta(dados);
                anunciarEstado('Nova consulta concluída.');
            } catch (erro) {
                botao.disabled = false;
                botao.textContent = 'Tentar novamente';
                anunciarEstado(erro?.message || 'Não foi possível atualizar a previsão.', 'alert');
            }
        });
    }

    function renderizarMapaPrevisaoQuinzeDias(dias) {
        const mapa = document.getElementById('publicForecastMap');
        if (!mapa) return;

        mapa.innerHTML = `<div class="public-forecast-map__labels" aria-hidden="true"><span>Data</span><span>Máx.</span><span>Chuva</span><span>UV</span></div>
            <div class="public-forecast-map__days">${dias.map((dia, indice) => {
                const maxima = numeroValido(dia.temperaturaMaxima);
                const chuva = numeroValido(dia.probabilidadeChuva);
                const uv = numeroValido(dia.indiceUvMaximo);
                const maiorIncerteza = dia.maiorIncerteza || indice >= 7;
                const rotulo = `${formatarDataCurta(dia.data)}: máxima ${formatarValor(maxima, '°C', 1)}, chuva ${formatarValor(chuva, '%', 0)}, UV ${formatarValor(uv, '', 1)}${maiorIncerteza ? ', maior incerteza' : ''}`;
                return `<button class="public-forecast-day${maiorIncerteza ? ' public-forecast-day--extended' : ''}" type="button" data-forecast-index="${indice}" aria-label="${rotulo}">
                    <strong>${formatarDataCurta(dia.data)}</strong>
                    <span class="public-forecast-day__temperature" style="--nivel: ${normalizarNivel(maxima, 18, 40)}">${formatarValor(maxima, '°', 0)}</span>
                    <span class="public-forecast-day__rain" style="--nivel: ${normalizarNivel(chuva, 0, 100)}">${formatarValor(chuva, '%', 0)}</span>
                    <span class="public-forecast-day__uv" style="--nivel: ${normalizarNivel(uv, 0, 11)}">${formatarValor(uv, '', 1)}</span>
                </button>`;
            }).join('')}</div>`;
    }

    function renderizarGraficoTemperaturaQuinzeDias(dias) {
        const canvas = document.getElementById('publicChartForecastTemperature');
        if (!canvas) return;
        const grafico = new Chart(canvas, {
            type: 'line',
            data: {
                labels: dias.map(dia => formatarDataCurta(dia.data)),
                datasets: [
                    criarSerieTemperaturaPrevisao('Máxima', dias.map(dia => dia.temperaturaMaxima), cores.amber, false),
                    criarSerieTemperaturaPrevisao('Mínima', dias.map(dia => dia.temperaturaMinima), cores.blue, true)
                ]
            },
            options: criarOpcoesPrevisaoQuinzeDias('°C', 1, false, true),
            plugins: [previsaoEstendidaPlugin]
        });
        registrarGraficoQuinzeDias('publicChartForecastTemperature', grafico, dias);
    }

    function criarSerieTemperaturaPrevisao(rotulo, dados, cor, preencher) {
        return {
            label: rotulo,
            data: dados,
            borderColor: cor,
            backgroundColor: corComTransparencia(cor, 0.1),
            fill: preencher ? '-1' : false,
            tension: 0.3,
            pointRadius: 3,
            pointHitRadius: 18,
            spanGaps: false,
            segment: {
                borderColor: contexto => contexto.p0DataIndex >= 6 ? corComTransparencia(cor, 0.5) : cor,
                borderDash: contexto => contexto.p0DataIndex >= 6 ? [5, 4] : undefined
            }
        };
    }

    function renderizarGraficoChuvaQuinzeDias(dias) {
        const canvas = document.getElementById('publicChartForecastRain');
        if (!canvas) return;
        const grafico = new Chart(canvas, {
            type: 'bar',
            data: {
                labels: dias.map(dia => formatarDataCurta(dia.data)),
                datasets: [
                    {
                        label: 'Volume', data: dias.map(dia => dia.precipitacao), yAxisID: 'yMilimetros',
                        backgroundColor: dias.map((_, indice) => corComTransparencia(cores.blue, indice >= 7 ? 0.28 : 0.56)),
                        borderColor: cores.blue, borderWidth: 1, borderRadius: 3, order: 2
                    },
                    {
                        type: 'line', label: 'Probabilidade', data: dias.map(dia => dia.probabilidadeChuva), yAxisID: 'yProbabilidade',
                        borderColor: cores.purple, backgroundColor: 'transparent', tension: 0.3, pointRadius: 3, pointHitRadius: 18, order: 1,
                        segment: {
                            borderColor: contexto => contexto.p0DataIndex >= 6 ? corComTransparencia(cores.purple, 0.5) : cores.purple,
                            borderDash: contexto => contexto.p0DataIndex >= 6 ? [5, 4] : undefined
                        }
                    }
                ]
            },
            options: criarOpcoesChuvaQuinzeDias(),
            plugins: [previsaoEstendidaPlugin]
        });
        registrarGraficoQuinzeDias('publicChartForecastRain', grafico, dias);
    }

    function renderizarGraficoUvQuinzeDias(dias) {
        const canvas = document.getElementById('publicChartForecastUv');
        if (!canvas) return;
        const grafico = new Chart(canvas, {
            type: 'bar',
            data: {
                labels: dias.map(dia => formatarDataCurta(dia.data)),
                datasets: [{
                    label: 'UV máximo', data: dias.map(dia => dia.indiceUvMaximo),
                    backgroundColor: dias.map((_, indice) => corComTransparencia(cores.amber, indice >= 7 ? 0.33 : 0.66)),
                    borderColor: cores.amber, borderWidth: 1, borderRadius: 3
                }]
            },
            options: criarOpcoesPrevisaoQuinzeDias('', 1, true),
            plugins: [previsaoEstendidaPlugin]
        });
        registrarGraficoQuinzeDias('publicChartForecastUv', grafico, dias);
    }

    function registrarGraficoQuinzeDias(id, grafico, dias) {
        grafico.$chavesSincronizacao = dias.map(dia => dia.data);
        grafico.$zoomPlugins = [previsaoEstendidaPlugin];
        grafico.$zoomKind = 'publicForecast';
        graficos[id] = grafico;
        window.SincronizadorGraficos?.registrar(grafico, GRUPO_PREVISAO_QUINZE_DIAS);
        grafico.update('none');
    }

    function criarOpcoesPrevisaoQuinzeDias(unidade, casasDecimais, iniciarEmZero = false, ordenarTooltip = false) {
        return adicionarInteracaoMapaPrevisao(mesclarRecursivamente(padroesGrafico, {
            interaction: { mode: 'index', intersect: false, axis: 'x' },
            plugins: {
                legend: { display: true, labels: { color: cores.text, boxWidth: 18, boxHeight: 2, padding: 12 } },
                tooltip: {
                    itemSort: ordenarTooltip ? ((a, b) => b.parsed.y - a.parsed.y) : undefined,
                    callbacks: { label: contexto => `${contexto.dataset.label}: ${Number(contexto.parsed.y).toFixed(casasDecimais)}${unidade}` }
                }
            },
            scales: {
                x: { ticks: { maxTicksLimit: limiteTicksQuinzeDias() } },
                y: { beginAtZero: iniciarEmZero, title: { display: Boolean(unidade), text: unidade, color: cores.text } }
            }
        }));
    }

    function criarOpcoesChuvaQuinzeDias() {
        const opcoes = mesclarRecursivamente(padroesGrafico, {
            interaction: { mode: 'index', intersect: false, axis: 'x' },
            plugins: {
                legend: { display: true, labels: { color: cores.text, boxWidth: 12, padding: 12 } },
                tooltip: { callbacks: { label: contexto => contexto.dataset.yAxisID === 'yProbabilidade'
                    ? `${contexto.dataset.label}: ${Number(contexto.parsed.y).toFixed(0)}%`
                    : `${contexto.dataset.label}: ${Number(contexto.parsed.y).toFixed(1)} mm` } }
            },
            scales: {
                x: { ticks: { maxTicksLimit: limiteTicksQuinzeDias() } },
                yMilimetros: { type: 'linear', position: 'left', beginAtZero: true, title: { display: true, text: 'mm', color: cores.text }, ticks: { color: cores.text, callback: valor => `${valor} mm` }, grid: { color: cores.grid } },
                yProbabilidade: { type: 'linear', position: 'right', min: 0, max: 100, title: { display: true, text: '%', color: cores.text }, ticks: { color: cores.text, callback: valor => `${valor}%` }, grid: { drawOnChartArea: false } }
            }
        });
        delete opcoes.scales.y;
        return adicionarInteracaoMapaPrevisao(opcoes);
    }

    function adicionarInteracaoMapaPrevisao(opcoes) {
        const tratar = (evento, elementosAtivos, grafico) => {
            window.SincronizadorGraficos?.tratarInteracao(grafico, evento, elementosAtivos);
            destacarDiaPrevisao(elementosAtivos?.[0]?.index ?? -1);
        };
        opcoes.onHover = tratar;
        opcoes.onClick = tratar;
        return opcoes;
    }

    function configurarInteracaoMapaPrevisao(dias) {
        const mapa = document.getElementById('publicForecastMap');
        if (!mapa) return;
        const botoes = [...mapa.querySelectorAll('[data-forecast-index]')];
        botoes.forEach(botao => {
            const ativar = () => {
                const indice = Number(botao.dataset.forecastIndex);
                destacarDiaPrevisao(indice);
                window.SincronizadorGraficos?.sincronizarIndice(GRUPO_PREVISAO_QUINZE_DIAS, indice, dias[indice]?.data);
            };
            botao.addEventListener('pointerenter', ativar);
            botao.addEventListener('focus', ativar);
            botao.addEventListener('click', ativar);
            botao.addEventListener('keydown', evento => {
                const destinos = { ArrowLeft: Number(botao.dataset.forecastIndex) - 1, ArrowRight: Number(botao.dataset.forecastIndex) + 1, Home: 0, End: botoes.length - 1 };
                if (!(evento.key in destinos)) return;
                evento.preventDefault();
                botoes[Math.max(0, Math.min(botoes.length - 1, destinos[evento.key]))]?.focus();
            });
        });
        mapa.addEventListener('pointerleave', limparDestaquePrevisao);
        mapa.addEventListener('focusout', evento => {
            if (!mapa.contains(evento.relatedTarget)) limparDestaquePrevisao();
        });
    }

    function destacarDiaPrevisao(indice) {
        document.querySelectorAll('#publicForecastMap [data-forecast-index]').forEach(botao => {
            botao.classList.toggle('is-active', Number(botao.dataset.forecastIndex) === indice);
        });
    }

    function limparDestaquePrevisao() {
        destacarDiaPrevisao(-1);
        window.SincronizadorGraficos?.limparGrupo(GRUPO_PREVISAO_QUINZE_DIAS);
    }

    function limiteTicksQuinzeDias() {
        return window.matchMedia?.('(max-width: 600px)')?.matches ? 5 : 15;
    }

    function normalizarNivel(valor, minimo, maximo) {
        const numero = numeroValido(valor);
        if (numero === null) return 0;
        return Math.max(0, Math.min(1, (numero - minimo) / (maximo - minimo))).toFixed(2);
    }

    function formatarValor(valor, unidade, casasDecimais) {
        return valor === null ? '--' : `${valor.toFixed(casasDecimais)}${unidade}`;
    }

    function formatarDataCurta(valor) {
        const data = novaData(`${String(valor).slice(0, 10)}T12:00:00`);
        return data ? `${preencherDigitos(data.getDate())}/${preencherDigitos(data.getMonth() + 1)}` : '--';
    }

    function renderizarGraficoLinha(id, janela, chaveMetrica, unidade, cor) {
        const canvas = document.getElementById(id);
        if (!canvas || !janela) return;

        const valores = janela[chaveMetrica] || [];
        const valoresMedidos = valores.map((valor, indice) => janela.tipos[indice] === 'medido' ? valor : null);
        const valoresPrevistos = valores.map((valor, indice) => janela.tipos[indice] === 'previsao' ? valor : null);
        const valorTransicao = valoresMedidos[janela.indiceAgora];
        if (janela.indiceAgora >= 0 && numeroValido(valorTransicao) !== null && valoresPrevistos.some(valor => numeroValido(valor) !== null)) {
            valoresPrevistos[janela.indiceAgora] = valorTransicao;
        }

        const temMedicao = valoresMedidos.some(valor => numeroValido(valor) !== null);
        const temPrevisao = valoresPrevistos.some((valor, indice) => indice > janela.indiceAgora && numeroValido(valor) !== null);
        if (!temMedicao && !temPrevisao) return;

        const corPrevisao = corComTransparencia(cor, 0.58);
        const conjuntosDados = [];
        if (temMedicao) {
            conjuntosDados.push({
                label: 'Medido',
                tipoDado: 'medido',
                data: valoresMedidos,
                borderColor: cor,
                backgroundColor: `${cor}22`,
                fill: true,
                tension: 0.35,
                pointRadius: 0,
                pointHitRadius: 18,
                spanGaps: false
            });
        }
        if (temPrevisao) {
            conjuntosDados.push({
                label: 'Previsão',
                tipoDado: 'previsao',
                data: valoresPrevistos,
                borderColor: corPrevisao,
                backgroundColor: corComTransparencia(cor, 0.05),
                borderDash: [6, 4],
                fill: false,
                tension: 0.35,
                pointRadius: 0,
                pointHitRadius: 18,
                spanGaps: false
            });
        }

        const grafico = new Chart(canvas, {
            type: 'line',
            data: { labels: janela.horarios.map(formatarHoraIso), datasets: conjuntosDados },
            options: criarOpcoesGraficoPublico(janela, unidade),
            plugins: [marcadorAgoraPlugin]
        });
        grafico.$marcadorAgora = { indice: janela.indiceAgora };
        grafico.$zoomPlugins = [marcadorAgoraPlugin];
        grafico.$zoomKind = 'publicWeather';
        grafico.$unidadePublica = unidade;
        grafico.$chavesSincronizacao = [...janela.horarios];
        window.SincronizadorGraficos?.registrar(grafico, 'publico');
        grafico.update('none');
        graficos[id] = grafico;
    }

    function renderizarGraficoChuva(dados) {
        const estado = document.getElementById('publicRainStatus');
        const janela = window.ClimaChuva?.montarJanela(dados.previsaoCurtoPrazo, dados.atualizadoEm);
        const analise = window.ClimaChuva?.analisarAgora(dados.climaAtual || {});
        if (estado) estado.textContent = analise?.rotulo || 'Condição atual indisponível';
        for (const [id, modo, rotulo] of [
            ['publicChartRain', 'precipitacao', 'precipitação'],
            ['publicChartRainProbability', 'probabilidade', 'chance de chuva']
        ]) {
            const canvas = document.getElementById(id);
            const cartao = canvas?.closest('.chart-card');
            if (!cartao) continue;
            const grafico = window.ClimaChuva?.criarGrafico({ canvas, janela, cores, modo, grupoSincronizacao: 'publico' });
            if (!grafico) {
                renderizarMensagemGrafico(cartao, `Sem dados de ${rotulo} para a janela consultada.`);
                continue;
            }
            grafico.$zoomKind = 'rain';
            graficos[id] = grafico;
        }
    }

    function criarOpcoesGraficoPublico(janela, unidade) {
        const limiteTicks = window.matchMedia?.('(max-width: 600px)')?.matches ? 7 : 13;
        return mesclarRecursivamente(padroesGrafico, {
            plugins: {
                legend: {
                    display: true,
                    labels: { color: cores.text, boxWidth: 18, boxHeight: 2, padding: 12 }
                },
                tooltip: {
                    filter: contexto => !(contexto.dataset.tipoDado === 'previsao' && contexto.dataIndex <= janela.indiceAgora),
                    callbacks: {
                        title: itens => formatarDataHoraTooltip(janela.horarios[itens[0]?.dataIndex]),
                        label: contexto => `${contexto.dataset.label}: ${Number(contexto.parsed.y).toFixed(2)}${unidade}`
                    }
                }
            },
            scales: {
                x: { ticks: { maxTicksLimit: limiteTicks, maxRotation: 45, minRotation: 45 } },
                y: { title: { display: true, text: unidade, color: cores.text } }
            }
        });
    }

    function montarJanelaObservadaEPrevista(seriesClimaticas, atualizadoEm, horasPrevisao = HORAS_PREVISAO_GRAFICOS) {
        const fim = novaData(atualizadoEm) || new Date();
        const inicio = new Date(fim.getTime() - 24 * 60 * 60 * 1000);
        const inicioPrevisao = new Date(fim);
        inicioPrevisao.setMinutes(0, 0, 0);
        inicioPrevisao.setHours(inicioPrevisao.getHours() + 1);
        const fimPrevisao = new Date(inicioPrevisao.getTime() + horasPrevisao * 60 * 60 * 1000);
        const resultado = {
            horarios: [], tipos: [], temperatura: [], sensacaoTermica: [], umidade: [], pressao: [], indiceAgora: -1
        };

        const pontos = (seriesClimaticas?.horarios || [])
            .map((horario, indice) => ({ horario, indice, dataHora: novaData(horario) }))
            .filter(ponto => ponto.dataHora)
            .sort((a, b) => a.dataHora - b.dataHora);

        pontos.forEach(ponto => {
            const eMedido = ponto.dataHora >= inicio && ponto.dataHora <= fim;
            const ePrevisao = ponto.dataHora >= inicioPrevisao && ponto.dataHora < fimPrevisao;
            if (!eMedido && !ePrevisao) return;

            resultado.horarios.push(ponto.horario);
            resultado.tipos.push(eMedido ? 'medido' : 'previsao');
            resultado.temperatura.push(seriesClimaticas.temperatura?.[ponto.indice] ?? null);
            resultado.sensacaoTermica.push(seriesClimaticas.sensacaoTermica?.[ponto.indice] ?? null);
            resultado.umidade.push(seriesClimaticas.umidade?.[ponto.indice] ?? null);
            resultado.pressao.push(seriesClimaticas.pressao?.[ponto.indice] ?? null);
            if (eMedido) resultado.indiceAgora = resultado.horarios.length - 1;
        });

        return resultado;
    }

    function numeroValido(valor) {
        if (valor === null || valor === undefined || valor === '') return null;
        const numero = Number(valor);
        return Number.isFinite(numero) ? numero : null;
    }

    function corComTransparencia(cor, opacidade) {
        const hexadecimal = String(cor || '').replace('#', '');
        if (!/^[0-9a-f]{6}$/i.test(hexadecimal)) return cor;
        const vermelho = Number.parseInt(hexadecimal.slice(0, 2), 16);
        const verde = Number.parseInt(hexadecimal.slice(2, 4), 16);
        const azul = Number.parseInt(hexadecimal.slice(4, 6), 16);
        return `rgba(${vermelho}, ${verde}, ${azul}, ${opacidade})`;
    }

    function formatarHoraIso(valor) {
        const data = novaData(valor);
        return data ? `${preencherDigitos(data.getHours())}:${preencherDigitos(data.getMinutes())}` : '--';
    }

    function formatarDataHoraTooltip(valor) {
        const data = novaData(valor);
        return data ? `${preencherDigitos(data.getDate())}/${preencherDigitos(data.getMonth() + 1)} ${preencherDigitos(data.getHours())}:${preencherDigitos(data.getMinutes())}` : '--';
    }

    function criarGraficoSolarDia(solar) {
        if (!solar) return;
        const canvas = document.getElementById('publicChartSolar');
        const pontos = [{ x: 0, y: 0 }, { x: solar.dawn, y: .08, label: 'Amanhecer', timeLabel: horaDecimal(solar.dawn) }, { x: solar.sunrise, y: .52, label: 'Nascer do sol', timeLabel: horaDecimal(solar.sunrise) }, { x: solar.zenith, y: 1, label: 'Zênite', timeLabel: horaDecimal(solar.zenith) }, { x: solar.sunset, y: .52, label: 'Pôr do sol', timeLabel: horaDecimal(solar.sunset) }, { x: solar.dusk, y: .08, label: 'Anoitecer', timeLabel: horaDecimal(solar.dusk) }, { x: 24, y: 0 }];
        const grafico = new Chart(canvas, {
            type: 'line',
            data: { datasets: [
                { label: 'Luz do dia', data: pontos, borderColor: '#facc15', backgroundColor: 'rgba(250,204,21,.22)', fill: true, tension: .42, pointRadius: 0, pointHitRadius: 18 },
                { type: 'scatter', label: 'Eventos solares', data: pontos.slice(1, 6), borderColor: '#f8fafc', backgroundColor: ['#fde68a', '#fb923c', '#facc15', '#f87171', '#818cf8'], pointBorderColor: '#0b1120', pointBorderWidth: 2, ...window.obterOpcoesPontosEventosSolares() }
            ] },
            options: obterOpcoesSolarDia(),
            plugins: [pluginFundoDiaSolar]
        });
        grafico.$solarDayTimes = solar;
        graficos.publicChartSolar = grafico;
    }

    function atualizarChipDuracaoSolar(solar) {
        const chip = document.getElementById('publicSolarDuration');
        if (!chip) return;
        const duracaoSegundos = Number(solar?.daylightDuration);
        const nascer = Number(solar?.sunrise);
        const por = Number(solar?.sunset);
        const minutos = Number.isFinite(duracaoSegundos) && duracaoSegundos > 0
            ? Math.round(duracaoSegundos / 60)
            : Number.isFinite(nascer) && Number.isFinite(por) && por >= nascer
                ? Math.round((por - nascer) * 60)
                : null;
        if (minutos === null) {
            chip.hidden = true;
            chip.textContent = '';
            return;
        }
        chip.textContent = `Duração do dia: ${Math.floor(minutos / 60)}h${preencherDigitos(minutos % 60)}`;
        chip.hidden = false;
    }

    function atualizarAqiPublico(aqui, local, atualizadoEm = null) {
        const indicador = document.getElementById('aqiIndicator');
        const popover = document.getElementById('aqiPopover');
        const valor = Number(aqui?.valor);
        const estado = classificarAqi(valor);
        indicador.className = `aqi-indicator aqi-indicator--${estado.chave}`;
        indicador.querySelector('.aqi-indicator__value').textContent = Number.isFinite(valor) ? Math.round(valor) : '--';
        indicador.querySelector('.aqi-indicator__status').textContent = '';
        indicador.title = Number.isFinite(valor) ? `AQI externo de ${local || 'localização atual'}: ${estado.nome}.` : 'AQI externo indisponível.';
        indicador.setAttribute('aria-label', Number.isFinite(valor) ? `AQI ${Math.round(valor)}. ${indicador.title}` : 'AQI --. AQI externo indisponível.');
        popover.hidden = true;
        popover.innerHTML = `
            <div class="aqi-popover__header"><span>AQI externo</span><strong>${Number.isFinite(valor) ? Math.round(valor) : '--'}</strong></div>
            ${Number.isFinite(valor) ? `<div class="aqi-popover__badge aqi-popover__badge--${estado.chave}">${estado.nome}</div>` : ''}
            <p class="aqi-popover__text">${Number.isFinite(valor) ? estado.impacto : 'Informe um CEP ou permita a localização para consultar o AQI externo.'}</p>
            ${Number.isFinite(valor) ? `<dl class="aqi-popover__meta"><div><dt>Origem</dt><dd>${escapar(local || 'localização consultada')}</dd></div><div><dt>Atualizado</dt><dd>${formatarDataHora(atualizadoEm)}</dd></div></dl><p class="aqi-popover__note">Dado externo da API Open-Meteo Air Quality; não usa sensores internos da estação.</p>` : ''}
        `;
    }

    function atualizarSolarPublico(solar, origem = null) {
        window.atualizarIndicadorAstronomicoPublico?.(solar, origem);
    }

    function classificarAqi(valor) {
        const numero = Number(valor);
        if (!Number.isFinite(numero)) return { chave: 'unknown', nome: 'Indisponível', impacto: '' };
        if (numero <= 50) return { chave: 'good', nome: 'Boa', impacto: 'Ar satisfatório, risco mínimo.' };
        if (numero <= 100) return { chave: 'moderate', nome: 'Moderado', impacto: 'Aceitável, mas pode haver risco moderado para grupos sensíveis.' };
        if (numero <= 150) return { chave: 'sensitive', nome: 'Insalubre para grupos sensíveis', impacto: 'Grupos de risco podem apresentar sintomas.' };
        if (numero <= 200) return { chave: 'unhealthy', nome: 'Insalubre', impacto: 'Toda a população pode começar a sentir efeitos na saúde.' };
        if (numero <= 300) return { chave: 'very-unhealthy', nome: 'Muito insalubre', impacto: 'Alerta de saúde; riscos aumentados para todos.' };
        return { chave: 'hazardous', nome: 'Perigoso', impacto: 'Condições de emergência; toda a população é severamente afetada.' };
    }

    function renderizarEstacao(estado) {
        if (!estado) return '';
        return `<div class="season-timeline__track" aria-label="Progresso anual das estações">${estado.estacoes.map(item => `<span class="season-timeline__segment season-timeline__segment--${item.chave}"><i>${item.nome}</i></span>`).join('')}<span class="season-timeline__marker" style="left: ${estado.progressoAno}%"></span></div>`;
    }

    function renderizarLua(estado) {
        if (!estado) return '';
        return `<div class="moon-summary__scene moon-summary__scene--${estado.fase.chave}" style="--moon-shadow: ${estado.sombra}%"><span class="moon-summary__orb" aria-hidden="true"></span></div><div class="moon-summary__content"><span>${estado.fase.nome}</span><strong>${estado.iluminacao}% iluminada</strong></div><dl class="moon-summary__details"><div><dt>Idade</dt><dd>${estado.idade.toFixed(1)} dias</dd></div><div><dt>Próx. cheia</dt><dd>${formatarDataCompleta(estado.proximaCheia)}</dd></div><div><dt>Próx. nova</dt><dd>${formatarDataCompleta(estado.proximaNova)}</dd></div></dl>`;
    }

    function limparGraficos() { Object.keys(graficos).forEach(chave => { window.SincronizadorGraficos?.desregistrar(graficos[chave]); graficos[chave]?.destroy?.(); delete graficos[chave]; }); }
    function novaData(valor) { const data = valor ? new Date(valor) : null; return data && !Number.isNaN(data.getTime()) ? data : null; }
    function preencherDigitos(valor) { return String(valor).padStart(2, '0'); }
    function formatarDataHora(data) { return data ? `${preencherDigitos(data.getDate())}/${preencherDigitos(data.getMonth() + 1)} ${preencherDigitos(data.getHours())}:${preencherDigitos(data.getMinutes())}` : '--'; }
    function formatarDataCompleta(data) { return data ? `${preencherDigitos(data.getDate())}/${preencherDigitos(data.getMonth() + 1)}/${data.getFullYear()}` : '--'; }
    function horaDecimal(valor) { return formatarTempo(Number(valor)); }
    function formatarDuracao(segundos) { const minutos = Math.round(Number(segundos) / 60); return Number.isFinite(minutos) ? `${Math.floor(minutos / 60)}h ${preencherDigitos(minutos % 60)}min` : '--'; }
    function escapar(valor) { const div = document.createElement('div'); div.textContent = String(valor ?? ''); return div.innerHTML; }

    window.PublicWeatherApp = { setup: configurarModulo, montarJanelaObservadaEPrevista };
})();
