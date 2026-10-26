using System.Globalization;
using System.Text;
using System.Text.Json;
using EstacaoClimatica.Application.Abstractions;
using EstacaoClimatica.Application.Builders;
using EstacaoClimatica.Application.Configuration;
using EstacaoClimatica.Application.Models;
using EstacaoClimatica.Application.Utilities;
using EstacaoClimatica.Domain;

namespace EstacaoClimatica.Application.Services;

public sealed class ServicoConsultasClimaticas : IServicoConsultasClimaticas
{
    private static readonly IReadOnlyList<DefinicaoAmbiente> Ambientes = CriarAmbientes();
    private static readonly HashSet<string> Severidades = new(StringComparer.OrdinalIgnoreCase)
    {
        "info", "warning", "critical"
    };

    private readonly IRepositorioDadosClimaticos _repositorio;
    private readonly IServicoDashboardClimatico _dashboard;

    public ServicoConsultasClimaticas(
        IRepositorioDadosClimaticos repositorio,
        IServicoDashboardClimatico dashboard)
    {
        _repositorio = repositorio;
        _dashboard = dashboard;
    }

    public IReadOnlyList<EnvironmentDescriptor> ObterAmbientes() => Ambientes
        .Select(ambiente => new EnvironmentDescriptor(
            ambiente.Chave,
            ambiente.Rotulo,
            ambiente.Metricas.Select(ParaDescritor).ToArray()))
        .ToArray();

    public async Task<LatestMeasurementResponse> ObterUltimaMedicaoAsync(
        string ambiente,
        CancellationToken tokenCancelamento)
    {
        var definicao = ObterAmbiente(ambiente);
        var dados = await ObterDadosAmbienteAsync(definicao.Chave, tokenCancelamento);
        var registro = LeitorRegistrosFirebase.EnumerarRegistros(definicao.SelecionarRaiz(dados), maisRecentesPrimeiro: true)
            .Select(item => (Record: item, Timestamp: CriarTimestamp(item)))
            .FirstOrDefault(item => item.Timestamp is not null);

        if (registro.Timestamp is null)
        {
            throw new KeyNotFoundException($"Nenhuma medição encontrada para {definicao.Rotulo}.");
        }

        return new LatestMeasurementResponse(
            definicao.Chave,
            definicao.Rotulo,
            CriarPonto(registro.Record, registro.Timestamp.Value, definicao, definicao.Metricas));
    }

    public async Task<MeasurementsResponse> ObterMedicoesAsync(
        string ambiente,
        string? inicio,
        string? fim,
        string? metricas,
        int pagina,
        int tamanhoPagina,
        CancellationToken tokenCancelamento)
    {
        if (pagina < 1) throw new ArgumentException("A página deve ser maior ou igual a 1.");
        if (tamanhoPagina is < 1 or > 500) throw new ArgumentException("O tamanho da página deve estar entre 1 e 500.");

        var definicao = ObterAmbiente(ambiente);
        var periodo = ObterPeriodo(null, inicio, fim);
        var metricasSelecionadas = ResolverMetricas(definicao, SepararLista(metricas), permitirTodas: true);
        var dados = await ObterDadosAmbienteAsync(definicao.Chave, tokenCancelamento);

        var registros = LeitorRegistrosFirebase.EnumerarRegistros(definicao.SelecionarRaiz(dados), maisRecentesPrimeiro: false)
            .Select(item => (Record: item, Timestamp: CriarTimestamp(item)))
            .Where(item => item.Timestamp is not null && item.Timestamp.Value.Date >= periodo.From && item.Timestamp.Value.Date <= periodo.To)
            .ToArray();
        var totalPaginas = registros.Length == 0
            ? 0
            : (int)Math.Ceiling(registros.Length / (double)tamanhoPagina);
        var itens = registros
            .Skip((pagina - 1) * tamanhoPagina)
            .Take(tamanhoPagina)
            .Select(item => CriarPonto(item.Record, item.Timestamp!.Value, definicao, metricasSelecionadas))
            .ToArray();

        return new MeasurementsResponse(
            definicao.Chave,
            definicao.Rotulo,
            FormatarData(periodo.From),
            FormatarData(periodo.To),
            pagina,
            tamanhoPagina,
            registros.Length,
            totalPaginas,
            metricasSelecionadas.Select(ParaDescritor).ToArray(),
            itens);
    }

