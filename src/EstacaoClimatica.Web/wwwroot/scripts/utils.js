
function renderizarMensagemGrafico(cartaoOuId, mensagem, tipo = 'empty') {
    const elementoDom = typeof cartaoOuId === 'string' ? document.getElementById(cartaoOuId) : cartaoOuId;
    if (!elementoDom) return;
    limparMensagemGrafico(elementoDom);
    const status = document.createElement('p');
    status.className = `chart-message state-message state-message--${tipo}`;
    status.innerText = mensagem;
    elementoDom.appendChild(status);
}

function limparMensagemGrafico(cartaoOuId) {
    const elementoDom = typeof cartaoOuId === 'string' ? document.getElementById(cartaoOuId) : cartaoOuId;
    const status = elementoDom ? elementoDom.querySelector('.chart-message') : null;
    if (status) status.remove();
}

function renderizarErro(id, mensagem, tipo = 'error') {
    const elementoDom = document.getElementById(id);
    if (!elementoDom) return;
    elementoDom.innerHTML = `<p class="state-message state-message--${tipo}">${escaparHtml(mensagem)}</p>`;
}

function definirCarregando(estaCarregando) {
    document.getElementById('loadingBar')?.classList.toggle('is-active', estaCarregando);
}

function deveMostrarFaixaConforto(seriesClimaticas) {
    return Boolean(obterFaixaConfortoSerie(seriesClimaticas));
}

function obterFaixaConfortoSerie(seriesClimaticas) {
    const chave = `${seriesClimaticas?.id || ''} ${seriesClimaticas?.label || ''}`.toLowerCase();
    if (chave.includes('umidade')) return faixaConfortoUmidade;
    if (chave.includes('aquario') && chave.includes('temp')) return faixaConfortoAquario;
    if (seriesClimaticas?.suffix === '°' && (chave.includes('temp') || chave.includes('sensação') || chave.includes('sensacao'))) return faixaConforto;
    return null;
}

function obterEscalaValores(valores) {
    const valoresValidos = valores.filter(Number.isFinite);
    if (!valoresValidos.length) return { min: 20, max: 30 };
    const minimo = Math.min(...valoresValidos);
    const maximo = Math.max(...valoresValidos);
    return minimo === maximo ? { min: minimo - 1, max: maximo + 1 } : { min: minimo, max: maximo };
}

function obterCorMapaCalor(valor, escala = { min: 20, max: 30 }) {
    if (!Number.isFinite(valor)) return 'rgba(71, 85, 105, 0.18)';
    const proporcao = Math.min(1, Math.max(0, (valor - escala.min) / (escala.max - escala.min)));
    if (proporcao < 0.5) return interpolarCor([56, 189, 248], [52, 211, 153], proporcao / 0.5);
    return interpolarCor([52, 211, 153], [251, 113, 133], (proporcao - 0.5) / 0.5);
}

function interpolarCor(de, ate, proporcao) {
    const cor = de.map((inicio, indice) => Math.round(inicio + (ate[indice] - inicio) * proporcao));
    return `rgba(${cor[0]}, ${cor[1]}, ${cor[2]}, 0.72)`;
}

function mesclarRecursivamente(destino, origem) {
    const resultado = { ...destino };
    for (const chave of Object.keys(origem)) {
        if (origem[chave] && typeof origem[chave] === 'object' && !Array.isArray(origem[chave])) {
            resultado[chave] = mesclarRecursivamente(destino[chave] || {}, origem[chave]);
        } else {
            resultado[chave] = origem[chave];
        }
    }
    return resultado;
}

function mapearIntervalo(valor, minimoEntrada, maximoEntrada, minimoSaida, maximoSaida) {
    return (valor - minimoEntrada) * (maximoSaida - minimoSaida) / (maximoEntrada - minimoEntrada) + minimoSaida;
}

function limitar(valor, minimo, maximo) {
    return Math.min(maximo, Math.max(minimo, valor));
}

function paraDataFirebase(dados) {
    return `${String(dados.getDate()).padStart(2, '0')}-${String(dados.getMonth() + 1).padStart(2, '0')}-${dados.getFullYear()}`;
}

function converterDataFirebaseParaEntrada(textoData) {
    const [dia, mes, ano] = textoData.split('-');
    return `${ano}-${mes}-${dia}`;
}

function converterDataEntradaParaFirebase(textoData) {
    const [ano, mes, dia] = textoData.split('-');
    return `${dia}-${mes}-${ano}`;
}

function formatarMetrica(valor, sufixo) {
    return valor == null ? '--' : `${Number(valor).toFixed(2)}${sufixo}`;
}

function formatarVariacao(valor, sufixo) {
    if (valor == null) return '--';
    return `${valor > 0 ? '+' : ''}${Number(valor).toFixed(2)}${sufixo}`;
}

function formatarNumero(valor) {
    return Number.isFinite(valor) ? valor.toFixed(2) : '--';
}

function formatarTempo(valor) {
    if (!Number.isFinite(valor)) return '--';
    const totalMinutos = Math.round(valor * 60);
    const horas = Math.floor(totalMinutos / 60) % 24;
    const minutos = ((totalMinutos % 60) + 60) % 60;
    return `${String(horas).padStart(2, '0')}:${String(minutos).padStart(2, '0')}`;
}

function formatarDataFirebase(dataReferencia) {
    return dataReferencia ? dataReferencia.replaceAll('-', '/') : '--';
}

function formatarDataHoraRelatorio(dataReferencia) {
    return dataReferencia.toLocaleString('pt-BR', {
        day: '2-digit',
        month: '2-digit',
        year: 'numeric',
        hour: '2-digit',
        minute: '2-digit'
    });
}

function escaparHtml(valor) {
    return String(valor ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#039;');
}

function gerarIdentificadorUrl(valor) {
    return String(valor)
        .normalize('NFD')
        .replace(/[\u0300-\u036f]/g, '')
        .toLowerCase()
        .replace(/[^a-z0-9]+/g, '-')
        .replace(/(^-|-$)/g, '');
}

Object.assign(window, {
    renderizarMensagemGrafico,
    limparMensagemGrafico,
    renderizarErro,
    definirCarregando,
    deveMostrarFaixaConforto,
    obterFaixaConfortoSerie,
    obterEscalaValores,
    obterCorCalor: obterCorMapaCalor,
    interpolarCor,
    mesclarProfundo: mesclarRecursivamente,
    mapearIntervalo,
    limitarNumero: limitar,
    paraDataFirebase,
    paraDataInput: converterDataFirebaseParaEntrada,
    deDataInput: converterDataEntradaParaFirebase,
    formatarMetrica,
    formatarVariacao,
    formatarNumero,
    formatarHora: formatarTempo,
    formatarDataFirebase,
    formatarDataHora: formatarDataHoraRelatorio,
    escaparHtml,
    slug: gerarIdentificadorUrl
});
