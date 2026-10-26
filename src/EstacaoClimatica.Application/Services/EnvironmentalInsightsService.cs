using System.Globalization;
using EstacaoClimatica.Application.Utilities;
using EstacaoClimatica.Domain;

namespace EstacaoClimatica.Application.Services;

public sealed class ServicoAnalisesAmbientais
{
    private const double UmidadeAtencao = 60;
    private const double UmidadeAlta = 70;
    private const double UmidadeMuitoAlta = 80;
    private const double DiferencaOrvalhoAtencao = 4;
    private const double DiferencaOrvalhoAlta = 2;
    private const double AqiAtencao = 100;
    private const double ChuvaProvavel = 60;
    private const double ChuvaPossivel = 35;
    private const double RajadaForte = 40;

    public double? CalcularPontoOrvalho(double? temperatura, double? umidade)
    {
        if (temperatura is null || umidade is null || !double.IsFinite(temperatura.Value) ||
            !double.IsFinite(umidade.Value) || umidade <= 0 || umidade > 100)
        {
            return null;
        }

        // Equação de Magnus: estimativa orientativa adequada para ambientes residenciais.
        const double constanteA = 17.62;
        const double constanteB = 243.12;
        var gama = Math.Log(umidade.Value / 100) +
            ((constanteA * temperatura.Value) / (constanteB + temperatura.Value));
        var pontoOrvalho = (constanteB * gama) / (constanteA - gama);
        return double.IsFinite(pontoOrvalho) ? pontoOrvalho : null;
    }

    public MoldRiskInsight AvaliarRiscoMofo(double? temperatura, double? umidade, double? pontoOrvalho = null)
    {
        var orvalho = NumeroOuNulo(pontoOrvalho) ?? CalcularPontoOrvalho(temperatura, umidade);
        var temperaturaValida = NumeroOuNulo(temperatura);
        var umidadeValida = NumeroOuNulo(umidade);
        if (temperaturaValida is null || umidadeValida is null || orvalho is null)
        {
            return RiscoIndisponivel("Sem dados suficientes para estimar o risco de mofo.");
        }

        var margem = temperaturaValida.Value - orvalho.Value;
        if (umidadeValida >= UmidadeMuitoAlta || margem <= DiferencaOrvalhoAlta)
        {
            return new("alto", "Risco alto", "alerta",
                "Umidade elevada ou ar próximo da condensação. Ventile e acompanhe superfícies frias.", orvalho, margem);
        }

        if (umidadeValida >= UmidadeAlta || margem <= DiferencaOrvalhoAtencao)
        {
            return new("moderado", "Atenção", "atencao",
                "Há condição favorável à condensação em superfícies mais frias.", orvalho, margem);
        }

        if (umidadeValida >= UmidadeAtencao)
        {
            return new("baixo", "Monitorar", "atencao",
                "Risco ainda baixo, mas a umidade já pede acompanhamento.", orvalho, margem);
        }

        return new("muito_baixo", "Risco baixo", "favoravel",
            "O ar está distante da condensação e da faixa mais favorável ao mofo.", orvalho, margem);
    }

    public UvIndexInsight AnalisarIndiceUv(double? valorAtual, double? valorMaximo = null)
    {
        var atual = NumeroOuNulo(valorAtual);
        var maximo = NumeroOuNulo(valorMaximo);
        var valor = atual ?? maximo;
        if (valor is null)
        {
            return new("Indisponível", "indisponivel", "Índice UV indisponível para esta localização.", null, maximo, false);
        }

        var alternativa = atual is null && maximo is not null;
        return valor.Value switch
        {
            < 3 => new("Baixo", "favoravel", "Proteção básica em exposições prolongadas.", valor, maximo, alternativa),
            < 6 => new("Moderado", "atencao", "Use protetor solar e prefira sombra perto do meio-dia.", valor, maximo, alternativa),
            < 8 => new("Alto", "alerta", "Reduza a exposição direta e use proteção solar completa.", valor, maximo, alternativa),
            < 11 => new("Muito alto", "alerta", "Evite exposição prolongada nos horários de pico.", valor, maximo, alternativa),
            _ => new("Extremo", "critico", "Evite o sol direto; pele e olhos podem sofrer dano rapidamente.", valor, maximo, alternativa)
        };
    }