    public async Task<MetricSeriesResponse> ObterSerieAsync(
        string metrica,
        string? data,
        string? ambientes,
        CancellationToken tokenCancelamento)
    {
        if (string.IsNullOrWhiteSpace(metrica)) throw new ArgumentException("A métrica é obrigatória.");

        var dataSelecionada = ObterData(data);
        var ambientesSelecionados = ResolverAmbientes(SepararLista(ambientes), permitirTodos: true);
        var pares = ambientesSelecionados
            .Select(ambiente => (Environment: ambiente, Metric: ResolverMetrica(ambiente, metrica)))
            .Where(item => item.Metric is not null)
            .ToArray();
        if (pares.Length == 0) throw new ArgumentException($"Métrica não reconhecida: {metrica}.");

        var dados = await _repositorio.ObterDadosDashboardAsync(FormatarData(dataSelecionada), tokenCancelamento);
        var seriesClimaticas = pares.Select(par =>
        {
            var pontos = LeitorRegistrosFirebase.EnumerarRegistros(par.Environment.SelecionarRaiz(dados))
                .Select(item => (Record: item, Timestamp: CriarTimestamp(item)))
                .Where(item => item.Timestamp?.Date == dataSelecionada)
                .Select(item => new MetricPoint(
                    item.Record.Data,
                    LeitorRegistrosFirebase.FormatarRotuloHora(item.Record.Horario),
                    FormatarTimestamp(item.Timestamp!.Value),
                    LerValor(item.Record.Item, par.Environment, par.Metric!)))
                .ToArray();
            return new EnvironmentMetricSeries(
                par.Environment.Chave,
                par.Environment.Rotulo,
                par.Metric!.Unidade,
                pontos);
        }).ToArray();

        return new MetricSeriesResponse(
            NormalizarChave(metrica),
            FormatarData(dataSelecionada),
            seriesClimaticas);
    }

    public async Task<DashboardSummaryResponse> ObterResumoAsync(
        string? data,
        CancellationToken tokenCancelamento)
    {
        var dataSelecionada = FormatarData(ObterData(data));
        var resposta = await _dashboard.ObterDashboardAsync(dataSelecionada, tokenCancelamento);
        return new DashboardSummaryResponse(
            dataSelecionada,
            Ambientes.Select(ambiente => new EnvironmentSummary(
                ambiente.Chave,
                ambiente.Rotulo,
                ambiente.SelecionarDashboard(resposta).Stats)).ToArray(),
            resposta.Solar.Day,
            resposta.Aqi);
    }

    public async Task<AirQualityResponse> ObterQualidadeArAsync(
        string? data,
        string? inicio,
        string? fim,
        CancellationToken tokenCancelamento)
    {
        var periodo = ObterPeriodo(data, inicio, fim);
        var dados = periodo.From == periodo.To
            ? await _repositorio.ObterDadosDashboardAsync(FormatarData(periodo.From), tokenCancelamento)
            : await ObterDadosAmbienteAsync("sala", tokenCancelamento);
        var amostras = LeitorRegistrosFirebase.EnumerarRegistros(dados.Sala)
            .Select(item => (Record: item, Timestamp: CriarTimestamp(item)))
            .Where(item => item.Timestamp is not null && item.Timestamp.Value.Date >= periodo.From && item.Timestamp.Value.Date <= periodo.To)
            .Select(item => (item.Record, Timestamp: item.Timestamp!.Value, Aqi: MontadorDashboardAqi.MontarRegistro(item.Record.Item, item.Timestamp.Value)))
            .Where(item => item.Aqi is not null)
            .Select(item => new AirQualitySample(
                item.Record.Data,
                LeitorRegistrosFirebase.FormatarRotuloHora(item.Record.Horario),
                FormatarTimestamp(item.Timestamp),
                item.Aqi!.Value,
                item.Aqi.Category,
                item.Aqi.Dominant,
                item.Aqi.SubIndexes))
            .ToArray();

        var ultimo = amostras.LastOrDefault();
        var aqiAtual = ultimo is null
            ? null
            : new AqiDashboard(
                ultimo.Value,
                ultimo.Category,
                ObterClasseAqi(ultimo.Value),
                ObterImpactoAqi(ultimo.Value),
                ultimo.Dominant,
                DateTime.ParseExact(ultimo.Timestamp, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture).ToString("dd/MM HH:mm", CultureInfo.InvariantCulture),
                ultimo.SubIndexes);

        return new AirQualityResponse(
            FormatarData(periodo.From),
            FormatarData(periodo.To),
            aqiAtual,
            amostras);
    }

