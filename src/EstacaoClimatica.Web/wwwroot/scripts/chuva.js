'use strict';

(function () {
    const HORAS_OBSERVADAS = 24;
    const HORAS_PREVISTAS = 12;
    const HORAS_PROBABILIDADE_ANTERIORES = 12;
    const CONSULTA_MOVEL = "(max-width: 640px)";

    const pluginMarcadorAgora = {
        id: "marcadorAgoraChuva",
        afterDraw(grafico) {
            const indice = grafico.$indiceAgora ?? grafico.$marcadorAgora?.indice;
            const escalaX = grafico.scales?.x;
            const area = grafico.chartArea;
            if (!Number.isInteger(indice) || indice < 0 || !escalaX || !area) return;

            const x = escalaX.getPixelForValue(indice);
            if (!Number.isFinite(x) || x < area.left || x > area.right) return;

            const contexto = grafico.ctx;
            contexto.save();
            contexto.strokeStyle = "rgba(148, 163, 184, 0.65)";
            contexto.lineWidth = 1;
            contexto.setLineDash([4, 4]);
            contexto.beginPath();
            contexto.moveTo(x, area.top);
            contexto.lineTo(x, area.bottom);
            contexto.stroke();
            contexto.restore();
        },
    };

    const pluginControleResponsivo = {
        id: "controleResponsivoChuva",
        beforeDestroy(grafico) {
            removerControleMovel(grafico);
        },
    };

    function montarJanela(previsao = [], atualizadoEm = new Date(), horasObservadas = HORAS_OBSERVADAS, horasPrevistas = HORAS_PREVISTAS) {
        const agora = dataValida(atualizadoEm) || new Date();
        const inicio = new Date(agora.getTime() - horasObservadas * 60 * 60 * 1000);
        const inicioProbabilidade = new Date(agora.getTime() - HORAS_PROBABILIDADE_ANTERIORES * 60 * 60 * 1000);
        const inicioPrevisao = new Date(agora);
        inicioPrevisao.setMinutes(0, 0, 0);
        inicioPrevisao.setHours(inicioPrevisao.getHours() + 1);
        const fimPrevisao = new Date(inicioPrevisao.getTime() + horasPrevistas * 60 * 60 * 1000);
        const resultado = {
            horarios: [],
            tipos: [],
            precipitacao: [],
            probabilidade: [],
            indiceAgora: -1,
            chance: {
                horarios: [],
                tipos: [],
                precipitacao: [],
                probabilidade: [],
                indiceAgora: -1,
            },
        };

        previsao.forEach(item => {
            const horario = dataValida(item?.horario);
            if (!horario) return;
            const observado = horario >= inicio && horario <= agora;
            const previsto = horario >= inicioPrevisao && horario < fimPrevisao;
            if (!observado && !previsto) return;

            resultado.horarios.push(item.horario);
            resultado.tipos.push(observado ? "observado" : "previsao");
            resultado.precipitacao.push(numeroOuNulo(item.precipitacao));
            resultado.probabilidade.push(numeroOuNulo(item.probabilidadeChuva));
            if (observado) resultado.indiceAgora = resultado.horarios.length - 1;

            const observadoNaChance = horario >= inicioProbabilidade && horario <= agora;
            if (observadoNaChance || previsto) {
                resultado.chance.horarios.push(item.horario);
                resultado.chance.tipos.push(observadoNaChance ? "observado" : "previsao");
                resultado.chance.precipitacao.push(numeroOuNulo(item.precipitacao));
                resultado.chance.probabilidade.push(numeroOuNulo(item.probabilidadeChuva));
                if (observadoNaChance) resultado.chance.indiceAgora = resultado.chance.horarios.length - 1;
            }
        });

        return resultado;
    }

    function analisarAgora(climaAtual = {}) {
        const precipitacao = numeroOuNulo(climaAtual.precipitacao);
        const chuva = numeroOuNulo(climaAtual.chuva);
        const codigoTempo = numeroOuNulo(climaAtual.codigoTempo);
        const codigoChuva = codigoTempo !== null && (
            (codigoTempo >= 51 && codigoTempo <= 67) ||
            (codigoTempo >= 80 && codigoTempo <= 82) ||
            (codigoTempo >= 95 && codigoTempo <= 99)
        );
        const temDados = precipitacao !== null || chuva !== null || codigoTempo !== null;
        const chovendo = (precipitacao ?? 0) > 0 || (chuva ?? 0) > 0 || codigoChuva;

        return {
            disponivel: temDados,
            chovendo,
            rotulo: !temDados ? "Condição atual indisponível" : chovendo ? "Chovendo agora" : "Sem chuva agora",
            precipitacao,
        };
    }

    function criarGrafico({ canvas, janela, graficoExistente, cores, grupoSincronizacao = null, modo = null }) {
        if (graficoExistente) {
            window.SincronizadorGraficos?.desregistrar(graficoExistente);
            graficoExistente.destroy();
        }
        if (!canvas || !window.Chart || !janela?.horarios?.length) return null;

        const modoFixo = modo === "precipitacao" || modo === "probabilidade" ? modo : null;
        const visualizacaoPrecipitacao = {
            horarios: [...janela.horarios],
            tipos: [...janela.tipos],
            indiceAgora: janela.indiceAgora,
            series: {
                observada: janela.precipitacao.map((valor, indice) => janela.tipos[indice] === "observado" ? valor : null),
                prevista: janela.precipitacao.map((valor, indice) => janela.tipos[indice] === "previsao" ? valor : null),
                probabilidade: [],
            },
        };
        const fonteChance = modoFixo === "probabilidade" ? janela : janela.chance;
        const visualizacaoChance = {
            horarios: [...(fonteChance?.horarios || [])],
            tipos: [...(fonteChance?.tipos || [])],
            indiceAgora: fonteChance?.indiceAgora ?? -1,
            series: {
                observada: (fonteChance?.precipitacao || []).map((valor, indice) => fonteChance.tipos[indice] === "observado" ? valor : null),
                prevista: (fonteChance?.precipitacao || []).map((valor, indice) => fonteChance.tipos[indice] === "previsao" ? valor : null),
                probabilidade: [...(fonteChance?.probabilidade || [])],
                probabilidadeObservada: (fonteChance?.probabilidade || []).map((valor, indice) => fonteChance.tipos[indice] === "observado" ? valor : null),
                probabilidadePrevista: (fonteChance?.probabilidade || []).map((valor, indice) => fonteChance.tipos[indice] === "previsao" ? valor : null),
            },
        };
        const valorTransicaoChance = visualizacaoChance.series.probabilidadeObservada[visualizacaoChance.indiceAgora];
        if (
            visualizacaoChance.indiceAgora >= 0
            && numeroOuNulo(valorTransicaoChance) !== null
            && visualizacaoChance.series.probabilidadePrevista.some(valor => numeroOuNulo(valor) !== null)
        ) {
            visualizacaoChance.series.probabilidadePrevista[visualizacaoChance.indiceAgora] = valorTransicaoChance;
        }
        const valoresDisponiveis = modoFixo === "precipitacao"
            ? [...visualizacaoPrecipitacao.series.observada, ...visualizacaoPrecipitacao.series.prevista]
            : modoFixo === "probabilidade"
            ? visualizacaoChance.series.probabilidade
            : [...visualizacaoPrecipitacao.series.observada, ...visualizacaoPrecipitacao.series.prevista, ...visualizacaoChance.series.probabilidade];
        const temDados = valoresDisponiveis.some(valor => numeroOuNulo(valor) !== null);
        if (!temDados) return null;

        const corChuva = cores?.blue || "#38bdf8";
        const corPrevisao = "rgba(125, 211, 252, 0.48)";
        const corProbabilidade = cores?.purple || "#a78bfa";
        const movel = estaEmTelaMovel();
        const visualizacaoInicial = modoFixo === "precipitacao"
            ? visualizacaoPrecipitacao
            : modoFixo === "probabilidade"
            ? visualizacaoChance
            : movel ? visualizacaoPrecipitacao : visualizacaoChance;
        const seriesPrecipitacao = [
            {
                label: "Precipitação observada",
                data: visualizacaoInicial.series.observada,
                yAxisID: "yMilimetros",
                backgroundColor: `${corChuva}aa`,
                borderColor: corChuva,
                borderWidth: 1,
                borderRadius: 3,
                chaveChuva: "observada",
                tipoDado: "observado",
                order: 2,
                hidden: false,
            },
            {
                label: "Precipitação prevista",
                data: visualizacaoInicial.series.prevista,
                yAxisID: "yMilimetros",
                backgroundColor: corPrevisao,
                borderColor: corPrevisao,
                borderWidth: 1,
                borderRadius: 3,
                chaveChuva: "prevista",
                tipoDado: "previsao",
                order: 2,
                hidden: false,
            },
        ];
        const serieProbabilidade = {
            type: "line",
            label: "Chance de chuva",
            data: visualizacaoInicial.series.probabilidade,
            yAxisID: "yProbabilidade",
            borderColor: corProbabilidade,
            backgroundColor: "transparent",
            borderWidth: 2,
            tension: 0.3,
            pointRadius: 0,
            pointHitRadius: 18,
            spanGaps: false,
            segment: {
                borderDash: contexto => contexto.chart.$tiposAtivos?.[contexto.p1DataIndex] === "previsao" ? [6, 4] : undefined,
            },
            chaveChuva: "probabilidade",
            tipoDado: "probabilidade",
            order: 1,
            hidden: movel,
        };
        const seriesProbabilidadePublica = [
            {
                ...serieProbabilidade,
                label: "Medido",
                data: visualizacaoInicial.series.probabilidadeObservada,
                chaveChuva: "probabilidadeObservada",
                tipoDado: "observado",
                hidden: false,
                segment: undefined,
            },
            {
                ...serieProbabilidade,
                label: "Previsão",
                data: visualizacaoInicial.series.probabilidadePrevista,
                chaveChuva: "probabilidadePrevista",
                tipoDado: "previsao",
                borderColor: "rgba(196, 181, 253, 0.58)",
                borderDash: [6, 4],
                hidden: false,
                segment: undefined,
            },
        ];
        const datasets = modoFixo === "probabilidade"
            ? seriesProbabilidadePublica
            : modoFixo === "precipitacao"
            ? seriesPrecipitacao
            : [...seriesPrecipitacao, serieProbabilidade];
        const grafico = new Chart(canvas.getContext("2d"), {
            type: "bar",
            data: {
                labels: visualizacaoInicial.horarios.map(formatarHora),
                datasets,
            },
            options: obterOpcoes({ janela, cores, modo: modoFixo }),
            plugins: [pluginMarcadorAgora, pluginControleResponsivo],
        });

        grafico.$visualizacoesChuva = {
            precipitacao: visualizacaoPrecipitacao,
            probabilidade: visualizacaoChance,
        };
        grafico.$indiceAgora = visualizacaoInicial.indiceAgora;
        grafico.$marcadorAgora = { indice: visualizacaoInicial.indiceAgora };
        grafico.$horariosAtivos = [...visualizacaoInicial.horarios];
        grafico.$tiposAtivos = [...visualizacaoInicial.tipos];
        grafico.$chavesSincronizacao = [...visualizacaoInicial.horarios];
        grafico.$zoomPlugins = [pluginMarcadorAgora];
        grafico.$modoChuvaFixo = modoFixo;
        grafico.$modoChuvaMovel = "precipitacao";
        sincronizarModoResponsivo(grafico);
        if (grupoSincronizacao) window.SincronizadorGraficos?.registrar(grafico, grupoSincronizacao);
        grafico.update("none");
        return grafico;
    }

    function obterOpcoes({ janela = null, cores = window.cores || {}, impressao = false, modo = null, controleResponsivo = true } = {}) {
        const movel = !impressao && estaEmTelaMovel();
        const modoFixo = modo === "precipitacao" || modo === "probabilidade" ? modo : null;
        const limiteTicks = movel ? 5 : 13;
        const padroes = mesclarRecursivamente(padroesGrafico, {});
        const opcoes = mesclarRecursivamente(padroes, {
            onResize: impressao || !controleResponsivo ? undefined : grafico => sincronizarModoResponsivo(grafico),
            interaction: { mode: "index", intersect: false, axis: "x" },
            plugins: {
                legend: {
                    display: modoFixo === "probabilidade" || !movel,
                    labels: {
                        color: cores.text,
                        boxWidth: 12,
                        padding: 12,
                        filter: (item, dados) => {
                            const serie = dados.datasets[item.datasetIndex];
                            return !serie.hidden && (serie.data || []).some(valor => numeroOuNulo(valor) !== null);
                        },
                    },
                },
                tooltip: {
                    mode: "index",
                    intersect: false,
                    filter: contexto => numeroOuNulo(contexto.raw) !== null
                        && !(contexto.dataset.tipoDado === "previsao" && contexto.dataIndex <= contexto.chart.$indiceAgora),
                    callbacks: {
                        title: itens => {
                            const indice = itens[0]?.dataIndex;
                            const horario = itens[0]?.chart?.$horariosAtivos?.[indice] || janela?.horarios?.[indice];
                            return horario ? formatarDataHora(horario) : itens[0]?.label || "--";
                        },
                        label: contexto => contexto.dataset.yAxisID === "yProbabilidade"
                            ? `${contexto.dataset.label}: ${Number(contexto.parsed.y).toFixed(0)}%`
                            : `${contexto.dataset.label}: ${Number(contexto.parsed.y).toFixed(1)} mm`,
                    },
                },
            },
            scales: {
                x: { stacked: true, ticks: { maxTicksLimit: limiteTicks } },
                yMilimetros: {
                    type: "linear",
                    position: "left",
                    beginAtZero: true,
                    display: impressao || modoFixo === "precipitacao" || (!modoFixo && movel),
                    title: { display: impressao, text: "mm", color: cores.text },
                    ticks: { color: cores.text, callback: valor => `${valor} mm` },
                    grid: { color: cores.grid },
                },
                yProbabilidade: {
                    type: "linear",
                    position: modoFixo === "probabilidade" ? "left" : "right",
                    min: 0,
                    max: 100,
                    display: impressao || modoFixo === "probabilidade" || (!modoFixo && !movel),
                    title: { display: !movel, text: "%", color: cores.text },
                    ticks: { color: cores.text, callback: valor => `${valor}%` },
                    grid: modoFixo === "probabilidade" ? { color: cores.grid } : { drawOnChartArea: false },
                },
            },
        });
        delete opcoes.scales.y;
        return opcoes;
    }

    function estaEmTelaMovel() {
        return !!window.matchMedia?.(CONSULTA_MOVEL)?.matches;
    }

    function sincronizarModoResponsivo(grafico) {
        if (!grafico?.data?.datasets || !grafico.options?.scales) return;
        const movel = estaEmTelaMovel();
        grafico.options.plugins.legend.display = grafico.$modoChuvaFixo === "probabilidade" || !movel;
        grafico.options.scales.x.ticks.maxTicksLimit = movel ? 5 : 13;
        grafico.options.scales.yMilimetros.title.display = false;
        grafico.options.scales.yProbabilidade.title.display = !movel;

        if (grafico.$modoChuvaFixo) {
            removerControleMovel(grafico);
            aplicarVisualizacao(grafico, grafico.$modoChuvaFixo, false);
            return;
        }

        if (!movel) {
            removerControleMovel(grafico);
            aplicarVisualizacao(grafico, "completo", false);
            return;
        }

        criarControleMovel(grafico);
        aplicarModoMovel(grafico, grafico.$modoChuvaMovel || "precipitacao", false);
    }

    function criarControleMovel(grafico) {
        const canvas = grafico?.canvas;
        const recipiente = canvas?.parentElement;
        if (!canvas || !recipiente) return;

        const seletor = `.rain-chart-toggle[data-rain-chart="${canvas.id}"]`;
        if (recipiente.querySelector(seletor)) return;

        const controle = document.createElement("div");
        controle.className = "rain-chart-toggle";
        controle.dataset.rainChart = canvas.id;
        controle.setAttribute("role", "group");
        controle.setAttribute("aria-label", "Dado exibido no gráfico de chuva");
        controle.innerHTML = `
            <button type="button" data-rain-mode="precipitacao" aria-pressed="true">Precipitação</button>
            <button type="button" data-rain-mode="probabilidade" aria-pressed="false">Chance</button>
        `;
        controle.addEventListener("click", evento => {
            const botao = evento.target.closest?.("[data-rain-mode]");
            if (!botao) return;
            aplicarModoMovel(grafico, botao.dataset.rainMode);
        });
        recipiente.insertBefore(controle, canvas);
    }

    function aplicarModoMovel(grafico, modo, atualizar = true) {
        if (!grafico?.data?.datasets || !estaEmTelaMovel()) return;
        grafico.$modoChuvaMovel = modo === "probabilidade" ? "probabilidade" : "precipitacao";
        aplicarVisualizacao(grafico, grafico.$modoChuvaMovel, atualizar);
    }

    function aplicarVisualizacao(grafico, modo, atualizar = true) {
        const mostrarProbabilidade = modo !== "precipitacao";
        const mostrarPrecipitacao = modo !== "probabilidade";
        const visualizacao = grafico.$visualizacoesChuva?.[mostrarProbabilidade ? "probabilidade" : "precipitacao"];
        if (!visualizacao) return;

        grafico.data.labels = visualizacao.horarios.map(formatarHora);
        grafico.data.datasets.forEach(serie => {
            serie.data = [...(visualizacao.series[serie.chaveChuva] || [])];
            serie.hidden = serie.yAxisID === "yProbabilidade" ? !mostrarProbabilidade : !mostrarPrecipitacao;
        });
        grafico.options.scales.yMilimetros.display = mostrarPrecipitacao;
        grafico.options.scales.yProbabilidade.display = mostrarProbabilidade;
        grafico.$indiceAgora = visualizacao.indiceAgora;
        grafico.$marcadorAgora = { indice: visualizacao.indiceAgora };
        grafico.$horariosAtivos = [...visualizacao.horarios];
        grafico.$tiposAtivos = [...visualizacao.tipos];
        grafico.$chavesSincronizacao = [...visualizacao.horarios];
        grafico.setActiveElements?.([]);
        grafico.tooltip?.setActiveElements?.([], { x: 0, y: 0 });

        const titulo = grafico.canvas?.parentElement?.querySelector(".chart-label");
        if (titulo) {
            titulo.textContent = mostrarProbabilidade && mostrarPrecipitacao
                ? "Chuva · 12h anteriores + próximas 12h"
                : mostrarProbabilidade
                ? `Chance de chuva · ${grafico.$modoChuvaFixo === "probabilidade" ? "24h" : "12h"} anteriores + próximas 12h`
                : "Chuva · 24h anteriores + próximas 12h";
        }
        grafico.canvas?.setAttribute(
            "aria-label",
            mostrarProbabilidade && mostrarPrecipitacao
                ? "Precipitação e probabilidade de chuva nas 12 horas anteriores e próximas 12 horas"
                : mostrarProbabilidade
                ? `Probabilidade de chuva nas ${grafico.$modoChuvaFixo === "probabilidade" ? "24" : "12"} horas anteriores e próximas 12 horas`
                : "Precipitação nas 24 horas anteriores e próximas 12 horas"
        );

        const controle = grafico.canvas?.parentElement?.querySelector(
            `.rain-chart-toggle[data-rain-chart="${grafico.canvas.id}"]`
        );
        controle?.querySelectorAll("[data-rain-mode]").forEach(botao => {
            botao.setAttribute("aria-pressed", String(botao.dataset.rainMode === grafico.$modoChuvaMovel));
        });
        if (atualizar) grafico.update("none");
    }

    function removerControleMovel(grafico) {
        const canvas = grafico?.canvas;
        if (!canvas?.parentElement) return;
        canvas.parentElement
            .querySelector(`.rain-chart-toggle[data-rain-chart="${canvas.id}"]`)
            ?.remove();
    }

    function dataValida(valor) {
        const dados = valor instanceof Date ? valor : new Date(valor);
        return Number.isNaN(dados.getTime()) ? null : dados;
    }

    function numeroOuNulo(valor) {
        if (valor === null || valor === undefined || valor === "") return null;
        const numero = Number(valor);
        return Number.isFinite(numero) ? numero : null;
    }

    function formatarHora(valor) {
        const dados = dataValida(valor);
        if (!dados) return "--";
        return `${String(dados.getHours()).padStart(2, "0")}:${String(dados.getMinutes()).padStart(2, "0")}`;
    }

    function formatarDataHora(valor) {
        const dados = dataValida(valor);
        if (!dados) return "--";
        return `${String(dados.getDate()).padStart(2, "0")}/${String(dados.getMonth() + 1).padStart(2, "0")} ${formatarHora(dados)}`;
    }

    window.ClimaChuva = {
        montarJanela,
        analisarAgora,
        criarGrafico,
        obterOpcoes,
    };
})();
