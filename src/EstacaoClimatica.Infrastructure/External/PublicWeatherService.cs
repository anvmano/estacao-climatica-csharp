using System.Globalization;
using System.Text;
using System.Text.Json;
using EstacaoClimatica.Application.Abstractions;
using EstacaoClimatica.Application.Configuration;
using EstacaoClimatica.Application.Services;
using EstacaoClimatica.Domain;

namespace EstacaoClimatica.Infrastructure.External;

public sealed class ServicoClimaPublico : IServicoClimaPublico
{
    private readonly HttpClient _clienteHttp;
    private readonly OpcoesClimaPublico _opcoes;
    private readonly ServicoAnalisesAmbientais _servicoInsights;

    public ServicoClimaPublico(HttpClient clienteHttp, OpcoesClimaPublico opcoes)
        : this(clienteHttp, opcoes, new ServicoAnalisesAmbientais())
    {
    }

    internal ServicoClimaPublico(
        HttpClient clienteHttp,
        OpcoesClimaPublico opcoes,
        ServicoAnalisesAmbientais servicoInsights)
    {
        _clienteHttp = clienteHttp;
        _opcoes = opcoes;
        _servicoInsights = servicoInsights;
    }

    public async Task<PublicWeatherData> BuscarPorCepAsync(string cepInformado, CancellationToken tokenCancelamento)
    {
        var cep = new string((cepInformado ?? "").Where(char.IsDigit).ToArray());
        if (cep.Length != 8)
        {
            throw new ArgumentException("Informe um CEP com 8 dígitos.");
        }

        var endereco = await BuscarEnderecoPorCepAsync(cep, tokenCancelamento);
        var coordenadas = await BuscarCoordenadasPorEnderecoAsync(endereco, tokenCancelamento);
        var rotulo = string.Join(" - ", new[] { endereco.Cidade, endereco.Estado }.Where(valor => !string.IsNullOrWhiteSpace(valor)));

        return await BuscarPorCoordenadasAsync(
            coordenadas.Latitude,
            coordenadas.Longitude,
            "cep",
            string.IsNullOrWhiteSpace(rotulo) ? $"CEP {cep}" : rotulo,
            cep,
            null,
            tokenCancelamento);
    }

    public async Task<IReadOnlyList<CidadePublica>> PesquisarCidadesAsync(
        string termoInformado,
        CancellationToken tokenCancelamento)
    {
        var termo = NormalizarTermoCidade(termoInformado);
        if (termo.Length < 2)
        {
            throw new ArgumentException("Informe pelo menos 2 caracteres para pesquisar a cidade.");
        }

        var url = MontarUrl(_opcoes.OpenMeteoGeocodingUrl,
            ("name", termo),
            ("count", "10"),
            ("language", "pt"),
            ("format", "json"),
            ("countryCode", "BR"));

        using var resposta = await _clienteHttp.GetAsync(url, tokenCancelamento);
        if (!resposta.IsSuccessStatusCode)
        {
            throw new HttpRequestException("Não foi possível pesquisar a localização.", null, resposta.StatusCode);
        }

        using var documento = await LerJsonAsync(resposta, tokenCancelamento);
        if (!documento.RootElement.TryGetProperty("results", out var resultados) ||
            resultados.ValueKind != JsonValueKind.Array ||
            resultados.GetArrayLength() == 0)
        {
            throw new KeyNotFoundException("Cidade não encontrada. Confira o nome e tente novamente.");
        }

        var cidades = resultados.EnumerateArray()
            .Select(NormalizarCidadeOpenMeteo)
            .Where(cidade => CoordenadasValidas(cidade.Latitude, cidade.Longitude))
            .ToArray();

        if (cidades.Length == 0)
        {
            throw new HttpRequestException("A pesquisa retornou cidades sem coordenadas válidas.");
        }

        var nomePesquisado = NormalizarComparacaoCidade(termo.Split(',')[0]);
        return cidades
            .DistinctBy(ObterChaveCidade)
            .OrderByDescending(cidade => NormalizarComparacaoCidade(cidade.Nome) == nomePesquisado)
            .Take(5)
            .ToArray();
    }