    public RainForecastInsight ResumirChuva(
        IReadOnlyList<PublicShortTermForecast>? previsao,
        string? atualizadoEm,
        int quantidadeHoras = 6)
    {
        if (!DateTime.TryParse(atualizadoEm, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var inicio))
        {
            inicio = RelogioClimatico.Agora;
        }

        var limite = inicio.AddHours(quantidadeHoras);
        var proximasHoras = (previsao ?? [])
            .Select(item => new { Item = item, Horario = DataValida(item.Horario) })
            .Where(item => item.Horario is not null && item.Horario >= inicio && item.Horario <= limite)
            .ToArray();
        if (proximasHoras.Length == 0)
        {
            return ChuvaIndisponivel(quantidadeHoras);
        }

        var probabilidades = proximasHoras.Select(item => NumeroOuNulo(item.Item.ProbabilidadeChuva)).Where(item => item is not null).Select(item => item!.Value).ToArray();
        var intensidades = proximasHoras.Select(item => NumeroOuNulo(item.Item.Precipitacao)).Where(item => item is not null).Select(item => item!.Value).ToArray();
        var probabilidade = probabilidades.Length > 0 ? probabilidades.Max() : (double?)null;
        var intensidadeMaxima = intensidades.Length > 0 ? intensidades.Max() : (double?)null;
        var acumulado = intensidades.Length > 0 ? intensidades.Sum() : (double?)null;

        var pico = proximasHoras
            .Where(item => NumeroOuNulo(item.Item.Precipitacao) is not null)
            .OrderByDescending(item => item.Item.Precipitacao)
            .FirstOrDefault();
        var horarioPico = pico?.Horario?.ToString("HH:mm", CultureInfo.InvariantCulture);

        var rotulo = "Sem chuva relevante";
        var classe = "favoravel";
        var descricao = "Baixa indicação de chuva nas próximas horas.";
        if ((probabilidade ?? 0) >= ChuvaProvavel || (intensidadeMaxima ?? 0) >= 2.5)
        {
            rotulo = (intensidadeMaxima ?? 0) >= 10 ? "Chuva forte" :
                (intensidadeMaxima ?? 0) >= 2.5 ? "Chuva moderada" : "Chuva provável";
            classe = (intensidadeMaxima ?? 0) >= 10 ? "critico" : "alerta";
            descricao = $"Maior intensidade prevista perto de {horarioPico ?? "--:--"}.";
        }
        else if ((probabilidade ?? 0) >= ChuvaPossivel || (acumulado ?? 0) >= 0.1)
        {
            rotulo = "Possibilidade de chuva";
            classe = "atencao";
            descricao = $"Pode chover perto de {horarioPico ?? "--:--"}.";
        }

        return new(rotulo, classe, descricao, probabilidade, intensidadeMaxima, acumulado, quantidadeHoras, horarioPico);
    }