    public async Task<SolarDetailsResponse> ObterSolarAsync(
        string? data,
        CancellationToken tokenCancelamento)
    {
        var dataSelecionada = FormatarData(ObterData(data));
        var resposta = await _dashboard.ObterDashboardAsync(dataSelecionada, tokenCancelamento);
        var dia = resposta.Solar.Day
            ?? throw new KeyNotFoundException($"Dados solares não encontrados para {dataSelecionada}.");
        var duracao = (int)Math.Round((dia.Sunset - dia.Sunrise) * 3600);
        return new SolarDetailsResponse(dataSelecionada, dia, Math.Max(0, duracao));
    }

    public async Task<AlertsResponse> ObterAlertasAsync(
        string? data,
        string? severidade,
        string? ambiente,
        CancellationToken tokenCancelamento)
    {
        var dataSelecionada = FormatarData(ObterData(data));
        if (!string.IsNullOrWhiteSpace(severidade) && !Severidades.Contains(severidade))
        {
            throw new ArgumentException("Severidade inválida. Use info, warning ou critical.");
        }

        var filtroAmbiente = string.IsNullOrWhiteSpace(ambiente) ? null : ObterAmbiente(ambiente).Chave;
        var resumo = await ObterResumoAsync(dataSelecionada, tokenCancelamento);
        var alertas = new List<ClimateAlert>();
        foreach (var item in resumo.Environments)
        {
            if (item.Metrics.All(metrica => metrica.Average is null))
            {
                alertas.Add(new ClimateAlert("warning", item.Environment, "dados", $"Sem dados para {item.Label} em {dataSelecionada}.", null, null, null, ""));
                continue;
            }

            if (item.Environment is "sala" or "quarto")
            {
                AdicionarAlertaFaixa(alertas, item, "temperatura", 20, 26);
                AdicionarAlertaFaixa(alertas, item, "sensacaoTermica", 20, 26);
                AdicionarAlertaFaixa(alertas, item, "umidade", 40, 60);
            }
            else if (item.Environment == "aquario")
            {
                AdicionarAlertaFaixa(alertas, item, "temperaturaDS18B20", 25, 27);
            }
        }

        if (resumo.Aqi is { Value: >= 101 } aqi)
        {
            alertas.Add(new ClimateAlert(
                aqi.Value >= 151 ? "critical" : "warning",
                "sala",
                "aqi",
                $"AQI {aqi.Value}: {aqi.Category}. Poluente dominante: {aqi.Dominant}.",
                aqi.Value,
                0,
                100,
                "AQI"));
        }

        var filtrados = alertas
            .Where(item => severidade is null || item.Severity.Equals(severidade, StringComparison.OrdinalIgnoreCase))
            .Where(item => filtroAmbiente is null || item.Environment == filtroAmbiente)
            .ToArray();
        return new AlertsResponse(dataSelecionada, filtrados.Length, filtrados);
    }

