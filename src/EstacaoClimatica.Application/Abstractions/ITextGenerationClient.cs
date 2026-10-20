namespace EstacaoClimatica.Application.Abstractions;

public interface IClienteGeracaoTexto
{
    Task<string> GerarTextoAsync(string instrucao, CancellationToken tokenCancelamento);
}
