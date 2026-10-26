namespace EstacaoClimatica.Domain;

public sealed record DashboardResponse(
    string Date,
    EnvironmentDashboard Quarto,
    EnvironmentDashboard Sala,
    EnvironmentDashboard Aquario,
    SolarDashboard Solar,
    AqiDashboard? Aqi,
    StationLatestDashboard? Latest = null,
    InternalEnvironmentalInsights? Insights = null)
{
    public IReadOnlyList<string> FailedBranches { get; init; } = [];
}

public sealed record StationLatestDashboard(
    LatestMetricMeasurement? SalaTemperatura,
    LatestMetricMeasurement? SalaUmidade,
    LatestMetricMeasurement? QuartoTemperatura,
    LatestMetricMeasurement? QuartoUmidade,
    LatestMetricMeasurement? AquarioTemperatura);

public sealed record LatestMetricMeasurement(double Value, string Date, string Time, MetricQuality? Quality = null);

public sealed record EnvironmentDashboard(
    IReadOnlyList<MetricSummary> Stats,
    IReadOnlyList<ChartSeries> Charts,
    TableData Table,
    AdvancedClimateDashboard? Advanced = null);

public sealed record MetricSummary(
    string Key,
    string Label,
    string Suffix,
    double? Average,
    double? Min,
    double? Max,
    double? Delta,
    TrendInfo Trend,
    MetricQuality? Quality = null);

public sealed record MetricQuality(
    string Level,
    string Label,
    string State,
    string StateLabel,
    int ValidReadings,
    int ExpectedReadings,
    double CoveragePercent,
    LatestQualityReading? LatestReading,
    IReadOnlyList<string> Warnings);

public sealed record LatestQualityReading(string Date, string Time);

public sealed record TrendInfo(string Label, string ClassName, string Symbol);

public sealed record ChartSeries(
    string Id,
    string Label,
    string Color,
    string Suffix,
    IReadOnlyList<ChartPoint> Points,
    MetricQuality? Quality = null);

public sealed record ChartPoint(string Label, double? Value, string? Instante = null);

public sealed record TableData(
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<IReadOnlyList<string?>>? CellQuality = null);

public sealed record AdvancedClimateDashboard(
    IReadOnlyList<HeatmapCell> MonthlyCalendar,
    IReadOnlyList<HeatmapCell> HourlyHeatmap,
    IReadOnlyList<WeeklyHeatmapCell> WeeklyHeatmap);

public sealed record HeatmapCell(int Index, string Label, double? Value);

public sealed record WeeklyHeatmapCell(int Weekday, int Hour, double? Value);

public sealed record SolarDashboard(
    ChartSeries SunHistoryDawn,
    ChartSeries SunHistorySunrise,
    ChartSeries SunHistorySunset,
    ChartSeries SunHistoryDusk,
    SolarDay? Day,
    SolarDay? CurrentDay = null);

public sealed record SolarDay(
    string Date,
    double Dawn,
    double Sunrise,
    double Zenith,
    double Sunset,
    double Dusk);

public sealed record AqiDashboard(
    int Value,
    string Category,
    string CategoryClass,
    string Impact,
    string Dominant,
    string Timestamp,
    IReadOnlyList<AqiSubIndex> SubIndexes);

public sealed record AqiSubIndex(
    string Label,
    string Unit,
    double Concentration,
    int Value);

public sealed record ChatRequest(
    string Question,
    string? ActiveTab,
    string? SelectedDate,
    ChatIntentSummary? PreviousIntent = null);

public sealed record ChatResponse(
    string Answer,
    string Source,
    ChatIntentSummary Intent);

public sealed record ChatIntentSummary(
    IReadOnlyList<string> Environments,
    IReadOnlyList<string> Metrics,
    string Operation,
    string Period,
    int Samples,
    string? Hour = null,
    ChatHourRangeSummary? HourRange = null,
    IReadOnlyList<string>? Dates = null);

public sealed record ChatHourRangeSummary(
    string Start,
    string End);