    public async Task<ComparisonResponse> CompararAsync(
        ComparisonRequest requisicao,
        CancellationToken tokenCancelamento)
    {
        if (requisicao is null) throw new ArgumentException("O corpo da comparação é obrigatório.");
        var datas = requisicao.Dates?.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct().ToArray() ?? [];
        if (datas.Length is < 2 or > 31) throw new ArgumentException("Informe entre 2 e 31 datas distintas.");
        var datasConvertidas = datas.Select(ObterData).ToArray();
        var ambientes = ResolverAmbientes(requisicao.Environments, permitirTodos: true);
        var metricasSolicitadas = requisicao.Metrics?.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct().ToArray();
        if (metricasSolicitadas is { Length: 0 }) metricasSolicitadas = null;

        if (metricasSolicitadas is not null && metricasSolicitadas.Any(metrica => ambientes.All(item => ResolverMetrica(item, metrica) is null)))
        {
            var invalida = metricasSolicitadas.First(metrica => ambientes.All(item => ResolverMetrica(item, metrica) is null));
            throw new ArgumentException($"Métrica não reconhecida: {invalida}.");
        }

        var dados = await _repositorio.ObterDadosPorRamosAsync(
            ambientes.Any(item => item.Chave == "quarto"), false,
            ambientes.Any(item => item.Chave == "aquario"),
            ambientes.Any(item => item.Chave == "sala"), tokenCancelamento);
        var seriesClimaticas = new List<ComparisonSeries>();
        foreach (var ambienteAtual in ambientes)
        {
            var metricas = metricasSolicitadas is null
                ? ambienteAtual.Metricas
                : metricasSolicitadas.Select(item => ResolverMetrica(ambienteAtual, item)).OfType<DefinicaoConsultaMetrica>().DistinctBy(item => item.Chave).ToArray();

            var registros = LeitorRegistrosFirebase.EnumerarRegistros(ambienteAtual.SelecionarRaiz(dados)).ToArray();
            foreach (var metricaAtual in metricas)
            {
                var amostras = datasConvertidas.Select((dataAtual, indice) =>
                {
                    var valores = registros
                        .Where(registro => DataFirebase.InterpretarDataFirebase(registro.Data) == dataAtual)
                        .Select(registro => LerValor(registro.Item, ambienteAtual, metricaAtual))
                        .OfType<double>()
                        .ToArray();
                    return new ComparisonSample(
                        datas[indice],
                        valores.Length == 0 ? null : Math.Round(valores.Average(), 4),
                        valores.Length == 0 ? null : Math.Round(valores.Min(), 4),
                        valores.Length == 0 ? null : Math.Round(valores.Max(), 4));
                }).ToArray();
                var primeiro = amostras[0].Average;
                var ultimo = amostras[^1].Average;
                double? diferenca = primeiro is null || ultimo is null ? null : Math.Round(ultimo.Value - primeiro.Value, 4);
                double? percentual = diferenca is null || primeiro is null || primeiro == 0
                    ? null
                    : Math.Round(diferenca.Value / Math.Abs(primeiro.Value) * 100, 2);
                seriesClimaticas.Add(new ComparisonSeries(
                    ambienteAtual.Chave,
                    metricaAtual.Chave,
                    metricaAtual.Rotulo,
                    metricaAtual.Unidade,
                    amostras,
                    diferenca,
                    percentual));
            }
        }

        return new ComparisonResponse(datas, seriesClimaticas);
    }

