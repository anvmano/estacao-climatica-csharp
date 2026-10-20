'use strict';

let estaAberto = false;
let estaOcupado = false;
let temporizadorFechamento = null;
let elementos = null;
let posicaoRolagemTravada = 0;
let memoriaConversa = null;

function configurar() {
    elementos = coletarElementos();
    if (!elementos.root || !elementos.toggle || !elementos.panel || !elementos.form || !elementos.input || !elementos.messages) {
        return;
    }

    elementos.toggle.addEventListener('click', alternarChat);
    elementos.close?.addEventListener('click', fecharChat);
    elementos.form.addEventListener('submit', aoEnviarFormulario);
    document.addEventListener('pointerdown', aoPressionarFora);
    document.addEventListener('keydown', fecharChatComEscape);

    elementos.quickActions.forEach((botao) => {
        botao.addEventListener('click', () => {
            const pergunta = botao.getAttribute('data-chat-question') || '';
            enviarPergunta(pergunta);
        });
    });

    adicionarMensagem('assistant', mensagensChat.inicial);
}

function coletarElementos() {
    return {
        root: document.getElementById(idsChat.root),
        toggle: document.getElementById(idsChat.toggle),
        panel: document.getElementById(idsChat.panel),
        close: document.getElementById(idsChat.close),
        quickActions: document.querySelectorAll(idsChat.quickActions),
        form: document.getElementById(idsChat.form),
        input: document.getElementById(idsChat.input),
        submit: document.getElementById(idsChat.submit),
        messages: document.getElementById(idsChat.messages),
    };
}

function alternarChat() {
    estaAberto ? fecharChat() : abrirChat();
}

function abrirChat() {
    estaAberto = true;
    clearTimeout(temporizadorFechamento);
    travarRolagemPagina();
    elementos.toggle?.setAttribute('aria-expanded', 'true');
    elementos.panel?.removeAttribute('hidden');

    requestAnimationFrame(() => {
        elementos.root?.classList.add('is-open');
    });

    setTimeout(() => elementos.input?.focus(), 50);
}

function fecharChat() {
    estaAberto = false;
    elementos.toggle?.setAttribute('aria-expanded', 'false');
    elementos.root?.classList.remove('is-open');
    liberarRolagemPagina();
    clearTimeout(temporizadorFechamento);

    temporizadorFechamento = setTimeout(() => {
        if (!estaAberto) elementos.panel?.setAttribute('hidden', '');
    }, 240);
}

async function aoEnviarFormulario(evento) {
    evento.preventDefault();
    if (estaOcupado) return;
    await enviarPergunta(elementos.input?.value || '');
}

async function enviarPergunta(pergunta) {
    const texto = (pergunta || '').trim();
    if (!texto || !elementos.input || estaOcupado) return;

    if (!estaAberto) abrirChat();
    adicionarMensagem('user', texto);
    elementos.input.value = '';
    definirOcupado(true);

    const indicador = adicionarMensagem('assistant', mensagensChat.analisando);
    try {
        const resposta = await window.ClimateAuth.fetchInterno('/api/chat', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                question: texto,
                activeTab: obterAbaAtivaChat(),
                selectedDate: typeof dataSelecionada === 'string' ? dataSelecionada : null,
                previousIntent: memoriaConversa,
            }),
        });

        if (!resposta.ok) throw new Error(`HTTP ${resposta.status}`);
        const conteudoSerializado = await resposta.json();
        indicador.textContent = conteudoSerializado.answer || mensagensChat.semResposta;
        atualizarMemoriaConversa(conteudoSerializado.intent);
    } catch (erro) {
        console.error(erro);
        indicador.textContent = mensagensChat.falha;
    } finally {
        definirOcupado(false);
    }
}

function adicionarMensagem(tipo, texto) {
    const mensagem = document.createElement('div');
    mensagem.className = `ai-chat__message ai-chat__message--${tipo}`;
    mensagem.textContent = texto;
    elementos.messages?.appendChild(mensagem);
    elementos.messages?.scrollTo({ top: elementos.messages.scrollHeight, behavior: 'smooth' });
    return mensagem;
}

function definirOcupado(ocupado) {
    estaOcupado = ocupado;
    if (elementos.input) elementos.input.disabled = ocupado;
    if (elementos.submit) elementos.submit.disabled = ocupado;
    elementos.quickActions.forEach((botao) => {
        botao.disabled = ocupado;
    });
}

function aoPressionarFora(evento) {
    if (!estaAberto || elementos.root?.contains(evento.target)) return;
    fecharChat();
}

function fecharChatComEscape(evento) {
    if (evento.key !== 'Escape' || !estaAberto) return;
    fecharChat();
}

function obterAbaAtivaChat() {
    const botaoAtivo = document.querySelector('.tablink.active');
    const alvo = botaoAtivo?.getAttribute('data-tab-target') || botaoAtivo?.getAttribute('aria-controls');
    if (alvo) return alvo;

    const painelAtivo = document.querySelector('.tabcontent.active');
    return painelAtivo?.id || 'Tab1';
}

function travarRolagemPagina() {
    if (document.body.classList.contains('ai-chat-scroll-locked')) return;
    posicaoRolagemTravada = window.scrollY || document.documentElement.scrollTop || 0;
    document.documentElement.classList.add('ai-chat-scroll-locked');
    document.body.classList.add('ai-chat-scroll-locked');
    document.body.style.top = `-${posicaoRolagemTravada}px`;
}

function liberarRolagemPagina() {
    document.documentElement.classList.remove('ai-chat-scroll-locked');
    document.body.classList.remove('ai-chat-scroll-locked');
    document.body.style.top = '';
    window.scrollTo(0, posicaoRolagemTravada);
}

function atualizarMemoriaConversa(intencao) {
    if (!intencao) return;
    memoriaConversa = intencao;
}