    public Task<PublicWeatherData> BuscarPorCidadeAsync(
        CidadePublica cidade,
        CancellationToken tokenCancelamento)
    {
        if (cidade is null || !CoordenadasValidas(cidade.Latitude, cidade.Longitude))
        {
            throw new ArgumentException("Selecione uma cidade válida.");
        }

        var rotulo = string.IsNullOrWhiteSpace(cidade.Rotulo) ? MontarRotuloCidade(cidade) : cidade.Rotulo.Trim();
        return BuscarPorCoordenadasAsync(
            cidade.Latitude!.Value,
            cidade.Longitude!.Value,
            "cidade",
            rotulo,
            null,
            null,
            tokenCancelamento);
    }

    public async Task<PublicWeatherData> BuscarPorCoordenadasAsync(
        double latitude,
        double longitude,
        string tipoOrigem,
        string rotuloOrigem,
        string? cep,
        double? precisao,
        CancellationToken tokenCancelamento)
    {
        if (!CoordenadasValidas(latitude, longitude))
        {
            throw new ArgumentException("Coordenadas inválidas.");
        }

        var tarefaClima = BuscarClimaAsync(latitude, longitude, tokenCancelamento);
        var tarefaQualidadeAr = BuscarQualidadeArOpcionalAsync(latitude, longitude, tokenCancelamento);
        await Task.WhenAll((Task)tarefaClima, tarefaQualidadeAr);

        using var clima = await tarefaClima;
        using var qualidadeAr = await tarefaQualidadeAr;

        return NormalizarRespostaPublica(
            clima.RootElement,
            qualidadeAr?.RootElement,
            new PublicWeatherOrigin(
                string.IsNullOrWhiteSpace(tipoOrigem) ? "localizacao" : tipoOrigem,
                string.IsNullOrWhiteSpace(rotuloOrigem) ? "Localização atual" : rotuloOrigem,
                latitude,
                longitude,
                cep,
                precisao));
    }

    private async Task<EnderecoCep> BuscarEnderecoPorCepAsync(string cep, CancellationToken tokenCancelamento)
    {
        try
        {
            using var respostaBrasilApi = await _clienteHttp.GetAsync($"{_opcoes.BrasilApiCepUrl.TrimEnd('/')}/{cep}", tokenCancelamento);
            if (respostaBrasilApi.IsSuccessStatusCode)
            {
                using var documento = await LerJsonAsync(respostaBrasilApi, tokenCancelamento);
                return NormalizarEnderecoBrasilApi(documento.RootElement, cep);
            }
        }
        catch (Exception erro) when (erro is HttpRequestException or JsonException)
        {
            // O comportamento de referência tenta ViaCEP quando BrasilAPI não responde ou retorna JSON inválido.
        }

        using var respostaViaCep = await _clienteHttp.GetAsync($"{_opcoes.ViaCepUrl.TrimEnd('/')}/{cep}/json/", tokenCancelamento);
        if (!respostaViaCep.IsSuccessStatusCode)
        {
            throw new HttpRequestException("Não foi possível consultar o CEP.", null, respostaViaCep.StatusCode);
        }

        using var viaCep = await LerJsonAsync(respostaViaCep, tokenCancelamento);
        if (LerBooleano(viaCep.RootElement, "erro"))
        {
            throw new KeyNotFoundException("CEP não encontrado.");
        }

        return NormalizarViaCep(viaCep.RootElement, cep);
    }

