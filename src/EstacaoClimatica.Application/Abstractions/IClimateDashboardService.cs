using EstacaoClimatica.Domain;

namespace EstacaoClimatica.Application.Abstractions;

public interface IServicoDashboardClimatico
{
    Task<DashboardResponse> ObterDashboardAsync(string? dataFirebase, CancellationToken tokenCancelamento);
}
