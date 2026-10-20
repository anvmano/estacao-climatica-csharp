using System.Text.Json;

namespace EstacaoClimatica.Application.Abstractions;

public interface IFonteDadosFirebase
{
    Task<JsonElement?> ObterCaminhoAsync(string caminho, CancellationToken tokenCancelamento);
    Task<JsonElement?> ObterCaminhoComQueryAsync(string caminho, string parametrosConsulta, CancellationToken tokenCancelamento);
}
