'use strict';

(function () {
    const ESTACOES_BASE = [
        { chave: "verao", nome: "Verão", mes: 11, dia: 21 },
        { chave: "outono", nome: "Outono", mes: 2, dia: 20 },
        { chave: "inverno", nome: "Inverno", mes: 5, dia: 21 },
        { chave: "primavera", nome: "Primavera", mes: 8, dia: 22 },
    ];

    let indicador = null;
    let janelaDetalhes = null;
    function configurarModulo({ indicatorId: idIndicador = "seasonIndicator", popoverId: idJanelaDetalhes = "seasonPopover" } = {}) {
        indicador = document.getElementById(idIndicador);
        janelaDetalhes = document.getElementById(idJanelaDetalhes);

        if (!indicador || !janelaDetalhes) return;

        indicador.addEventListener("click", evento => {
            evento.stopPropagation();
            alternarPopover();
        });

        document.addEventListener("click", evento => {
            if (!janelaDetalhes || janelaDetalhes.hidden) return;
            if (janelaDetalhes.contains(evento.target) || indicador.contains(evento.target)) return;
            fecharPopover();
        });

        document.addEventListener("keydown", evento => {
            if (evento.key === "Escape") fecharPopover();
        });

        window.addEventListener("header-popover-open", evento => {
            if (evento.detail?.source !== "season") fecharPopover();
        });

        atualizarModulo();
    }

    function atualizarModulo(dataFirebase = dataAtualFirebase()) {
        if (!indicador || !janelaDetalhes) return;

        const dados = interpretarDadosFirebase(dataFirebase);
        if (!dados) {
            renderizarIndisponivel();
            return;
        }

        const estado = obterEstadoDaEstacao(dados);
        const descricao = `${estado.estacao.nome}: começa em ${formatarDataCompleta(estado.estacao.inicio)}.`;

        indicador.className = `season-indicator season-indicator--${estado.estacao.chave}`;
        indicador.title = descricao;
        indicador.setAttribute("aria-label", descricao);

        const valor = indicador.querySelector(".season-indicator__value");
        if (valor) valor.textContent = estado.estacao.nome;

        renderizarPopover(estado);
    }

    function renderizarIndisponivel() {
        indicador.className = "season-indicator season-indicator--unknown";
        indicador.title = "Estação do ano indisponível";
        indicador.setAttribute("aria-label", indicador.title);
        indicador.setAttribute("aria-expanded", "false");

        const valor = indicador.querySelector(".season-indicator__value");
        if (valor) valor.textContent = "--";

        janelaDetalhes.hidden = true;
        janelaDetalhes.innerHTML = `
            <div class="season-header-popover__header">
                <span>Estação do ano</span>
                <strong>--</strong>
            </div>
            <p class="season-header-popover__text">Não foi possível identificar a data.</p>
        `;
    }

    function renderizarPopover(estado) {
        janelaDetalhes.innerHTML = `
            <div class="season-header-popover__header">
                <span>Estação do ano</span>
                <strong>${estado.estacao.nome}</strong>
            </div>
            <dl class="season-header-popover__list">
                ${estado.estacoes.map(estacao => `
                    <div class="${estacao.chave === estado.estacao.chave ? "is-current" : ""}">
                        <dt>${estacao.nome}</dt>
                        <dd>${formatarDataCompleta(estacao.inicio)}</dd>
                    </div>
                `).join("")}
            </dl>
        `;
    }

    function alternarPopover() {
        if (!indicador || !janelaDetalhes) return;

        if (janelaDetalhes.hidden) {
            window.dispatchEvent(new CustomEvent("header-popover-open", { detail: { source: "season" } }));
            atualizarModulo();
            janelaDetalhes.hidden = false;
            indicador.setAttribute("aria-expanded", "true");
        } else {
            fecharPopover();
        }
    }

    function fecharPopover() {
        if (!indicador || !janelaDetalhes) return;
        janelaDetalhes.hidden = true;
        indicador.setAttribute("aria-expanded", "false");
    }

    function obterEstadoDaEstacao(dados) {
        const ano = dados.getFullYear();
        const inicioVeraoAtual = new Date(ano, 11, 21);
        const anoFimCiclo = dados >= inicioVeraoAtual ? ano + 1 : ano;
        const estacoes = ESTACOES_BASE.map(estacao => ({
            ...estacao,
            inicio: new Date(estacao.chave === "verao" ? anoFimCiclo - 1 : anoFimCiclo, estacao.mes, estacao.dia),
        }));
        const proximoVerao = { ...ESTACOES_BASE[0], inicio: new Date(anoFimCiclo, 11, 21) };
        const indiceAtual = estacoes.findIndex((estacao, indice) => {
            const proxima = estacoes[indice + 1] || proximoVerao;
            return dados >= estacao.inicio && dados < proxima.inicio;
        });
        const indiceSeguro = indiceAtual >= 0 ? indiceAtual : 0;
        const estacao = estacoes[indiceSeguro];
        const proxima = estacoes[indiceSeguro + 1] || proximoVerao;
        const duracao = proxima.inicio - estacao.inicio || 1;
        const progressoSegmento = Math.min(1, Math.max(0, (dados - estacao.inicio) / duracao));
        const progressoAno = (indiceSeguro * 25) + (progressoSegmento * 25);

        return {
            estacao,
            estacoes,
            progressoAno: progressoAno.toFixed(2),
            progressoEstacao: (progressoSegmento * 100).toFixed(2),
        };
    }

    function obterEstado(dataFirebase = dataAtualFirebase()) {
        const dados = interpretarDadosFirebase(dataFirebase);
        return dados ? obterEstadoDaEstacao(dados) : null;
    }

    function interpretarDadosFirebase(valor) {
        if (valor instanceof Date && !Number.isNaN(valor.getTime())) return valor;
        if (valor) {
            const partes = String(valor).match(/^(\d{2})-(\d{2})-(\d{4})$/);
            if (partes) {
                const dados = new Date(Number(partes[3]), Number(partes[2]) - 1, Number(partes[1]));
                if (!Number.isNaN(dados.getTime())) return dados;
            }
        }

        const dataAtual = new Date();
        return Number.isNaN(dataAtual.getTime()) ? null : dataAtual;
    }

    function dataAtualFirebase() {
        return typeof paraDataFirebase === "function" ? paraDataFirebase(new Date()) : null;
    }

    function formatarDataCompleta(dados) {
        return `${String(dados.getDate()).padStart(2, "0")}/${String(dados.getMonth() + 1).padStart(2, "0")}/${dados.getFullYear()}`;
    }

    window.ClimateSeason = {
        setup: configurarModulo,
        update: atualizarModulo,
        getState: obterEstado,
    };
})();
