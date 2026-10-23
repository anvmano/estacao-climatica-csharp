using System.Globalization;
using System.Text;
using System.Text.Json;
using EstacaoClimatica.Application.Abstractions;
using EstacaoClimatica.Application.Configuration;
using EstacaoClimatica.Application.Utilities;
using EstacaoClimatica.Domain;

namespace EstacaoClimatica.Application.Services;

public sealed partial class ServicoAssistenteClimatico : IServicoAssistenteClimatico
{
    private const int DiasRecentesPadrao = 7;
    private readonly IRepositorioDadosClimaticos _repositorio;
    private readonly IClienteGeracaoTexto _clienteGeracaoTexto;
    private readonly OpcoesIa _opcoesIa;
    private readonly IReadOnlyDictionary<string, ConfiguracaoAmbiente> _ambientes;
    private readonly Func<DateTime> _obterAgora;

    public ServicoAssistenteClimatico(IRepositorioDadosClimaticos repositorio, IClienteGeracaoTexto clienteGeracaoTexto, OpcoesIa opcoesIa)
        : this(repositorio, clienteGeracaoTexto, opcoesIa, () => DateTime.Now)
    {
    }

    internal ServicoAssistenteClimatico(IRepositorioDadosClimaticos repositorio, IClienteGeracaoTexto clienteGeracaoTexto, OpcoesIa opcoesIa, Func<DateTime> obterAgora)
    {
        _repositorio = repositorio;
        _clienteGeracaoTexto = clienteGeracaoTexto;
        _opcoesIa = opcoesIa;
        _ambientes = CriarAmbientes();
        _obterAgora = obterAgora;
    }

    public async Task<ChatResponse> ResponderAsync(ChatRequest requisicao, CancellationToken tokenCancelamento)
    {
        var pergunta = (requisicao.Question ?? "").Trim();
        if (string.IsNullOrWhiteSpace(pergunta))
        {
            return CriarRespostaLocal("Digite uma pergunta sobre os dados da estação climática.", IntencaoVazia());
        }

        var dataSelecionada = DataFirebase.Normalizar(requisicao.SelectedDate);
        var abaAtiva = requisicao.ActiveTab ?? "Tab1";
        var intencao = await ResolverIntencaoAsync(
            pergunta, new ContextoAssistente(new DadosDashboardFirebase(null, null, null, null), abaAtiva, dataSelecionada),
            requisicao.PreviousIntent, tokenCancelamento);
        var metricasSolicitadas = intencao.Metrics.Count > 0 ? intencao.Metrics : InferirMetricasPergunta(NormalizarTexto(pergunta));
        var consultarSolar = intencao.IncludeSolar || intencao.Operation.StartsWith("solar_", StringComparison.Ordinal)
            || metricasSolicitadas.Any(metrica => NormalizarTexto(metrica) == "ciclo_solar");
        var consultarAqi = !consultarSolar && metricasSolicitadas.Any(EhMetricaAqi);
        IReadOnlyList<ConfiguracaoAmbiente> ambientesParaBusca = consultarSolar ? [] : consultarAqi
            ? new[] { _ambientes["sala"] }
            : RedirecionarAmbientesPorMetricasExclusivas(intencao.Environments, metricasSolicitadas);
        var dados = await _repositorio.ObterDadosPorRamosAsync(
            ambientesParaBusca.Any(item => item.Key == "quarto"),
            consultarSolar,
            ambientesParaBusca.Any(item => item.Key == "aquario"),
            ambientesParaBusca.Any(item => item.Key == "sala"),
            tokenCancelamento);
        var contexto = new ContextoAssistente(dados, abaAtiva, dataSelecionada);
        var resultado = ExecutarConsulta(contexto, intencao, pergunta);

        if (resultado.NeedsClarification)
        {
            return CriarRespostaLocal(resultado.Message, CriarResumoIntencao(resultado));
        }

        var alternativa = FormatarResultadoAlternativo(resultado);
        if (!_opcoesIa.Enabled)
        {
            return new ChatResponse(alternativa, "local", CriarResumoIntencao(resultado));
        }

        try
        {
            var instrucao = MontarInstrucaoResposta(pergunta, resultado);
            var resposta = await _clienteGeracaoTexto.GerarTextoAsync(instrucao, tokenCancelamento);
            return new ChatResponse(string.IsNullOrWhiteSpace(resposta) ? alternativa : resposta.Trim(), "ai", CriarResumoIntencao(resultado));
        }
        catch
        {
            return new ChatResponse(alternativa, "local-fallback", CriarResumoIntencao(resultado));
        }
    }

    private async Task<IntencaoConsulta> ResolverIntencaoAsync(string pergunta, ContextoAssistente contexto, ChatIntentSummary? memoriaConversa, CancellationToken tokenCancelamento)
    {
        var normalizada = NormalizarTexto(pergunta);
        var mencionados = EncontrarAmbientesMencionados(normalizada);
        var ambienteAlternativo = ObterAmbientePorAbaAtiva(contexto.ActiveTab);
        var intencaoClassificada = await ClassificarIntencaoPerguntaAsync(pergunta, contexto, tokenCancelamento);
        var ambientesClassificados = ObterAmbientesDaIntencao(intencaoClassificada);
        var operacao = NormalizarOperacao(intencaoClassificada?.Operation, normalizada);
        var periodo = NormalizarPeriodo(intencaoClassificada, normalizada, contexto.SelectedDate, operacao);
        var faixaHoraria = NormalizarFaixaHoraria(intencaoClassificada?.Period) ?? ExtrairFaixaHorariaPergunta(normalizada);
        var hora = faixaHoraria is not null
            ? null
            : ExtrairHoraPergunta(normalizada)
            ?? NormalizarFiltroHora(intencaoClassificada?.Period?.Hour)
            ?? NormalizarFiltroHora(intencaoClassificada?.Hour);
        var metricas = NormalizarMetricas(intencaoClassificada);
        var intencaoSolar = TemPerguntaSolar(normalizada) || operacao.StartsWith("solar_", StringComparison.Ordinal) || intencaoClassificada?.Solar == true;
        var metricasFinais = intencaoSolar ? new[] { "ciclo_solar" } : metricas;

        var intencao = new IntencaoConsulta(
            mencionados.Count > 0 ? mencionados : (ambientesClassificados.Count > 0 ? ambientesClassificados : new[] { ambienteAlternativo }),
            metricasFinais,
            operacao,
            periodo,
            NormalizarTexto(intencaoClassificada?.Criterion),
            hora,
            faixaHoraria,
            intencaoClassificada?.Confidence,
            intencaoClassificada?.NeedsClarification == true,
            intencaoClassificada?.ClarificationQuestion,
            intencaoSolar);

        intencao = AplicarMemoriaCurta(intencao, normalizada, memoriaConversa);
        return AplicarAtalhosContextuais(intencao, normalizada, contexto.ActiveTab);
    }

    private async Task<IntencaoClassificada?> ClassificarIntencaoPerguntaAsync(string pergunta, ContextoAssistente contexto, CancellationToken tokenCancelamento)
    {
        if (!_opcoesIa.Enabled) return null;

        var instrucao = $$"""
Interprete a pergunta abaixo para um dashboard de estação climática. Ela pode conter erros de digitação, gírias, fala informal e português não padrão.
Responda somente JSON válido, sem markdown.

Schema obrigatório:
{
  "ambientes": ["sala" | "quarto" | "aquario"] ou [],
  "metricas": ["temperatura" | "sensacao_termica" | "umidade" | "pressao" | "ciclo_solar" | "aqi" | "iaq" | "qualidade_ar" | "ph" | "tds" | "turbidez" | "co" | "co2" | "acetona" | "alcool" | "amonia" | "tolueno"] ou [],
  "operacao": "media" | "maxima" | "minima" | "delta" | "tendencia" | "resumo" | "valor" | "ultima_medicao" | "comparar_dias" | "dia_mais_frio" | "dia_mais_quente" | "alertas" | "status_faixa" | "horario_maior_valor" | "horario_menor_valor" | "calendario_dia_maior_valor" | "calendario_dia_menor_valor" | "heatmap_hora_maior_valor" | "heatmap_hora_menor_valor" | "heatmap_semana_maior_valor" | "heatmap_semana_menor_valor" | "solar_maior_duracao_luz" | "solar_menor_duracao_luz" | "solar_tendencia_nascer" | "solar_tendencia_por" | "solar_comparar_nascer" | "solar_comparar_por" | "solar_duracao_dia" ou null,
    "periodo": {
    "tipo": "data_especifica" | "datas_relativas" | "ultimos_dias" | "ultimas_24h" | "intervalo" | "calendario" | "mes_selecionado" | "semana_selecionada" | "ano_selecionado" ou null,
    "data": "DD-MM-AAAA" | "hoje" | "ontem" | "anteontem" ou null,
    "datas": ["DD-MM-AAAA" | "hoje" | "ontem" | "anteontem"] ou [],
    "quantidade": número ou null,
    "inicio": "DD-MM-AAAA" ou null,
    "fim": "DD-MM-AAAA" ou null,
    "hora": "HH:mm" | "HH" ou null,
    "hora_inicio": "HH:mm" | "HH" ou null,
    "hora_fim": "HH:mm" | "HH" ou null
  },
  "criterio": "media_diaria" | "maxima_registrada" | "minima_registrada" | "mais_quente" | "mais_frio" ou null,
  "confianca": número de 0 a 1,
  "precisa_esclarecimento": true ou false,
  "pergunta_esclarecimento": string ou null,
  "solar": true ou false
}

Regras:
- "últimos dias", "esses últimos dias" ou frase parecida significa últimos {{DiasRecentesPadrao}} dias.
- "últimas 24 horas", "últimas 24h" ou frase parecida deve usar periodo.tipo "ultimas_24h".
- "dia mais frio" usa menor média diária, exceto se pedir explicitamente menor registro.
- "dia mais quente" usa maior média diária, exceto se pedir explicitamente maior registro.
- Se ambiente não aparecer, deixe "ambientes" vazio. O código usará a aba ativa.
- Se a pergunta pedir o valor atual/agora ou apenas perguntar "qual o valor" de uma métrica sem pedir média, máxima, mínima, tendência ou período inteiro, use operação "ultima_medicao".
- "ontem ou anteontem foi mais quente que hoje" deve usar operação "comparar_dias" e datas ["ontem", "anteontem", "hoje"].
- Se a pergunta mencionar AQI, IAQ, qualidade do ar, índice de qualidade do ar ou ar da sala, use métrica "qualidade_ar".
- Se a pergunta mencionar faixa, conforto, ideal, normal, dentro da faixa, fora da faixa, pior horário fora da faixa ou quantas horas fora, use operação "status_faixa".
- Se métrica não aparecer mas a pergunta falar frio/quente, use "temperatura".
- Se a pergunta tiver "às 14h", "14:00", "as 14", preencha "hora": "14".
- Se a pergunta tiver faixa horária como "entre 8h e 18h", "das 8 as 18" ou "de 8h a 18h", preencha "hora_inicio" e "hora_fim".
- Se a pergunta pedir "qual horário foi mais quente", "qual horário teve maior umidade" ou equivalente, use operação "horario_maior_valor".
- Se a pergunta pedir "qual horário foi mais frio", "qual horário teve menor pressão" ou equivalente, use operação "horario_menor_valor".
- Se a pergunta pedir "qual dia do mês" ou "calendário climático", use operação "calendario_dia_maior_valor" ou "calendario_dia_menor_valor" e periodo.tipo "mes_selecionado".
- Se a pergunta pedir "qual hora costuma" ou "heatmap por hora", use operação "heatmap_hora_maior_valor" ou "heatmap_hora_menor_valor".
- Se a pergunta pedir "qual dia/hora da semana", "mapa semanal" ou "pico semanal", use operação "heatmap_semana_maior_valor" ou "heatmap_semana_menor_valor" e periodo.tipo "semana_selecionada".
- Se a pergunta pedir maior/menor duração de luz, duração do dia, tempo de luz, luz solar, dia mais longo ou dia mais curto, use métrica "ciclo_solar", solar true e operação solar correspondente.
- Para "dia mais longo" ou "dia com mais tempo de luz solar", use periodo.tipo "ano_selecionado" quando nenhum mês/período for informado. Se houver mês informado, use "mes_selecionado".
- Se a pergunta perguntar se nascer do sol ou pôr do sol está ficando mais cedo/tarde, use operação "solar_tendencia_nascer" ou "solar_tendencia_por".
- Se a pergunta pedir comparar nascer do sol ou pôr do sol da semana, use "solar_comparar_nascer" ou "solar_comparar_por" e periodo.tipo "semana_selecionada".
- Se a pergunta mencionar nascer do sol, pôr do sol, zênite, amanhecer, anoitecer, sol ou ciclo solar, use métrica "ciclo_solar" e solar true.
- Não responda a pergunta do usuário, apenas classifique.

Data selecionada na página: {{FormatarData(contexto.SelectedDate)}}
Aba ativa: {{ObterRotuloAba(contexto.ActiveTab)}}
Pergunta: {{pergunta}}
""";

        try
        {
            var resposta = await _clienteGeracaoTexto.GerarTextoAsync(TruncarPrompt(instrucao), tokenCancelamento);
            return InterpretarJsonIntencao(resposta);
        }
        catch
        {
            return null;
        }
    }

    private ResultadoConsultaAssistente ExecutarConsulta(ContextoAssistente contexto, IntencaoConsulta intencao, string pergunta)
    {
        if (intencao.NeedsClarification)
        {
            return ResultadoConsultaAssistente.CriarEsclarecimento(intencao.ClarificationQuestion ?? "Preciso de mais detalhes para responder com segurança.", pergunta, intencao);
        }

        var nomesMetricasSolicitadas = intencao.Metrics.Count > 0 ? intencao.Metrics : InferirMetricasPergunta(NormalizarTexto(pergunta));
        if (intencao.IncludeSolar || intencao.Operation.StartsWith("solar_", StringComparison.Ordinal) || nomesMetricasSolicitadas.Any(metrica => NormalizarTexto(metrica) == "ciclo_solar"))
        {
            return ExecutarConsultaSolar(contexto, intencao, pergunta);
        }

        if (nomesMetricasSolicitadas.Any(EhMetricaAqi))
        {
            return ExecutarConsultaAqi(contexto, intencao, pergunta);
        }

        var ambientes = RedirecionarAmbientesPorMetricasExclusivas(intencao.Environments.ToArray(), nomesMetricasSolicitadas).ToArray();
        var metricas = ResolverMetricasAmbientes(ambientes, nomesMetricasSolicitadas, pergunta).ToArray();
        var datasPeriodo = ResolverDatasPeriodo(intencao.Period).ToArray();

        if (ambientes.Length == 0)
        {
            return ResultadoConsultaAssistente.CriarEsclarecimento("Não consegui identificar o ambiente. Você quer consultar Sala, Quarto ou Aquário?", pergunta, intencao);
        }

        if (datasPeriodo.Length == 0)
        {
            return ResultadoConsultaAssistente.CriarEsclarecimento("Não consegui identificar o período da consulta. Tente informar uma data ou período.", pergunta, intencao);
        }

        var resultadosAmbientes = ambientes
            .Select(ambiente => ExecutarConsultaAmbiente(contexto, ambiente, metricas, datasPeriodo, intencao))
            .ToArray();

        return new ResultadoConsultaAssistente(
            pergunta,
            intencao,
            new IntencaoCalculada(
                ambientes.Select(ambiente => ambiente.Label).ToArray(),
                metricas.Select(metrica => metrica.Label).Distinct().ToArray(),
                intencao.Operation,
                intencao.Criterion,
                FormatarRotuloPeriodo(intencao.Period, datasPeriodo),
                datasPeriodo),
            resultadosAmbientes,
            false,
            "");
    }

