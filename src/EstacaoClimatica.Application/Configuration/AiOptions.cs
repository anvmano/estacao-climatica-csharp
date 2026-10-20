namespace EstacaoClimatica.Application.Configuration;

public sealed class OpcoesIa
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "FirebaseAI";
    public string Model { get; set; } = "gemini-3.5-flash";
    public string ApiKey { get; set; } = "";
    public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
    public int MaxPeriodDays { get; set; } = 30;
    public int MaxPromptChars { get; set; } = 9000;
    public bool RequireAppCheck { get; set; }
    public OpcoesAppCheck AppCheck { get; set; } = new();
}

public sealed class OpcoesAppCheck
{
    public string Provider { get; set; } = "RecaptchaEnterprise";
    public string SiteKey { get; set; } = "";
    public bool EnforceAfterProductionValidation { get; set; }
}
