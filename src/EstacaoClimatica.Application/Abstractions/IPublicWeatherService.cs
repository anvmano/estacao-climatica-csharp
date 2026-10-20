using EstacaoClimatica.Domain;

namespace EstacaoClimatica.Application.Abstractions;

public interface IServicoClimaPublico
{
    Task<PublicWeatherData> BuscarPorCepAsync(string cep, CancellationToken tokenCancelamento);

    Task<IReadOnlyList<CidadePublica>> PesquisarCidadesAsync(string termo, CancellationToken tokenCancelamento);

    Task<PublicWeatherData> BuscarPorCidadeAsync(CidadePublica cidade, CancellationToken tokenCancelamento);

    Task<PublicWeatherData> BuscarPorCoordenadasAsync(
        double latitude,
        double longitude,
        string tipoOrigem,
        string rotuloOrigem,
        string? cep,
        double? precisao,
        CancellationToken tokenCancelamento);
}
