namespace EstacaoClimatica.Application.Abstractions;

public interface IVerificadorTokenAcessoInterno
{
    Task<string?> ObterEmailAsync(string idToken, CancellationToken tokenCancelamento);
}
