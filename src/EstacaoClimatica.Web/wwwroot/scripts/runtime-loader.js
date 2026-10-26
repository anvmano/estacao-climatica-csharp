'use strict';

(function () {
    const carregamentos = new Map();
    let controlesPreparados = false;

    function carregarScript(caminho) {
        if (carregamentos.has(caminho)) return carregamentos.get(caminho);
        const promessa = new Promise((resolver, rejeitar) => {
            const script = document.createElement('script');
            script.src = caminho;
            script.onload = resolver;
            script.onerror = () => { script.remove(); carregamentos.delete(caminho); rejeitar(new Error('Falha ao carregar recurso. Tente novamente.')); };
            document.head.appendChild(script);
        });
        carregamentos.set(caminho, promessa);
        return promessa;
    }

    async function carregarChart() {
        if (!window.Chart) await carregarScript('vendor/chart.umd.min.js');
        Chart.register(pluginFaixaConforto);
    }

    const carregarRelatorio = () => carregarScript('scripts/pdf-report.js?v=20261024');

    function carregarCssZoom() {
        const caminho = 'styles/zoom.css?v=20261025';
        if (carregamentos.has(caminho)) return carregamentos.get(caminho);
        const promessa = new Promise((resolver, rejeitar) => {
            const estilo = document.createElement('link');
            estilo.rel = 'stylesheet';
            estilo.href = caminho;
            estilo.onload = resolver;
            estilo.onerror = () => {
                estilo.remove();
                carregamentos.delete(caminho);
                rejeitar(new Error('Falha ao carregar o estilo do zoom. Tente novamente.'));
            };
            document.head.appendChild(estilo);
        });
        carregamentos.set(caminho, promessa);
        return promessa;
    }

    function prepararControlesInternos() {
        if (controlesPreparados) return;
        controlesPreparados = true;
        const exportar = document.getElementById('btnExportData');
        const atualizarRotulo = () => { if (exportar) exportar.textContent = document.querySelector('input[name="exportFormat"]:checked')?.value === 'json' ? 'Exportar JSON' : 'Exportar PDF'; };
        document.querySelectorAll('input[name="exportFormat"]').forEach(entrada => entrada.addEventListener('change', atualizarRotulo));
        atualizarRotulo();
        exportar?.addEventListener('click', async () => {
            exportar.disabled = true;
            try { await carregarRelatorio(); await exportarAbaAtiva(exportar); }
            catch (erro) { window.alert(erro.message); }
            finally { exportar.disabled = false; atualizarRotulo(); }
        });
        document.getElementById('btnRetryExport')?.addEventListener('click', () => exportar?.click());
        const botaoChat = document.getElementById('aiChatToggle');
        const abrir = async () => {
            botaoChat.disabled = true;
            try {
                await carregarScript('scripts/chat.js?v=20261024');
                botaoChat.removeEventListener('click', abrir);
                configurar();
                if (!document.getElementById('privateApp').hidden) abrirChat();
            } catch (erro) { window.alert(erro.message); }
            finally { botaoChat.disabled = false; }
        };
        botaoChat?.addEventListener('click', abrir);
    }

    window.RecursosClimaticos = { carregarChart, carregarRelatorio, carregarCssZoom, prepararControlesInternos };
})();
