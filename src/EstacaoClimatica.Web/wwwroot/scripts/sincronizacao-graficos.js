'use strict';

(function () {
    const graficosPorGrupo = new Map();
    const assinaturaAtivaPorGrupo = new Map();

    function registrar(grafico, grupo) {
        if (!grafico || !grupo) return;
        desregistrar(grafico);

        const graficos = graficosPorGrupo.get(grupo) || new Set();
        graficos.add(grafico);
        graficosPorGrupo.set(grupo, graficos);
        assinaturaAtivaPorGrupo.delete(grupo);
        grafico.$grupoSincronizacao = grupo;
    }

    function desregistrar(grafico) {
        const grupo = grafico?.$grupoSincronizacao;
        if (!grupo) return;

        const graficos = graficosPorGrupo.get(grupo);
        graficos?.delete(grafico);
        if (!graficos?.size) {
            graficosPorGrupo.delete(grupo);
            assinaturaAtivaPorGrupo.delete(grupo);
        }
        delete grafico.$grupoSincronizacao;
    }

    function tratarInteracao(graficoOrigem, evento, elementosAtivos) {
        const grupo = graficoOrigem?.$grupoSincronizacao;
        if (!grupo) return;

        if (evento?.type === 'mouseout' || !elementosAtivos?.length) {
            limparGrupo(grupo);
            return;
        }

        const indice = elementosAtivos[0].index;
        const chaveTemporal = graficoOrigem.$chavesSincronizacao?.[indice] || null;
        sincronizarIndice(grupo, indice, chaveTemporal, graficoOrigem);
    }

    function sincronizarIndice(grupo, indice, chaveTemporal = null, graficoOrigem = null) {
        const assinatura = chaveTemporal || indice;
        if (!Number.isInteger(indice) || assinaturaAtivaPorGrupo.get(grupo) === assinatura) return;

        const graficos = graficosPorGrupo.get(grupo);
        if (!graficos?.size) return;
        assinaturaAtivaPorGrupo.set(grupo, assinatura);

        for (const grafico of [...graficos]) {
            if (!grafico?.canvas?.isConnected) {
                desregistrar(grafico);
                continue;
            }
            if (grafico === graficoOrigem) continue;
            ativarIndice(grafico, localizarIndiceTemporal(grafico, indice, chaveTemporal));
        }
    }

    function localizarIndiceTemporal(grafico, indiceOriginal, chaveTemporal) {
        const chaves = grafico.$chavesSincronizacao;
        if (!chaveTemporal || !Array.isArray(chaves) || !chaves.length) return indiceOriginal;

        const indiceExato = chaves.indexOf(chaveTemporal);
        if (indiceExato >= 0) return indiceExato;

        const instanteAlvo = converterInstante(chaveTemporal);
        if (instanteAlvo === null) return indiceOriginal;

        let melhorIndice = -1;
        let menorDiferenca = Number.POSITIVE_INFINITY;
        chaves.forEach((chave, indice) => {
            const instante = converterInstante(chave);
            if (instante === null) return;
            const diferenca = Math.abs(instante - instanteAlvo);
            if (diferenca < menorDiferenca) {
                menorDiferenca = diferenca;
                melhorIndice = indice;
            }
        });

        return menorDiferenca <= 45 * 60 * 1000 ? melhorIndice : -1;
    }

    function converterInstante(valor) {
        const instante = new Date(String(valor || '').replace(' ', 'T')).getTime();
        return Number.isFinite(instante) ? instante : null;
    }

    function ativarIndice(grafico, indice) {
        if (!Number.isInteger(indice) || indice < 0) {
            limparGrafico(grafico);
            return;
        }

        const elementos = (grafico.data?.datasets || []).flatMap((serie, indiceSerie) => {
            const valor = obterValorPonto(serie.data?.[indice]);
            return Number.isFinite(valor) ? [{ datasetIndex: indiceSerie, index: indice }] : [];
        });
        const primeiro = elementos[0];
        const elemento = primeiro ? grafico.getDatasetMeta?.(primeiro.datasetIndex)?.data?.[indice] : null;
        const area = grafico.chartArea || {};
        const posicao = elemento?.tooltipPosition?.() || {
            x: grafico.scales?.x?.getPixelForValue?.(indice) ?? area.left ?? 0,
            y: area.top ?? 0
        };

        grafico.setActiveElements?.(elementos);
        grafico.tooltip?.setActiveElements?.(elementos, posicao);
        grafico.update?.('none');
    }

    function obterValorPonto(ponto) {
        if (ponto === null || ponto === undefined || ponto === '') return null;
        return Number(ponto && typeof ponto === 'object' ? ponto.y : ponto);
    }

    function limparGrafico(grafico) {
        grafico.setActiveElements?.([]);
        grafico.tooltip?.setActiveElements?.([], { x: 0, y: 0 });
        grafico.update?.('none');
    }

    function limparGrupo(grupo) {
        const graficos = graficosPorGrupo.get(grupo);
        if (!graficos?.size || !assinaturaAtivaPorGrupo.has(grupo)) return;
        assinaturaAtivaPorGrupo.delete(grupo);

        for (const grafico of [...graficos]) {
            if (!grafico?.canvas?.isConnected) {
                desregistrar(grafico);
                continue;
            }
            limparGrafico(grafico);
        }
    }

    window.SincronizadorGraficos = { registrar, desregistrar, tratarInteracao, sincronizarIndice, limparGrupo };
})();
