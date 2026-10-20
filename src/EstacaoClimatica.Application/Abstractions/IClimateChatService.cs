using EstacaoClimatica.Domain;

namespace EstacaoClimatica.Application.Abstractions;

public interface IServicoAssistenteClimatico
{
    Task<ChatResponse> ResponderAsync(ChatRequest requisicao, CancellationToken tokenCancelamento);
}