    private async Task<Coordenadas> BuscarCoordenadasPorEnderecoAsync(EnderecoCep endereco, CancellationToken tokenCancelamento)
    {
        if (endereco.Latitude is double latitude && endereco.Longitude is double longitude)
        {
            return new Coordenadas(latitude, longitude);
        }

        var tentativas = new[]
        {
            endereco.Cidade,
            string.Join(" ", new[] { endereco.Cidade, endereco.Estado }.Where(valor => !string.IsNullOrWhiteSpace(valor))),
            string.Join(" ", new[] { endereco.Cidade, endereco.Estado, "Brasil" }.Where(valor => !string.IsNullOrWhiteSpace(valor)))
        }.Where(valor => !string.IsNullOrWhiteSpace(valor));

        foreach (var termo in tentativas)
        {
            var url = MontarUrl(_opcoes.OpenMeteoGeocodingUrl,
                ("name", termo!),
                ("count", "1"),
                ("language", "pt"),
                ("format", "json"),
                ("countryCode", "BR"));

            using var resposta = await _clienteHttp.GetAsync(url, tokenCancelamento);
            if (!resposta.IsSuccessStatusCode) continue;

            using var documento = await LerJsonAsync(resposta, tokenCancelamento);
            if (!documento.RootElement.TryGetProperty("results", out var resultados) ||
                resultados.ValueKind != JsonValueKind.Array ||
                resultados.GetArrayLength() == 0)
            {
                continue;
            }

            var resultado = resultados[0];
            var latitudeResultado = LerNumero(resultado, "latitude");
            var longitudeResultado = LerNumero(resultado, "longitude");
            if (latitudeResultado is not null && longitudeResultado is not null)
            {
                return new Coordenadas(latitudeResultado.Value, longitudeResultado.Value);
            }
        }

        throw new KeyNotFoundException("Não foi possível encontrar coordenadas para o CEP.");
    }

    private Task<JsonDocument> BuscarClimaAsync(double latitude, double longitude, CancellationToken tokenCancelamento)
    {
        var url = MontarUrl(_opcoes.OpenMeteoForecastUrl,
            ("latitude", FormatarNumero(latitude)),
            ("longitude", FormatarNumero(longitude)),
            ("timezone", "auto"),
            ("past_hours", "24"),
            ("forecast_hours", "13"),
            ("forecast_days", "15"),
            ("current", "temperature_2m,relative_humidity_2m,apparent_temperature,dew_point_2m,pressure_msl,precipitation,rain,weather_code,cloud_cover,wind_speed_10m,wind_gusts_10m,uv_index"),
            ("hourly", "temperature_2m,relative_humidity_2m,apparent_temperature,dew_point_2m,pressure_msl,precipitation_probability,precipitation,rain,showers,weather_code,cloud_cover,wind_speed_10m,wind_gusts_10m,uv_index"),
            ("daily", "temperature_2m_min,temperature_2m_max,precipitation_sum,precipitation_probability_max,uv_index_max,weather_code,sunrise,sunset,daylight_duration"));

        return ObterJsonObrigatorioAsync(url, "Não foi possível consultar o clima externo.", tokenCancelamento);
    }