    public VentilationRecommendation RecomendarVentilacaoExterna(
        PublicCurrentWeather climaAtual,
        double? valorAqi,
        RainForecastInsight? chuva)
    {
        var temperatura = NumeroOuNulo(climaAtual.Temperatura);
        var umidade = NumeroOuNulo(climaAtual.Umidade);
        var rajada = NumeroOuNulo(climaAtual.RajadaVento);
        var aqi = NumeroOuNulo(valorAqi);
        if (temperatura is null && umidade is null && aqi is null)
        {
            return VentilacaoIndisponivel("Sem dados externos suficientes para recomendar ventilação.");
        }

        var impedimentos = new List<string>();
        if (aqi > AqiAtencao) impedimentos.Add("qualidade do ar externa desfavorável");
        if (chuva?.Classe is "alerta" or "critico") impedimentos.Add("chuva provável");
        if (rajada >= RajadaForte) impedimentos.Add("rajadas fortes");
        if (temperatura is < 12 or > 33) impedimentos.Add("temperatura externa extrema");
        if (impedimentos.Count > 0)
        {
            return new("Evite por enquanto", "alerta", $"Condição externa limitante: {string.Join(", ", impedimentos)}.");
        }

        var ressalvas = new List<string>();
        if (umidade is < 30 or > 78) ressalvas.Add("umidade externa pouco favorável");
        if (temperatura is < 16 or > 30) ressalvas.Add("temperatura externa fora da faixa amena");
        if (chuva?.Classe == "atencao") ressalvas.Add("há possibilidade de chuva");
        return ressalvas.Count > 0
            ? new("Ventile com atenção", "atencao", $"{string.Join("; ", ressalvas)}. Prefira uma abertura curta e acompanhe o ambiente.")
            : new("Boa hora para ventilar", "favoravel", "As condições externas estão favoráveis à renovação do ar.");
    }

    public VentilationRecommendation RecomendarVentilacaoInterna(
        double? temperatura,
        double? umidade,
        double? aqi,
        MoldRiskInsight? riscoMofo)
    {
        temperatura = NumeroOuNulo(temperatura);
        umidade = NumeroOuNulo(umidade);
        aqi = NumeroOuNulo(aqi);
        if (temperatura is null && umidade is null && aqi is null)
        {
            return VentilacaoIndisponivel("Sem leituras internas suficientes para avaliar ventilação.");
        }

        var motivos = new List<string>();
        if (aqi > AqiAtencao) motivos.Add("AQI interno elevado");
        if (umidade > 60) motivos.Add("umidade acima da faixa de conforto");
        if (temperatura > 26) motivos.Add("temperatura acima da faixa de conforto");
        if (riscoMofo?.Nivel is "moderado" or "alto") motivos.Add("risco de condensação/mofo");
        return motivos.Count > 0
            ? new("Renovação recomendada", "atencao", $"{string.Join("; ", motivos)}. Consulte o clima externo antes de abrir as janelas.")
            : new("Ambiente equilibrado", "favoravel", "Os sensores internos não indicam necessidade urgente de ventilação.");
    }

    public VentilationRecommendation RecomendarVentilacaoCombinada(
        VentilationRecommendation interna,
        VentilationRecommendation externa)
    {
        if (interna.Classe == "indisponivel") return externa;
        if (externa.Classe == "indisponivel") return interna;
        if (externa.Classe is "alerta" or "critico") return externa;
        if (interna.Rotulo == "Renovação recomendada" && externa.Classe == "favoravel")
        {
            return new("Ventile agora", "favoravel", "O ambiente pede renovação e as condições externas estão favoráveis.");
        }
        if (interna.Rotulo == "Renovação recomendada")
        {
            return new("Ventile brevemente", "atencao", "O ambiente pede renovação, mas há ressalvas no clima externo.");
        }
        return externa.Classe == "favoravel" ? interna : externa;
    }

    private static double? NumeroOuNulo(double? valor) => valor is not null && double.IsFinite(valor.Value) ? valor : null;

    private static DateTime? DataValida(string? valor) =>
        DateTime.TryParse(valor, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var data) ? data : null;

    private static MoldRiskInsight RiscoIndisponivel(string descricao) =>
        new("indisponivel", "Indisponível", "indisponivel", descricao, null, null);

    private static RainForecastInsight ChuvaIndisponivel(int horas) =>
        new("Indisponível", "indisponivel", $"Previsão de chuva indisponível para as próximas {horas}h.", null, null, null, horas, null);

    private static VentilationRecommendation VentilacaoIndisponivel(string descricao) =>
        new("Indisponível", "indisponivel", descricao);
}
