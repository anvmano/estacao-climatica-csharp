using EstacaoClimatica.Domain;

namespace EstacaoClimatica.Application.Abstractions;

public interface IServicoConsultasClimaticas
{
    IReadOnlyList<EnvironmentDescriptor> ObterAmbientes();

    Task<LatestMeasurementResponse> ObterUltimaMedicaoAsync(
        string ambiente,
        CancellationToken tokenCancelamento);

    Task<MeasurementsResponse> ObterMedicoesAsync(
        string ambiente,
        string? inicio,
        string? fim,
        string? metricas,
        int pagina,
        int tamanhoPagina,
        CancellationToken tokenCancelamento);

    Task<MetricSeriesResponse> ObterSerieAsync(
        string metrica,
        string? data,
        string? ambientes,
        CancellationToken tokenCancelamento);

    Task<DashboardSummaryResponse> ObterResumoAsync(
        string? data,
        CancellationToken tokenCancelamento);

    Task<AirQualityResponse> ObterQualidadeArAsync(
        string? data,
        string? inicio,
        string? fim,
        CancellationToken tokenCancelamento);

    Task<SolarDetailsResponse> ObterSolarAsync(
        string? data,
        CancellationToken tokenCancelamento);

    Task<AlertsResponse> ObterAlertasAsync(
        string? data,
        string? severidade,
        string? ambiente,
        CancellationToken tokenCancelamento);

    Task<ComparisonResponse> CompararAsync(
        ComparisonRequest requisicao,
        CancellationToken tokenCancelamento);

    Task<ClimateExport> ExportarAsync(
        string formato,
        string? data,
        string? aba,
        CancellationToken tokenCancelamento);
}