    private async Task<JsonDocument?> BuscarQualidadeArOpcionalAsync(double latitude, double longitude, CancellationToken tokenCancelamento)
    {
        var url = MontarUrl(_opcoes.OpenMeteoAirQualityUrl,
            ("latitude", FormatarNumero(latitude)),
            ("longitude", FormatarNumero(longitude)),
            ("timezone", "auto"),
            ("past_days", "1"),
            ("forecast_days", "1"),
            ("current", "us_aqi"),
            ("hourly", "us_aqi"));

        try
        {
            return await ObterJsonObrigatorioAsync(url, "Não foi possível consultar AQI externo.", tokenCancelamento);
        }
        catch (Exception erro) when (erro is HttpRequestException or JsonException && !tokenCancelamento.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task<JsonDocument> ObterJsonObrigatorioAsync(string url, string mensagemErro, CancellationToken tokenCancelamento)
    {
        using var resposta = await _clienteHttp.GetAsync(url, tokenCancelamento);
        if (!resposta.IsSuccessStatusCode)
        {
            throw new HttpRequestException(mensagemErro, null, resposta.StatusCode);
        }

        return await LerJsonAsync(resposta, tokenCancelamento);
    }

    private static async Task<JsonDocument> LerJsonAsync(HttpResponseMessage resposta, CancellationToken tokenCancelamento)
    {
        await using var fluxo = await resposta.Content.ReadAsStreamAsync(tokenCancelamento);
        return await JsonDocument.ParseAsync(fluxo, cancellationToken: tokenCancelamento);
    }

    private PublicWeatherData NormalizarRespostaPublica(
        JsonElement clima,
        JsonElement? qualidadeAr,
        PublicWeatherOrigin origem)
    {
        var atual = ObterObjeto(clima, "current");
        var horario = ObterObjeto(clima, "hourly");
        var atualAqi = qualidadeAr is JsonElement elementoAqi ? ObterObjeto(elementoAqi, "current") : null;
        var horarioAqi = qualidadeAr is JsonElement elementoHorarioAqi ? ObterObjeto(elementoHorarioAqi, "hourly") : null;

        var atualizadoEm = LerTexto(atual, "time") ?? DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);
        var climaAtual = new PublicCurrentWeather(
                LerNumero(atual, "temperature_2m"),
                LerNumero(atual, "apparent_temperature"),
                LerNumero(atual, "relative_humidity_2m"),
                LerNumero(atual, "pressure_msl"),
                LerNumero(atual, "dew_point_2m"),
                LerNumero(atual, "precipitation"),
                LerNumero(atual, "rain"),
                LerNumero(atual, "weather_code"),
                LerNumero(atual, "cloud_cover"),
                LerNumero(atual, "wind_speed_10m"),
                LerNumero(atual, "wind_gusts_10m"),
                LerNumero(atual, "uv_index"));
        var seriesHorarias = new PublicHourlySeries(
                LerTextos(horario, "time"),
                LerNumeros(horario, "temperature_2m"),
                LerNumeros(horario, "apparent_temperature"),
                LerNumeros(horario, "relative_humidity_2m"),
                LerNumeros(horario, "pressure_msl"),
                LerNumeros(horario, "dew_point_2m"));
        var aqi = new PublicAirQuality(
                LerNumero(atualAqi, "us_aqi"),
                LerTextos(horarioAqi, "time"),
                LerNumeros(horarioAqi, "us_aqi"));
        var diario = ObterObjeto(clima, "daily");
        var previsaoCurtoPrazo = MontarPrevisaoCurtoPrazo(horario);
        var previsaoDiaria = ObterPrevisaoDiaria(diario, atualizadoEm);
        var previsaoQuinzeDias = MontarPrevisaoQuinzeDias(diario, atualizadoEm);
        var chuva = _servicoInsights.ResumirChuva(previsaoCurtoPrazo, atualizadoEm);
        var indiceUv = _servicoInsights.AnalisarIndiceUv(climaAtual.IndiceUv, previsaoDiaria.IndiceUvMaximo);
        var riscoMofo = _servicoInsights.AvaliarRiscoMofo(
            climaAtual.Temperatura,
            climaAtual.Umidade,
            climaAtual.PontoOrvalho);
        var ventilacao = _servicoInsights.RecomendarVentilacaoExterna(climaAtual, aqi.Valor, chuva);

        return new PublicWeatherData(
            origem,
            atualizadoEm,
            climaAtual,
            seriesHorarias,
            aqi,
            ObterEventosSolares(diario, atualizadoEm),
            previsaoCurtoPrazo,
            previsaoDiaria,
            new PublicEnvironmentalInsights(ventilacao, chuva, indiceUv, riscoMofo),
            previsaoQuinzeDias);
    }

    private static PublicSolarCycle? ObterEventosSolares(JsonElement? diario, string horarioAtual)
    {
        var indice = ObterIndiceDiario(diario, horarioAtual);
        var nascerTexto = LerTextoNoIndice(diario, "sunrise", indice);
        var porTexto = LerTextoNoIndice(diario, "sunset", indice);
        if (!DateTime.TryParse(nascerTexto, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var nascer) ||
            !DateTime.TryParse(porTexto, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var por))
        {
            return null;
        }

        var nascerDoSol = HoraDecimal(nascer);
        var porDoSol = HoraDecimal(por);
        var duracao = LerNumeroNoIndice(diario, "daylight_duration", indice) ?? ((porDoSol - nascerDoSol) * 3600);

        return new PublicSolarCycle(
            Math.Max(0, nascerDoSol - 1),
            nascerDoSol,
            nascerDoSol + ((porDoSol - nascerDoSol) / 2),
            porDoSol,
            Math.Min(24, porDoSol + 1),
            duracao);
    }

    private static IReadOnlyList<PublicShortTermForecast> MontarPrevisaoCurtoPrazo(JsonElement? horario)
    {
        var horarios = LerTextos(horario, "time");
        return horarios.Select((valor, indice) => new PublicShortTermForecast(
            valor,
            LerNumeroNoIndice(horario, "precipitation_probability", indice),
            LerNumeroNoIndice(horario, "precipitation", indice),
            LerNumeroNoIndice(horario, "rain", indice),
            LerNumeroNoIndice(horario, "showers", indice),
            LerNumeroNoIndice(horario, "weather_code", indice),
            LerNumeroNoIndice(horario, "cloud_cover", indice),
            LerNumeroNoIndice(horario, "wind_speed_10m", indice),
            LerNumeroNoIndice(horario, "wind_gusts_10m", indice),
            LerNumeroNoIndice(horario, "uv_index", indice))).ToArray();
    }

    private static PublicDailyForecast ObterPrevisaoDiaria(JsonElement? diario, string atualizadoEm)
    {
        var indice = ObterIndiceDiario(diario, atualizadoEm);
        return new PublicDailyForecast(
            LerNumeroNoIndice(diario, "uv_index_max", indice),
            LerNumeroNoIndice(diario, "precipitation_probability_max", indice),
            LerNumeroNoIndice(diario, "precipitation_sum", indice),
            LerNumeroNoIndice(diario, "weather_code", indice));
    }

    private static IReadOnlyList<PrevisaoDiaria> MontarPrevisaoQuinzeDias(JsonElement? diario, string atualizadoEm)
    {
        var datas = LerTextos(diario, "time");
        if (datas.Count == 0)
        {
            return [];
        }

        var indiceInicial = ObterIndiceDiario(diario, atualizadoEm);
        return datas
            .Select((data, indice) => new { data, indice })
            .Skip(indiceInicial)
            .Take(15)
            .Select((item, deslocamento) => DateOnly.TryParseExact(
                item.data,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var data)
                    ? new PrevisaoDiaria(
                        data,
                        LerNumeroNoIndice(diario, "temperature_2m_min", item.indice),
                        LerNumeroNoIndice(diario, "temperature_2m_max", item.indice),
                        LerNumeroNoIndice(diario, "precipitation_sum", item.indice),
                        LerNumeroNoIndice(diario, "precipitation_probability_max", item.indice),
                        LerNumeroNoIndice(diario, "uv_index_max", item.indice),
                        LerInteiroNoIndice(diario, "weather_code", item.indice),
                        deslocamento >= 7)
                    : null)
            .Where(previsao => previsao is not null)
            .OrderBy(previsao => previsao!.Data)
            .Cast<PrevisaoDiaria>()
            .ToArray();
    }

    private static int ObterIndiceDiario(JsonElement? diario, string atualizadoEm)
    {
        var dataAlvo = atualizadoEm.Length >= 10 ? atualizadoEm[..10] : atualizadoEm;
        var datas = LerTextos(diario, "time");
        var indice = datas.ToList().FindIndex(data => data.StartsWith(dataAlvo, StringComparison.Ordinal));
        return indice >= 0 ? indice : 0;
    }

    private static EnderecoCep NormalizarEnderecoBrasilApi(JsonElement dados, string cep)
    {
        var coordenadas = ObterObjeto(ObterObjeto(dados, "location"), "coordinates");
        return new EnderecoCep(
            cep,
            LerTexto(dados, "city"),
            LerTexto(dados, "state"),
            LerNumero(coordenadas, "latitude") ?? LerNumero(coordenadas, "lat"),
            LerNumero(coordenadas, "longitude") ?? LerNumero(coordenadas, "lng"));
    }

    private static CidadePublica NormalizarCidadeOpenMeteo(JsonElement dados)
    {
        var cidade = new CidadePublica(
            LerInteiroLongo(dados, "id"),
            (LerTexto(dados, "name") ?? "").Trim(),
            (LerTexto(dados, "admin1") ?? "").Trim(),
            (LerTexto(dados, "admin2") ?? "").Trim(),
            (LerTexto(dados, "country") ?? "Brasil").Trim(),
            (LerTexto(dados, "country_code") ?? "BR").Trim(),
            LerNumero(dados, "latitude"),
            LerNumero(dados, "longitude"),
            (LerTexto(dados, "timezone") ?? "").Trim(),
            "");

        return cidade with { Rotulo = MontarRotuloCidade(cidade) };
    }

    private static string MontarRotuloCidade(CidadePublica cidade)
    {
        var nome = cidade.Nome.Trim();
        var estado = cidade.Estado.Trim();
        var regiao = cidade.RegiaoAdministrativa.Trim();
        var regiaoDiferente = !string.IsNullOrWhiteSpace(regiao) &&
            NormalizarComparacaoCidade(regiao) != NormalizarComparacaoCidade(nome) &&
            NormalizarComparacaoCidade(regiao) != NormalizarComparacaoCidade(estado);
        var localidade = regiaoDiferente ? $"{nome} ({regiao})" : nome;
        var rotulo = string.Join(" - ", new[] { localidade, estado }.Where(valor => !string.IsNullOrWhiteSpace(valor)));
        return string.IsNullOrWhiteSpace(rotulo) ? "Cidade selecionada" : rotulo;
    }

    private static string NormalizarTermoCidade(string? valor) =>
        string.Join(' ', (valor ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string NormalizarComparacaoCidade(string? valor)
    {
        var semAcentos = new string((valor ?? "")
            .Normalize(NormalizationForm.FormD)
            .Where(caractere => CharUnicodeInfo.GetUnicodeCategory(caractere) != UnicodeCategory.NonSpacingMark)
            .ToArray());
        return NormalizarTermoCidade(semAcentos).ToLower(new CultureInfo("pt-BR"));
    }

    private static string ObterChaveCidade(CidadePublica cidade) => string.Join(':',
        cidade.Id?.ToString(CultureInfo.InvariantCulture) ?? "",
        FormatarNumero(cidade.Latitude!.Value),
        FormatarNumero(cidade.Longitude!.Value));

    private static bool CoordenadasValidas(double? latitude, double? longitude) =>
        latitude is double latitudeValor && longitude is double longitudeValor &&
        double.IsFinite(latitudeValor) && double.IsFinite(longitudeValor) &&
        latitudeValor is >= -90 and <= 90 && longitudeValor is >= -180 and <= 180;

    private static long? LerInteiroLongo(JsonElement elemento, string propriedade)
    {
        if (!elemento.TryGetProperty(propriedade, out var resultado)) return null;
        if (resultado.ValueKind == JsonValueKind.Number && resultado.TryGetInt64(out var numero)) return numero;
        if (resultado.ValueKind == JsonValueKind.String && long.TryParse(resultado.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out numero)) return numero;
        return null;
    }

    private static EnderecoCep NormalizarViaCep(JsonElement dados, string cep)
    {
        return new EnderecoCep(cep, LerTexto(dados, "localidade"), LerTexto(dados, "uf"), null, null);
    }

    private static JsonElement? ObterObjeto(JsonElement? elemento, string propriedade)
    {
        if (elemento is not JsonElement valor || valor.ValueKind != JsonValueKind.Object) return null;
        return valor.TryGetProperty(propriedade, out var resultado) && resultado.ValueKind == JsonValueKind.Object
            ? resultado
            : null;
    }

    private static string? LerTexto(JsonElement? elemento, string propriedade)
    {
        if (elemento is not JsonElement valor || valor.ValueKind != JsonValueKind.Object || !valor.TryGetProperty(propriedade, out var resultado))
        {
            return null;
        }

        return resultado.ValueKind == JsonValueKind.String ? resultado.GetString() : resultado.ToString();
    }

    private static double? LerNumero(JsonElement? elemento, string propriedade)
    {
        if (elemento is not JsonElement valor || valor.ValueKind != JsonValueKind.Object || !valor.TryGetProperty(propriedade, out var resultado))
        {
            return null;
        }

        if (resultado.ValueKind == JsonValueKind.Number && resultado.TryGetDouble(out var numero)) return numero;
        if (resultado.ValueKind == JsonValueKind.String && double.TryParse(resultado.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out numero)) return numero;
        return null;
    }

    private static bool LerBooleano(JsonElement elemento, string propriedade)
    {
        return elemento.ValueKind == JsonValueKind.Object &&
            elemento.TryGetProperty(propriedade, out var resultado) &&
            resultado.ValueKind == JsonValueKind.True;
    }

    private static IReadOnlyList<string> LerTextos(JsonElement? elemento, string propriedade)
    {
        if (elemento is not JsonElement valor || valor.ValueKind != JsonValueKind.Object ||
            !valor.TryGetProperty(propriedade, out var vetor) || vetor.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return vetor.EnumerateArray().Select(item => item.GetString() ?? "").ToArray();
    }

    private static IReadOnlyList<double?> LerNumeros(JsonElement? elemento, string propriedade)
    {
        if (elemento is not JsonElement valor || valor.ValueKind != JsonValueKind.Object ||
            !valor.TryGetProperty(propriedade, out var vetor) || vetor.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return vetor.EnumerateArray().Select(item =>
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var numero)) return (double?)numero;
            if (item.ValueKind == JsonValueKind.String && double.TryParse(item.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out numero)) return numero;
            return null;
        }).ToArray();
    }

    private static string? LerPrimeiroTexto(JsonElement? elemento, string propriedade)
    {
        if (elemento is not JsonElement valor || valor.ValueKind != JsonValueKind.Object ||
            !valor.TryGetProperty(propriedade, out var vetor) || vetor.ValueKind != JsonValueKind.Array || vetor.GetArrayLength() == 0)
        {
            return null;
        }

        return vetor[0].ValueKind == JsonValueKind.String ? vetor[0].GetString() : null;
    }

    private static double? LerPrimeiroNumero(JsonElement? elemento, string propriedade)
    {
        if (elemento is not JsonElement valor || valor.ValueKind != JsonValueKind.Object ||
            !valor.TryGetProperty(propriedade, out var vetor) || vetor.ValueKind != JsonValueKind.Array || vetor.GetArrayLength() == 0)
        {
            return null;
        }

        return vetor[0].ValueKind == JsonValueKind.Number && vetor[0].TryGetDouble(out var numero) ? numero : null;
    }

    private static string? LerTextoNoIndice(JsonElement? elemento, string propriedade, int indice)
    {
        if (elemento is not JsonElement valor || valor.ValueKind != JsonValueKind.Object ||
            !valor.TryGetProperty(propriedade, out var vetor) || vetor.ValueKind != JsonValueKind.Array ||
            indice < 0 || indice >= vetor.GetArrayLength())
        {
            return null;
        }

        return vetor[indice].ValueKind == JsonValueKind.String ? vetor[indice].GetString() : null;
    }

    private static double? LerNumeroNoIndice(JsonElement? elemento, string propriedade, int indice)
    {
        if (elemento is not JsonElement valor || valor.ValueKind != JsonValueKind.Object ||
            !valor.TryGetProperty(propriedade, out var vetor) || vetor.ValueKind != JsonValueKind.Array ||
            indice < 0 || indice >= vetor.GetArrayLength())
        {
            return null;
        }

        var item = vetor[indice];
        if (item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var numero)) return numero;
        if (item.ValueKind == JsonValueKind.String && double.TryParse(item.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out numero)) return numero;
        return null;
    }

    private static int? LerInteiroNoIndice(JsonElement? elemento, string propriedade, int indice)
    {
        var numero = LerNumeroNoIndice(elemento, propriedade, indice);
        return numero is not null && numero >= int.MinValue && numero <= int.MaxValue
            ? Convert.ToInt32(numero.Value)
            : null;
    }

    private static string MontarUrl(string urlBase, params (string Nome, string Valor)[] parametros)
    {
        var construtor = new StringBuilder(urlBase);
        construtor.Append(urlBase.Contains("?", StringComparison.Ordinal) ? '&' : '?');
        construtor.Append(string.Join("&", parametros.Select(parametro =>
            $"{Uri.EscapeDataString(parametro.Nome)}={Uri.EscapeDataString(parametro.Valor)}")));
        return construtor.ToString();
    }

    private static string FormatarNumero(double valor) => valor.ToString("0.########", CultureInfo.InvariantCulture);

    private static double HoraDecimal(DateTime data) => data.Hour + (data.Minute / 60d) + (data.Second / 3600d);

    private sealed record EnderecoCep(string Cep, string? Cidade, string? Estado, double? Latitude, double? Longitude);

    private sealed record Coordenadas(double Latitude, double Longitude);
}
