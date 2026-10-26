namespace EstacaoClimatica.Application.Utilities;

public static class RelogioClimatico
{
    private static readonly AsyncLocal<TimeZoneInfo?> FusoConsulta = new();
    public static DateTime Agora => Converter(DateTimeOffset.UtcNow, FusoConsulta.Value ?? TimeZoneInfo.Local);
    public static DateTime Hoje => Agora.Date;

    public static DateTime Converter(DateTimeOffset instante, TimeZoneInfo fuso) =>
        TimeZoneInfo.ConvertTime(instante, fuso).DateTime;

    public static IDisposable UsarFuso(TimeZoneInfo fuso)
    {
        var anterior = FusoConsulta.Value;
        FusoConsulta.Value = fuso;
        return new EscopoFuso(anterior);
    }

    private sealed class EscopoFuso(TimeZoneInfo? anterior) : IDisposable
    {
        public void Dispose() => FusoConsulta.Value = anterior;
    }
}
