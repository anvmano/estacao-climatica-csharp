namespace EstacaoClimatica.Domain;

public sealed record EnvironmentDescriptor(
    string Key,
    string Label,
    IReadOnlyList<MetricDescriptor> Metrics);

public sealed record MetricDescriptor(string Key, string Label, string Unit);

public sealed record MeasurementPoint(
    string Date,
    string Time,
    string Timestamp,
    IReadOnlyDictionary<string, double?> Values);

public sealed record LatestMeasurementResponse(
    string Environment,
    string Label,
    MeasurementPoint Measurement);

public sealed record MeasurementsResponse(
    string Environment,
    string Label,
    string From,
    string To,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages,
    IReadOnlyList<MetricDescriptor> Metrics,
    IReadOnlyList<MeasurementPoint> Items);

public sealed record MetricSeriesResponse(
    string Metric,
    string Date,
    IReadOnlyList<EnvironmentMetricSeries> Series);

public sealed record EnvironmentMetricSeries(
    string Environment,
    string Label,
    string Unit,
    IReadOnlyList<MetricPoint> Points);

public sealed record MetricPoint(string Date, string Time, string Timestamp, double? Value);

public sealed record DashboardSummaryResponse(
    string Date,
    IReadOnlyList<EnvironmentSummary> Environments,
    SolarDay? Solar,
    AqiDashboard? Aqi);

public sealed record EnvironmentSummary(
    string Environment,
    string Label,
    IReadOnlyList<MetricSummary> Metrics);

public sealed record AirQualityResponse(
    string From,
    string To,
    AqiDashboard? Latest,
    IReadOnlyList<AirQualitySample> Samples);

public sealed record AirQualitySample(
    string Date,
    string Time,
    string Timestamp,
    int Value,
    string Category,
    string Dominant,
    IReadOnlyList<AqiSubIndex> SubIndexes);

public sealed record SolarDetailsResponse(
    string Date,
    SolarDay Day,
    int DaylightSeconds);

public sealed record AlertsResponse(string Date, int Count, IReadOnlyList<ClimateAlert> Items);

public sealed record ClimateAlert(
    string Severity,
    string Environment,
    string Metric,
    string Message,
    double? Value,
    double? Min,
    double? Max,
    string Unit);

public sealed record ComparisonRequest(
    IReadOnlyList<string> Dates,
    IReadOnlyList<string>? Environments = null,
    IReadOnlyList<string>? Metrics = null);

public sealed record ComparisonResponse(
    IReadOnlyList<string> Dates,
    IReadOnlyList<ComparisonSeries> Series);

public sealed record ComparisonSeries(
    string Environment,
    string Metric,
    string Label,
    string Unit,
    IReadOnlyList<ComparisonSample> Samples,
    double? AbsoluteChange,
    double? PercentChange);

public sealed record ComparisonSample(string Date, double? Average, double? Min, double? Max);

public sealed record ClimateExport(
    string FileName,
    string ContentType,
    byte[] Content);
