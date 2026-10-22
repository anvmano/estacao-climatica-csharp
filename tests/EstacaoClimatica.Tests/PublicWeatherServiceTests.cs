using System.Net;
using System.Text;
using EstacaoClimatica.Application.Configuration;
using EstacaoClimatica.Domain;
using EstacaoClimatica.Infrastructure.External;

namespace EstacaoClimatica.Tests;

public sealed class TestesServicoClimaPublico
{
    [Fact]
    public async Task PesquisarCidades_DeveRejeitarTermoLongoSemConsultarApi()
    {
        var requisicoes = 0;
        using var cliente = new HttpClient(new ManipuladorRoteadorHttp(_ =>
        {
            requisicoes++;
            return CriarJson(HttpStatusCode.OK, "{}");
        }));
        var servico = new ServicoClimaPublico(cliente, CriarOpcoes());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            servico.PesquisarCidadesAsync(new string('a', 101), CancellationToken.None));
        Assert.Equal(0, requisicoes);
    }

    [Fact]
    public async Task BuscarPorCep_DeveRejeitarCepInvalidoSemConsultarApis()
    {
        var requisicoes = 0;
        using var cliente = new HttpClient(new ManipuladorRoteadorHttp(_ =>
        {
            requisicoes++;
            return CriarJson(HttpStatusCode.OK, "{}");
        }));
        var servico = new ServicoClimaPublico(cliente, CriarOpcoes());

        var erro = await Assert.ThrowsAsync<ArgumentException>(() =>
            servico.BuscarPorCepAsync("123", CancellationToken.None));

        Assert.Equal("Informe um CEP com 8 dígitos.", erro.Message);
        Assert.Equal(0, requisicoes);
    }

    [Fact]
    public async Task BuscarPorCep_DeveUsarCoordenadasDaBrasilApiENormalizarResposta()
    {
        var urls = new List<string>();
        using var cliente = new HttpClient(new ManipuladorRoteadorHttp(requisicao =>
        {
            var url = requisicao.RequestUri!.ToString();
            urls.Add(url);

            if (url.StartsWith("https://local.test/brasil/01001000", StringComparison.Ordinal))
            {
                return CriarJson(HttpStatusCode.OK, """
                {
                  "cep": "01001-000",
                  "city": "São Paulo",
                  "state": "SP",
                  "location": { "coordinates": { "latitude": "-23.5505", "longitude": "-46.6333" } }
                }
                """);
            }

            if (url.StartsWith("https://local.test/forecast", StringComparison.Ordinal))
            {
                return CriarJson(HttpStatusCode.OK, ClimaJson);
            }

            if (url.StartsWith("https://local.test/air", StringComparison.Ordinal))
            {
                return CriarJson(HttpStatusCode.OK, JsonAqi);
            }

            return CriarJson(HttpStatusCode.NotFound, "{}");
        }));
        var servico = new ServicoClimaPublico(cliente, CriarOpcoes());

        var resultado = await servico.BuscarPorCepAsync("01001-000", CancellationToken.None);

        Assert.Equal("cep", resultado.Origem.Tipo);
        Assert.Equal("01001000", resultado.Origem.Cep);
        Assert.Equal("São Paulo - SP", resultado.Origem.Rotulo);
        Assert.Equal(-23.5505, resultado.Origem.Latitude, 4);
        Assert.Equal(-46.6333, resultado.Origem.Longitude, 4);
        Assert.Equal("2026-06-03T12:00", resultado.AtualizadoEm);
        Assert.Equal(21.4, resultado.ClimaAtual.Temperatura);
        Assert.Equal(20.8, resultado.ClimaAtual.SensacaoTermica);
        Assert.Equal(63, resultado.ClimaAtual.Umidade);
        Assert.Equal(1017.2, resultado.ClimaAtual.Pressao);
        Assert.Equal(14.1, resultado.ClimaAtual.PontoOrvalho);
        Assert.Equal(4.5, resultado.ClimaAtual.IndiceUv);
        Assert.Equal(42, resultado.Aqi.Valor);
        Assert.Equal(3, resultado.SeriesHorarias.Horarios.Count);
        Assert.Null(resultado.SeriesHorarias.Temperatura[2]);
        Assert.NotNull(resultado.CicloSolar);
        Assert.Equal(6.5, resultado.CicloSolar!.Sunrise, 3);
        Assert.Equal(17.75, resultado.CicloSolar.Sunset, 3);
        Assert.NotNull(resultado.Insights);
        Assert.Equal("Chuva moderada", resultado.Insights!.Chuva.Rotulo);
        Assert.Equal(70, resultado.Insights.Chuva.Probabilidade);
        Assert.Equal("Moderado", resultado.Insights.IndiceUv.Rotulo);
        Assert.False(resultado.Insights.IndiceUv.UsaMaximoComoFallback);
        Assert.Equal("Monitorar", resultado.Insights.RiscoMofo.Rotulo);
        Assert.Contains(urls, url => url.Contains("past_hours=24", StringComparison.Ordinal));
        Assert.Contains(urls, url => url.Contains("forecast_hours=13", StringComparison.Ordinal));
        Assert.Contains(urls, url => url.Contains("forecast_days=15", StringComparison.Ordinal));
        Assert.Contains(urls, url => url.Contains("temperature_2m_min", StringComparison.Ordinal));
        Assert.Contains(urls, url => url.Contains("temperature_2m_max", StringComparison.Ordinal));
        Assert.Equal(1, urls.Count(url => url.StartsWith("https://local.test/forecast", StringComparison.Ordinal)));
        Assert.DoesNotContain(urls, url => url.StartsWith("https://local.test/geo", StringComparison.Ordinal));
        Assert.DoesNotContain(urls, url => url.StartsWith("https://local.test/via", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BuscarPorCoordenadas_DeveNormalizarQuinzeDiasSemTransformarAusenciasEmZero()
    {
        using var cliente = new HttpClient(new ManipuladorRoteadorHttp(requisicao =>
            requisicao.RequestUri!.ToString().StartsWith("https://local.test/forecast", StringComparison.Ordinal)
                ? CriarJson(HttpStatusCode.OK, ClimaQuinzeDiasJson)
                : CriarJson(HttpStatusCode.OK, JsonAqi)));
        var servico = new ServicoClimaPublico(cliente, CriarOpcoes());

        var resultado = await servico.BuscarPorCoordenadasAsync(
            -23.55,
            -46.63,
            "localizacao",
            "Localização atual",
            null,
            20,
            CancellationToken.None);

        var previsao = Assert.IsAssignableFrom<IReadOnlyList<PrevisaoDiaria>>(resultado.PrevisaoQuinzeDias);
        Assert.Equal(15, previsao.Count);
        Assert.Equal(new DateOnly(2026, 6, 3), previsao[0].Data);
        Assert.Equal(13.2, previsao[0].TemperaturaMinima);
        Assert.Equal(22.4, previsao[0].TemperaturaMaxima);
        Assert.Equal(2.5, previsao[0].Precipitacao);
        Assert.Equal(70, previsao[0].ProbabilidadeChuva);
        Assert.Equal(5.8, previsao[0].IndiceUvMaximo);
        Assert.Equal(61, previsao[0].CodigoClimatico);
        Assert.False(previsao[6].MaiorIncerteza);
        Assert.True(previsao[7].MaiorIncerteza);
        Assert.Null(previsao[2].TemperaturaMaxima);
        Assert.Null(previsao[1].Precipitacao);
        Assert.Null(previsao[1].CodigoClimatico);
        Assert.Equal(previsao.OrderBy(item => item.Data), previsao);
    }

    [Fact]
    public async Task BuscarPorCep_DeveUsarViaCepEGeocodificacaoQuandoBrasilApiFalhar()
    {
        var urls = new List<string>();
        using var cliente = new HttpClient(new ManipuladorRoteadorHttp(requisicao =>
        {
            var url = requisicao.RequestUri!.ToString();
            urls.Add(url);

            if (url.StartsWith("https://local.test/brasil/80010000", StringComparison.Ordinal))
            {
                return CriarJson(HttpStatusCode.ServiceUnavailable, "{}");
            }

            if (url.StartsWith("https://local.test/via/80010000/json/", StringComparison.Ordinal))
            {
                return CriarJson(HttpStatusCode.OK, """{"cep":"80010-000","localidade":"Curitiba","uf":"PR"}""");
            }

            if (url.StartsWith("https://local.test/geo", StringComparison.Ordinal))
            {
                return CriarJson(HttpStatusCode.OK, """{"results":[{"latitude":-25.4284,"longitude":-49.2733}]}""");
            }

            if (url.StartsWith("https://local.test/forecast", StringComparison.Ordinal))
            {
                return CriarJson(HttpStatusCode.OK, ClimaJson);
            }

            if (url.StartsWith("https://local.test/air", StringComparison.Ordinal))
            {
                return CriarJson(HttpStatusCode.ServiceUnavailable, "{}");
            }

            return CriarJson(HttpStatusCode.NotFound, "{}");
        }));
        var servico = new ServicoClimaPublico(cliente, CriarOpcoes());

        var resultado = await servico.BuscarPorCepAsync("80010-000", CancellationToken.None);

        Assert.Equal("Curitiba - PR", resultado.Origem.Rotulo);
        Assert.Equal(-25.4284, resultado.Origem.Latitude, 4);
        Assert.Equal(-49.2733, resultado.Origem.Longitude, 4);
        Assert.Null(resultado.Aqi.Valor);
        Assert.Empty(resultado.Aqi.Horarios);
        Assert.Contains(urls, url => url.StartsWith("https://local.test/via/80010000/json/", StringComparison.Ordinal));
        Assert.Contains(urls, url => url.Contains("name=Curitiba", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BuscarPorCep_DeveInformarCepNaoEncontrado()
    {
        using var cliente = new HttpClient(new ManipuladorRoteadorHttp(requisicao =>
        {
            var url = requisicao.RequestUri!.ToString();
            return url.StartsWith("https://local.test/brasil", StringComparison.Ordinal)
                ? CriarJson(HttpStatusCode.NotFound, "{}")
                : CriarJson(HttpStatusCode.OK, """{"erro":true}""");
        }));
        var servico = new ServicoClimaPublico(cliente, CriarOpcoes());

        var erro = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            servico.BuscarPorCepAsync("00000-000", CancellationToken.None));

        Assert.Equal("CEP não encontrado.", erro.Message);
    }

    [Fact]
    public async Task BuscarPorCoordenadas_DeveManterErroDoClimaComoBloqueante()
    {
        using var cliente = new HttpClient(new ManipuladorRoteadorHttp(requisicao =>
        {
            var url = requisicao.RequestUri!.ToString();
            return url.StartsWith("https://local.test/forecast", StringComparison.Ordinal)
                ? CriarJson(HttpStatusCode.BadGateway, "{}")
                : CriarJson(HttpStatusCode.OK, JsonAqi);
        }));
        var servico = new ServicoClimaPublico(cliente, CriarOpcoes());

        var erro = await Assert.ThrowsAsync<HttpRequestException>(() =>
            servico.BuscarPorCoordenadasAsync(-23.5, -46.6, "localizacao", "Localização atual", null, 20, CancellationToken.None));

        Assert.Equal("Não foi possível consultar o clima externo.", erro.Message);
    }

    [Fact]
    public async Task BuscarPorCoordenadas_DeveRejeitarValoresNaoFinitosSemConsultarApis()
    {
        var requisicoes = 0;
        using var cliente = new HttpClient(new ManipuladorRoteadorHttp(_ =>
        {
            requisicoes++;
            return CriarJson(HttpStatusCode.OK, ClimaJson);
        }));
        var servico = new ServicoClimaPublico(cliente, CriarOpcoes());

        var erro = await Assert.ThrowsAsync<ArgumentException>(() =>
            servico.BuscarPorCoordenadasAsync(double.NaN, -46.6, "localizacao", "Localização atual", null, null, CancellationToken.None));

        Assert.Equal("Coordenadas inválidas.", erro.Message);
        Assert.Equal(0, requisicoes);
    }

    [Fact]
    public async Task BuscarPorCep_DeveInformarQuandoGeocodificacaoNaoEncontrarCoordenadas()
    {
        using var cliente = new HttpClient(new ManipuladorRoteadorHttp(requisicao =>
        {
            var url = requisicao.RequestUri!.ToString();
            if (url.StartsWith("https://local.test/brasil", StringComparison.Ordinal))
            {
                return CriarJson(HttpStatusCode.OK, """{"city":"Cidade Teste","state":"SP","location":{"coordinates":{}}}""");
            }
            if (url.StartsWith("https://local.test/geo", StringComparison.Ordinal))
            {
                return CriarJson(HttpStatusCode.OK, """{"results":[]}""");
            }
            return CriarJson(HttpStatusCode.NotFound, "{}");
        }));
        var servico = new ServicoClimaPublico(cliente, CriarOpcoes());

        var erro = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            servico.BuscarPorCepAsync("01001000", CancellationToken.None));

        Assert.Equal("Não foi possível encontrar coordenadas para o CEP.", erro.Message);
    }

    [Fact]
    public async Task PesquisarCidades_DeveRejeitarTermoCurtoSemConsultarApi()
    {
        var requisicoes = 0;
        using var cliente = new HttpClient(new ManipuladorRoteadorHttp(_ =>
        {
            requisicoes++;
            return CriarJson(HttpStatusCode.OK, "{}");
        }));
        var servico = new ServicoClimaPublico(cliente, CriarOpcoes());

        var erro = await Assert.ThrowsAsync<ArgumentException>(() =>
            servico.PesquisarCidadesAsync(" A ", CancellationToken.None));

        Assert.Equal("Informe pelo menos 2 caracteres para pesquisar a cidade.", erro.Message);
        Assert.Equal(0, requisicoes);
    }

    [Fact]
    public async Task PesquisarCidades_DeveNormalizarPriorizarDeduplicarRotularELimitar()
    {
        string? urlRecebida = null;
        using var cliente = new HttpClient(new ManipuladorRoteadorHttp(requisicao =>
        {
            urlRecebida = requisicao.RequestUri!.ToString();
            return CriarJson(HttpStatusCode.OK, """
            {
              "results": [
                { "id": 10, "name": "Gramado Novo", "admin1": "Rio Grande do Sul", "admin2": "Região Nova", "country": "Brasil", "country_code": "BR", "latitude": -29.10, "longitude": -50.10, "timezone": "America/Sao_Paulo" },
                { "id": 1, "name": "Gramado", "admin1": "Rio Grande do Sul", "admin2": "Gramado", "country": "Brasil", "country_code": "BR", "latitude": -29.3788, "longitude": -50.8726, "timezone": "America/Sao_Paulo" },
                { "id": 2, "name": "GRAMADO", "admin1": "Rio Grande do Sul", "admin2": "Sobradinho", "country": "Brasil", "country_code": "BR", "latitude": -29.40, "longitude": -53.00, "timezone": "America/Sao_Paulo" },
                { "id": 3, "name": "Grâmado", "admin1": "Rio Grande do Sul", "admin2": "Marau", "country": "Brasil", "country_code": "BR", "latitude": -28.45, "longitude": -52.20, "timezone": "America/Sao_Paulo" },
                { "id": 2, "name": "GRAMADO", "admin1": "Rio Grande do Sul", "admin2": "Sobradinho", "country": "Brasil", "country_code": "BR", "latitude": -29.40, "longitude": -53.00, "timezone": "America/Sao_Paulo" },
                { "id": 4, "name": "Gramadinho", "admin1": "Rio Grande do Sul", "admin2": "Interior", "country": "Brasil", "country_code": "BR", "latitude": -28.20, "longitude": -51.20, "timezone": "America/Sao_Paulo" },
                { "id": 5, "name": "Gramado Sul", "admin1": "Rio Grande do Sul", "admin2": "Sul", "country": "Brasil", "country_code": "BR", "latitude": -30.20, "longitude": -51.20, "timezone": "America/Sao_Paulo" },
                { "id": 6, "name": "Gramado Norte", "admin1": "Rio Grande do Sul", "admin2": "Norte", "country": "Brasil", "country_code": "BR", "latitude": -27.20, "longitude": -51.20, "timezone": "America/Sao_Paulo" },
                { "id": 7, "name": "Gramado Inválido", "admin1": "Rio Grande do Sul", "admin2": "Teste", "country": "Brasil", "country_code": "BR", "latitude": 999, "longitude": -51.20, "timezone": "America/Sao_Paulo" }
              ]
            }
            """);
        }));
        var servico = new ServicoClimaPublico(cliente, CriarOpcoes());

        var cidades = await servico.PesquisarCidadesAsync("  grÁmado,   RS  ", CancellationToken.None);

        Assert.Equal(5, cidades.Count);
        Assert.Equal("Gramado - Rio Grande do Sul", cidades[0].Rotulo);
        Assert.Equal("GRAMADO (Sobradinho) - Rio Grande do Sul", cidades[1].Rotulo);
        Assert.Equal("Grâmado (Marau) - Rio Grande do Sul", cidades[2].Rotulo);
        Assert.Equal([1L, 2L, 3L, 10L, 4L], cidades.Select(cidade => cidade.Id).ToArray());
        Assert.Equal("BR", cidades[0].CodigoPais);
        Assert.Equal("America/Sao_Paulo", cidades[0].FusoHorario);
        var urlDecodificada = Uri.UnescapeDataString(urlRecebida!);
        Assert.Contains("name=grÁmado, RS", urlDecodificada, StringComparison.Ordinal);
        Assert.Contains("count=10", urlDecodificada, StringComparison.Ordinal);
        Assert.Contains("language=pt", urlDecodificada, StringComparison.Ordinal);
        Assert.Contains("countryCode=BR", urlDecodificada, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PesquisarCidades_DeveDistinguirAusenciaDeResultadosECoordenadasInvalidas()
    {
        var resposta = """{"results":[]}""";
        using var cliente = new HttpClient(new ManipuladorRoteadorHttp(_ => CriarJson(HttpStatusCode.OK, resposta)));
        var servico = new ServicoClimaPublico(cliente, CriarOpcoes());

        var naoEncontrada = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            servico.PesquisarCidadesAsync("Cidade inexistente", CancellationToken.None));
        Assert.Equal("Cidade não encontrada. Confira o nome e tente novamente.", naoEncontrada.Message);

        resposta = """{"results":[{"id":1,"name":"Teste","latitude":-23.5},{"id":2,"name":"Teste 2","latitude":100,"longitude":-46.6}]}""";
        var semCoordenadas = await Assert.ThrowsAsync<HttpRequestException>(() =>
            servico.PesquisarCidadesAsync("Teste", CancellationToken.None));
        Assert.Equal("A pesquisa retornou cidades sem coordenadas válidas.", semCoordenadas.Message);
    }

    [Fact]
    public async Task PesquisarCidades_DeveInformarFalhaDaGeocodificacao()
    {
        using var cliente = new HttpClient(new ManipuladorRoteadorHttp(_ => CriarJson(HttpStatusCode.BadGateway, "{}")));
        var servico = new ServicoClimaPublico(cliente, CriarOpcoes());

        var erro = await Assert.ThrowsAsync<HttpRequestException>(() =>
            servico.PesquisarCidadesAsync("Gramado", CancellationToken.None));

        Assert.Equal("Não foi possível pesquisar a localização.", erro.Message);
    }

    [Fact]
    public async Task BuscarPorCidade_DeveReutilizarConsultaPorCoordenadas()
    {
        var urls = new List<string>();
        using var cliente = new HttpClient(new ManipuladorRoteadorHttp(requisicao =>
        {
            var url = requisicao.RequestUri!.ToString();
            urls.Add(url);
            if (url.StartsWith("https://local.test/forecast", StringComparison.Ordinal)) return CriarJson(HttpStatusCode.OK, ClimaJson);
            if (url.StartsWith("https://local.test/air", StringComparison.Ordinal)) return CriarJson(HttpStatusCode.OK, JsonAqi);
            return CriarJson(HttpStatusCode.NotFound, "{}");
        }));
        var servico = new ServicoClimaPublico(cliente, CriarOpcoes());
        var cidade = CriarCidadePublica();

        var resultado = await servico.BuscarPorCidadeAsync(cidade, CancellationToken.None);

        Assert.Equal("cidade", resultado.Origem.Tipo);
        Assert.Equal("Gramado - Rio Grande do Sul", resultado.Origem.Rotulo);
        Assert.Equal(-29.3788, resultado.Origem.Latitude, 4);
        Assert.Equal(-50.8726, resultado.Origem.Longitude, 4);
        Assert.Contains(urls, url => Uri.UnescapeDataString(url).Contains("latitude=-29.3788", StringComparison.Ordinal));
        Assert.DoesNotContain(urls, url => url.StartsWith("https://local.test/geo", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null, -50.8)]
    [InlineData(-29.3, null)]
    [InlineData(91.0, -50.8)]
    [InlineData(-29.3, -181.0)]
    public async Task BuscarPorCidade_DeveRejeitarCoordenadasInvalidas(double? latitude, double? longitude)
    {
        var requisicoes = 0;
        using var cliente = new HttpClient(new ManipuladorRoteadorHttp(_ =>
        {
            requisicoes++;
            return CriarJson(HttpStatusCode.OK, ClimaJson);
        }));
        var servico = new ServicoClimaPublico(cliente, CriarOpcoes());
        var cidade = CriarCidadePublica() with { Latitude = latitude, Longitude = longitude };

        var erro = await Assert.ThrowsAsync<ArgumentException>(() =>
            servico.BuscarPorCidadeAsync(cidade, CancellationToken.None));

        Assert.Equal("Selecione uma cidade válida.", erro.Message);
        Assert.Equal(0, requisicoes);
    }

    private static CidadePublica CriarCidadePublica() => new(
        3459038,
        "Gramado",
        "Rio Grande do Sul",
        "Gramado",
        "Brasil",
        "BR",
        -29.3788,
        -50.8726,
        "America/Sao_Paulo",
        "Gramado - Rio Grande do Sul");

    private static OpcoesClimaPublico CriarOpcoes() => new()
    {
        BrasilApiCepUrl = "https://local.test/brasil",
        ViaCepUrl = "https://local.test/via",
        OpenMeteoGeocodingUrl = "https://local.test/geo",
        OpenMeteoForecastUrl = "https://local.test/forecast",
        OpenMeteoAirQualityUrl = "https://local.test/air"
    };

    private static HttpResponseMessage CriarJson(HttpStatusCode status, string conteudo) => new(status)
    {
        Content = new StringContent(conteudo, Encoding.UTF8, "application/json")
    };

    private const string ClimaJson = """
    {
      "current": {
        "time": "2026-06-03T12:00",
        "temperature_2m": 21.4,
        "apparent_temperature": 20.8,
        "relative_humidity_2m": 63,
        "pressure_msl": 1017.2,
        "dew_point_2m": 14.1,
        "precipitation": 0.0,
        "rain": 0.0,
        "weather_code": 2,
        "cloud_cover": 40,
        "wind_speed_10m": 8,
        "wind_gusts_10m": 16,
        "uv_index": 4.5
      },
      "hourly": {
        "time": ["2026-06-03T11:00", "2026-06-03T12:00", "2026-06-03T14:00"],
        "temperature_2m": [20.9, 21.4, null],
        "apparent_temperature": [20.2, 20.8, 21.5],
        "relative_humidity_2m": [65, 63, 62],
        "pressure_msl": [1017.6, 1017.2, 1016.8],
        "dew_point_2m": [14.0, 14.1, 14.4],
        "precipitation_probability": [20, 40, 70],
        "precipitation": [0, 0.2, 3.0],
        "rain": [0, 0.2, 3.0],
        "showers": [0, 0, 0],
        "weather_code": [2, 51, 61],
        "cloud_cover": [40, 55, 80],
        "wind_speed_10m": [8, 9, 12],
        "wind_gusts_10m": [16, 18, 25],
        "uv_index": [3.8, 4.5, 3.1]
      },
      "daily": {
        "time": ["2026-06-03"],
        "sunrise": ["2026-06-03T06:30"],
        "sunset": ["2026-06-03T17:45"],
        "daylight_duration": [40500],
        "uv_index_max": [5.2],
        "precipitation_probability_max": [70],
        "precipitation_sum": [3.2],
        "weather_code": [61]
      }
    }
    """;

    private const string JsonAqi = """
    {
      "current": { "us_aqi": 42 },
      "hourly": {
        "time": ["2026-06-03T11:00", "2026-06-03T12:00"],
        "us_aqi": [40, 42]
      }
    }
    """;

    private const string ClimaQuinzeDiasJson = """
    {
      "current": {
        "time": "2026-06-03T12:00",
        "temperature_2m": 21.4,
        "apparent_temperature": 20.8,
        "relative_humidity_2m": 63,
        "pressure_msl": 1017.2,
        "dew_point_2m": 14.1,
        "uv_index": 4.5
      },
      "hourly": {
        "time": ["2026-06-03T12:00"],
        "temperature_2m": [21.4],
        "apparent_temperature": [20.8],
        "relative_humidity_2m": [63],
        "pressure_msl": [1017.2]
      },
      "daily": {
        "time": ["2026-06-03","2026-06-04","2026-06-05","2026-06-06","2026-06-07","2026-06-08","2026-06-09","2026-06-10","2026-06-11","2026-06-12","2026-06-13","2026-06-14","2026-06-15","2026-06-16","2026-06-17"],
        "temperature_2m_min": [13.2,14.1,15.0,14.8,13.9,13.2,12.8,12.5,13.0,13.8,14.2,15.1,15.4,15.0,14.7],
        "temperature_2m_max": [22.4,23.1],
        "precipitation_sum": [2.5],
        "precipitation_probability_max": [70,40,20,10,30,50,60,40,30,20,10,20,30,40,50],
        "uv_index_max": [5.8,6.0,6.2,5.9,5.7,5.6,5.5,5.4,5.3,5.2,5.1,5.0,4.9,4.8,4.7],
        "weather_code": [61],
        "sunrise": ["2026-06-03T06:30"],
        "sunset": ["2026-06-03T17:45"],
        "daylight_duration": [40500]
      }
    }
    """;

    private sealed class ManipuladorRoteadorHttp : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public ManipuladorRoteadorHttp(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage requisicao, CancellationToken tokenCancelamento)
        {
            return Task.FromResult(_responder(requisicao));
        }
    }
}
