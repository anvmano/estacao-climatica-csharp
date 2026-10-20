using EstacaoClimatica.Application.Services;

namespace EstacaoClimatica.Application.Abstractions;

public interface IRepositorioDadosClimaticos
{
    Task<DadosDashboardFirebase> ObterDadosDashboardAsync(CancellationToken tokenCancelamento);
    Task<DadosDashboardFirebase> ObterDadosDashboardAsync(string? dataSelecionada, CancellationToken tokenCancelamento);
}