    public async Task<ClimateExport> ExportarAsync(
        string formato,
        string? data,
        string? aba,
        CancellationToken tokenCancelamento)
    {
        if (!formato.Equals("json", StringComparison.OrdinalIgnoreCase) && !formato.Equals("csv", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Formato inválido. Use json ou csv.");
        }

        var dataSelecionada = FormatarData(ObterData(data));
        var dashboard = await _dashboard.ObterDashboardAsync(dataSelecionada, tokenCancelamento);
        var (chaveAba, conteudo) = ObterConteudoAba(dashboard, aba);
        if (formato.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            return new ClimateExport(
                $"estacao-climatica-{chaveAba}-{dataSelecionada}.json",
                "application/json; charset=utf-8",
                JsonSerializer.SerializeToUtf8Bytes(conteudo, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        }

        return new ClimateExport(
            $"estacao-climatica-{chaveAba}-{dataSelecionada}.csv",
            "text/csv; charset=utf-8",
            Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(CriarCsv(dashboard, chaveAba))).ToArray());
    }

    private Task<DadosDashboardFirebase> ObterDadosAmbienteAsync(string ambiente, CancellationToken tokenCancelamento) =>
        _repositorio.ObterDadosPorRamosAsync(
            ambiente == "quarto", false, ambiente == "aquario", ambiente == "sala", tokenCancelamento);

    private static IReadOnlyList<DefinicaoAmbiente> CriarAmbientes() =>
    [
        new("sala", "Sala", dados => dados.Sala, dashboard => dashboard.Sala,
        [
            new(CamposFirebase.Sala.Temperatura, "Temperatura", "°C", ["temp"]),
            new(CamposFirebase.Sala.SensacaoTermica, "Sensação térmica", "°C", ["sensacao", "sensação"]),
            new(CamposFirebase.Sala.Umidade, "Umidade", "%", []),
            new(CamposFirebase.Sala.Pressao, "Pressão", "hPa", ["pressão"]),
            new(CamposFirebase.Sala.Co, "CO", "ppm", ["monoxido de carbono", "monóxido de carbono"]),
            new(CamposFirebase.Sala.Co2, "CO2", "ppm", ["dioxido de carbono", "dióxido de carbono"]),
            new(CamposFirebase.Sala.Acetona, "Acetona", "ppm", ["aceton"]),
            new(CamposFirebase.Sala.Alcool, "Álcool", "ppm", ["alcohol", "alcool"]),
            new(CamposFirebase.Sala.Amonia, "Amônia", "ppm", ["nh4", "amonia"]),
            new(CamposFirebase.Sala.Tolueno, "Tolueno", "ppm", ["toluen"])
        ]),
        new("quarto", "Quarto", dados => dados.Quarto, dashboard => dashboard.Quarto,
        [
            new(CamposFirebase.Quarto.Temperatura, "Temperatura", "°C", ["temp"]),
            new(CamposFirebase.Quarto.SensacaoTermica, "Sensação térmica", "°C", ["sensacao", "sensação", "sensacaoTermica"]),
            new(CamposFirebase.Quarto.Umidade, "Umidade", "%", [])
        ]),
        new("aquario", "Aquário", dados => dados.Aquario, dashboard => dashboard.Aquario,
        [
            new(CamposFirebase.Aquario.Temperatura, "Temperatura", "°C", ["temperatura", "temp", "temperatura aquario"]),
            new(CamposFirebase.Aquario.Ph, "pH", "", ["ph"]),
            new(CamposFirebase.Aquario.Tds, "TDS", "ppm", ["tds"]),
            new(CamposFirebase.Aquario.Turbidez, "Turbidez", "NTU", [])
        ])
    ];

    private static DefinicaoAmbiente ObterAmbiente(string ambiente)
    {
        var normalizado = NormalizarChave(ambiente);
        return Ambientes.FirstOrDefault(item => NormalizarChave(item.Chave) == normalizado || NormalizarChave(item.Rotulo) == normalizado)
            ?? throw new KeyNotFoundException($"Ambiente não encontrado: {ambiente}.");
    }

    private static IReadOnlyList<DefinicaoAmbiente> ResolverAmbientes(IEnumerable<string>? nomes, bool permitirTodos)
    {
        var itens = nomes?.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray() ?? [];
        if (itens.Length == 0 && permitirTodos) return Ambientes;
        return itens.Select(ObterAmbiente).DistinctBy(item => item.Chave).ToArray();
    }

    private static IReadOnlyList<DefinicaoConsultaMetrica> ResolverMetricas(
        DefinicaoAmbiente ambiente,
        IEnumerable<string>? nomes,
        bool permitirTodas)
    {
        var itens = nomes?.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray() ?? [];
        if (itens.Length == 0 && permitirTodas) return ambiente.Metricas;
        return itens.Select(item => ResolverMetrica(ambiente, item)
            ?? throw new ArgumentException($"Métrica não reconhecida em {ambiente.Rotulo}: {item}."))
            .DistinctBy(item => item.Chave)
            .ToArray();
    }

    private static DefinicaoConsultaMetrica? ResolverMetrica(DefinicaoAmbiente ambiente, string nome)
    {
        var normalizado = NormalizarChave(nome);
        return ambiente.Metricas.FirstOrDefault(metrica =>
            NormalizarChave(metrica.Chave) == normalizado
            || NormalizarChave(metrica.Rotulo) == normalizado
            || metrica.NomesAlternativos.Any(alias => NormalizarChave(alias) == normalizado));
    }

    private static MeasurementPoint CriarPonto(
        RegistroFirebase registro,
        DateTime instante,
        DefinicaoAmbiente ambiente,
        IReadOnlyList<DefinicaoConsultaMetrica> metricas)
    {
        var valores = metricas.ToDictionary(
            metrica => metrica.Chave,
            metrica => LerValor(registro.Item, ambiente, metrica));
        return new MeasurementPoint(
            registro.Data,
            LeitorRegistrosFirebase.FormatarRotuloHora(registro.Horario),
            FormatarTimestamp(instante),
            valores);
    }

    private static double? LerValor(JsonElement item, DefinicaoAmbiente ambiente, DefinicaoConsultaMetrica metrica)
    {
        var valor = LeitorNumerosJson.ObterNumero(item, metrica.Chave);
        return NormalizadorMedicoesAquario.NormalizarValorMedicao(metrica.Chave, valor);
    }

    private static DateTime? CriarTimestamp(RegistroFirebase registro)
    {
        var data = DataFirebase.InterpretarDataFirebase(registro.Data);
        var hora = LeitorRegistrosFirebase.ConverterHora(registro.Horario);
        return data is null || hora is null
            ? null
            : new DateTime(data.Value.Year, data.Value.Month, data.Value.Day, hora.Value.Hour, hora.Value.Minute, hora.Value.Second ?? 0);
    }

    private static (DateTime From, DateTime To) ObterPeriodo(string? data, string? inicio, string? fim)
    {
        if (!string.IsNullOrWhiteSpace(data) && (!string.IsNullOrWhiteSpace(inicio) || !string.IsNullOrWhiteSpace(fim)))
        {
            throw new ArgumentException("Use date ou o intervalo from/to, não ambos.");
        }

        if (!string.IsNullOrWhiteSpace(data))
        {
            var dataUnica = ObterData(data);
            return (dataUnica, dataUnica);
        }

        var de = ObterData(inicio ?? fim);
        var ate = ObterData(fim ?? inicio);
        if (de > ate) throw new ArgumentException("A data inicial não pode ser posterior à data final.");
        if ((ate - de).TotalDays > 366) throw new ArgumentException("O intervalo máximo permitido é de 366 dias.");
        return (de, ate);
    }

    private static DateTime ObterData(string? data)
    {
        if (string.IsNullOrWhiteSpace(data)) return RelogioClimatico.Hoje;
        return DataFirebase.InterpretarDataFirebase(data)
            ?? throw new ArgumentException("Data inválida. Use o formato dd-MM-yyyy.");
    }

    private static string[] SepararLista(string? valor) => string.IsNullOrWhiteSpace(valor)
        ? []
        : valor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string NormalizarChave(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return string.Empty;
        var decomposicao = valor.Trim().Normalize(NormalizationForm.FormD);
        var semAcentos = new string(decomposicao.Where(caractere => CharUnicodeInfo.GetUnicodeCategory(caractere) != UnicodeCategory.NonSpacingMark).ToArray());
        return new string(semAcentos.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    private static string FormatarData(DateTime data) => data.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
    private static string FormatarTimestamp(DateTime instante) => instante.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
    private static MetricDescriptor ParaDescritor(DefinicaoConsultaMetrica metrica) => new(metrica.Chave, metrica.Rotulo, metrica.Unidade);

    private static void AdicionarAlertaFaixa(
        ICollection<ClimateAlert> alertas,
        EnvironmentSummary ambiente,
        string chave,
        double minimo,
        double maximo)
    {
        var definicao = Ambientes.First(item => item.Chave == ambiente.Environment);
        var metricaDefinida = ResolverMetrica(definicao, chave);
        if (metricaDefinida is null) return;
        var resumo = ambiente.Metrics.FirstOrDefault(item => NormalizarChave(item.Key) == NormalizarChave(metricaDefinida.Chave));
        if (resumo?.Average is null || resumo.Average >= minimo && resumo.Average <= maximo) return;
        alertas.Add(new ClimateAlert(
            "warning",
            ambiente.Environment,
            metricaDefinida.Chave,
            $"{metricaDefinida.Rotulo} média fora da faixa de conforto ({minimo:0.##} a {maximo:0.##} {metricaDefinida.Unidade}).",
            Math.Round(resumo.Average.Value, 2),
            minimo,
            maximo,
            metricaDefinida.Unidade));
    }

    private static string ObterClasseAqi(int valor) => valor switch
    {
        <= 50 => "good",
        <= 100 => "moderate",
        <= 150 => "sensitive",
        <= 200 => "unhealthy",
        <= 300 => "very-unhealthy",
        _ => "hazardous"
    };

    private static string ObterImpactoAqi(int valor) => valor switch
    {
        <= 50 => "Ar satisfatório, risco mínimo.",
        <= 100 => "Aceitável, mas pode haver risco moderado para grupos sensíveis.",
        <= 150 => "Grupos de risco podem apresentar sintomas.",
        <= 200 => "Toda a população pode começar a sentir efeitos na saúde.",
        <= 300 => "Alerta de saúde; riscos aumentados para todos.",
        _ => "Condições de emergência; toda a população é severamente afetada."
    };

    private static (string Key, object Content) ObterConteudoAba(DashboardResponse dashboard, string? aba)
    {
        var normalizada = NormalizarChave(aba ?? "Tab0");
        return normalizada switch
        {
            "tab1" or "sala" => ("sala", dashboard.Sala),
            "tab2" or "quarto" => ("quarto", dashboard.Quarto),
            "tab3" or "aquario" => ("aquario", dashboard.Aquario),
            "tab0" or "estacao" => ("estacao", dashboard),
            _ => throw new ArgumentException("Aba inválida. Use Tab0, Tab1, Tab2, Tab3, estacao, sala, quarto ou aquario.")
        };
    }

    private static string CriarCsv(DashboardResponse dashboard, string aba)
    {
        if (aba != "estacao")
        {
            var ambiente = aba switch
            {
                "sala" => dashboard.Sala,
                "quarto" => dashboard.Quarto,
                _ => dashboard.Aquario
            };
            return CriarCsvTabela(ambiente.Table.Headers, ambiente.Table.Rows);
        }

        var linhas = new List<IReadOnlyList<string>>();
        foreach (var definicao in Ambientes)
        {
            foreach (var metrica in definicao.SelecionarDashboard(dashboard).Stats)
            {
                linhas.Add([
                    definicao.Rotulo,
                    metrica.Label,
                    metrica.Average?.ToString("0.####", CultureInfo.InvariantCulture) ?? "",
                    metrica.Min?.ToString("0.####", CultureInfo.InvariantCulture) ?? "",
                    metrica.Max?.ToString("0.####", CultureInfo.InvariantCulture) ?? "",
                    metrica.Delta?.ToString("0.####", CultureInfo.InvariantCulture) ?? "",
                    metrica.Suffix
                ]);
            }
        }
        return CriarCsvTabela(["Ambiente", "Métrica", "Média", "Mínimo", "Máximo", "Variação", "Unidade"], linhas);
    }

    private static string CriarCsvTabela(IReadOnlyList<string> cabecalhos, IReadOnlyList<IReadOnlyList<string>> linhas)
    {
        static string Escapar(string valor)
        {
            var texto = valor.TrimStart();
            if (texto.Length > 0 && "=+-@".Contains(texto[0])
                && !double.TryParse(valor, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                valor = "'" + valor;
            return valor.Contains(';') || valor.Contains('"') || valor.Contains('\n') || valor.Contains('\r')
                ? $"\"{valor.Replace("\"", "\"\"")}\""
                : valor;
        }

        var construtor = new StringBuilder();
        construtor.AppendLine(string.Join(';', cabecalhos.Select(Escapar)));
        foreach (var linha in linhas) construtor.AppendLine(string.Join(';', linha.Select(Escapar)));
        return construtor.ToString();
    }

    private sealed record DefinicaoAmbiente(
        string Chave,
        string Rotulo,
        Func<DadosDashboardFirebase, JsonElement?> SelecionarRaiz,
        Func<DashboardResponse, EnvironmentDashboard> SelecionarDashboard,
        IReadOnlyList<DefinicaoConsultaMetrica> Metricas);

    private sealed record DefinicaoConsultaMetrica(
        string Chave,
        string Rotulo,
        string Unidade,
        IReadOnlyList<string> NomesAlternativos);
}
