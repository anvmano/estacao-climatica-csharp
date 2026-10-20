namespace EstacaoClimatica.Application.Configuration;

public sealed class OpcoesFirebase
{
    public string DatabaseUrl { get; set; } = "";
    public CaminhosFirebase Paths { get; set; } = new();
    public int CacheDurationSeconds { get; set; } = 30;
    public int MaxRetryAttempts { get; set; } = 3;
}

public sealed class CaminhosFirebase
{
    public string Room { get; set; } = "historico/Temperatura";
    public string Solar { get; set; } = "historico/NascePorDoSol";
    public string Aquarium { get; set; } = "historico/Aquario";
    public string LivingRoom { get; set; } = "historico/AirQuality";
}
