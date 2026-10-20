using System.Text.Json;
using EstacaoClimatica.Application.Configuration;
using EstacaoClimatica.Application.Utilities;
using EstacaoClimatica.Domain;

namespace EstacaoClimatica.Application.Builders;

internal static class MontadorQualidadeDados
{
    private const int LeiturasEsperadasPorDia = 24;
    private const double PercentualMinimoCobertura = 90;
    private const int MinutosAteDesatualizar = 150;

    private static readonly IReadOnlyDictionary<string, ConfiguracaoQualidade> Configuracoes =
        new Dictionary<string, ConfiguracaoQualidade>(StringComparer.Ordinal)
        {
            [CamposFirebase.Quarto.Temperatura] = new(-20, 60),
            [CamposFirebase.Quarto.SensacaoTermica] = new(-30, 70),
            [CamposFirebase.Quarto.Umidade] = new(0, 100),
            [CamposFirebase.Sala.Temperatura] = new(-20, 60),
            [CamposFirebase.Sala.SensacaoTermica] = new(-30, 70),
            [CamposFirebase.Sala.Umidade] = new(0, 100),
            [CamposFirebase.Sala.Pressao] = new(850, 1100),
            [CamposFirebase.Aquario.Temperatura] = new(0, 45),
            [CamposFirebase.Aquario.Ph] = new(4, 11, SaltoMaximo: 2, QuantidadeRepeticoes: 4, ToleranciaRepeticao: 0.001),
            [CamposFirebase.Aquario.Tds] = new(0, 5000),
            [CamposFirebase.Aquario.Turbidez] = new(0, 4000, QuantidadeRepeticoes: 6, ToleranciaRepeticao: 0.001, SinalizarZeroConstante: true),
            [CamposFirebase.Sala.Co] = new(0, 1000),
            [CamposFirebase.Sala.Co2] = new(0, 10000),
            [CamposFirebase.Sala.Acetona] = new(0, 1000),
            [CamposFirebase.Sala.Alcool] = new(0, 1000),
            [CamposFirebase.Sala.Amonia] = new(0, 1000),
            [CamposFirebase.Sala.Tolueno] = new(0, 1000)
        };

    public static AnaliseQualidadeDados AnalisarSerie(JsonElement? dados, string campo, DateTime? agora = null)
    {
        var leituras = LeitorRegistrosFirebase.EnumerarRegistros(dados)
            .Select(registro => new LeituraQualidadeDados(
                registro,
                NormalizadorMedicoesAquario.NormalizarValorMedicao(campo, LeitorNumerosJson.ObterNumero(registro.Item, campo))))
            .Where(leitura => leitura.Valor is not null)
            .Select(leitura => leitura with { Valor = leitura.Valor!.Value })
            .ToList();
        var niveis = leituras.ToDictionary(leitura => CriarChaveLeitura(leitura.Registro, campo), _ => "normal", StringComparer.Ordinal);
        var avisos = new List<string>();
        var configuracao = Configuracoes.GetValueOrDefault(campo) ?? new ConfiguracaoQualidade(null, null);

        var critico = leituras.Where(leitura =>
            (configuracao.MinimoCritico is { } minimo && leitura.Valor < minimo)
            || (configuracao.MaximoCritico is { } maximo && leitura.Valor > maximo)).ToList();
        foreach (var leitura in critico) niveis[CriarChaveLeitura(leitura.Registro, campo)] = "critica";
        if (critico.Count > 0) avisos.Add($"{critico.Count} leitura(s) fora da faixa crítica configurada.");

        if (configuracao.SaltoMaximo is { } saltoMaximo && leituras.Count > 1)
        {
            var saltos = 0;
            for (var indice = 1; indice < leituras.Count; indice++)
            {
                if (Math.Abs(leituras[indice].Valor!.Value - leituras[indice - 1].Valor!.Value) <= saltoMaximo) continue;
                MarcarSuspeito(niveis, CriarChaveLeitura(leituras[indice].Registro, campo));
                saltos++;
            }
            if (saltos > 0) avisos.Add($"{saltos} salto(s) abrupto(s) detectado(s).");
        }

        if (configuracao.QuantidadeRepeticoes is { } quantidadeRepeticoes && quantidadeRepeticoes >= 2 && leituras.Count >= quantidadeRepeticoes)
        {
            var inicio = 0;
            var sequencias = 0;
            for (var indice = 1; indice <= leituras.Count; indice++)
            {
                var continua = indice < leituras.Count
                    && Math.Abs(leituras[indice].Valor!.Value - leituras[inicio].Valor!.Value) <= configuracao.ToleranciaRepeticao;
                if (continua) continue;
                if (indice - inicio >= quantidadeRepeticoes)
                {
                    for (var posicao = inicio; posicao < indice; posicao++) MarcarSuspeito(niveis, CriarChaveLeitura(leituras[posicao].Registro, campo));
                    sequencias++;
                }
                inicio = indice;
            }
            if (sequencias > 0) avisos.Add("Série com valor repetido por período prolongado; verificar sensor.");
        }

        if (configuracao.SinalizarZeroConstante && leituras.Count >= (configuracao.QuantidadeRepeticoes ?? 2)
            && leituras.All(leitura => leitura.Valor == 0))
        {
            foreach (var leitura in leituras) MarcarSuspeito(niveis, CriarChaveLeitura(leitura.Registro, campo));
            avisos.Add("Série constante em zero; confirmar se é leitura válida, limite de detecção ou falha do sensor.");
        }

        var referencia = agora ?? DateTime.Now;
        var esperado = LeitorRegistrosFirebase.EnumerarDatas(dados).Sum(par =>
            par.Date == referencia.ToString("dd-MM-yyyy")
                ? Math.Min(LeiturasEsperadasPorDia, referencia.Hour + 1)
                : LeiturasEsperadasPorDia);
        var cobertura = esperado > 0 ? Math.Min(100, leituras.Count * 100d / esperado) : 0;
        var incompleto = leituras.Count > 0 && cobertura < PercentualMinimoCobertura;
        var semDados = leituras.Count == 0;
        var desatualizado = VerificarDesatualizacao(leituras, dados, referencia);
        var possuiCritico = niveis.Values.Contains("critica", StringComparer.Ordinal);
        var possuiSuspeito = niveis.Values.Contains("suspeita", StringComparer.Ordinal);
        if (incompleto) avisos.Insert(0, $"Dados incompletos: {leituras.Count}/{esperado} leituras.");
        if (desatualizado) avisos.Insert(0, "Sensor sem leitura recente no dia atual.");

        var nivel = possuiCritico ? "critica" : possuiSuspeito ? "suspeita" : semDados ? "sem_dados"
            : desatualizado ? "desatualizada" : incompleto ? "incompleta" : "adequada";
        var estado = semDados ? "offline" : possuiCritico || possuiSuspeito ? "suspeito"
            : desatualizado ? "desatualizado" : incompleto ? "parcial" : "ok";
        var maisRecente = leituras.LastOrDefault();
        var qualidade = new MetricQuality(
            nivel,
            ObterRotuloNivel(nivel),
            estado,
            ObterRotuloEstado(estado),
            leituras.Count,
            esperado,
            cobertura,
            maisRecente is null ? null : new LatestQualityReading(maisRecente.Registro.Data, LeitorRegistrosFirebase.FormatarRotuloHora(maisRecente.Registro.Horario)),
            avisos.Distinct(StringComparer.Ordinal).ToArray());
        return new AnaliseQualidadeDados(qualidade, niveis);
    }