    private ResultadoConsultaAmbiente ExecutarConsultaAmbiente(ContextoAssistente contexto, ConfiguracaoAmbiente ambiente, IReadOnlyList<ConfiguracaoMetrica> metricasSolicitadas, IReadOnlyList<string> datasPeriodo, IntencaoConsulta intencao)
    {
        var dadosAmbiente = ObterDadosAmbiente(contexto.Data, ambiente);
        var chavesPermitidas = ambiente.Metrics.Select(metrica => metrica.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var metricas = metricasSolicitadas.Where(metrica => chavesPermitidas.Contains(metrica.Key)).ToArray();
        if (metricas.Length == 0) metricas = new[] { ambiente.Metrics[0] };
        var janelaMovel = ObterJanelaMovel(intencao.Period);

        var resultadosMetricas = metricas
            .Select(metrica =>
            {
                var estatisticasDiarias = datasPeriodo
                    .Select(data => MontarEstatisticasDiarias(dadosAmbiente, metrica, data, intencao.Hour, intencao.HourRange, janelaMovel))
                    .Where(dia => dia is not null)
                    .Cast<EstatisticasDiarias>()
                    .ToArray();

                return MontarResultadoMetrica(ambiente, metrica, estatisticasDiarias, datasPeriodo, intencao);
            })
            .ToArray();

        return new ResultadoConsultaAmbiente(ambiente.Label, resultadosMetricas);
    }


    private ResultadoConsultaMetrica MontarResultadoMetrica(ConfiguracaoAmbiente ambiente, ConfiguracaoMetrica metrica, IReadOnlyList<EstatisticasDiarias> estatisticasDiarias, IReadOnlyList<string> datasPeriodo, IntencaoConsulta intencao)
    {
        var todosValores = estatisticasDiarias.SelectMany(dia => dia.Values).ToArray();
        var resultadoBase = new ResultadoConsultaMetrica(
            ambiente.Label,
            metrica.Label,
            metrica.Unit,
            intencao.Operation,
            intencao.Criterion.Length > 0 ? intencao.Criterion : CriterioPadraoOperacao(intencao.Operation),
            FormatarRotuloPeriodo(intencao.Period, datasPeriodo),
            datasPeriodo.Select(FormatarData).ToArray(),
            estatisticasDiarias.Select(dia => dia.DateLabel).ToArray(),
            todosValores.Length,
            false,
            "",
            null,
            null,
            null,
            null,
            null,
            "",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            Array.Empty<ResumoDiario>());

        if (intencao.Operation == "status_faixa")
        {
            return MontarResultadoFaixaConforto(resultadoBase, ambiente, metrica, estatisticasDiarias, datasPeriodo, intencao);
        }

        if (intencao.Operation is "horario_maior_valor" or "horario_menor_valor")
        {
            return MontarResultadoExtremoHorario(resultadoBase, metrica, estatisticasDiarias, intencao);
        }

        if (intencao.Operation is "calendario_dia_maior_valor" or "calendario_dia_menor_valor")
        {
            return MontarResultadoExtremoDiaCalendario(resultadoBase, estatisticasDiarias, intencao);
        }

        if (intencao.Operation is "heatmap_hora_maior_valor" or "heatmap_hora_menor_valor")
        {
            return MontarResultadoExtremoHoraDia(resultadoBase, estatisticasDiarias, intencao);
        }

        if (intencao.Operation is "heatmap_semana_maior_valor" or "heatmap_semana_menor_valor")
        {
            return MontarResultadoExtremoFaixaSemanal(resultadoBase, estatisticasDiarias, intencao);
        }

        if (intencao.Operation == "ultima_medicao")
        {
            return MontarResultadoUltimaMedicao(resultadoBase, estatisticasDiarias);
        }

        if (intencao.Operation == "comparar_dias")
        {
            return MontarResultadoComparacaoDias(resultadoBase, estatisticasDiarias);
        }

        if (todosValores.Length == 0)
        {
            return resultadoBase with
            {
                SemDados = true,
                Mensagem = intencao.Hour is null
                    ? $"Sem dados de {metrica.Label} em {ambiente.Label} para {FormatarRotuloPeriodo(intencao.Period, datasPeriodo)}."
                    : $"Sem dados de {metrica.Label} em {ambiente.Label} para {FormatarRotuloPeriodo(intencao.Period, datasPeriodo)} às {FormatarRotuloHora(intencao.Hour)}."
            };
        }

        if (intencao.Operation is "media" or "maxima" or "minima" or "delta" or "tendencia")
        {
            return MontarResultadoEstatistico(resultadoBase, estatisticasDiarias, intencao);
        }

        var estatisticasGerais = CalcularEstatisticas(todosValores);
        var resumosDiarios = estatisticasDiarias.Select(dia => new ResumoDiario(
            dia.DateLabel,
            Arredondar(dia.Stats.Avg),
            Arredondar(dia.Stats.Min),
            Arredondar(dia.Stats.Max),
            Arredondar(dia.Stats.Delta),
            dia.Values.Count)).ToArray();

        return resultadoBase with
        {
            Media = Arredondar(estatisticasGerais.Avg),
            Minima = Arredondar(estatisticasGerais.Min),
            Maxima = Arredondar(estatisticasGerais.Max),
            Delta = Arredondar(estatisticasGerais.Delta),
            Tendencia = TendenciaDaDiferenca(estatisticasGerais.Delta),
            DiaMaisFrio = SelecionarDia(resumosDiarios, resumo => resumo.Media, "min"),
            DiaMaisQuente = SelecionarDia(resumosDiarios, resumo => resumo.Media, "max"),
            MenorRegistro = SelecionarDia(resumosDiarios, resumo => resumo.Minima, "min"),
            MaiorRegistro = SelecionarDia(resumosDiarios, resumo => resumo.Maxima, "max"),
            Comparacao = MontarComparacao(resumosDiarios),
            PorDia = resumosDiarios
        };
    }

    private ResultadoConsultaMetrica MontarResultadoComparacaoDias(ResultadoConsultaMetrica resultadoBase, IReadOnlyList<EstatisticasDiarias> estatisticasDiarias)
    {
        var resumosDiarios = estatisticasDiarias.Select(dia => new ResumoDiario(
            dia.DateLabel,
            Arredondar(dia.Stats.Avg),
            Arredondar(dia.Stats.Min),
            Arredondar(dia.Stats.Max),
            Arredondar(dia.Stats.Delta),
            dia.Values.Count)).ToArray();

        if (resumosDiarios.Length < 2)
        {
            return resultadoBase with
            {
                SemDados = true,
                Mensagem = $"Preciso de pelo menos dois dias com dados de {resultadoBase.Metrica} em {resultadoBase.Ambiente} para comparar."
            };
        }

        var ordenados = resumosDiarios
            .Where(dia => dia.Media is not null)
            .OrderByDescending(dia => dia.Media)
            .ToArray();
        if (ordenados.Length < 2)
        {
            return resultadoBase with
            {
                SemDados = true,
                Mensagem = $"Preciso de pelo menos dois dias com dados de {resultadoBase.Metrica} em {resultadoBase.Ambiente} para comparar."
            };
        }

        var vencedor = ordenados[0];
        var referencia = ordenados[1];

        return resultadoBase with
        {
            TipoResultado = "comparacao_dias",
            Criterio = "maior_media_diaria",
            Media = vencedor.Media,
            Delta = Arredondar((vencedor.Media ?? 0) - (referencia.Media ?? 0)),
            Comparacao = MontarComparacao(resumosDiarios),
            PorDia = ordenados
        };
    }

    private ResultadoConsultaMetrica MontarResultadoEstatistico(ResultadoConsultaMetrica resultadoBase, IReadOnlyList<EstatisticasDiarias> estatisticasDiarias, IntencaoConsulta intencao)
    {
        var valores = estatisticasDiarias.SelectMany(dia => dia.Values).ToArray();
        var registros = estatisticasDiarias
            .SelectMany(dia => dia.Samples)
            .OrderBy(amostra => DataFirebase.InterpretarDataFirebase(amostra.DateLabel))
            .ThenBy(amostra => amostra.TimeLabel)
            .ToArray();
        var estatisticas = CalcularEstatisticas(valores);

        if (intencao.Operation == "media")
        {
            return resultadoBase with
            {
                TipoResultado = "estatistica_media",
                Media = Arredondar(estatisticas.Avg),
                Criterio = "media_registrada"
            };
        }

        if (intencao.Operation is "maxima" or "minima")
        {
            var registro = intencao.Operation == "maxima"
                ? registros.OrderByDescending(amostra => amostra.Value).First()
                : registros.OrderBy(amostra => amostra.Value).First();
            return resultadoBase with
            {
                TipoResultado = "estatistica_extremo",
                Criterio = intencao.Operation == "maxima" ? "maior_registro" : "menor_registro",
                Estatistica = new ResultadoEstatistica(Arredondar(registro.Value), registro.DateLabel, registro.TimeLabel)
            };
        }

        var primeiro = registros.First();
        var ultimo = registros.Last();
        var diferenca = Arredondar(ultimo.Value - primeiro.Value);
        return resultadoBase with
        {
            TipoResultado = intencao.Operation == "tendencia" ? "tendencia_periodo" : "variacao_periodo",
            Variacao = new ResultadoVariacaoPeriodo(
                Arredondar(primeiro.Value), primeiro.DateLabel, primeiro.TimeLabel,
                Arredondar(ultimo.Value), ultimo.DateLabel, ultimo.TimeLabel,
                diferenca, TendenciaDaDiferenca(ultimo.Value - primeiro.Value))
        };
    }

    private ResultadoConsultaMetrica MontarResultadoUltimaMedicao(ResultadoConsultaMetrica resultadoBase, IReadOnlyList<EstatisticasDiarias> estatisticasDiarias)
    {
        var maisRecente = estatisticasDiarias
            .SelectMany(dia => dia.Samples)
            .OrderByDescending(amostra => DataFirebase.InterpretarDataFirebase(amostra.Date))
            .ThenByDescending(amostra => LeitorRegistrosFirebase.ConverterHora(amostra.TimeLabel) is { } hora ? hora.Hour * 3600 + hora.Minute * 60 + (hora.Second ?? 0) : -1)
            .FirstOrDefault();

        if (maisRecente is null)
        {
            return resultadoBase with
            {
                SemDados = true,
                Mensagem = $"Sem dados de {resultadoBase.Metrica} em {resultadoBase.Ambiente} para {resultadoBase.Periodo}."
            };
        }

        return resultadoBase with
        {
            Amostras = 0,
            TipoResultado = "consulta_horaria",
            Media = Arredondar(maisRecente.Value),
            Periodo = maisRecente.DateLabel,
            DatasConsultadas = new[] { maisRecente.DateLabel },
            AnaliseHoraria = new ResultadoAnaliseHoraria(maisRecente.DateLabel, maisRecente.TimeLabel, Arredondar(maisRecente.Value), null, null, 1, null)
        };
    }

    private EstatisticasDiarias? MontarEstatisticasDiarias(JsonElement? raiz, ConfiguracaoMetrica metrica, string data, string? hora, FaixaHoraria? faixaHoraria, JanelaMovel? janelaMovel)
    {
        var raizDia = LeitorRegistrosFirebase.FiltrarDataSelecionada(raiz, data);
        var amostras = LeitorRegistrosFirebase.EnumerarRegistros(raizDia)
            .Where(registro => hora is null || NormalizarHoraRegistro(registro.Horario) == hora)
            .Where(registro => HoraEstaNaFaixa(NormalizarHoraRegistro(registro.Horario), faixaHoraria))
            .Where(registro => EstaNaJanelaMovel(data, registro.Horario, janelaMovel))
            .Select(registro =>
            {
                var valor = LeitorNumerosJson.ObterNumero(registro.Item, metrica.Key);
                valor = metrica.EnvironmentKey == "aquario" ? NormalizadorMedicoesAquario.NormalizarValorMedicao(metrica.Key, valor) : valor;
                return valor is null ? null : new AmostraMetrica(data, FormatarData(data), FormatarHoraRegistro(registro.Horario), valor.Value);
            })
            .Where(amostra => amostra is not null)
            .Cast<AmostraMetrica>()
            .ToArray();

        var valores = amostras.Select(amostra => amostra.Value).ToArray();
        if (valores.Length == 0) return null;
        return new EstatisticasDiarias(data, FormatarData(data), valores, amostras, CalcularEstatisticas(valores));
    }

    private ResultadoConsultaMetrica MontarResultadoFaixaConforto(ResultadoConsultaMetrica resultadoBase, ConfiguracaoAmbiente ambiente, ConfiguracaoMetrica metrica, IReadOnlyList<EstatisticasDiarias> estatisticasDiarias, IReadOnlyList<string> datasPeriodo, IntencaoConsulta intencao)
    {
        var faixa = ObterFaixaConforto(ambiente, metrica);
        if (faixa is null)
        {
            return resultadoBase with
            {
                SemFaixa = true,
                Mensagem = $"Não há faixa de conforto configurada para {metrica.Label} em {ambiente.Label}."
            };
        }

        var amostras = estatisticasDiarias.SelectMany(dia => dia.Samples).ToArray();
        if (amostras.Length == 0)
        {
            return resultadoBase with
            {
                SemDados = true,
                Mensagem = intencao.Hour is null
                    ? intencao.HourRange is null
                        ? $"Sem dados de {metrica.Label} em {ambiente.Label} para {FormatarRotuloPeriodo(intencao.Period, datasPeriodo)}."
                        : $"Sem dados de {metrica.Label} em {ambiente.Label} para {FormatarRotuloPeriodo(intencao.Period, datasPeriodo)} entre {FormatarRotuloFaixaHora(intencao.HourRange)}."
                    : $"Sem dados de {metrica.Label} em {ambiente.Label} para {FormatarRotuloPeriodo(intencao.Period, datasPeriodo)} às {FormatarRotuloHora(intencao.Hour)}.",
                Conforto = new ResultadoFaixaConforto(FormatarFaixa(faixa.Value, metrica.Unit), faixa.Value.Min, faixa.Value.Max, "", 0, 0, 0, 0, 0, Array.Empty<string>(), null)
            };
        }

        var fora = amostras.Where(amostra => EstaForaDaFaixa(amostra.Value, faixa.Value)).ToArray();
        var dentro = amostras.Where(amostra => !EstaForaDaFaixa(amostra.Value, faixa.Value)).ToArray();
        var pior = fora
            .Select(amostra => new AmostraForaConforto(amostra.DateLabel, amostra.TimeLabel, Arredondar(amostra.Value), Arredondar(DistanciaDaFaixa(amostra.Value, faixa.Value)), amostra.Value < faixa.Value.Min ? "abaixo" : "acima"))
            .OrderByDescending(amostra => amostra.Distance)
            .FirstOrDefault();
        var intervalosFora = FaixasUnicas(fora).ToArray();

        return resultadoBase with
        {
            Amostras = amostras.Length,
            Conforto = new ResultadoFaixaConforto(
                FormatarFaixa(faixa.Value, metrica.Unit),
                faixa.Value.Min,
                faixa.Value.Max,
                fora.Length == 0 ? "dentro_da_faixa" : "fora_da_faixa",
                amostras.Length,
                dentro.Length,
                fora.Length,
                Arredondar((dentro.Length / (double)amostras.Length) * 100) ?? 0,
                intervalosFora.Length,
                intervalosFora,
                pior)
        };
    }

    private ResultadoConsultaMetrica MontarResultadoExtremoHorario(ResultadoConsultaMetrica resultadoBase, ConfiguracaoMetrica metrica, IReadOnlyList<EstatisticasDiarias> estatisticasDiarias, IntencaoConsulta intencao)
    {
        var amostras = estatisticasDiarias.SelectMany(dia => dia.Samples).ToArray();
        if (amostras.Length == 0)
        {
            return resultadoBase with
            {
                SemDados = true,
                Mensagem = $"Sem dados de {resultadoBase.Metrica} em {resultadoBase.Ambiente} para {resultadoBase.Periodo}."
            };
        }

        var agrupados = amostras
            .GroupBy(amostra => $"{amostra.DateLabel}|{amostra.TimeLabel}")
            .Select(grupo =>
            {
                var valores = grupo.Select(amostra => amostra.Value).ToArray();
                var estatisticas = CalcularEstatisticas(valores);
                var primeiro = grupo.First();
                return new IntervaloHorario(primeiro.DateLabel, primeiro.TimeLabel, Arredondar(estatisticas.Avg), Arredondar(estatisticas.Min), Arredondar(estatisticas.Max), valores.Length);
            })
            .ToArray();

        var ehMinimo = intencao.Operation == "horario_menor_valor";
        var melhorResultado = ehMinimo
            ? agrupados.OrderBy(intervaloHorario => intervaloHorario.Media).First()
            : agrupados.OrderByDescending(intervaloHorario => intervaloHorario.Media).First();

        return resultadoBase with
        {
            TipoResultado = "analise_horaria",
            Criterio = ehMinimo ? "menor_media_horaria" : "maior_media_horaria",
            Media = melhorResultado.Media,
            Minima = melhorResultado.Minima,
            Maxima = melhorResultado.Maxima,
            Amostras = amostras.Length,
            AnaliseHoraria = new ResultadoAnaliseHoraria(
                melhorResultado.Data,
                melhorResultado.Horario,
                melhorResultado.Media,
                melhorResultado.Minima,
                melhorResultado.Maxima,
                melhorResultado.Amostras,
                intencao.HourRange is null ? null : FormatarRotuloFaixaHora(intencao.HourRange))
        };
    }

    private ResultadoConsultaMetrica MontarResultadoExtremoDiaCalendario(ResultadoConsultaMetrica resultadoBase, IReadOnlyList<EstatisticasDiarias> estatisticasDiarias, IntencaoConsulta intencao)
    {
        var modo = intencao.Operation == "calendario_dia_menor_valor" ? "min" : "max";
        var intervalosHorarios = estatisticasDiarias
            .Select(dia => new ResultadoAnaliseMapaCalor("analise_calendario_mensal", dia.DateLabel, null, null, Arredondar(dia.Stats.Avg), Arredondar(dia.Stats.Min), Arredondar(dia.Stats.Max), dia.Values.Count))
            .Where(intervaloHorario => intervaloHorario.Value is not null)
            .ToArray();

        var melhorResultado = SelecionarIntervaloMapaCalor(intervalosHorarios, modo);
        if (melhorResultado is null) return MontarResultadoAnaliticoSemDados(resultadoBase, "calendário mensal");

        return resultadoBase with
        {
            TipoResultado = "analise_calendario_mensal",
            Criterio = modo == "min" ? "menor_media_diaria" : "maior_media_diaria",
            Media = melhorResultado.Value,
            Minima = melhorResultado.Minima,
            Maxima = melhorResultado.Maxima,
            Amostras = estatisticasDiarias.Sum(dia => dia.Values.Count),
            AnaliseHeatmap = melhorResultado
        };
    }

    private ResultadoConsultaMetrica MontarResultadoExtremoHoraDia(ResultadoConsultaMetrica resultadoBase, IReadOnlyList<EstatisticasDiarias> estatisticasDiarias, IntencaoConsulta intencao)
    {
        var modo = intencao.Operation == "heatmap_hora_menor_valor" ? "min" : "max";
        var intervalosHorarios = estatisticasDiarias
            .SelectMany(dia => dia.Samples.Select(amostra => amostra with { DateLabel = dia.DateLabel }))
            .GroupBy(amostra => amostra.TimeLabel[..2])
            .Select(grupo =>
            {
                var valores = grupo.Select(amostra => amostra.Value).ToArray();
                var estatisticas = CalcularEstatisticas(valores);
                return new ResultadoAnaliseMapaCalor("analise_heatmap_horario", null, null, $"{grupo.Key}:00", Arredondar(estatisticas.Avg), Arredondar(estatisticas.Min), Arredondar(estatisticas.Max), valores.Length);
            })
            .ToArray();

        var melhorResultado = SelecionarIntervaloMapaCalor(intervalosHorarios, modo);
        if (melhorResultado is null) return MontarResultadoAnaliticoSemDados(resultadoBase, "heatmap por hora");

        return resultadoBase with
        {
            TipoResultado = "analise_heatmap_horario",
            Criterio = modo == "min" ? "menor_media_por_hora" : "maior_media_por_hora",
            Media = melhorResultado.Value,
            Minima = melhorResultado.Minima,
            Maxima = melhorResultado.Maxima,
            Amostras = estatisticasDiarias.Sum(dia => dia.Values.Count),
            AnaliseHeatmap = melhorResultado
        };
    }

    private ResultadoConsultaMetrica MontarResultadoExtremoFaixaSemanal(ResultadoConsultaMetrica resultadoBase, IReadOnlyList<EstatisticasDiarias> estatisticasDiarias, IntencaoConsulta intencao)
    {
        var modo = intencao.Operation == "heatmap_semana_menor_valor" ? "min" : "max";
        var rotulosSemana = new[] { "Domingo", "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado" };
        var intervalosHorarios = estatisticasDiarias
            .SelectMany(dia => dia.Samples.Select(amostra => (dia, amostra)))
            .GroupBy(item =>
            {
                var data = DataFirebase.InterpretarDataFirebase(item.dia.Date) ?? DateTime.Today;
                return $"{(int)data.DayOfWeek}|{item.amostra.TimeLabel[..2]}";
            })
            .Select(grupo =>
            {
                var valores = grupo.Select(item => item.amostra.Value).ToArray();
                var estatisticas = CalcularEstatisticas(valores);
                var primeiro = grupo.First();
                var data = DataFirebase.InterpretarDataFirebase(primeiro.dia.Date) ?? DateTime.Today;
                return new ResultadoAnaliseMapaCalor("analise_heatmap_semanal", null, rotulosSemana[(int)data.DayOfWeek], $"{primeiro.amostra.TimeLabel[..2]}:00", Arredondar(estatisticas.Avg), Arredondar(estatisticas.Min), Arredondar(estatisticas.Max), valores.Length);
            })
            .ToArray();

        var melhorResultado = SelecionarIntervaloMapaCalor(intervalosHorarios, modo);
        if (melhorResultado is null) return MontarResultadoAnaliticoSemDados(resultadoBase, "mapa semanal");

        return resultadoBase with
        {
            TipoResultado = "analise_heatmap_semanal",
            Criterio = modo == "min" ? "menor_media_dia_hora" : "maior_media_dia_hora",
            Media = melhorResultado.Value,
            Minima = melhorResultado.Minima,
            Maxima = melhorResultado.Maxima,
            Amostras = estatisticasDiarias.Sum(dia => dia.Values.Count),
            AnaliseHeatmap = melhorResultado
        };
    }

    private ResultadoConsultaMetrica MontarResultadoAnaliticoSemDados(ResultadoConsultaMetrica resultadoBase, string rotulo)
    {
        return resultadoBase with
        {
            SemDados = true,
            Mensagem = $"Sem dados de {resultadoBase.Metrica} em {resultadoBase.Ambiente} para análise de {rotulo} no período {resultadoBase.Periodo}."
        };
    }

    private ResultadoAnaliseMapaCalor? SelecionarIntervaloMapaCalor(IReadOnlyList<ResultadoAnaliseMapaCalor> intervalosHorarios, string modo)
    {
        if (intervalosHorarios.Count == 0) return null;
        return modo == "min"
            ? intervalosHorarios.Where(intervaloHorario => intervaloHorario.Value is not null).OrderBy(intervaloHorario => intervaloHorario.Value).FirstOrDefault()
            : intervalosHorarios.Where(intervaloHorario => intervaloHorario.Value is not null).OrderByDescending(intervaloHorario => intervaloHorario.Value).FirstOrDefault();
    }

    private string MontarInstrucaoResposta(string pergunta, ResultadoConsultaAssistente resultado)
    {
        var conteudoSerializado = JsonSerializer.Serialize(resultado, new JsonSerializerOptions { WriteIndented = true });
        var instrucao = $"""
Você é o assistente da página "Estação Climática".
Responda em português do Brasil, de forma curta, natural e objetiva.
Use somente o resultado calculado pelo C# abaixo.
Não recalcule, não invente medições e não use conhecimento externo.
Quando relevante, cite o período usado, o critério usado e a quantidade de amostras.
Se houver comparação, explique quem venceu e por quê.
Se houver hora específica consultada, responda somente o valor da métrica, ambiente, data e hora. Não mostre média, mínima, máxima, delta nem número de amostras.
Se a operação for "ultima_medicao", responda somente o último valor medido, ambiente, data e horário.
Se o resultado tiver tipo "comparacao_dias", responda qual dia teve maior media, o valor do dia comparado e a diferenca.
Se o resultado for de faixa de conforto, diga se ficou dentro ou fora da faixa, informe a faixa usada, quantas horas ficaram fora e o pior horário fora da faixa quando existir.
Se o resultado for uma análise horária, responda o horário/período encontrado, o valor principal e a data. Se houver faixa horária consultada, diga que a análise ficou restrita a essa faixa.
Se o resultado for de calendário mensal, heatmap por hora ou mapa semanal, responda o dia/hora encontrado, valor principal e período consultado.
Se o resultado for solar, informe os eventos solares ou a duração/tendência/comparação calculada conforme o tipo do resultado.
Se o resultado for de qualidade do ar, informe AQI estimado, classificação e poluente dominante quando existirem.

Pergunta do usuário:
{pergunta}

Resultado calculado:
{conteudoSerializado}
""";

        return TruncarPrompt(instrucao);
    }

    private string FormatarResultadoAlternativo(ResultadoConsultaAssistente resultado)
    {
        var metricas = resultado.Results.SelectMany(ambiente => ambiente.Metricas).ToArray();
        if (metricas.Length == 0) return "Não encontrei dados suficientes para responder.";

        if (metricas.Length > 1 && resultado.Intent.Operation == "resumo")
        {
            return string.Join("\n\n", metricas.Select(metrica => metrica.SemDados
                ? metrica.Mensagem
                : $"{metrica.Metrica} em {metrica.Ambiente}:\n\nMédia: {FormatarNumero(metrica.Media)}{metrica.Unidade}\nMínima: {FormatarNumero(metrica.Minima)}{metrica.Unidade}\nMáxima: {FormatarNumero(metrica.Maxima)}{metrica.Unidade}\n\nPeríodo: {metrica.Periodo}"));
        }

        if (metricas.Length > 1)
        {
            return string.Join("\n\n", metricas.Select(metrica => FormatarResultadoAlternativo(resultado with
            {
                Results = new[] { new ResultadoConsultaAmbiente(metrica.Ambiente, new[] { metrica }) }
            })));
        }

        var primeiraMetrica = metricas[0];
        if (primeiraMetrica.SemDados) return primeiraMetrica.Mensagem;
        if (primeiraMetrica.SemFaixa) return primeiraMetrica.Mensagem;

        if (primeiraMetrica.Aqi is { } aqi)
        {
            if (aqi.TipoResultado == "consulta_horaria")
            {
                return $"AQI estimado em {primeiraMetrica.Ambiente} às {FormatarRotuloHora(resultado.RawIntent.Hour)}: {aqi.Valor} ({aqi.Classificacao}). Dominante: {aqi.Dominante}.";
            }

            return $"AQI estimado em {primeiraMetrica.Ambiente}: {aqi.Valor} ({aqi.Classificacao}). Dominante: {aqi.Dominante}. Atualizado em {aqi.AtualizadoEm}.";
        }

        if (primeiraMetrica.Solar is { } solar)
        {
            if (solar.TipoResultado == "solar_duracao_dia")
            {
                return $"A duração do dia em {solar.Data} foi {solar.DuracaoDia}, de {solar.NascerDoSol} até {solar.PorDoSol}.";
            }

            if (solar.TipoResultado == "solar_extremo_duracao_luz")
            {
                var descritor = solar.Criterio == "menor_duracao_luz" ? "menor duração de luz" : "maior duração de luz";
                return $"No período {primeiraMetrica.Periodo}, o dia com {descritor} foi {solar.Data}: {solar.DuracaoDia} de luz ({solar.NascerDoSol} às {solar.PorDoSol}).";
            }

            if (solar.TipoResultado == "solar_tendencia_evento")
            {
                var delta = solar.DeltaMinutos is null ? "--" : $"{Math.Abs(solar.DeltaMinutos.Value)} min";
                return $"Na {primeiraMetrica.Periodo}, o {solar.Evento} ficou {solar.Tendencia}; variação de {delta} entre o primeiro e o último registro.";
            }

            if (solar.TipoResultado == "solar_comparacao_evento")
            {
                var linhas = solar.PorDiaEvento
                    .Select(item =>
                    {
                        var diferenca = item.DiferencaMinutos is null
                            ? ""
                            : item.DiferencaMinutos == 0
                                ? " (referência)"
                                : $" ({Math.Abs(item.DiferencaMinutos.Value)} min {(item.DiferencaMinutos.Value > 0 ? "mais tarde" : "mais cedo")})";
                        return $"{item.Data}: {item.Horario}{diferenca}";
                    });
                return $"Comparação de {solar.Evento} em {primeiraMetrica.Periodo}: {string.Join("; ", linhas)}.";
            }

            if (solar.Dia is { } dia)
            {
                return $"Ciclo solar em {dia.Data}: amanhecer {dia.Amanhecer}, nascer do sol {dia.NascerDoSol}, zênite {dia.Zenite}, pôr do sol {dia.PorDoSol}, anoitecer {dia.Anoitecer}, duração do dia {dia.DuracaoDia}.";
            }
        }

        if (primeiraMetrica.Conforto is { } conforto)
        {
            var status = conforto.Status == "dentro_da_faixa" ? "ficou dentro da faixa" : "ficou fora da faixa";
            var linhas = new List<string>
            {
                $"{primeiraMetrica.Metrica} em {primeiraMetrica.Ambiente} {status} no período {primeiraMetrica.Periodo}.",
                "",
                $"Faixa usada: {conforto.Faixa}",
                $"Horas fora da faixa: {conforto.HorasFora}",
                $"Dentro da faixa: {FormatarNumero(conforto.PercentualDentro)}%"
            };

            if (conforto.PiorHorarioFora is { } pior)
            {
                linhas.Add($"Pior horário fora da faixa: {pior.Data} às {pior.Horario}, com {FormatarNumero(pior.Valor)}{primeiraMetrica.Unidade} ({pior.Direcao} da faixa).");
            }

            return string.Join("\n", linhas);
        }

        if (primeiraMetrica.TipoResultado == "estatistica_media")
        {
            return $"{primeiraMetrica.Metrica} em {primeiraMetrica.Ambiente}: média {FormatarNumero(primeiraMetrica.Media)}{primeiraMetrica.Unidade} no período {primeiraMetrica.Periodo}.";
        }

        if (primeiraMetrica.TipoResultado == "estatistica_extremo" && primeiraMetrica.Estatistica is { } extremo)
        {
            var descritor = primeiraMetrica.Operacao == "minima" ? "menor" : "maior";
            return $"{primeiraMetrica.Metrica} em {primeiraMetrica.Ambiente}: {descritor} valor {FormatarNumero(extremo.Valor)}{primeiraMetrica.Unidade}, em {extremo.Data} às {extremo.Horario}.";
        }

        if (primeiraMetrica.TipoResultado is "variacao_periodo" or "tendencia_periodo" && primeiraMetrica.Variacao is { } variacao)
        {
            var prefixo = primeiraMetrica.TipoResultado == "tendencia_periodo" ? $"Tendência {variacao.Tendencia}. " : "";
            return $"{prefixo}{primeiraMetrica.Metrica} em {primeiraMetrica.Ambiente} foi de {FormatarNumero(variacao.ValorInicial)}{primeiraMetrica.Unidade} em {variacao.DataInicial} às {variacao.HorarioInicial} para {FormatarNumero(variacao.ValorFinal)}{primeiraMetrica.Unidade} em {variacao.DataFinal} às {variacao.HorarioFinal}, diferença de {FormatarNumero(variacao.Diferenca)}{primeiraMetrica.Unidade} no período {primeiraMetrica.Periodo}.";
        }

        if (primeiraMetrica.Operacao == "ultima_medicao" && primeiraMetrica.AnaliseHoraria is { } ultimaMedicao)
        {
            return $"{primeiraMetrica.Metrica} em {primeiraMetrica.Ambiente} na última medição de {ultimaMedicao.Data} às {ultimaMedicao.Horario}: {FormatarNumero(ultimaMedicao.Valor)}{primeiraMetrica.Unidade}.";
        }

        if (primeiraMetrica.TipoResultado == "comparacao_dias")
        {
            var ordenados = primeiraMetrica.PorDia.Where(dia => dia.Media is not null).OrderByDescending(dia => dia.Media).ToArray();
            if (ordenados.Length >= 2)
            {
                var vencedor = ordenados[0];
                var referencia = ordenados[1];
                var diferenca = Arredondar((vencedor.Media ?? 0) - (referencia.Media ?? 0));
                return $"{vencedor.Data} teve maior média de {primeiraMetrica.Metrica} em {primeiraMetrica.Ambiente}: {FormatarNumero(vencedor.Media)}{primeiraMetrica.Unidade}. {referencia.Data} ficou com {FormatarNumero(referencia.Media)}{primeiraMetrica.Unidade}, diferença de {FormatarNumero(diferenca)}{primeiraMetrica.Unidade}.";
            }
        }

        if (primeiraMetrica.TipoResultado == "analise_horaria" && primeiraMetrica.AnaliseHoraria is { } analise)
        {
            var descritor = primeiraMetrica.Criterio == "menor_media_horaria" ? "menor valor médio" : "maior valor médio";
            var intervalo = analise.FaixaHorariaConsultada is null ? "" : $", considerando apenas {analise.FaixaHorariaConsultada}";
            return $"O {descritor} de {primeiraMetrica.Metrica} em {primeiraMetrica.Ambiente}{intervalo} foi em {analise.Data} às {analise.Horario}: {FormatarNumero(analise.Valor)}{primeiraMetrica.Unidade}.";
        }

        if (primeiraMetrica.TipoResultado == "analise_calendario_mensal" && primeiraMetrica.AnaliseHeatmap is { } calendario)
        {
            var descritor = primeiraMetrica.Criterio == "menor_media_diaria" ? "menor média diária" : "maior média diária";
            return $"No calendário mensal de {primeiraMetrica.Periodo}, o dia com {descritor} de {primeiraMetrica.Metrica} em {primeiraMetrica.Ambiente} foi {calendario.Date}: {FormatarNumero(calendario.Value)}{primeiraMetrica.Unidade}.";
        }

        if (primeiraMetrica.TipoResultado == "analise_heatmap_horario" && primeiraMetrica.AnaliseHeatmap is { } horario)
        {
            var descritor = primeiraMetrica.Criterio == "menor_media_por_hora" ? "menor média por hora" : "maior média por hora";
            return $"No período {primeiraMetrica.Periodo}, a hora com {descritor} de {primeiraMetrica.Metrica} em {primeiraMetrica.Ambiente} foi {horario.Hour}: {FormatarNumero(horario.Value)}{primeiraMetrica.Unidade}.";
        }

        if (primeiraMetrica.TipoResultado == "analise_heatmap_semanal" && primeiraMetrica.AnaliseHeatmap is { } semana)
        {
            var descritor = primeiraMetrica.Criterio == "menor_media_dia_hora" ? "menor média por dia/hora" : "maior média por dia/hora";
            return $"No mapa semanal ({primeiraMetrica.Periodo}), o ponto com {descritor} de {primeiraMetrica.Metrica} em {primeiraMetrica.Ambiente} foi {semana.Weekday} às {semana.Hour}: {FormatarNumero(semana.Value)}{primeiraMetrica.Unidade}.";
        }

        if (resultado.RawIntent.Hour is not null)
        {
            var rotuloData = primeiraMetrica.DatasConsultadas.FirstOrDefault() ?? primeiraMetrica.Periodo;
            return $"{primeiraMetrica.Metrica} em {primeiraMetrica.Ambiente} no dia {rotuloData} às {FormatarRotuloHora(resultado.RawIntent.Hour)}: {FormatarNumero(primeiraMetrica.Media)}{primeiraMetrica.Unidade}.";
        }

        if (resultado.Intent.Operation == "resumo")
        {
            var partes = metricas.Take(4).Select(metrica => metrica.SemDados
                ? $"{metrica.Metrica}: sem dados"
                : $"{metrica.Metrica}: média {FormatarNumero(metrica.Media)}{metrica.Unidade}, mínima {FormatarNumero(metrica.Minima)}{metrica.Unidade}, máxima {FormatarNumero(metrica.Maxima)}{metrica.Unidade}");
            var faixa = resultado.RawIntent.HourRange is null ? "" : $" entre {FormatarRotuloFaixaHora(resultado.RawIntent.HourRange)}";
            return $"Resumo de {resultado.Intent.PeriodLabel}{faixa}: {string.Join("; ", partes)}.";
        }

        if (resultado.Intent.Operation == "alertas")
        {
            var alertas = metricas
                .Where(metrica => !metrica.SemDados)
                .Where(metrica => EstaForaDoConforto(metrica))
                .Select(metrica => $"{metrica.Metrica} em {metrica.Ambiente}")
                .ToArray();

            return alertas.Length == 0
                ? $"Nenhum indicador consultado ficou fora da faixa ideal em {resultado.Intent.PeriodLabel}."
                : $"Fora da faixa ideal em {resultado.Intent.PeriodLabel}: {string.Join(", ", alertas)}.";
        }

        if (primeiraMetrica.Operacao == "dia_mais_frio" && primeiraMetrica.DiaMaisFrio is { } frio)
        {
            return $"No período {primeiraMetrica.Periodo}, o dia mais frio em {primeiraMetrica.Ambiente} foi {frio.Data}, com média de {FormatarNumero(frio.Valor)}{primeiraMetrica.Unidade}.";
        }

        if (primeiraMetrica.Operacao == "dia_mais_quente" && primeiraMetrica.DiaMaisQuente is { } quente)
        {
            return $"No período {primeiraMetrica.Periodo}, o dia mais quente em {primeiraMetrica.Ambiente} foi {quente.Data}, com média de {FormatarNumero(quente.Valor)}{primeiraMetrica.Unidade}.";
        }

        return $"{primeiraMetrica.Metrica} em {primeiraMetrica.Ambiente}: média {FormatarNumero(primeiraMetrica.Media)}{primeiraMetrica.Unidade}, mínima {FormatarNumero(primeiraMetrica.Minima)}{primeiraMetrica.Unidade}, máxima {FormatarNumero(primeiraMetrica.Maxima)}{primeiraMetrica.Unidade}, com {primeiraMetrica.Amostras} amostra(s).";
    }

    private IntencaoClassificada? InterpretarJsonIntencao(string resposta)
    {
        var texto = (resposta ?? "").Trim();
        var inicio = texto.IndexOf('{');
        var fim = texto.LastIndexOf('}');
        if (inicio >= 0 && fim > inicio) texto = texto[inicio..(fim + 1)];

        try
        {
            using var documento = JsonDocument.Parse(texto);
            var raiz = documento.RootElement;
            var periodo = raiz.TryGetProperty("periodo", out var elementoPeriodo) && elementoPeriodo.ValueKind == JsonValueKind.Object
                ? new PeriodoIntencao(
                    ObterTexto(elementoPeriodo, "tipo"),
                    ObterTexto(elementoPeriodo, "data"),
                    ObterVetorTextos(elementoPeriodo, "datas"),
                    ObterInteiro(elementoPeriodo, "quantidade"),
                    ObterTexto(elementoPeriodo, "inicio"),
                    ObterTexto(elementoPeriodo, "fim"),
                    ObterTexto(elementoPeriodo, "hora"),
                    ObterTexto(elementoPeriodo, "hora_inicio"),
                    ObterTexto(elementoPeriodo, "hora_fim"))
                : null;

            return new IntencaoClassificada(
                ObterVetorTextos(raiz, "ambientes"),
                ObterVetorTextos(raiz, "metricas"),
                ObterTexto(raiz, "operacao"),
                periodo,
                ObterTexto(raiz, "criterio"),
                ObterTexto(raiz, "hora"),
                ObterNumeroOpcional(raiz, "confianca"),
                ObterBooleano(raiz, "precisa_esclarecimento"),
                ObterTexto(raiz, "pergunta_esclarecimento"),
                ObterBooleano(raiz, "solar"));
        }
        catch
        {
            return null;
        }
    }

    private IReadOnlyList<ConfiguracaoAmbiente> ObterAmbientesDaIntencao(IntencaoClassificada? intencao)
    {
        return (intencao?.Environments ?? Array.Empty<string>())
            .Select(ObterAmbientePorChave)
            .Where(ambiente => ambiente is not null)
            .Cast<ConfiguracaoAmbiente>()
            .ToArray();
    }

    private IReadOnlyList<string> NormalizarMetricas(IntencaoClassificada? intencao)
    {
        return (intencao?.Metrics ?? Array.Empty<string>())
            .Select(NormalizarTexto)
            .Where(valor => valor.Length > 0)
            .ToArray();
    }

    private IntencaoConsulta AplicarMemoriaCurta(IntencaoConsulta intencao, string perguntaNormalizada, ChatIntentSummary? memoria)
    {
        if (memoria is null || !PareceContinuidade(perguntaNormalizada)) return intencao;

        var ambientes = DeveHerdarAmbiente(intencao, perguntaNormalizada)
            ? ObterAmbientesDoResumo(memoria)
            : intencao.Environments;
        var metricas = intencao.Metrics.Count == 0 && memoria.Metrics.Count > 0
            ? memoria.Metrics.Select(NormalizarTexto).ToArray()
            : intencao.Metrics;
        var operacao = DeveHerdarOperacao(intencao.Operation, perguntaNormalizada) && !string.IsNullOrWhiteSpace(memoria.Operation)
            ? memoria.Operation
            : intencao.Operation;
        var faixaHoraria = intencao.HourRange ?? (memoria.HourRange is null ? null : new FaixaHoraria(memoria.HourRange.Start, memoria.HourRange.End));
        var hora = faixaHoraria is null ? intencao.Hour ?? memoria.Hour : null;
        var periodo = DeveHerdarPeriodo(perguntaNormalizada, memoria)
            ? new PeriodoConsulta("datas", memoria.Dates!, null, null, null)
            : intencao.Period;

        return intencao with
        {
            Environments = ambientes.Count > 0 ? ambientes : intencao.Environments,
            Metrics = metricas,
            Operation = operacao,
            Period = periodo,
            Hour = hora,
            HourRange = faixaHoraria,
            NeedsClarification = false,
            ClarificationQuestion = null
        };
    }

    private bool PareceContinuidade(string perguntaNormalizada)
    {
        var prefixos = new[] { "e ", "e no ", "e na ", "e em ", "tambem", "também", "agora no", "agora na" };
        if (prefixos.Any(prefixo => perguntaNormalizada.StartsWith(NormalizarTexto(prefixo), StringComparison.Ordinal))) return true;

        var termosIsolados = new[] { "no quarto", "na sala", "no aquario", "no aquário", "ontem", "anteontem", "antiontem" };
        return termosIsolados.Any(term => perguntaNormalizada == NormalizarTexto(term));
    }

    private bool DeveHerdarAmbiente(IntencaoConsulta intencao, string perguntaNormalizada)
    {
        if (intencao.Environments.Count == 0) return true;
        return !_ambientes.Values.SelectMany(ambiente => ambiente.Aliases).Any(alias => TemPalavra(perguntaNormalizada, NormalizarTexto(alias)));
    }

    private bool DeveHerdarOperacao(string operacao, string perguntaNormalizada)
    {
        if (string.IsNullOrWhiteSpace(operacao) || operacao == "resumo") return true;
        if (operacao == "ultima_medicao" && !TemIntencaoUltimaMedicao(perguntaNormalizada)) return false;
        return false;
    }

    private bool DeveHerdarPeriodo(string perguntaNormalizada, ChatIntentSummary memoria)
    {
        if (memoria.Dates is not { Count: > 0 }) return false;

        var mencionaHorario = new[]
        {
            "hoje",
            "hj",
            "ontem",
            "anteontem",
            "antiontem",
            "ultim",
            "urtim"
        }.Any(term => perguntaNormalizada.Contains(NormalizarTexto(term), StringComparison.Ordinal))
            || System.Text.RegularExpressions.Regex.IsMatch(perguntaNormalizada, @"\b\d{1,2}[/-]\d{1,2}[/-]\d{4}\b");

        return !mencionaHorario;
    }

    private IReadOnlyList<ConfiguracaoAmbiente> ObterAmbientesDoResumo(ChatIntentSummary memoria)
    {
        return memoria.Environments
            .Select(ambiente => ObterAmbientePorChave(ambiente) ?? _ambientes.Values.FirstOrDefault(configuracao => NormalizarTexto(configuracao.Label) == NormalizarTexto(ambiente)))
            .Where(ambiente => ambiente is not null)
            .Cast<ConfiguracaoAmbiente>()
            .ToArray();
    }

    private string NormalizarOperacao(string? valor, string perguntaNormalizada)
    {
        var operacao = NormalizarTexto(valor);
        var operacaoSolar = InferirOperacaoSolar(perguntaNormalizada);
        if (operacaoSolar is not null) return operacaoSolar;
        if (operacao is "valor" or "ultima_medicao" || TemIntencaoUltimaMedicao(perguntaNormalizada)) return "ultima_medicao";
        if (TemIntencaoFaixa(perguntaNormalizada)) return "status_faixa";
        var operacaoMapaCalor = InferirOperacaoMapaCalor(perguntaNormalizada);
        if (operacaoMapaCalor is not null) return operacaoMapaCalor;
        var operacaoHoraria = InferirOperacaoHoraria(perguntaNormalizada);
        if (operacaoHoraria is not null) return operacaoHoraria;
        if (perguntaNormalizada.Contains("tendencia") || perguntaNormalizada.Contains("variou") || perguntaNormalizada.Contains("variacao") || perguntaNormalizada.Contains("delta"))
        {
            return perguntaNormalizada.Contains("tendencia") ? "tendencia" : "delta";
        }
        if (perguntaNormalizada.Contains("dia mais fri") || perguntaNormalizada.Contains("dia mais baix")) return "dia_mais_frio";
        if (perguntaNormalizada.Contains("dia mais quent") || perguntaNormalizada.Contains("dia mais alt")) return "dia_mais_quente";
        if (perguntaNormalizada.Contains("mais alta") || perguntaNormalizada.Contains("maior valor") || perguntaNormalizada.Contains("maxim")) return "maxima";
        if (perguntaNormalizada.Contains("mais baixa") || perguntaNormalizada.Contains("menor valor") || perguntaNormalizada.Contains("minim")) return "minima";
        if (perguntaNormalizada.Contains("media")) return "media";
        if (TemIntencaoComparacaoEntreDias(perguntaNormalizada)) return "comparar_dias";
        if (perguntaNormalizada.Contains("fora") || perguntaNormalizada.Contains("alert")) return "alertas";
        if (operacao.Length > 0 && operacao != "resumo") return operacao;
        return "resumo";
    }

    private IntencaoConsulta AplicarAtalhosContextuais(IntencaoConsulta intencao, string perguntaNormalizada, string abaAtiva)
    {
        var pedeResumo = perguntaNormalizada.Contains("resumo da data")
            || perguntaNormalizada.Contains("resumo dos dados")
            || perguntaNormalizada == "resumo";
        var pedeAlertas = perguntaNormalizada.Contains("alerta")
            || perguntaNormalizada.Contains("indicadores ficaram fora")
            || perguntaNormalizada.Contains("indicadores estao fora");
        var pedeMediaTemperatura = perguntaNormalizada.Contains("temperatura media da data selecionada");
       var pedeMaximaTemperatura = perguntaNormalizada.Contains("temperatura maxima da data selecionada");
        var pedeMaximaAtalho = pedeMaximaTemperatura
            || perguntaNormalizada.Contains("maxima da data selecionada")
            || perguntaNormalizada.Contains("maxima do dia");
        if (!pedeResumo && !pedeAlertas && !pedeMediaTemperatura && !pedeMaximaTemperatura) return intencao;

        var ambientes = abaAtiva == "Tab0"
            ? _ambientes.Values.ToArray()
            : new[] { ObterAmbientePorAbaAtiva(abaAtiva) };
        var metricas = pedeMediaTemperatura || pedeMaximaAtalho
            ? new[] { "temperatura" }
            : pedeAlertas
                ? new[] { "temperatura", "sensacao_termica", "umidade" }
                : Array.Empty<string>();
        var operacao = pedeMediaTemperatura ? "media" : pedeMaximaAtalho ? "maxima" : pedeAlertas ? "status_faixa" : "resumo";

        return intencao with
        {
            Environments = ambientes,
            Metrics = metricas,
            Operation = operacao,
            IncludeSolar = false,
            NeedsClarification = false,
            ClarificationQuestion = null
        };
    }

    private bool TemIntencaoComparacaoEntreDias(string perguntaNormalizada)
    {
        var mencionaDiaComparavel = new[]
        {
            "hoje",
            "hj",
            "ontem",
            "anteontem",
            "antiontem"
        }.Any(term => TemPalavra(perguntaNormalizada, NormalizarTexto(term)))
            || ExtrairDatasPergunta(perguntaNormalizada).Take(2).Count() >= 2;

        if (!mencionaDiaComparavel) return false;

        return new[]
        {
            "compar",
            "diferenca",
            "diferença",
            "maior que",
            "menor que",
            "mais quent",
            "mais fri",
            "mais alto",
            "mais baixo"
        }.Any(term => perguntaNormalizada.Contains(NormalizarTexto(term), StringComparison.Ordinal))
            || TemPalavra(perguntaNormalizada, "ou");
    }

    private bool TemIntencaoUltimaMedicao(string perguntaNormalizada)
    {
        var termosValorAtual = new[]
        {
            "agora",
            "atual",
            "atualmente",
            "nesse momento",
            "neste momento",
            "ultima medicao",
            "ultima medição",
            "ultimo valor",
            "último valor",
            "valor atual"
        };
        if (termosValorAtual.Any(term => perguntaNormalizada.Contains(NormalizarTexto(term), StringComparison.Ordinal))) return true;

        var termosValorSimples = new[] { "qual o", "qual a", "quanto esta", "quanto está", "quanto ta", "quanto tá" };
        if (!termosValorSimples.Any(term => perguntaNormalizada.Contains(NormalizarTexto(term), StringComparison.Ordinal))) return false;

        var termosAgregacao = new[]
        {
            "media",
            "média",
            "maxim",
            "minim",
            "tendencia",
            "tendência",
            "delta",
            "diferenca",
            "diferença",
            "faixa",
            "conforto",
            "horario",
            "horário",
            "dia mais",
            "ultimas",
            "últimas",
            "ultimos",
            "últimos"
        };

        return !termosAgregacao.Any(term => perguntaNormalizada.Contains(NormalizarTexto(term), StringComparison.Ordinal));
    }

    private PeriodoConsulta NormalizarPeriodo(IntencaoClassificada? intencao, string perguntaNormalizada, string dataSelecionada, string operacao)
    {
        var datasExplicitas = ExtrairDatasPergunta(perguntaNormalizada).ToArray();
        var periodo = intencao?.Period;
        var tipoNormalizado = NormalizarTexto(periodo?.Type);
        var dataPeriodoMensal = ExtrairDataPeriodoMensal(perguntaNormalizada, dataSelecionada);
        var dataPeriodoAnual = ExtrairDataPeriodoAnual(perguntaNormalizada, dataSelecionada);
        if (tipoNormalizado == "ultimas_24h" || PossuiIntencaoHorasMoveis(perguntaNormalizada))
        {
            var horasSolicitadas = ExtrairJanelaHoras(perguntaNormalizada) ?? periodo?.Quantity ?? 24;
            var horas = LimitarHoras(horasSolicitadas);
            return new PeriodoConsulta("rolling_hours", datasExplicitas, horas, null, null, dataSelecionada, null, horasSolicitadas);
        }

        if (tipoNormalizado == "mes_selecionado" || operacao is "calendario_dia_maior_valor" or "calendario_dia_menor_valor" || (operacao is "solar_maior_duracao_luz" or "solar_menor_duracao_luz" && !string.IsNullOrWhiteSpace(dataPeriodoMensal)) || (operacao.StartsWith("heatmap_hora_") && (perguntaNormalizada.Contains("costuma") || perguntaNormalizada.Contains("tipic"))))
        {
            return new PeriodoConsulta("selected_month", datasExplicitas.Length > 0 ? datasExplicitas : new[] { string.IsNullOrWhiteSpace(dataPeriodoMensal) ? dataSelecionada : dataPeriodoMensal }, null, null, null);
        }

        if (tipoNormalizado == "ano_selecionado" || operacao is "solar_maior_duracao_luz" or "solar_menor_duracao_luz")
        {
            return new PeriodoConsulta("selected_year", datasExplicitas.Length > 0 ? datasExplicitas : new[] { dataPeriodoAnual }, null, null, null);
        }

        if (tipoNormalizado == "semana_selecionada" || operacao.StartsWith("heatmap_semana_") || operacao is "solar_tendencia_nascer" or "solar_tendencia_por" or "solar_comparar_nascer" or "solar_comparar_por" || perguntaNormalizada.Contains("semana"))
        {
            return new PeriodoConsulta("selected_week", datasExplicitas.Length > 0 ? datasExplicitas : new[] { dataSelecionada }, null, null, null);
        }

        if (datasExplicitas.Length > 0) return new PeriodoConsulta("datas", datasExplicitas, null, null, null);

        if (tipoNormalizado == "ultimos_dias" || perguntaNormalizada.Contains("ultim") || perguntaNormalizada.Contains("urtim"))
        {
            var diasSolicitados = ExtrairQuantidadeUltimosDias(perguntaNormalizada) ?? periodo?.Quantity ?? DiasRecentesPadrao;
            var dias = LimitarDias(diasSolicitados);
            return new PeriodoConsulta("last_days", Array.Empty<string>(), dias, null, null, dataSelecionada, diasSolicitados, null);
        }

        var datasClassificadas = (periodo?.Dates ?? Array.Empty<string>())
            .Select(NormalizarDataRelativaOuExplicita)
            .Where(data => data is not null)
            .Cast<string>()
            .ToArray();
        if (datasClassificadas.Length > 0) return new PeriodoConsulta("datas", datasClassificadas, null, null, null);

        var dataUnica = NormalizarDataRelativaOuExplicita(periodo?.Date);
        if (dataUnica is not null) return new PeriodoConsulta("datas", new[] { dataUnica }, null, null, null);

        if (tipoNormalizado == "intervalo")
        {
            var inicio = NormalizarDataRelativaOuExplicita(periodo?.Start);
            var fim = NormalizarDataRelativaOuExplicita(periodo?.End);
            if (inicio is not null && fim is not null) return new PeriodoConsulta("range", Array.Empty<string>(), null, inicio, fim);
        }

        if (TemPalavra(perguntaNormalizada, "hoje") || TemPalavra(perguntaNormalizada, "hj")) return new PeriodoConsulta("datas", new[] { DataAtualSistema() }, null, null, null);
        if (TemPalavra(perguntaNormalizada, "ontem")) return new PeriodoConsulta("datas", new[] { DeslocamentoHoje(-1) }, null, null, null);
        if (TemPalavra(perguntaNormalizada, "anteontem") || TemPalavra(perguntaNormalizada, "antiontem")) return new PeriodoConsulta("datas", new[] { DeslocamentoHoje(-2) }, null, null, null);

        return new PeriodoConsulta("datas", new[] { dataSelecionada }, null, null, null);
    }

    private IEnumerable<string> ResolverDatasPeriodo(PeriodoConsulta periodo)
    {
        if (periodo.Type == "datas") return ObterDatasUnicas(periodo.Dates).Take(_opcoesIa.MaxPeriodDays);
        if (periodo.Type == "rolling_hours") return ResolverDatasHorasMoveis(periodo);
        if (periodo.Type == "selected_month") return MontarDatasMes(periodo.Dates.FirstOrDefault() ?? DataFirebase.Normalizar(null));
        if (periodo.Type == "selected_year") return MontarDatasAno(periodo.Dates.FirstOrDefault() ?? DataFirebase.Normalizar(null));
        if (periodo.Type == "selected_week") return MontarDatasSemana(periodo.Dates.FirstOrDefault() ?? DataFirebase.Normalizar(null));
        if (periodo.Type == "last_days") return MontarUltimosDias(periodo.Days ?? DiasRecentesPadrao, periodo.SelectedDate);
        if (periodo.Type == "range" && periodo.Start is not null && periodo.End is not null) return MontarIntervaloDatas(periodo.Start, periodo.End);
        return new[] { DataFirebase.Normalizar(null) };
    }

    private IEnumerable<string> MontarUltimosDias(int dias, string? dataSelecionada = null)
    {
        var clamped = LimitarDias(dias);
        var dataFinal = DataFirebase.InterpretarDataFirebase(dataSelecionada ?? DataFirebase.Normalizar(null)) ?? DateTime.Today;
        return Enumerable.Range(0, clamped)
            .Select(indice => dataFinal.AddDays(-(clamped - 1 - indice)))
            .Select(FormatarDataFirebase);
    }

    private IEnumerable<string> MontarIntervaloDatas(string inicio, string fim)
    {
        var dataInicial = DataFirebase.InterpretarDataFirebase(inicio) ?? DateTime.Today;
        var dataFinal = DataFirebase.InterpretarDataFirebase(fim) ?? dataInicial;
        var direcao = dataInicial <= dataFinal ? 1 : -1;
        var cursor = dataInicial;
        var datas = new List<string>();

        while (datas.Count < _opcoesIa.MaxPeriodDays)
        {
            datas.Add(FormatarDataFirebase(cursor));
            if (cursor.Date == dataFinal.Date) break;
            cursor = cursor.AddDays(direcao);
        }

        return datas;
    }

    private IEnumerable<string> MontarDatasMes(string dataSelecionada)
    {
        var data = DataFirebase.InterpretarDataFirebase(dataSelecionada) ?? DateTime.Today;
        var diasNoMes = DateTime.DaysInMonth(data.Year, data.Month);
        return Enumerable.Range(1, diasNoMes)
            .Select(dia => FormatarDataFirebase(new DateTime(data.Year, data.Month, dia)));
    }

    private IEnumerable<string> MontarDatasAno(string dataSelecionada)
    {
        var data = DataFirebase.InterpretarDataFirebase(dataSelecionada) ?? DateTime.Today;
        var cursor = new DateTime(data.Year, 1, 1);
        while (cursor.Year == data.Year)
        {
            yield return FormatarDataFirebase(cursor);
            cursor = cursor.AddDays(1);
        }
    }

    private IEnumerable<string> MontarDatasSemana(string dataSelecionada)
    {
        var data = DataFirebase.InterpretarDataFirebase(dataSelecionada) ?? DateTime.Today;
        var inicio = data.AddDays(-(int)data.DayOfWeek);
        var datas = new List<string>();
        for (var cursor = inicio; cursor.Date <= data.Date && datas.Count < 7; cursor = cursor.AddDays(1))
        {
            datas.Add(FormatarDataFirebase(cursor));
        }

        return datas;
    }

    private IEnumerable<string> ResolverDatasHorasMoveis(PeriodoConsulta periodo)
    {
        var dataSelecionada = periodo.SelectedDate ?? periodo.Dates.FirstOrDefault() ?? DataFirebase.Normalizar(null);
        var fim = ObterFimJanelaMovel(dataSelecionada);
        var inicio = fim.AddHours(-(periodo.Days ?? 24));
        return ObterDatasUnicas(new[] { FormatarDataFirebase(inicio), FormatarDataFirebase(fim) });
    }

    private int? ExtrairJanelaHoras(string perguntaNormalizada)
    {
        var correspondencia = System.Text.RegularExpressions.Regex.Match(perguntaNormalizada, @"\b(?:ultim|urtim)[a-z]*\s+(.+?)\s*(?:h|hora|horas)\b");
        return correspondencia.Success ? InterpretarQuantidade(correspondencia.Groups[1].Value) : null;
    }

    private int? ExtrairQuantidadeUltimosDias(string perguntaNormalizada)
    {
        var correspondencia = System.Text.RegularExpressions.Regex.Match(perguntaNormalizada, @"\b(?:ultim|urtim)[a-z]*\s+(.+?)\s+dias?\b");
        return correspondencia.Success ? InterpretarQuantidade(correspondencia.Groups[1].Value) : null;
    }

    private int? InterpretarQuantidade(string valor)
    {
        var normalizado = NormalizarTexto(valor).Trim();
        if (int.TryParse(normalizado, out var numero)) return numero;

        var unidades = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["um"] = 1, ["uma"] = 1, ["dois"] = 2, ["duas"] = 2, ["tres"] = 3,
            ["quatro"] = 4, ["cinco"] = 5, ["seis"] = 6, ["sete"] = 7, ["oito"] = 8,
            ["nove"] = 9, ["dez"] = 10, ["onze"] = 11, ["doze"] = 12, ["treze"] = 13,
            ["quatorze"] = 14, ["catorze"] = 14, ["quinze"] = 15, ["dezesseis"] = 16,
            ["dezessete"] = 17, ["dezoito"] = 18, ["dezenove"] = 19
        };
        if (unidades.TryGetValue(normalizado, out var unidade)) return unidade;

        var dezenas = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["vinte"] = 20, ["trinta"] = 30, ["quarenta"] = 40, ["cinquenta"] = 50, ["sessenta"] = 60
        };
        var composto = System.Text.RegularExpressions.Regex.Match(normalizado, @"^(vinte|trinta|quarenta|cinquenta|sessenta)(?:\s+e\s+(.+))?$");
        if (!composto.Success || !dezenas.TryGetValue(composto.Groups[1].Value, out var dez)) return null;
        return composto.Groups[2].Success && unidades.TryGetValue(composto.Groups[2].Value, out var restante) ? dez + restante : dez;
    }

    private IEnumerable<string> ExtrairDatasPergunta(string perguntaNormalizada)
    {
        var correspondencias = System.Text.RegularExpressions.Regex.Matches(perguntaNormalizada, @"\b(\d{1,2})[/-](\d{1,2})[/-](\d{4})\b");
        foreach (System.Text.RegularExpressions.Match correspondencia in correspondencias)
        {
            yield return $"{int.Parse(correspondencia.Groups[1].Value):00}-{int.Parse(correspondencia.Groups[2].Value):00}-{correspondencia.Groups[3].Value}";
        }

        if (TemPalavra(perguntaNormalizada, "anteontem") || TemPalavra(perguntaNormalizada, "antiontem")) yield return DeslocamentoHoje(-2);
        if (TemPalavra(perguntaNormalizada, "ontem")) yield return DeslocamentoHoje(-1);
        if (TemPalavra(perguntaNormalizada, "hoje") || TemPalavra(perguntaNormalizada, "hj")) yield return DataAtualSistema();
    }

    private string? ExtrairHoraPergunta(string perguntaNormalizada)
    {
        var correspondencia = System.Text.RegularExpressions.Regex.Match(perguntaNormalizada, @"\b(?:as|às)\s*(\d{1,2})(?:h|:00)?\b|\b(\d{1,2})(?:h|:00)\b");
        if (!correspondencia.Success) return null;

        var valor = correspondencia.Groups[1].Success ? correspondencia.Groups[1].Value : correspondencia.Groups[2].Value;
        return NormalizarFiltroHora(valor);
    }

    private string? NormalizarFiltroHora(string? valor)
    {
        var normalizado = NormalizarTexto(valor).Trim();
        if (normalizado.Length == 0) return null;

        var correspondencia = System.Text.RegularExpressions.Regex.Match(normalizado, @"\b(\d{1,2})(?:h|:(\d{1,2}))?\b");
        if (!correspondencia.Success || !int.TryParse(correspondencia.Groups[1].Value, out var hora)) return null;
        if (hora is < 0 or > 23) return null;
        return $"{hora:00}";
    }

    private FaixaHoraria? NormalizarFaixaHoraria(PeriodoIntencao? periodo)
    {
        if (periodo is null) return null;
        var inicio = NormalizarFiltroHora(periodo.HourStart);
        var fim = NormalizarFiltroHora(periodo.HourEnd);
        return inicio is not null && fim is not null ? new FaixaHoraria(inicio, fim) : null;
    }

    private FaixaHoraria? ExtrairFaixaHorariaPergunta(string perguntaNormalizada)
    {
        var correspondencia = System.Text.RegularExpressions.Regex.Match(perguntaNormalizada, @"\b(?:entre|das|de)\s*(\d{1,2})(?:h|:00)?\s*(?:e|a|as|-)\s*(\d{1,2})(?:h|:00)?\b");
        if (!correspondencia.Success) return null;
        var inicio = NormalizarFiltroHora(correspondencia.Groups[1].Value);
        var fim = NormalizarFiltroHora(correspondencia.Groups[2].Value);
        return inicio is not null && fim is not null ? new FaixaHoraria(inicio, fim) : null;
    }

    private bool HoraEstaNaFaixa(string? hora, FaixaHoraria? faixaHoraria)
    {
        if (faixaHoraria is null || hora is null) return true;
        if (!int.TryParse(hora, out var atual) || !int.TryParse(faixaHoraria.Start, out var inicio) || !int.TryParse(faixaHoraria.End, out var fim)) return true;
        return inicio <= fim ? atual >= inicio && atual <= fim : atual >= inicio || atual <= fim;
    }

    private string? NormalizarHoraRegistro(string horario)
    {
        var valorConvertido = LeitorRegistrosFirebase.ConverterHora(horario);
        return valorConvertido is null ? null : $"{valorConvertido.Value.Hour:00}";
    }

    private string ExtrairDataPeriodoMensal(string perguntaNormalizada, string dataSelecionada)
    {
        var indiceMes = ObterIndiceMesMencionado(perguntaNormalizada);
        if (indiceMes is null)
        {
            return perguntaNormalizada.Contains("mes") || perguntaNormalizada.Contains("mês")
                ? dataSelecionada
                : "";
        }

        var selecionado = DataFirebase.InterpretarDataFirebase(dataSelecionada) ?? DateTime.Today;
        var ano = ExtrairAnoPergunta(perguntaNormalizada) ?? selecionado.Year;
        return FormatarDataFirebase(new DateTime(ano, indiceMes.Value + 1, 1));
    }

    private string ExtrairDataPeriodoAnual(string perguntaNormalizada, string dataSelecionada)
    {
        var selecionado = DataFirebase.InterpretarDataFirebase(dataSelecionada) ?? DateTime.Today;
        var ano = ExtrairAnoPergunta(perguntaNormalizada) ?? selecionado.Year;
        return FormatarDataFirebase(new DateTime(ano, selecionado.Month, selecionado.Day));
    }

    private int? ExtrairAnoPergunta(string perguntaNormalizada)
    {
        var correspondencia = System.Text.RegularExpressions.Regex.Match(perguntaNormalizada, @"\b(20\d{2})\b");
        return correspondencia.Success && int.TryParse(correspondencia.Groups[1].Value, out var ano) ? ano : null;
    }

    private int? ObterIndiceMesMencionado(string perguntaNormalizada)
    {
        var meses = new[]
        {
            new[] { "janeiro", "jan" },
            new[] { "fevereiro", "fev" },
            new[] { "marco", "março", "mar" },
            new[] { "abril", "abr" },
            new[] { "maio", "mai" },
            new[] { "junho", "jun" },
            new[] { "julho", "jul" },
            new[] { "agosto", "ago" },
            new[] { "setembro", "set" },
            new[] { "outubro", "out" },
            new[] { "novembro", "nov" },
            new[] { "dezembro", "dez" }
        };

        for (var indice = 0; indice < meses.Length; indice++)
        {
            if (meses[indice].Any(alias => TemPalavra(perguntaNormalizada, NormalizarTexto(alias)))) return indice;
        }

        return null;
    }

    private bool PossuiIntencaoHorasMoveis(string perguntaNormalizada)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(perguntaNormalizada, @"\b(ultim|urtim)[a-z]*\s+.+?\s*(h|hora|horas)\b");
    }

    private string? InferirOperacaoHoraria(string perguntaNormalizada)
    {
        var pedeHorario = new[]
        {
            "qual horario",
            "qual foi o horario",
            "que horario",
            "em qual horario",
            "qual hora",
            "qual foi a hora",
            "que hora",
            "periodo do dia",
            "faixa do dia"
        }.Any(term => perguntaNormalizada.Contains(NormalizarTexto(term), StringComparison.Ordinal));
        if (!pedeHorario) return null;

        var pedeMaior = new[] { "mais quent", "maior", "maxim", "mais alto", "pico" }
            .Any(term => perguntaNormalizada.Contains(NormalizarTexto(term), StringComparison.Ordinal));
        if (pedeMaior) return "horario_maior_valor";

        var pedeMenor = new[] { "mais fri", "menor", "minim", "mais baixo" }
            .Any(term => perguntaNormalizada.Contains(NormalizarTexto(term), StringComparison.Ordinal));
        return pedeMenor ? "horario_menor_valor" : null;
    }

    private string? InferirOperacaoSolar(string perguntaNormalizada)
    {
        if (!TemPerguntaSolar(perguntaNormalizada)) return null;

        var ehNascerDoSol = perguntaNormalizada.Contains("nascer", StringComparison.Ordinal);
        var ehPorDoSol = perguntaNormalizada.Contains("por do sol", StringComparison.Ordinal) || perguntaNormalizada.Contains("por-do-sol", StringComparison.Ordinal);
        var pedeTendencia = perguntaNormalizada.Contains("ficando", StringComparison.Ordinal) || perguntaNormalizada.Contains("tendencia", StringComparison.Ordinal);
        var pedeComparacao = perguntaNormalizada.Contains("compar", StringComparison.Ordinal);

        if (perguntaNormalizada.Contains("dia mais long", StringComparison.Ordinal) || perguntaNormalizada.Contains("dia com mais tempo de luz", StringComparison.Ordinal)) return "solar_maior_duracao_luz";
        if (perguntaNormalizada.Contains("dia mais curt", StringComparison.Ordinal) || perguntaNormalizada.Contains("dia com menos tempo de luz", StringComparison.Ordinal)) return "solar_menor_duracao_luz";

        if (perguntaNormalizada.Contains("duracao", StringComparison.Ordinal)
            || perguntaNormalizada.Contains("tempo de luz", StringComparison.Ordinal)
            || perguntaNormalizada.Contains("luz solar", StringComparison.Ordinal)
            || perguntaNormalizada.Contains("periodo de luz", StringComparison.Ordinal)
            || perguntaNormalizada.Contains("fotoperiodo", StringComparison.Ordinal))
        {
            if (perguntaNormalizada.Contains("maior", StringComparison.Ordinal) || perguntaNormalizada.Contains("mais long", StringComparison.Ordinal)) return "solar_maior_duracao_luz";
            if (perguntaNormalizada.Contains("menor", StringComparison.Ordinal) || perguntaNormalizada.Contains("mais curt", StringComparison.Ordinal)) return "solar_menor_duracao_luz";
            return "solar_duracao_dia";
        }

        if (pedeComparacao && ehNascerDoSol) return "solar_comparar_nascer";
        if (pedeComparacao && ehPorDoSol) return "solar_comparar_por";
        if (pedeTendencia && ehNascerDoSol) return "solar_tendencia_nascer";
        if (pedeTendencia && ehPorDoSol) return "solar_tendencia_por";

        return null;
    }

    private string? InferirOperacaoMapaCalor(string perguntaNormalizada)
    {
        var modo = InferirModoExtremo(perguntaNormalizada);
        if (modo is null) return null;

        if (perguntaNormalizada.Contains("dia do mes") || perguntaNormalizada.Contains("dia no mes") || perguntaNormalizada.Contains("calendario"))
        {
            return modo == "min" ? "calendario_dia_menor_valor" : "calendario_dia_maior_valor";
        }

        if (perguntaNormalizada.Contains("dia/hora") || perguntaNormalizada.Contains("dia hora") || perguntaNormalizada.Contains("dia e hora") || perguntaNormalizada.Contains("mapa semanal") || perguntaNormalizada.Contains("heatmap semanal") || perguntaNormalizada.Contains("semana"))
        {
            return modo == "min" ? "heatmap_semana_menor_valor" : "heatmap_semana_maior_valor";
        }

        if (perguntaNormalizada.Contains("hora costuma") || perguntaNormalizada.Contains("horario costuma") || perguntaNormalizada.Contains("costuma ser") || perguntaNormalizada.Contains("heatmap por hora") || perguntaNormalizada.Contains("por hora do dia"))
        {
            return modo == "min" ? "heatmap_hora_menor_valor" : "heatmap_hora_maior_valor";
        }

        return null;
    }

    private string? InferirModoExtremo(string perguntaNormalizada)
    {
        var pedeMaior = new[] { "mais quent", "maior", "maxim", "mais alto", "pico" }
            .Any(term => perguntaNormalizada.Contains(NormalizarTexto(term), StringComparison.Ordinal));
        if (pedeMaior) return "max";

        var pedeMenor = new[] { "mais fri", "menor", "minim", "mais baixo" }
            .Any(term => perguntaNormalizada.Contains(NormalizarTexto(term), StringComparison.Ordinal));
        return pedeMenor ? "min" : null;
    }

    private JanelaMovel? ObterJanelaMovel(PeriodoConsulta periodo)
    {
        if (periodo.Type != "rolling_hours") return null;
        var dataSelecionada = periodo.SelectedDate ?? periodo.Dates.FirstOrDefault() ?? DataFirebase.Normalizar(null);
        var fim = ObterFimJanelaMovel(dataSelecionada);
        return new JanelaMovel(fim.AddHours(-(periodo.Days ?? 24)), fim);
    }

    private DateTime ObterFimJanelaMovel(string dataSelecionada)
    {
        var selecionado = DataFirebase.InterpretarDataFirebase(dataSelecionada) ?? DateTime.Today;
        var agora = _obterAgora();
        return new DateTime(selecionado.Year, selecionado.Month, selecionado.Day, agora.Hour, agora.Minute, agora.Second, agora.Millisecond);
    }

    private bool EstaNaJanelaMovel(string data, string horario, JanelaMovel? janelaMovel)
    {
        if (janelaMovel is null) return true;
        var instante = InterpretarDataHoraFirebase(data, horario);
        return instante is not null && instante.Value >= janelaMovel.Start && instante.Value <= janelaMovel.End;
    }

    private DateTime? InterpretarDataHoraFirebase(string data, string horario)
    {
        var dataConvertida = DataFirebase.InterpretarDataFirebase(data);
        var horaConvertida = LeitorRegistrosFirebase.ConverterHora(horario);
        if (dataConvertida is null || horaConvertida is null) return null;
        return new DateTime(dataConvertida.Value.Year, dataConvertida.Value.Month, dataConvertida.Value.Day, horaConvertida.Value.Hour, horaConvertida.Value.Minute, 0);
    }

    private string? NormalizarDataRelativaOuExplicita(string? valor)
    {
        var normalizado = NormalizarTexto(valor).Trim();
        if (normalizado.Length == 0) return null;

        var correspondencia = System.Text.RegularExpressions.Regex.Match(normalizado, @"\b(\d{1,2})[/-](\d{1,2})[/-](\d{4})\b");
        if (correspondencia.Success)
        {
            return $"{int.Parse(correspondencia.Groups[1].Value):00}-{int.Parse(correspondencia.Groups[2].Value):00}-{correspondencia.Groups[3].Value}";
        }

        if (normalizado == "hoje") return DataFirebase.Normalizar(null);
        if (normalizado == "ontem") return DeslocamentoHoje(-1);
        if (normalizado is "anteontem" or "antiontem") return DeslocamentoHoje(-2);
        return null;
    }

    private IEnumerable<ConfiguracaoMetrica> ResolverMetricasAmbientes(IReadOnlyList<ConfiguracaoAmbiente> ambientes, IReadOnlyList<string> metricasSolicitadas, string pergunta)
    {
        var perguntaNormalizada = NormalizarTexto(pergunta);
        var solicitado = metricasSolicitadas.Count > 0 ? metricasSolicitadas : InferirMetricasPergunta(perguntaNormalizada);
        var metricas = new List<ConfiguracaoMetrica>();

        foreach (var ambiente in ambientes)
        {
            foreach (var metrica in ambiente.Metrics)
            {
                if (solicitado.Count == 0 || solicitado.Any(valor => CorrespondenciasMetricas(metrica, valor)))
                {
                    metricas.Add(metrica);
                }
            }
        }

        return MetricasUnicas(metricas.Count > 0 ? metricas : ambientes.Select(ambiente => ambiente.Metrics[0]));
    }

    private IReadOnlyList<ConfiguracaoAmbiente> RedirecionarAmbientesPorMetricasExclusivas(IReadOnlyList<ConfiguracaoAmbiente> ambientes, IReadOnlyList<string> metricasSolicitadas)
    {
        if (metricasSolicitadas.Count == 0) return ambientes;

        var redirecionados = new List<ConfiguracaoAmbiente>();
        foreach (var metricaSolicitada in metricasSolicitadas)
        {
            var ambientesComMetrica = _ambientes.Values
                .Where(ambiente => ambiente.Metrics.Any(metrica => CorrespondenciasMetricas(metrica, metricaSolicitada)))
                .ToArray();

            if (ambientesComMetrica.Length == 1)
            {
                redirecionados.Add(ambientesComMetrica[0]);
            }
        }

        return redirecionados.Count > 0 ? redirecionados.DistinctBy(ambiente => ambiente.Key).ToArray() : ambientes;
    }

    private IReadOnlyList<string> InferirMetricasPergunta(string perguntaNormalizada)
    {
        var metricas = new List<string>();
        if (perguntaNormalizada.Contains("temperatura") || perguntaNormalizada.Contains("temp") || perguntaNormalizada.Contains("fri") || perguntaNormalizada.Contains("quent") || perguntaNormalizada.Contains("calor")) metricas.Add("temperatura");
        if (perguntaNormalizada.Contains("sensacao") || perguntaNormalizada.Contains("termica")) metricas.Add("sensacao_termica");
        if (perguntaNormalizada.Contains("umid") || perguntaNormalizada.Contains("humid")) metricas.Add("umidade");
        if (perguntaNormalizada.Contains("press")) metricas.Add("pressao");
        if (perguntaNormalizada.Contains("co2")) metricas.Add("co2");
        if (TemPalavra(perguntaNormalizada, "co")) metricas.Add("co");
        if (perguntaNormalizada.Contains("acet")) metricas.Add("acetona");
        if (perguntaNormalizada.Contains("alcool") || perguntaNormalizada.Contains("alcohol")) metricas.Add("alcool");
        if (perguntaNormalizada.Contains("amonia") || perguntaNormalizada.Contains("nh4")) metricas.Add("amonia");
        if (perguntaNormalizada.Contains("toluen")) metricas.Add("tolueno");
        if (perguntaNormalizada.Contains("aqi") || perguntaNormalizada.Contains("iaq") || perguntaNormalizada.Contains("qualidade do ar") || perguntaNormalizada.Contains("qualidade ar") || perguntaNormalizada.Contains("indice de qualidade do ar") || perguntaNormalizada.Contains("indice do ar") || perguntaNormalizada.Contains("ar da sala")) metricas.Add("qualidade_ar");
        if (perguntaNormalizada.Contains("ph")) metricas.Add("ph");
        if (perguntaNormalizada.Contains("tds")) metricas.Add("tds");
        if (perguntaNormalizada.Contains("turb")) metricas.Add("turbidez");
        return metricas;
    }

    private bool EhMetricaAqi(string metrica)
    {
        var normalizado = NormalizarTexto(metrica).Replace("_", " ");
        return normalizado is "aqi" or "iaq" or "qualidade ar" or "qualidade do ar"
            || normalizado.Contains("indice de qualidade do ar", StringComparison.Ordinal)
            || normalizado.Contains("indice do ar", StringComparison.Ordinal);
    }

    private bool CorrespondenciasMetricas(ConfiguracaoMetrica metrica, string metricaSolicitada)
    {
        var solicitado = NormalizarTexto(metricaSolicitada).Replace("_", " ");
        var aliases = new[] { metrica.Label, metrica.Key }.Concat(metrica.Aliases).Select(valor => NormalizarTexto(valor).Replace("_", " "));
        return aliases.Any(alias =>
            alias == solicitado ||
            (solicitado.Length > 2 && alias.Contains(solicitado, StringComparison.Ordinal)) ||
            (alias.Length > 2 && solicitado.Contains(alias, StringComparison.Ordinal)));
    }

    private IReadOnlyList<ConfiguracaoAmbiente> EncontrarAmbientesMencionados(string perguntaNormalizada)
    {
        return _ambientes.Values
            .Where(ambiente => ambiente.Aliases.Any(alias => TemPalavra(perguntaNormalizada, NormalizarTexto(alias))))
            .ToArray();
    }

    private ConfiguracaoAmbiente? ObterAmbientePorChave(string? chave)
    {
        var normalizado = NormalizarTexto(chave);
        return normalizado.Length > 0 && _ambientes.TryGetValue(normalizado, out var ambiente) ? ambiente : null;
    }

    private ConfiguracaoAmbiente ObterAmbientePorAbaAtiva(string abaAtiva)
    {
        return _ambientes.Values.FirstOrDefault(ambiente => ambiente.ActiveTab == abaAtiva) ?? _ambientes["sala"];
    }

    private JsonElement? ObterDadosAmbiente(DadosDashboardFirebase dados, ConfiguracaoAmbiente ambiente)
    {
        return ambiente.Key switch
        {
            "sala" => dados.Sala,
            "quarto" => dados.Quarto,
            "aquario" => dados.Aquario,
            _ => null
        };
    }

    private Estatisticas CalcularEstatisticas(IReadOnlyList<double> valores)
    {
        var primeiro = valores[0];
        var ultimo = valores[^1];
        return new Estatisticas(valores.Average(), valores.Min(), valores.Max(), ultimo - primeiro);
    }

    private DiaSelecionado? SelecionarDia(IReadOnlyList<ResumoDiario> resumos, Func<ResumoDiario, double?> seletor, string modo)
    {
        var validos = resumos.Where(resumo => seletor(resumo) is not null).ToArray();
        if (validos.Length == 0) return null;
        var ordenados = modo == "min"
            ? validos.OrderBy(resumo => seletor(resumo))
            : validos.OrderByDescending(resumo => seletor(resumo));
        var item = ordenados.First();
        return new DiaSelecionado(item.Data, seletor(item), item.Amostras);
    }

    private ResultadoComparacao? MontarComparacao(IReadOnlyList<ResumoDiario> resumosDiarios)
    {
        if (resumosDiarios.Count < 2) return null;
        var porMedia = resumosDiarios.Where(dia => dia.Media is not null).OrderByDescending(dia => dia.Media).ToArray();
        if (porMedia.Length == 0) return null;
        var rotuloHoje = FormatarData(DataAtualSistema());
        var hoje = resumosDiarios.FirstOrDefault(dia => dia.Data == rotuloHoje);
        var maisQuentesQueHoje = hoje is null
            ? Array.Empty<ComparacaoDiaria>()
            : resumosDiarios.Where(dia => dia.Media > hoje.Media).Select(dia => new ComparacaoDiaria(dia.Data, dia.Media, Arredondar((dia.Media ?? 0) - (hoje.Media ?? 0)))).ToArray();

        return new ResultadoComparacao(
            new ComparacaoDiaria(porMedia[0].Data, porMedia[0].Media, null),
            new ComparacaoDiaria(porMedia[^1].Data, porMedia[^1].Media, null),
            hoje,
            maisQuentesQueHoje);
    }

    private bool EstaForaDoConforto(ResultadoConsultaMetrica metrica)
    {
        var chave = NormalizarTexto($"{metrica.Ambiente} {metrica.Metrica}");
        (double Min, double Max)? faixa = null;
        if (chave.Contains("umidade")) faixa = (40, 60);
        else if (chave.Contains("aquario") && chave.Contains("temperatura")) faixa = (25, 27);
        else if (chave.Contains("temperatura") || chave.Contains("sensacao")) faixa = (20, 26);
        if (faixa is null || metrica.Media is null) return false;
        return metrica.Media < faixa.Value.Min || metrica.Media > faixa.Value.Max;
    }

    private bool TemIntencaoFaixa(string perguntaNormalizada)
    {
        return new[]
        {
            "faixa",
            "conforto",
            "ideal",
            "normal",
            "fora da faixa",
            "dentro da faixa",
            "fora do ideal",
            "dentro do ideal",
            "pior horario",
            "pior horário",
            "quantas horas fora"
        }.Any(term => perguntaNormalizada.Contains(NormalizarTexto(term), StringComparison.Ordinal));
    }

    private (double Min, double Max)? ObterFaixaConforto(ConfiguracaoAmbiente ambiente, ConfiguracaoMetrica metrica)
    {
        var nomeMetrica = NormalizarTexto(metrica.Label);
        if (nomeMetrica.Contains("umidade")) return (40, 60);
        if (ambiente.Key == "aquario" && nomeMetrica.Contains("temperatura")) return (25, 27);
        if (nomeMetrica.Contains("temperatura") || nomeMetrica.Contains("sensacao")) return (20, 26);
        return null;
    }

    private bool EstaForaDaFaixa(double valor, (double Min, double Max) faixa) => valor < faixa.Min || valor > faixa.Max;

    private double DistanciaDaFaixa(double valor, (double Min, double Max) faixa)
    {
        if (valor < faixa.Min) return faixa.Min - valor;
        if (valor > faixa.Max) return valor - faixa.Max;
        return 0;
    }

    private string FormatarFaixa((double Min, double Max) faixa, string unidade) => $"{FormatarNumero(faixa.Min)}{unidade} a {FormatarNumero(faixa.Max)}{unidade}";

    private IEnumerable<string> FaixasUnicas(IEnumerable<AmostraMetrica> amostras)
    {
        var encontrados = new HashSet<string>(StringComparer.Ordinal);
        foreach (var amostra in amostras)
        {
            var intervaloHorario = $"{amostra.DateLabel} {amostra.TimeLabel}";
            if (encontrados.Add(intervaloHorario)) yield return intervaloHorario;
        }
    }

    private string TendenciaDaDiferenca(double delta)
    {
        if (Math.Abs(delta) < 0.05) return "estável";
        return delta > 0 ? "subindo" : "caindo";
    }

    private string CriterioPadraoOperacao(string operacao)
    {
        return operacao switch
        {
            "dia_mais_frio" or "dia_mais_quente" or "comparar_dias" => "media_diaria",
            "maxima" => "maxima_registrada",
            "minima" => "minima_registrada",
            _ => "valores_registrados"
        };
    }

    private double? Arredondar(double? valor) => valor is null || !double.IsFinite(valor.Value) ? null : Math.Round(valor.Value, 2);

    private int LimitarDias(int? dias)
    {
        if (dias is null or <= 0) return DiasRecentesPadrao;
        return Math.Min(Math.Max(dias.Value, 1), _opcoesIa.MaxPeriodDays);
    }

    private int LimitarHoras(int? horas)
    {
        if (horas is null or <= 0) return 24;
        return Math.Min(Math.Max(horas.Value, 1), _opcoesIa.MaxPeriodDays * 24);
    }

    private IEnumerable<ConfiguracaoMetrica> MetricasUnicas(IEnumerable<ConfiguracaoMetrica> metricas)
    {
        var encontrados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var metrica in metricas)
        {
            if (encontrados.Add(metrica.Key)) yield return metrica;
        }
    }

    private IEnumerable<string> ObterDatasUnicas(IEnumerable<string> datas)
    {
        return datas.Where(data => !string.IsNullOrWhiteSpace(data)).Distinct(StringComparer.Ordinal);
    }

    private string DeslocamentoHoje(int deslocamentoDia) => FormatarDataFirebase(_obterAgora().Date.AddDays(deslocamentoDia));

    private string DataAtualSistema() => FormatarDataFirebase(_obterAgora().Date);

    private string FormatarDataFirebase(DateTime data) => $"{data.Day:00}-{data.Month:00}-{data.Year}";

    private string FormatarRotuloPeriodo(IReadOnlyList<string> datas)
    {
        if (datas.Count == 0) return "--";
        if (datas.Count == 1) return FormatarData(datas[0]);
        return $"{FormatarData(datas[0])} a {FormatarData(datas[^1])}";
    }

    private string FormatarRotuloPeriodo(PeriodoConsulta periodo, IReadOnlyList<string> datas)
    {
        if (periodo.Type == "rolling_hours")
        {
            return $"últimas {periodo.Days ?? 24} horas ({FormatarRotuloPeriodo(datas)})";
        }

        if (periodo.Type == "last_days")
        {
            var limite = periodo.RequestedDays is not null && periodo.RequestedDays != periodo.Days
                ? $"; solicitados {periodo.RequestedDays} dias, limitado a {periodo.Days} dias"
                : "";
            var dataFinal = periodo.SelectedDate ?? datas.LastOrDefault() ?? DataFirebase.Normalizar(null);
            return $"últimos {periodo.Days ?? DiasRecentesPadrao} dias até {FormatarData(dataFinal)} ({FormatarRotuloPeriodo(datas)}{limite})";
        }

        if (periodo.Type == "selected_month")
        {
            var data = DataFirebase.InterpretarDataFirebase(periodo.Dates.FirstOrDefault() ?? datas.FirstOrDefault() ?? DataFirebase.Normalizar(null)) ?? DateTime.Today;
            return $"{data.Month:00}/{data.Year}";
        }

        if (periodo.Type == "selected_week")
        {
            return $"semana de {FormatarRotuloPeriodo(datas)}";
        }

        return FormatarRotuloPeriodo(datas);
    }

    private bool TemPerguntaSolar(string perguntaNormalizada)
    {
        return new[]
        {
            "solar",
            "sol",
            "nascer",
            "por do sol",
            "por-do-sol",
            "zenite",
            "amanhecer",
            "anoitecer",
            "duracao do dia",
            "duracao de luz",
            "tempo de luz",
            "luz solar",
            "periodo de luz",
            "fotoperiodo",
            "dia mais longo",
            "dia mais curto"
        }
            .Any(term => perguntaNormalizada.Contains(NormalizarTexto(term), StringComparison.Ordinal));
    }

    private bool TemPalavra(string texto, string palavra)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(texto, $@"(^|\W){System.Text.RegularExpressions.Regex.Escape(palavra)}(\W|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private string NormalizarTexto(string? texto)
    {
        var normalizado = (texto ?? "").Normalize(NormalizationForm.FormD);
        var construtor = new StringBuilder();
        foreach (var caractere in normalizado)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(caractere) != UnicodeCategory.NonSpacingMark)
            {
                construtor.Append(char.ToLowerInvariant(caractere));
            }
        }

        return construtor.ToString();
    }

    private string ObterRotuloAba(string idAba) => idAba switch
    {
        "Tab0" => "Estação",
        "Tab1" => "Sala",
        "Tab2" => "Quarto",
        "Tab3" => "Aquário",
        _ => string.IsNullOrWhiteSpace(idAba) ? "--" : idAba
    };

    private string FormatarData(string? dataFirebase) => string.IsNullOrWhiteSpace(dataFirebase) ? "--" : dataFirebase.Replace('-', '/');

    private string FormatarRotuloHora(string? hora) => NormalizarFiltroHora(hora) is { } normalizado ? $"{normalizado}:00" : "--";

    private string FormatarRotuloFaixaHora(FaixaHoraria intervalo) => $"{FormatarRotuloHora(intervalo.Start)} a {FormatarRotuloHora(intervalo.End)}";

    private string FormatarHoraRegistro(string horario)
    {
        var valorConvertido = LeitorRegistrosFirebase.ConverterHora(horario);
        return valorConvertido is null ? horario.Replace('-', ':') : $"{valorConvertido.Value.Hour:00}:{valorConvertido.Value.Minute:00}";
    }

    private string FormatarNumero(double? valor) => valor is null ? "--" : valor.Value.ToString("0.00", CultureInfo.GetCultureInfo("pt-BR"));

    private string TruncarPrompt(string instrucao)
    {
        var limite = _opcoesIa.MaxPromptChars <= 0 ? 9000 : _opcoesIa.MaxPromptChars;
        return instrucao.Length <= limite ? instrucao : instrucao[..limite];
    }

    private ChatResponse CriarRespostaLocal(string resposta, ChatIntentSummary intencao) => new(resposta, "local", intencao);

    private ChatIntentSummary IntencaoVazia() => new(Array.Empty<string>(), Array.Empty<string>(), "", "", 0);

    private ChatIntentSummary CriarResumoIntencao(ResultadoConsultaAssistente resultado)
    {
        return new ChatIntentSummary(
            resultado.Intent.Environments.ToArray(),
            resultado.Intent.Metrics.ToArray(),
            resultado.Intent.Operation,
            resultado.Intent.PeriodLabel,
            resultado.Results.SelectMany(ambiente => ambiente.Metricas).Sum(metrica => metrica.Amostras),
            resultado.RawIntent.Hour,
            resultado.RawIntent.HourRange is null ? null : new ChatHourRangeSummary(resultado.RawIntent.HourRange.Start, resultado.RawIntent.HourRange.End),
            resultado.Intent.Dates.ToArray());
    }

    private string ObterTexto(JsonElement elemento, string nome)
    {
        return elemento.TryGetProperty(nome, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() ?? "" : "";
    }

    private IReadOnlyList<string> ObterVetorTextos(JsonElement elemento, string nome)
    {
        if (!elemento.TryGetProperty(nome, out var valor)) return Array.Empty<string>();
        if (valor.ValueKind == JsonValueKind.Array) return valor.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString() ?? "").ToArray();
        return valor.ValueKind == JsonValueKind.String ? new[] { valor.GetString() ?? "" } : Array.Empty<string>();
    }

    private int? ObterInteiro(JsonElement elemento, string nome)
    {
        if (!elemento.TryGetProperty(nome, out var valor)) return null;
        if (valor.ValueKind == JsonValueKind.Number && valor.TryGetInt32(out var numero)) return numero;
        if (valor.ValueKind == JsonValueKind.String && int.TryParse(valor.GetString(), out numero)) return numero;
        return null;
    }

    private double? ObterNumeroOpcional(JsonElement elemento, string nome)
    {
        if (!elemento.TryGetProperty(nome, out var valor)) return null;
        if (valor.ValueKind == JsonValueKind.Number && valor.TryGetDouble(out var numero)) return numero;
        if (valor.ValueKind == JsonValueKind.String && double.TryParse(valor.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out numero)) return numero;
        return null;
    }

    private bool? ObterBooleano(JsonElement elemento, string nome)
    {
        if (!elemento.TryGetProperty(nome, out var valor)) return null;
        return valor.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private IReadOnlyDictionary<string, ConfiguracaoAmbiente> CriarAmbientes()
    {
        var sala = new ConfiguracaoAmbiente("sala", "Sala", "Tab1", new[] { "sala" }, new[]
        {
            new ConfiguracaoMetrica("sala", "Temperatura", CamposFirebase.Sala.Temperatura, "°C", new[] { "temperatura", "temp", "frio", "quente", "calor" }),
            new ConfiguracaoMetrica("sala", "Sensação térmica", CamposFirebase.Sala.SensacaoTermica, "°C", new[] { "sensacao", "sensacao termica", "sensação térmica" }),
            new ConfiguracaoMetrica("sala", "Umidade", CamposFirebase.Sala.Umidade, "%", new[] { "umidade", "humidade", "humidad" }),
            new ConfiguracaoMetrica("sala", "Pressão", CamposFirebase.Sala.Pressao, "hPa", new[] { "pressao", "pressão", "hpa" }),
            new ConfiguracaoMetrica("sala", "CO", CamposFirebase.Sala.Co, "ppm", new[] { "co" }),
            new ConfiguracaoMetrica("sala", "CO2", CamposFirebase.Sala.Co2, "ppm", new[] { "co2" }),
            new ConfiguracaoMetrica("sala", "Acetona", CamposFirebase.Sala.Acetona, "ppm", new[] { "acetona", "aceton" }),
            new ConfiguracaoMetrica("sala", "Álcool", CamposFirebase.Sala.Alcool, "ppm", new[] { "alcool", "álcool", "alcohol" }),
            new ConfiguracaoMetrica("sala", "Amônia", CamposFirebase.Sala.Amonia, "ppm", new[] { "amonia", "amônia", "nh4" }),
            new ConfiguracaoMetrica("sala", "Tolueno", CamposFirebase.Sala.Tolueno, "ppm", new[] { "tolueno", "toluen" })
        });

        var quarto = new ConfiguracaoAmbiente("quarto", "Quarto", "Tab2", new[] { "quarto" }, new[]
        {
            new ConfiguracaoMetrica("quarto", "Temperatura", CamposFirebase.Quarto.Temperatura, "°C", new[] { "temperatura", "temp", "frio", "quente", "calor" }),
            new ConfiguracaoMetrica("quarto", "Sensação térmica", CamposFirebase.Quarto.SensacaoTermica, "°C", new[] { "sensacao", "sensacao termica", "sensação térmica" }),
            new ConfiguracaoMetrica("quarto", "Umidade", CamposFirebase.Quarto.Umidade, "%", new[] { "umidade", "humidade", "humidad" })
        });

        var aquario = new ConfiguracaoAmbiente("aquario", "Aquário", "Tab3", new[] { "aquario", "aquário" }, new[]
        {
            new ConfiguracaoMetrica("aquario", "Temperatura", CamposFirebase.Aquario.Temperatura, "°C", new[] { "temperatura", "temp", "frio", "quente", "calor" }),
            new ConfiguracaoMetrica("aquario", "pH", CamposFirebase.Aquario.Ph, "", new[] { "ph" }),
            new ConfiguracaoMetrica("aquario", "TDS", CamposFirebase.Aquario.Tds, "ppm", new[] { "tds", "solidos", "sólidos" }),
            new ConfiguracaoMetrica("aquario", "Turbidez", CamposFirebase.Aquario.Turbidez, "NTU", new[] { "turbidez", "ntu" })
        });

        return new Dictionary<string, ConfiguracaoAmbiente>
        {
            ["sala"] = sala,
            ["quarto"] = quarto,
            ["aquario"] = aquario
        };
    }

    private sealed record ContextoAssistente(DadosDashboardFirebase Data, string ActiveTab, string SelectedDate);
    private sealed record ConfiguracaoAmbiente(string Key, string Label, string ActiveTab, IReadOnlyList<string> Aliases, IReadOnlyList<ConfiguracaoMetrica> Metrics);
    private sealed record ConfiguracaoMetrica(string EnvironmentKey, string Label, string Key, string Unit, IReadOnlyList<string> Aliases);
    private sealed record IntencaoConsulta(IReadOnlyList<ConfiguracaoAmbiente> Environments, IReadOnlyList<string> Metrics, string Operation, PeriodoConsulta Period, string Criterion, string? Hour, FaixaHoraria? HourRange, double? Confidence, bool NeedsClarification, string? ClarificationQuestion, bool IncludeSolar)
    {
        public string PeriodLabel => "";
    }
    private sealed record PeriodoConsulta(string Type, IReadOnlyList<string> Dates, int? Days, string? Start, string? End, string? SelectedDate = null, int? RequestedDays = null, int? RequestedHours = null);
    private sealed record JanelaMovel(DateTime Start, DateTime End);
    private sealed record FaixaHoraria(string Start, string End);
    private sealed record IntencaoClassificada(IReadOnlyList<string> Environments, IReadOnlyList<string> Metrics, string Operation, PeriodoIntencao? Period, string Criterion, string Hour, double? Confidence, bool? NeedsClarification, string? ClarificationQuestion, bool? Solar);
    private sealed record PeriodoIntencao(string Type, string Date, IReadOnlyList<string> Dates, int? Quantity, string Start, string End, string Hour, string HourStart, string HourEnd);
    private sealed record IntencaoCalculada(IReadOnlyList<string> Environments, IReadOnlyList<string> Metrics, string Operation, string Criterion, string PeriodLabel, IReadOnlyList<string> Dates);
    private sealed record ResultadoConsultaAssistente(string Question, IntencaoConsulta RawIntent, IntencaoCalculada Intent, IReadOnlyList<ResultadoConsultaAmbiente> Results, bool NeedsClarification, string Message)
    {
        public static ResultadoConsultaAssistente CriarEsclarecimento(string mensagem, string pergunta, IntencaoConsulta intencao) => new(
            pergunta,
            intencao,
            new IntencaoCalculada(intencao.Environments.Select(ambiente => ambiente.Label).ToArray(), intencao.Metrics, intencao.Operation, intencao.Criterion, "", Array.Empty<string>()),
            Array.Empty<ResultadoConsultaAmbiente>(),
            true,
            mensagem);
    }
    private sealed record ResultadoConsultaAmbiente(string Ambiente, IReadOnlyList<ResultadoConsultaMetrica> Metricas);
    private sealed record ResultadoConsultaMetrica(
        string Ambiente,
        string Metrica,
        string Unidade,
        string Operacao,
        string Criterio,
        string Periodo,
        IReadOnlyList<string> DatasConsultadas,
        IReadOnlyList<string> DiasComDados,
        int Amostras,
        bool SemDados,
        string Mensagem,
        string? TipoResultado,
        double? Media,
        double? Minima,
        double? Maxima,
        double? Delta,
        string Tendencia,
        DiaSelecionado? DiaMaisFrio,
        DiaSelecionado? DiaMaisQuente,
        DiaSelecionado? MenorRegistro,
        DiaSelecionado? MaiorRegistro,
        ResultadoComparacao? Comparacao,
        ResultadoFaixaConforto? Conforto,
        ResultadoAnaliseHoraria? AnaliseHoraria,
        ResultadoAnaliseMapaCalor? AnaliseHeatmap,
        ResultadoAnaliseAqi? Aqi,
        ResultadoAnaliseSolar? Solar,
        bool SemFaixa,
        IReadOnlyList<ResumoDiario> PorDia,
        ResultadoEstatistica? Estatistica = null,
        ResultadoVariacaoPeriodo? Variacao = null);
    private sealed record EstatisticasDiarias(string Date, string DateLabel, IReadOnlyList<double> Values, IReadOnlyList<AmostraMetrica> Samples, Estatisticas Stats);
    private sealed record AmostraMetrica(string Date, string DateLabel, string TimeLabel, double Value);
    private sealed record Estatisticas(double Avg, double Min, double Max, double Delta);
    private sealed record ResumoDiario(string Data, double? Media, double? Minima, double? Maxima, double? Delta, int Amostras);
    private sealed record DiaSelecionado(string Data, double? Valor, int Amostras);
    private sealed record ComparacaoDiaria(string Data, double? Media, double? Diferenca);
    private sealed record ResultadoEstatistica(double? Valor, string Data, string Horario);
    private sealed record ResultadoVariacaoPeriodo(double? ValorInicial, string DataInicial, string HorarioInicial, double? ValorFinal, string DataFinal, string HorarioFinal, double? Diferenca, string Tendencia);
    private sealed record ResultadoComparacao(ComparacaoDiaria MaisQuentePorMedia, ComparacaoDiaria MaisFrioPorMedia, ResumoDiario? Hoje, IReadOnlyList<ComparacaoDiaria> DiasMaisQuentesQueHoje);
    private sealed record ResultadoFaixaConforto(string Faixa, double LimiteMinimo, double LimiteMaximo, string Status, int TotalRegistros, int RegistrosDentro, int RegistrosFora, double PercentualDentro, int HorasFora, IReadOnlyList<string> HorariosFora, AmostraForaConforto? PiorHorarioFora);
    private sealed record AmostraForaConforto(string Data, string Horario, double? Valor, double? Distance, string Direcao);
    private sealed record IntervaloHorario(string Data, string Horario, double? Media, double? Minima, double? Maxima, int Amostras);
    private sealed record ResultadoAnaliseHoraria(string Data, string Horario, double? Valor, double? Minima, double? Maxima, int RegistrosNoHorario, string? FaixaHorariaConsultada);
    private sealed record ResultadoAnaliseMapaCalor(string TipoResultado, string? Date, string? Weekday, string? Hour, double? Value, double? Minima, double? Maxima, int Samples);
    private sealed record CategoriaAqi(int Min, int Max, string Label, string Impact);
    private sealed record FaixaAqi(double Low, double High, double IndexLow, double IndexHigh);
    private sealed record PoluenteAqi(string Key, string Label, string Unit, IReadOnlyList<FaixaAqi> Breakpoints);
    private sealed record SubindiceAqi(string Key, string Label, string Unit, double Value, double Aqi);
    private sealed record ResultadoAqiDiario(string Data, int Aqi, string Classificacao, string Impacto, string Dominante, string Horario, IReadOnlyList<SubindiceAqi> Subindices);
    private sealed record ResultadoAnaliseAqi(string TipoResultado, int Valor, string Classificacao, string Impacto, string Dominante, string AtualizadoEm, IReadOnlyList<SubindiceAqi> Subindices, IReadOnlyList<ResultadoAqiDiario> PorDia);
    private sealed record ResultadoDiaSolar(string Data, string Amanhecer, string NascerDoSol, string Zenite, string PorDoSol, string Anoitecer, int? NascerMinutos, int? PorMinutos, string DuracaoDia, int? DuracaoMinutos, string PeriodoLuzTotal, int? PeriodoLuzTotalMinutos);
    private sealed record ComparacaoEventoSolar(string Data, string Horario, int? DiferencaMinutos);
    private sealed record ItemClassificacaoSolar(string Data, string DuracaoDia, string NascerDoSol, string PorDoSol);
    private sealed record ResultadoAnaliseSolar(string TipoResultado, string? Data, string? Criterio, string? Evento, string? Tendencia, string? DuracaoDia, int? DuracaoMinutos, string? NascerDoSol, string? PorDoSol, ResultadoDiaSolar? Dia, int? DeltaMinutos, string? Horario, IReadOnlyList<ComparacaoEventoSolar> PorDiaEvento, IReadOnlyList<ItemClassificacaoSolar> Ranking, IReadOnlyList<ResultadoDiaSolar> PorDia);
}
