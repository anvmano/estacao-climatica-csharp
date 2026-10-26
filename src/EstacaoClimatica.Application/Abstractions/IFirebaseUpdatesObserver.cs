namespace EstacaoClimatica.Application.Abstractions;

public sealed record AtualizacaoFirebase(string Ramo, bool Falhou);

public interface IObservadorAtualizacoesFirebase
{
    IAsyncEnumerable<AtualizacaoFirebase> ObservarAsync(CancellationToken tokenCancelamento);
}
