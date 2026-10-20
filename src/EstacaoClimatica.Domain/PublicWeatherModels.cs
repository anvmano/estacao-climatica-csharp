namespace EstacaoClimatica.Domain;

public sealed record PublicWeatherData(
    PublicWeatherOrigin Origem,
    string AtualizadoEm,
    PublicCurrentWeather ClimaAtual,
    PublicHourlySeries SeriesHorarias,
    PublicAirQuality Aqi,
    PublicSolarCycle? CicloSolar,
    IReadOnlyList<PublicShortTermForecast>? PrevisaoCurtoPrazo = null,
    PublicDailyForecast? PrevisaoDiaria = null,
    PublicEnvironmentalInsights? Insights = null,
    IReadOnlyList<PrevisaoDiaria>? PrevisaoQuinzeDias = null);

public sealed record PublicWeatherOrigin(
    string Tipo,
    string Rotulo,
    double Latitude,
    double Longitude,
    string? Cep = null,
    double? Precisao = null);

public sealed record CidadePublica(
    long? Id,
    string Nome,
    string Estado,
    string RegiaoAdministrativa,
    string Pais,
    string CodigoPais,
    double? Latitude,
    double? Longitude,
    string FusoHorario,
    string Rotulo);

public sealed record PublicCurrentWeather(
    double? Temperatura,
    double? SensacaoTermica,
    double? Umidade,
    double? Pressao,
    double? PontoOrvalho = null,
    double? Precipitacao = null,
    double? Chuva = null,
    double? CodigoTempo = null,
    double? Nebulosidade = null,
    double? VelocidadeVento = null,
    double? RajadaVento = null,
    double? IndiceUv = null);

public sealed record PublicHourlySeries(
    IReadOnlyList<string> Horarios,
    IReadOnlyList<double?> Temperatura,
    IReadOnlyList<double?> SensacaoTermica,
    IReadOnlyList<double?> Umidade,
    IReadOnlyList<double?> Pressao,
    IReadOnlyList<double?>? PontoOrvalho = null);

public sealed record PublicAirQuality(
    double? Valor,
    IReadOnlyList<string> Horarios,
    IReadOnlyList<double?> Serie);

public sealed record PublicSolarCycle(
    double Dawn,
    double Sunrise,
    double Zenith,
    double Sunset,
    double Dusk,
    double DaylightDuration);

public sealed record PublicShortTermForecast(
    string Horario,
    double? ProbabilidadeChuva,
    double? Precipitacao,
    double? Chuva,
    double? Pancadas,
    double? CodigoTempo,
    double? Nebulosidade,
    double? VelocidadeVento,
    double? RajadaVento,
    double? IndiceUv);

public sealed record PublicDailyForecast(
    double? IndiceUvMaximo,
    double? ProbabilidadeChuvaMaxima,
    double? PrecipitacaoTotal,
    double? CodigoTempo);

public sealed record PrevisaoDiaria(
    DateOnly Data,
    double? TemperaturaMinima,
    double? TemperaturaMaxima,
    double? Precipitacao,
    double? ProbabilidadeChuva,
    double? IndiceUvMaximo,
    int? CodigoClimatico,
    bool MaiorIncerteza);

public sealed record PublicEnvironmentalInsights(
    VentilationRecommendation Ventilacao,
    RainForecastInsight Chuva,
    UvIndexInsight IndiceUv,
    MoldRiskInsight RiscoMofo);

public sealed record InternalEnvironmentalInsights(
    VentilationRecommendation Ventilacao,
    MoldRiskInsight RiscoSala,
    MoldRiskInsight RiscoQuarto);

public sealed record VentilationRecommendation(
    string Rotulo,
    string Classe,
    string Descricao);

public sealed record RainForecastInsight(
    string Rotulo,
    string Classe,
    string Descricao,
    double? Probabilidade,
    double? IntensidadeMaxima,
    double? Acumulado,
    int PeriodoHoras,
    string? HorarioPico);

public sealed record UvIndexInsight(
    string Rotulo,
    string Classe,
    string Descricao,
    double? Valor,
    double? Maximo,
    bool UsaMaximoComoFallback);

public sealed record MoldRiskInsight(
    string Nivel,
    string Rotulo,
    string Classe,
    string Descricao,
    double? PontoOrvalho,
    double? MargemCondensacao);
