using EstacaoClimatica.Application.Services;

namespace EstacaoClimatica.Application.Abstractions;

public interface IRepositorioDadosClimaticos
{
    Task<DadosDashboardFirebase> ObterDadosDashboardAsync(CancellationToken tokenCancelamento);
    Task<DadosDashboardFirebase> ObterDadosDashboardAsync(string? dataSelecionada, CancellationToken tokenCancelamento);

    Task<DadosDashboardFirebase> ObterDadosPorRamosAsync(bool quarto, bool solar, bool aquario, bool sala, CancellationToken tokenCancelamento) =>
        ObterDadosDashboardAsync(tokenCancelamento);
}
