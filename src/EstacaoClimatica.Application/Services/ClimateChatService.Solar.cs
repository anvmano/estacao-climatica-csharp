using System.Text.Json;
using EstacaoClimatica.Application.Configuration;
using EstacaoClimatica.Application.Utilities;

namespace EstacaoClimatica.Application.Services;

public sealed partial class ServicoAssistenteClimatico
{
    private ResultadoConsultaAssistente ExecutarConsultaSolar(ContextoAssistente contexto, IntencaoConsulta intencao, string pergunta)
    {
        var datasPeriodo = ResolverDatasPeriodo(intencao.Period).ToArray();
        if (datasPeriodo.Length == 0)
        {
            return ResultadoConsultaAssistente.CriarEsclarecimento("Não consegui identificar o período da consulta solar.", pergunta, intencao);
        }

        var diasSolares = datasPeriodo
            .Select(data => MontarCicloSolarDiarioPorEventos(contexto.Data.Solar, data))
            .Where(dia => dia is not null)
            .Cast<ResultadoDiaSolar>()
            .ToArray();

        var resultadoBase = new ResultadoConsultaMetrica(
            "Solar",
            "Ciclo solar",
            "",
            intencao.Operation,
            "dados_solares_registrados",
            FormatarRotuloPeriodo(intencao.Period, datasPeriodo),
            datasPeriodo.Select(FormatarData).ToArray(),
            diasSolares.Select(dia => dia.Data).ToArray(),
            diasSolares.Length,
            false,
            "",
            "ciclo_solar",
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

        var metricaSolar = diasSolares.Length == 0
            ? resultadoBase with
            {
                SemDados = true,
                Mensagem = $"Sem dados de ciclo solar para {FormatarRotuloPeriodo(intencao.Period, datasPeriodo)}."
            }
            : MontarResultadoAnaliticoSolar(resultadoBase, diasSolares, intencao);

        return new ResultadoConsultaAssistente(
            pergunta,
            intencao,
            new IntencaoCalculada(
                new[] { "Solar" },
                new[] { "Ciclo solar" },
                intencao.Operation,
                "dados_solares_registrados",
                FormatarRotuloPeriodo(intencao.Period, datasPeriodo),
                datasPeriodo),
            new[] { new ResultadoConsultaAmbiente("Solar", new[] { metricaSolar }) },
            false,
            "");
    }

    private ResultadoConsultaMetrica MontarResultadoAnaliticoSolar(ResultadoConsultaMetrica resultadoBase, IReadOnlyList<ResultadoDiaSolar> diasSolares, IntencaoConsulta intencao)
    {
        if (intencao.Operation == "solar_duracao_dia")
        {
            var dia = diasSolares[0];
            return resultadoBase with
            {
                TipoResultado = "solar_duracao_dia",
                Solar = new ResultadoAnaliseSolar("solar_duracao_dia", dia.Data, null, null, null, dia.DuracaoDia, dia.DuracaoMinutos, dia.NascerDoSol, dia.PorDoSol, dia, null, null, Array.Empty<ComparacaoEventoSolar>(), Array.Empty<ItemClassificacaoSolar>(), diasSolares)
            };
        }

        if (intencao.Operation is "solar_maior_duracao_luz" or "solar_menor_duracao_luz")
        {
            var menor = intencao.Operation == "solar_menor_duracao_luz";
            var classificacao = (menor
                    ? diasSolares.Where(dia => dia.DuracaoMinutos is not null).OrderBy(dia => dia.DuracaoMinutos)
                    : diasSolares.Where(dia => dia.DuracaoMinutos is not null).OrderByDescending(dia => dia.DuracaoMinutos))
                .ToArray();
            var melhor = classificacao.FirstOrDefault();
            if (melhor is null)
            {
                return resultadoBase with
                {
                    SemDados = true,
                    Mensagem = $"Sem dados de duração de luz para {resultadoBase.Periodo}."
                };
            }

            return resultadoBase with
            {
                TipoResultado = "solar_extremo_duracao_luz",
                Solar = new ResultadoAnaliseSolar(
                    "solar_extremo_duracao_luz",
                    melhor.Data,
                    menor ? "menor_duracao_luz" : "maior_duracao_luz",
                    null,
                    null,
                    melhor.DuracaoDia,
                    melhor.DuracaoMinutos,
                    melhor.NascerDoSol,
                    melhor.PorDoSol,
                    melhor,
                    null,
                    null,
                    Array.Empty<ComparacaoEventoSolar>(),
                    classificacao.Take(6).Select(dia => new ItemClassificacaoSolar(dia.Data, dia.DuracaoDia, dia.NascerDoSol, dia.PorDoSol)).ToArray(),
                    diasSolares)
            };
        }

        if (intencao.Operation is "solar_tendencia_nascer" or "solar_tendencia_por")
        {
            var evento = intencao.Operation == "solar_tendencia_nascer" ? "nascer do sol" : "pôr do sol";
            var serie = MontarSerieEventoSolar(diasSolares, intencao.Operation == "solar_tendencia_nascer");
            if (serie.Length == 0) return resultadoBase with { SemDados = true, Mensagem = $"Sem dados de {evento} para {resultadoBase.Periodo}." };

            var ultimo = serie[^1];
            var delta = ultimo.DiferencaMinutos;
            return resultadoBase with
            {
                TipoResultado = "solar_tendencia_evento",
                Solar = new ResultadoAnaliseSolar(
                    "solar_tendencia_evento",
                    ultimo.Data,
                    null,
                    evento,
                    TendenciaPorDeltaMinutos(delta ?? 0),
                    null,
                    null,
                    null,
                    null,
                    null,
                    delta,
                    ultimo.Horario,
                    serie,
                    Array.Empty<ItemClassificacaoSolar>(),
                    diasSolares)
            };
        }

        if (intencao.Operation is "solar_comparar_nascer" or "solar_comparar_por")
        {
            var evento = intencao.Operation == "solar_comparar_nascer" ? "nascer do sol" : "pôr do sol";
            var serie = MontarSerieEventoSolar(diasSolares, intencao.Operation == "solar_comparar_nascer");
            if (serie.Length == 0) return resultadoBase with { SemDados = true, Mensagem = $"Sem dados de {evento} para {resultadoBase.Periodo}." };

            return resultadoBase with
            {
                TipoResultado = "solar_comparacao_evento",
                Solar = new ResultadoAnaliseSolar("solar_comparacao_evento", null, null, evento, null, null, null, null, null, null, null, null, serie, Array.Empty<ItemClassificacaoSolar>(), diasSolares)
            };
        }

        return resultadoBase with
        {
            Solar = new ResultadoAnaliseSolar("ciclo_solar", diasSolares[0].Data, null, null, null, null, null, null, null, diasSolares[0], null, null, Array.Empty<ComparacaoEventoSolar>(), Array.Empty<ItemClassificacaoSolar>(), diasSolares)
        };
    }

    private ResultadoDiaSolar? MontarCicloSolarDiarioPorEventos(JsonElement? origemSolar, string data)
    {
        var item = ObterPrimeiroItemSolar(LeitorRegistrosFirebase.ObterElementoData(origemSolar, data));
        if (item is null) return null;

        var amanhecer = LerHorarioSolar(item.Value, CamposFirebase.Solar.HoraAmanhecer, CamposFirebase.Solar.MinutoAmanhecer);
        var nascerDoSol = LerHorarioSolar(item.Value, CamposFirebase.Solar.HoraNascerDoSol, CamposFirebase.Solar.MinutoNascerDoSol);
        var porDoSol = LerHorarioSolar(item.Value, CamposFirebase.Solar.HoraPorDoSol, CamposFirebase.Solar.MinutoPorDoSol);
        var anoitecer = LerHorarioSolar(item.Value, CamposFirebase.Solar.HoraAnoitecer, CamposFirebase.Solar.MinutoAnoitecer);
        var zenite = LerHorarioSolar(item.Value, CamposFirebase.Solar.HoraZenite, CamposFirebase.Solar.MinutoZenite);

        if (amanhecer is null || nascerDoSol is null || porDoSol is null || anoitecer is null) return null;
        zenite ??= nascerDoSol.Value + ((porDoSol.Value - nascerDoSol.Value) / 2);

        return new ResultadoDiaSolar(
            FormatarData(data),
            FormatarMinutos(amanhecer.Value),
            FormatarMinutos(nascerDoSol.Value),
            FormatarMinutos(zenite.Value),
            FormatarMinutos(porDoSol.Value),
            FormatarMinutos(anoitecer.Value),
            nascerDoSol,
            porDoSol,
            CalcularRotuloDuracao(nascerDoSol.Value, porDoSol.Value),
            CalcularDuracaoMinutos(nascerDoSol.Value, porDoSol.Value),
            CalcularRotuloDuracao(amanhecer.Value, anoitecer.Value),
            CalcularDuracaoMinutos(amanhecer.Value, anoitecer.Value));
    }

    private JsonElement? ObterPrimeiroItemSolar(JsonElement? dadosData)
    {
        if (dadosData is null || dadosData.Value.ValueKind != JsonValueKind.Object) return null;

        foreach (var propriedade in dadosData.Value.EnumerateObject().OrderByDescending(par => par.Name))
        {
            if (PossuiCamposSolares(propriedade.Value)) return propriedade.Value;
            if (propriedade.Value.ValueKind != JsonValueKind.Object) continue;

            foreach (var aninhado in propriedade.Value.EnumerateObject().OrderByDescending(par => par.Name))
            {
                if (PossuiCamposSolares(aninhado.Value)) return aninhado.Value;
            }
        }

        return null;
    }

    private bool PossuiCamposSolares(JsonElement item)
    {
        return item.ValueKind == JsonValueKind.Object && CamposFirebase.Solar.CamposIdentificacao.Any(chave => item.TryGetProperty(chave, out _));
    }

    private int? LerHorarioSolar(JsonElement item, IReadOnlyList<string> nomesHora, IReadOnlyList<string> nomesMinuto)
    {
        var hora = LeitorNumerosJson.ObterNumero(item, nomesHora);
        var minuto = LeitorNumerosJson.ObterNumero(item, nomesMinuto) ?? 0;
        if (hora is null) return null;
        return (int)(hora.Value * 60 + minuto);
    }

    private ComparacaoEventoSolar[] MontarSerieEventoSolar(IReadOnlyList<ResultadoDiaSolar> diasSolares, bool usarNascerDoSol)
    {
        var serieBase = diasSolares
            .Select(dia => new
            {
                dia.Data,
                Horario = usarNascerDoSol ? dia.NascerDoSol : dia.PorDoSol,
                Minutos = usarNascerDoSol ? dia.NascerMinutos : dia.PorMinutos
            })
            .Where(item => item.Minutos is not null)
            .ToArray();

        var minutosIniciais = serieBase.FirstOrDefault()?.Minutos;
        return serieBase
            .Select(item => new ComparacaoEventoSolar(
                item.Data,
                item.Horario,
                minutosIniciais is null || item.Minutos is null ? null : item.Minutos.Value - minutosIniciais.Value))
            .ToArray();
    }

    private int CalcularDuracaoMinutos(int inicio, int fim)
    {
        var diferenca = fim - inicio;
        return diferenca < 0 ? diferenca + (24 * 60) : diferenca;
    }

    private string CalcularRotuloDuracao(int inicio, int fim)
    {
        var diferenca = CalcularDuracaoMinutos(inicio, fim);
        var horas = diferenca / 60;
        var minutos = diferenca % 60;
        return minutos == 0 ? $"{horas}h" : $"{horas}h{minutos:00}";
    }

    private string FormatarMinutos(int minutos) => $"{(minutos / 60) % 24:00}:{minutos % 60:00}";

    private string TendenciaPorDeltaMinutos(int delta)
    {
        if (Math.Abs(delta) < 1) return "estável";
        return delta > 0 ? "mais tarde" : "mais cedo";
    }
}