    public static string CriarChaveLeitura(EstacaoClimatica.Application.Models.RegistroFirebase registro, string campo) =>
        $"{registro.Data}|{registro.Horario}|{registro.IdItem}|{campo}";

    private static bool VerificarDesatualizacao(IReadOnlyList<LeituraQualidadeDados> leituras, JsonElement? dados, DateTime agora)
    {
        if (leituras.Count == 0) return false;
        var hoje = agora.ToString("dd-MM-yyyy");
        if (!LeitorRegistrosFirebase.EnumerarDatas(dados).Any(par => par.Date == hoje)) return false;
        var maisRecente = leituras[^1].Registro;
        if (maisRecente.Data != hoje || LeitorRegistrosFirebase.ConverterHora(maisRecente.Horario) is not { } horario) return true;
        var instante = new DateTime(agora.Year, agora.Month, agora.Day, horario.Hour, horario.Minute, horario.Second ?? 0);
        return agora - instante > TimeSpan.FromMinutes(MinutosAteDesatualizar);
    }

    private static void MarcarSuspeito(IDictionary<string, string> niveis, string chave)
    {
        if (niveis.TryGetValue(chave, out var atual) && atual != "critica") niveis[chave] = "suspeita";
    }

    private static string ObterRotuloNivel(string nivel) => nivel switch
    {
        "critica" => "Leitura crítica",
        "suspeita" => "Verificar sensor",
        "sem_dados" => "Sem dados",
        "desatualizada" => "Sensor desatualizado",
        "incompleta" => "Dados incompletos",
        _ => "Dados suficientes"
    };

    private static string ObterRotuloEstado(string estado) => estado switch
    {
        "ok" => "OK",
        "parcial" => "Parcial",
        "desatualizado" => "Desatualizado",
        "suspeito" => "Suspeito",
        _ => "Offline"
    };

    private sealed record ConfiguracaoQualidade(
        double? MinimoCritico,
        double? MaximoCritico,
        double? SaltoMaximo = null,
        int? QuantidadeRepeticoes = null,
        double ToleranciaRepeticao = 0,
        bool SinalizarZeroConstante = false);

    private sealed record LeituraQualidadeDados(EstacaoClimatica.Application.Models.RegistroFirebase Registro, double? Valor);
}

internal sealed record AnaliseQualidadeDados(
    MetricQuality Qualidade,
    IReadOnlyDictionary<string, string> NiveisPorLeitura);
