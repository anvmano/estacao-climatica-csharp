namespace EstacaoClimatica.Application.Configuration;

internal static class CamposFirebase
{
    public static class Quarto
    {
        public const string Temperatura = "Temperatura";
        public const string SensacaoTermica = "Sensacao termica";
        public const string Umidade = "Umidade";
    }

    public static class Sala
    {
        public const string Temperatura = "temperatura";
        public const string SensacaoTermica = "sensacaoTermica";
        public const string Umidade = "umidade";
        public const string Pressao = "pressao";
        public const string Co = "CO";
        public const string Co2 = "CO2";
        public const string Acetona = "Aceton";
        public const string Alcool = "Alcohol";
        public const string Amonia = "NH4";
        public const string Tolueno = "Toluen";
    }

    public static class Aquario
    {
        public const string Temperatura = "temperaturaDS18B20";
        public const string Ph = "PH";
        public const string Tds = "TDS";
        public const string Turbidez = "Turbidez";
    }

    public static class Solar
    {
        public static readonly string[] CamposIdentificacao =
        {
            "HourNascerDoSol",
            "HoraNascerDoSol",
            "HoraAmanhecer",
            "HourAmanhecer"
        };

        public static readonly string[] HoraAmanhecer = { "HoraAmanhecer", "HourAmanhecer" };
        public static readonly string[] MinutoAmanhecer = { "MinuteAmanhecer", "MinutoAmanhecer" };
        public static readonly string[] HoraNascerDoSol = { "HourNascerDoSol", "HoraNascerDoSol" };
        public static readonly string[] MinutoNascerDoSol = { "MinuteNascerDoSol", "MinutoNascerDoSol" };
        public static readonly string[] HoraPorDoSol = { "HoraPorDoSol", "HourPorDoSol" };
        public static readonly string[] MinutoPorDoSol = { "MinutePorDoSol", "MinutoPorDoSol" };
        public static readonly string[] HoraAnoitecer = { "HourAnoitecer", "HoraAnoitecer" };
        public static readonly string[] MinutoAnoitecer = { "MinuteAnoitecer", "MinutoAnoitecer" };
        public static readonly string[] HoraZenite = { "HoraZenite", "HourZenith", "HoraZenith", "HourZenite", "HoraZênite" };
        public static readonly string[] MinutoZenite = { "MinuteZenite", "MinutoZenite", "MinuteZenith", "MinutoZenith", "MinutoZênite" };
    }
}
