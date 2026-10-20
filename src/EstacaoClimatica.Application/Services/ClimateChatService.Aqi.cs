using System.Text.Json;
using EstacaoClimatica.Application.Configuration;
using EstacaoClimatica.Application.Utilities;

namespace EstacaoClimatica.Application.Services;

public sealed partial class ServicoAssistenteClimatico
{
    private ResultadoConsultaAssistente ExecutarConsultaAqi(ContextoAssistente contexto, IntencaoConsulta intencao, string pergunta)
    {
        var datasPeriodo = ResolverDatasPeriodo(intencao.Period).ToArray();
        if (datasPeriodo.Length == 0)
        {
            return ResultadoConsultaAssistente.CriarEsclarecimento("Não consegui identificar o período da consulta de qualidade do ar.", pergunta, intencao);
        }

        var janelaMovel = ObterJanelaMovel(intencao.Period);
        var diasAqi = datasPeriodo
            .Select(data => MontarQualidadeArDiaria(contexto.Data.Sala, data, intencao.Hour, intencao.HourRange, janelaMovel))
            .Where(dia => dia is not null)
            .Cast<ResultadoAqiDiario>()
            .ToArray();

        var resultadoBase = new ResultadoConsultaMetrica(
            "Sala",
            "AQI estimado",
            "",
            intencao.Operation,
            "aqi_estimado_mq135",
            FormatarRotuloPeriodo(intencao.Period, datasPeriodo),
            datasPeriodo.Select(FormatarData).ToArray(),
            diasAqi.Select(dia => dia.Data).ToArray(),
            diasAqi.Length,
            false,
            "",
            "qualidade_ar",
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

        var metricaAqi = diasAqi.Length == 0
            ? resultadoBase with
            {
                SemDados = true,
                Mensagem = intencao.Hour is null
                    ? $"Sem dados de AQI estimado em Sala para {FormatarRotuloPeriodo(intencao.Period, datasPeriodo)}."
                    : $"Sem dados de AQI estimado em Sala para {FormatarRotuloPeriodo(intencao.Period, datasPeriodo)} às {FormatarRotuloHora(intencao.Hour)}."
            }
            : MontarResultadoAnaliticoAqi(resultadoBase, diasAqi, intencao);

        return new ResultadoConsultaAssistente(
            pergunta,
            intencao,
            new IntencaoCalculada(
                new[] { "Sala" },
                new[] { "AQI estimado" },
                intencao.Operation,
                "aqi_estimado_mq135",
                FormatarRotuloPeriodo(intencao.Period, datasPeriodo),
                datasPeriodo),
            new[] { new ResultadoConsultaAmbiente("Sala", new[] { metricaAqi }) },
            false,
            "");
    }

    private ResultadoConsultaMetrica MontarResultadoAnaliticoAqi(ResultadoConsultaMetrica resultadoBase, IReadOnlyList<ResultadoAqiDiario> diasAqi, IntencaoConsulta intencao)
    {
        if (intencao.Hour is not null)
        {
            var dia = diasAqi[0];
            return resultadoBase with
            {
                TipoResultado = "consulta_horaria",
                Media = dia.Aqi,
                Minima = dia.Aqi,
                Maxima = dia.Aqi,
                Amostras = 1,
                Aqi = new ResultadoAnaliseAqi("consulta_horaria", dia.Aqi, dia.Classificacao, dia.Impacto, dia.Dominante, dia.Horario, dia.Subindices, diasAqi)
            };
        }

        var valores = diasAqi.Select(dia => (double)dia.Aqi).ToArray();
        var estatisticas = CalcularEstatisticas(valores);
        var maisRecente = diasAqi[^1];

        return resultadoBase with
        {
            Media = Math.Round(estatisticas.Avg),
            Minima = Math.Round(estatisticas.Min),
            Maxima = Math.Round(estatisticas.Max),
            Delta = Math.Round(estatisticas.Delta),
            Tendencia = TendenciaDaDiferenca(estatisticas.Delta),
            Amostras = diasAqi.Count,
            Aqi = new ResultadoAnaliseAqi("qualidade_ar", maisRecente.Aqi, maisRecente.Classificacao, maisRecente.Impacto, maisRecente.Dominante, maisRecente.Horario, maisRecente.Subindices, diasAqi)
        };
    }

    private ResultadoAqiDiario? MontarQualidadeArDiaria(JsonElement? raiz, string data, string? hora, FaixaHoraria? faixaHoraria, JanelaMovel? janelaMovel)
    {
        var raizDia = LeitorRegistrosFirebase.FiltrarDataSelecionada(raiz, data);
        var maisRecente = LeitorRegistrosFirebase.EnumerarRegistros(raizDia)
            .Where(registro => hora is null || NormalizarHoraRegistro(registro.Horario) == hora)
            .Where(registro => HoraEstaNaFaixa(NormalizarHoraRegistro(registro.Horario), faixaHoraria))
            .Where(registro => EstaNaJanelaMovel(data, registro.Horario, janelaMovel))
            .Select(registro => new
            {
                Timestamp = InterpretarDataHoraFirebase(data, registro.Horario),
                Horario = FormatarHoraRegistro(registro.Horario),
                Item = registro.Item
            })
            .Where(registro => registro.Timestamp is not null)
            .OrderBy(registro => registro.Timestamp)
            .LastOrDefault();

        if (maisRecente is null) return null;

        var subindices = CriarPoluentesAqi()
            .Select(poluente => CalcularSubindiceAqi(poluente, maisRecente.Item))
            .Where(item => item is not null)
            .Cast<SubindiceAqi>()
            .OrderByDescending(item => item.Aqi)
            .ToArray();

        if (subindices.Length == 0) return null;

        var dominante = subindices[0];
        var aqi = Math.Min(500, Math.Max(0, (int)Math.Round(dominante.Aqi)));
        var categoria = ObterCategoriaAqi(aqi);

        return new ResultadoAqiDiario(
            FormatarData(data),
            aqi,
            categoria.Label,
            categoria.Impact,
            $"{dominante.Label} ({FormatarNumero(dominante.Value)}{dominante.Unit})",
            $"{FormatarData(data)[..5]} {maisRecente.Horario}",
            subindices.Take(6).Select(item => new SubindiceAqi(item.Key, item.Label, item.Unit, item.Value, Math.Round(item.Aqi))).ToArray());
    }

    private SubindiceAqi? CalcularSubindiceAqi(PoluenteAqi poluente, JsonElement item)
    {
        var concentracao = LeitorNumerosJson.ObterNumero(item, poluente.Key);
        if (concentracao is null) return null;

        var faixaIndice = poluente.Breakpoints.FirstOrDefault(faixaIndice => concentracao >= faixaIndice.Low && concentracao <= faixaIndice.High)
            ?? poluente.Breakpoints[^1];
        var limitado = Math.Min(faixaIndice.High, Math.Max(faixaIndice.Low, concentracao.Value));
        var aqi = ((faixaIndice.IndexHigh - faixaIndice.IndexLow) / (faixaIndice.High - faixaIndice.Low)) * (limitado - faixaIndice.Low) + faixaIndice.IndexLow;

        return new SubindiceAqi(poluente.Key, poluente.Label, poluente.Unit, concentracao.Value, aqi);
    }

    private CategoriaAqi ObterCategoriaAqi(int aqi)
    {
        return CriarCategoriasAqi().FirstOrDefault(categoria => aqi >= categoria.Min && aqi <= categoria.Max)
            ?? CriarCategoriasAqi()[^1];
    }

    private CategoriaAqi[] CriarCategoriasAqi()
    {
        return new[]
        {
            new CategoriaAqi(0, 50, "Boa", "Ar satisfatório, risco mínimo."),
            new CategoriaAqi(51, 100, "Moderado", "Aceitável, mas pode haver risco moderado para grupos sensíveis."),
            new CategoriaAqi(101, 150, "Insalubre para grupos sensíveis", "Grupos de risco podem apresentar sintomas."),
            new CategoriaAqi(151, 200, "Insalubre", "Toda a população pode começar a sentir efeitos na saúde."),
            new CategoriaAqi(201, 300, "Muito insalubre", "Alerta de saúde; riscos aumentados para todos."),
            new CategoriaAqi(301, 500, "Perigoso", "Condições de emergência; toda a população é severamente afetada.")
        };
    }

    private PoluenteAqi[] CriarPoluentesAqi()
    {
        return new[]
        {
            new PoluenteAqi(CamposFirebase.Sala.Co, "CO", "ppm", new[] { new FaixaAqi(0, 4.4, 0, 50), new FaixaAqi(4.5, 9.4, 51, 100), new FaixaAqi(9.5, 12.4, 101, 150), new FaixaAqi(12.5, 15.4, 151, 200), new FaixaAqi(15.5, 30.4, 201, 300), new FaixaAqi(30.5, 50.4, 301, 500) }),
            new PoluenteAqi(CamposFirebase.Sala.Co2, "CO2", "ppm", new[] { new FaixaAqi(0, 700, 0, 50), new FaixaAqi(701, 1000, 51, 100), new FaixaAqi(1001, 1500, 101, 150), new FaixaAqi(1501, 2000, 151, 200), new FaixaAqi(2001, 5000, 201, 300), new FaixaAqi(5001, 10000, 301, 500) }),
            new PoluenteAqi(CamposFirebase.Sala.Tolueno, "Tolueno", "ppm", new[] { new FaixaAqi(0, 1, 0, 50), new FaixaAqi(1.1, 5, 51, 100), new FaixaAqi(5.1, 10, 101, 150), new FaixaAqi(10.1, 25, 151, 200), new FaixaAqi(25.1, 50, 201, 300), new FaixaAqi(50.1, 100, 301, 500) }),
            new PoluenteAqi(CamposFirebase.Sala.Amonia, "Amônia", "ppm", new[] { new FaixaAqi(0, 1, 0, 50), new FaixaAqi(1.1, 5, 51, 100), new FaixaAqi(5.1, 10, 101, 150), new FaixaAqi(10.1, 25, 151, 200), new FaixaAqi(25.1, 50, 201, 300), new FaixaAqi(50.1, 100, 301, 500) }),
            new PoluenteAqi(CamposFirebase.Sala.Acetona, "Acetona", "ppm", new[] { new FaixaAqi(0, 1, 0, 50), new FaixaAqi(1.1, 5, 51, 100), new FaixaAqi(5.1, 10, 101, 150), new FaixaAqi(10.1, 25, 151, 200), new FaixaAqi(25.1, 50, 201, 300), new FaixaAqi(50.1, 100, 301, 500) }),
            new PoluenteAqi(CamposFirebase.Sala.Alcool, "Álcool", "ppm", new[] { new FaixaAqi(0, 1, 0, 50), new FaixaAqi(1.1, 5, 51, 100), new FaixaAqi(5.1, 10, 101, 150), new FaixaAqi(10.1, 25, 151, 200), new FaixaAqi(25.1, 50, 201, 300), new FaixaAqi(50.1, 100, 301, 500) })
        };
    }
}
