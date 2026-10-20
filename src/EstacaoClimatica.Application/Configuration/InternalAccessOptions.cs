namespace EstacaoClimatica.Application.Configuration;

public sealed class OpcoesAcessoInterno
{
    public bool Enforce { get; init; } = true;
    public string FirebaseApiKey { get; init; } = "SUA_CHAVE_FIREBASE";
    public string TokenLookupUrl { get; init; } = "https://identitytoolkit.googleapis.com/v1/accounts:lookup";
    public string[] AuthorizedEmails { get; init; } = ["usuario@example.com", "outra@example.com"];
}
