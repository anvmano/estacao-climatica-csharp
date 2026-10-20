namespace EstacaoClimatica.Application.Configuration;

public sealed class OpcoesClimaPublico
{
    public string BrasilApiCepUrl { get; set; } = "https://brasilapi.com.br/api/cep/v2";
    public string ViaCepUrl { get; set; } = "https://viacep.com.br/ws";
    public string OpenMeteoGeocodingUrl { get; set; } = "https://geocoding-api.open-meteo.com/v1/search";
    public string OpenMeteoForecastUrl { get; set; } = "https://api.open-meteo.com/v1/forecast";
    public string OpenMeteoAirQualityUrl { get; set; } = "https://air-quality-api.open-meteo.com/v1/air-quality";
}
