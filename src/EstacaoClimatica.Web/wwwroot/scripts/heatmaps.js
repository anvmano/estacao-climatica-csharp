
function renderizarAvancado(visualizacoesAvancadas, recipientes = idsAvancados.quarto) {
    if (!visualizacoesAvancadas) return;
    renderizarVisualizacaoAvancadaSegura(
        () => renderizarCalendarioClimaticoMensal(visualizacoesAvancadas.monthlyCalendar, recipientes.monthlyCalendar),
        recipientes.monthlyCalendar,
        mensagensAvancadas.calendario
    );
    renderizarVisualizacaoAvancadaSegura(
        () => renderizarMapaCalorHorario(visualizacoesAvancadas.hourlyHeatmap, recipientes.hourlyHeatmap),
        recipientes.hourlyHeatmap,
        mensagensAvancadas.heatmapHorario
    );
    renderizarVisualizacaoAvancadaSegura(
        () => renderizarMapaCalorSemanal(visualizacoesAvancadas.weeklyHeatmap, recipientes.weeklyHeatmap),
        recipientes.weeklyHeatmap,
        mensagensAvancadas.mapaSemanal
    );
}

function renderizarVisualizacaoAvancadaSegura(renderizar, idRecipiente, mensagemErro) {
    try {
        renderizar();
    } catch (erro) {
        console.warn(mensagemErro, erro);
        renderizarErro(idRecipiente, mensagemErro, 'error');
    }
}

function renderizarCalendarioClimaticoMensal(celulas, idRecipiente) {
    const elementoDom = document.getElementById(idRecipiente);
    if (!elementoDom) return;
    elementoDom.innerHTML = '';

    const [diaSelecionado, mesSelecionado, anoSelecionado] = dataSelecionada.split('-').map(Number);
    const primeiroDiaSemana = new Date(anoSelecionado, mesSelecionado - 1, 1).getDay();
    const escala = obterEscalaValores(celulas.map(celula => celula.value));

    // Cabeçalho e deslocamento preservam a posição real do primeiro dia no calendário mensal.
    ['D', 'S', 'T', 'Q', 'Q', 'S', 'S'].forEach(rotulo => {
        const cabecalho = document.createElement('span');
        cabecalho.className = 'calendar-heatmap__weekday';
        cabecalho.textContent = rotulo;
        elementoDom.appendChild(cabecalho);
    });

    for (let i = 0; i < primeiroDiaSemana; i++) {
        const vazio = document.createElement('span');
        vazio.className = 'heatmap-cell heatmap-cell--empty';
        elementoDom.appendChild(vazio);
    }

    celulas.forEach(celula => {
        const dia = Number(celula.label);
        const no = criarCelulaCalor(celula.label, celula.value, 'calendar-heatmap__day', escala);
        if (dia === diaSelecionado) no.classList.add('is-selected');
        no.title = celula.value == null
            ? `${String(dia).padStart(2, '0')}/${String(mesSelecionado).padStart(2, '0')} sem dados`
            : `${String(dia).padStart(2, '0')}/${String(mesSelecionado).padStart(2, '0')} média ${celula.value.toFixed(1)}°C`;
        elementoDom.appendChild(no);
    });
}

function renderizarMapaCalorHorario(celulas, idRecipiente) {
    const elementoDom = document.getElementById(idRecipiente);
    if (!elementoDom) return;
    elementoDom.innerHTML = '';

    const escala = obterEscalaValores(celulas.map(celula => celula.value));
    const hoje = new Date();
    const destacarHoraAtual = dataSelecionada === paraDataFirebase(hoje);
    const horaAtual = hoje.getHours();
    celulas.forEach(celula => {
        const no = criarCelulaCalor(celula.label, celula.value, 'hourly-heatmap__cell', escala);
        if (destacarHoraAtual && Number.parseInt(celula.label, 10) === horaAtual) no.classList.add('is-selected');
        no.title = celula.value == null ? `${celula.label} sem dados` : `${celula.label} média ${celula.value.toFixed(1)}°C`;
        elementoDom.appendChild(no);
    });
}

function renderizarMapaCalorSemanal(celulas, idRecipiente) {
    const elementoDom = document.getElementById(idRecipiente);
    if (!elementoDom) return;
    elementoDom.innerHTML = '';

    const escala = obterEscalaValores(celulas.map(celula => celula.value));
    const rotulos = ['Dom', 'Seg', 'Ter', 'Qua', 'Qui', 'Sex', 'Sáb'];
    const hoje = new Date();
    const destacarCelulaAtual = dataSelecionada === paraDataFirebase(hoje);
    const diaSemanaAtual = hoje.getDay();
    const horaAtual = hoje.getHours();

    // A primeira linha cria o eixo de horas; cada linha seguinte representa um dia da semana.
    elementoDom.appendChild(criarCelulaEixo('', 'weekly-heatmap__axis--corner'));
    for (let hora = 0; hora < 24; hora++) elementoDom.appendChild(criarCelulaEixo(`${hora}h`));

    for (let diaSemana = 0; diaSemana < 7; diaSemana++) {
        elementoDom.appendChild(criarCelulaEixo(rotulos[diaSemana], 'weekly-heatmap__day'));
        celulas.filter(celula => celula.weekday === diaSemana).forEach(celula => {
            const no = criarCelulaCalor('', celula.value, 'weekly-heatmap__cell', escala, false);
            if (destacarCelulaAtual && diaSemana === diaSemanaAtual && Number(celula.hour) === horaAtual) {
                no.classList.add('is-selected');
            }
            no.title = celula.value == null ? `${rotulos[diaSemana]} ${celula.hour}h sem dados` : `${rotulos[diaSemana]} ${celula.hour}h média ${celula.value.toFixed(1)}°C`;
            no.setAttribute('aria-label', no.title);
            elementoDom.appendChild(no);
        });
    }
}

function criarCelulaCalor(rotulo, valor, classeAdicional, escala, mostrarConteudo = true) {
    const celula = document.createElement('span');
    celula.className = `heatmap-cell ${classeAdicional}`;
    celula.style.backgroundColor = obterCorMapaCalor(valor, escala);
    if (mostrarConteudo) {
        celula.innerHTML = `<span>${escaparHtml(rotulo)}</span><strong>${valor == null ? '--' : valor.toFixed(1) + '°'}</strong>`;
    }
    return celula;
}

function criarCelulaEixo(rotulo, classeAdicional = '') {
    const celula = document.createElement('span');
    celula.className = `weekly-heatmap__axis ${classeAdicional}`;
    celula.textContent = rotulo;
    return celula;
}

Object.assign(window, {
    renderizarAvancado,
    renderizarVisualizacaoAvancadaSegura,
    renderizarCalendarioClimaticoMensal,
    renderizarHeatmapHorario: renderizarMapaCalorHorario,
    renderizarHeatmapSemanal: renderizarMapaCalorSemanal,
    criarCelulaCalor,
    criarCelulaEixo
});
