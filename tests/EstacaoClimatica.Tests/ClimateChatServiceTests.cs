using System.Text.Json;
using EstacaoClimatica.Application.Abstractions;
using EstacaoClimatica.Application.Configuration;
using EstacaoClimatica.Application.Services;
using EstacaoClimatica.Domain;

namespace EstacaoClimatica.Tests;

public sealed class TestesServicoAssistenteClimatico
{
    [Fact]
    public async Task ResponderAsync_ComIaDesativada_UsaCalculoLocalDaDataEAba()
    {
        var servico = CriarServico();

        var resposta = await servico.ResponderAsync(new("Qual foi a temperatura média da sala?", "Tab1", "03-06-2026"), CancellationToken.None);

        Assert.Equal("local", resposta.Source);
        Assert.Contains("Temperatura em Sala", resposta.Answer);
        Assert.Contains("média 23,50°C", resposta.Answer);
        Assert.Contains("Sala", resposta.Intent.Environments);
        Assert.Contains("Temperatura", resposta.Intent.Metrics);
        Assert.Equal(2, resposta.Intent.Samples);
    }

    [Fact]
    public async Task ResponderAsync_ConsultaToluenoComoMetricaDaSala()
    {
        var servico = CriarServico();

        var resposta = await servico.ResponderAsync(new("Qual foi a média de tolueno da sala?", "Tab1", "03-06-2026"), CancellationToken.None);

        Assert.Equal("local", resposta.Source);
        Assert.Contains("Tolueno em Sala", resposta.Answer);
        Assert.Contains("média 0,50ppm", resposta.Answer);
        Assert.Contains("Tolueno", resposta.Intent.Metrics);
    }

    [Fact]
    public async Task ResponderAsync_ComHoraEspecifica_RetornaSomenteValorDoHorario()
    {
        var servico = CriarServico();

        var resposta = await servico.ResponderAsync(new("Qual era a temperatura da sala às 08h?", "Tab1", "03-06-2026"), CancellationToken.None);

        Assert.Equal("local", resposta.Source);
        Assert.Contains("Temperatura em Sala no dia 03/06/2026 às 08:00: 19,00°C.", resposta.Answer);
        Assert.DoesNotContain("média", resposta.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mínima", resposta.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("máxima", resposta.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("amostra", resposta.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResponderAsync_ValorAtual_RetornaUltimaMedicaoDaMetrica()
    {
        var servico = CriarServico();

        var resposta = await servico.ResponderAsync(new("Qual a temperatura da sala agora?", "Tab1", "04-06-2026"), CancellationToken.None);

        Assert.Equal("local", resposta.Source);
        Assert.Contains("Temperatura em Sala na última medição de 04/06/2026 às 23:00: 30,00°C.", resposta.Answer);
        Assert.Equal("ultima_medicao", resposta.Intent.Operation);
        Assert.DoesNotContain("mínima", resposta.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("máxima", resposta.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResponderAsync_PerguntaDeContinuacao_HerdaMetricaEOperacao()
    {
        var servico = CriarServico();
        var memoria = new ChatIntentSummary(new[] { "Sala" }, new[] { "Temperatura" }, "media", "03/06/2026", 2, Dates: new[] { "03-06-2026" });

        var resposta = await servico.ResponderAsync(new("e no quarto?", "Tab1", "03-06-2026", memoria), CancellationToken.None);

        Assert.Equal("local", resposta.Source);
        Assert.Contains("Temperatura em Quarto", resposta.Answer);
        Assert.Contains("média 20,00°C", resposta.Answer);
        Assert.Equal("media", resposta.Intent.Operation);
        Assert.Contains("Quarto", resposta.Intent.Environments);
    }

    [Fact]
    public async Task ResponderAsync_PerguntaDeContinuacao_HerdaPeriodoAnterior()
    {
        var servico = CriarServico();
        var memoria = new ChatIntentSummary(new[] { "Sala" }, new[] { "Temperatura" }, "media", "02/06/2026", 2, Dates: new[] { "02-06-2026" });

        var resposta = await servico.ResponderAsync(new("e no quarto?", "Tab1", "03-06-2026", memoria), CancellationToken.None);

        Assert.Equal("local", resposta.Source);
        Assert.Contains("Temperatura em Quarto", resposta.Answer);
        Assert.Contains("média 20,00°C", resposta.Answer);
        Assert.Contains("02-06-2026", resposta.Intent.Dates ?? Array.Empty<string>());
    }

    [Fact]
    public async Task ResponderAsync_PerguntaDeContinuacao_HerdaHoraConsultada()
    {
        var servico = CriarServico();
        var memoria = new ChatIntentSummary(new[] { "Sala" }, new[] { "Temperatura" }, "resumo", "03/06/2026", 1, "08", Dates: new[] { "03-06-2026" });

        var resposta = await servico.ResponderAsync(new("e no quarto?", "Tab1", "03-06-2026", memoria), CancellationToken.None);

        Assert.Equal("local", resposta.Source);
        Assert.Contains("Temperatura em Quarto no dia 03/06/2026 às 08:00: 20,00°C.", resposta.Answer);
        Assert.Equal("08", resposta.Intent.Hour);
        Assert.DoesNotContain("média", resposta.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResponderAsync_MetricaExclusivaUsaAmbienteQuePossuiMedicao()
    {
        var servico = CriarServico();

        var resposta = await servico.ResponderAsync(new("Qual foi a média de CO2?", "Tab2", "03-06-2026"), CancellationToken.None);

        Assert.Equal("local", resposta.Source);
        Assert.Contains("CO2 em Sala", resposta.Answer);
        Assert.Contains("média 405,00ppm", resposta.Answer);
        Assert.Contains("Sala", resposta.Intent.Environments);
        Assert.DoesNotContain("Quarto", resposta.Intent.Environments);
    }

    [Fact]
    public async Task ResponderAsync_MetricaExclusivaCorrigeAmbienteIncompativel()
    {
        var servico = CriarServico();

        var resposta = await servico.ResponderAsync(new("Qual foi a média de tolueno no quarto?", "Tab2", "03-06-2026"), CancellationToken.None);

        Assert.Equal("local", resposta.Source);
        Assert.Contains("Tolueno em Sala", resposta.Answer);
        Assert.Contains("média 0,50ppm", resposta.Answer);
        Assert.Contains("Sala", resposta.Intent.Environments);
        Assert.DoesNotContain("Quarto", resposta.Intent.Environments);
    }

    [Fact]
    public async Task ResponderAsync_QualidadeDoAr_RedirecionaParaSalaECalculaAqi()
    {
        var servico = CriarServico();

        var resposta = await servico.ResponderAsync(new("Como está a qualidade do ar?", "Tab2", "03-06-2026"), CancellationToken.None);

        Assert.Equal("local", resposta.Source);
        Assert.Contains("AQI estimado em Sala: 30 (Boa)", resposta.Answer);
        Assert.Contains("Dominante: Tolueno (0,60ppm)", resposta.Answer);
        Assert.Contains("Sala", resposta.Intent.Environments);
        Assert.Contains("AQI estimado", resposta.Intent.Metrics);
    }

    [Fact]
    public async Task ResponderAsync_QualidadeDoArComHora_RetornaAqiDoHorario()
    {
        var servico = CriarServico();

        var resposta = await servico.ResponderAsync(new("Qual era o AQI às 08h?", "Tab3", "03-06-2026"), CancellationToken.None);

        Assert.Equal("local", resposta.Source);
        Assert.Contains("AQI estimado em Sala às 08:00: 29 (Boa)", resposta.Answer);
        Assert.Contains("Dominante: CO2 (400,00ppm)", resposta.Answer);
        Assert.DoesNotContain("média", resposta.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResponderAsync_StatusFaixa_UsaFaixaDeConfortoDaMetrica()
    {
        var servico = CriarServico();

        var resposta = await servico.ResponderAsync(new("A temperatura do quarto ficou dentro da faixa?", "Tab2", "03-06-2026"), CancellationToken.None);

        Assert.Equal("local", resposta.Source);
        Assert.Contains("Temperatura em Quarto ficou dentro da faixa", resposta.Answer);
        Assert.Contains("Faixa usada: 20,00°C a 26,00°C", resposta.Answer);
        Assert.Contains("Horas fora da faixa: 0", resposta.Answer);
        Assert.Contains("Temperatura", resposta.Intent.Metrics);
    }

    [Fact]
    public async Task ResponderAsync_StatusFaixa_InformaPiorHorarioForaDaFaixa()
    {
        var servico = CriarServico();

        var resposta = await servico.ResponderAsync(new("Qual foi o pior horário fora da faixa da temperatura da sala?", "Tab1", "03-06-2026"), CancellationToken.None);

        Assert.Equal("local", resposta.Source);
        Assert.Contains("Temperatura em Sala ficou fora da faixa", resposta.Answer);
        Assert.Contains("Horas fora da faixa: 2", resposta.Answer);
        Assert.Contains("Pior horário fora da faixa: 03/06/2026 às 09:00, com 28,00°C (acima da faixa).", resposta.Answer);
    }

    [Fact]
    public async Task ResponderAsync_StatusFaixa_MetricaSemFaixaConfiguradaExplicaLimite()
    {
        var servico = CriarServico();

        var resposta = await servico.ResponderAsync(new("O pH do aquário ficou dentro da faixa?", "Tab3", "03-06-2026"), CancellationToken.None);

        Assert.Equal("local", resposta.Source);
        Assert.Contains("Não há faixa de conforto configurada para pH em Aquário.", resposta.Answer);
    }

    [Fact]
    public async Task ResponderAsync_Ultimas24Horas_UsaJanelaMovelDaDataSelecionada()
    {
        var servico = CriarServico(() => new DateTime(2026, 6, 4, 23, 30, 0));

        var resposta = await servico.ResponderAsync(new("Como foi a temperatura da sala nas últimas 24 horas?", "Tab1", "04-06-2026"), CancellationToken.None);

        Assert.Equal("local", resposta.Source);
        Assert.Contains("últimas 24 horas", resposta.Answer);
        Assert.Contains("Temperatura: média 30,00°C", resposta.Answer);
        Assert.DoesNotContain("Sem dados", resposta.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResponderAsync_CompararDias_RetornaVencedorReferenciaEDiferenca()
    {
        var servico = CriarServico();

        var resposta = await servico.ResponderAsync(new("Compare 02/06/2026 e 03/06/2026 a temperatura da sala", "Tab1", "03-06-2026"), CancellationToken.None);

        Assert.Equal("local", resposta.Source);
        Assert.Contains("03/06/2026 teve maior média de Temperatura em Sala: 23,50°C.", resposta.Answer);
        Assert.Contains("02/06/2026 ficou com 23,00°C, diferença de 0,50°C.", resposta.Answer);
        Assert.Equal("comparar_dias", resposta.Intent.Operation);
    }

    [Fact]
    public async Task ResponderAsync_ComparacaoInformalEntreDias_UsaCompararDias()
    {
        var servico = CriarServico();

        var resposta = await servico.ResponderAsync(new("02/06/2026 ou 03/06/2026 foi mais quente na sala?", "Tab1", "03-06-2026"), CancellationToken.None);

        Assert.Equal("local", resposta.Source);
        Assert.Contains("03/06/2026 teve maior média de Temperatura em Sala: 23,50°C.", resposta.Answer);
        Assert.Equal("comparar_dias", resposta.Intent.Operation);
    }

    [Fact]
    public async Task ResponderAsync_FaixaHoraria_ConsideraSomenteIntervaloInclusivo()
    {
        var servico = CriarServico();

        var resposta = await servico.ResponderAsync(new("Entre 8h e 8h qual foi a média de temperatura da sala?", "Tab1", "03-06-2026"), CancellationToken.None);

        Assert.Equal("local", resposta.Source);
        Assert.Contains("Temperatura em Sala", resposta.Answer);
        Assert.Contains("média 19,00°C", resposta.Answer);
        Assert.DoesNotContain("28,00°C", resposta.Answer);
    }

    [Fact]
    public async Task ResponderAsync_AnaliseHoraria_CalculaHorarioDeMaiorValorMedio()
    {
        var servico = CriarServico();

        var resposta = await servico.ResponderAsync(new("Qual horário foi mais quente na sala?", "Tab1", "03-06-2026"), CancellationToken.None);

        Assert.Equal("local", resposta.Source);
        Assert.Contains("O maior valor médio de Temperatura em Sala", resposta.Answer);
        Assert.Contains("03/06/2026 às 09:00: 28,00°C.", resposta.Answer);
    }

    [Fact]
    public async Task ResponderAsync_AnaliseHoraria_RespeitaFaixaHorariaInformada()
    {
        var servico = CriarServico();

        var resposta = await servico.ResponderAsync(new("Entre 8h e 8h, qual horário foi mais quente na sala?", "Tab1", "03-06-2026"), CancellationToken.None);

        Assert.Equal("local", resposta.Source);
        Assert.Contains("considerando apenas 08:00 a 08:00", resposta.Answer);
        Assert.Contains("03/06/2026 às 08:00: 19,00°C.", resposta.Answer);
        Assert.DoesNotContain("09:00", resposta.Answer);
    }

    [Fact]
    public async Task ResponderAsync_HeatmapCalendario_EncontraDiaDoMesComMaiorMedia()
    {
        var servico = CriarServico();

        var resposta = await servico.ResponderAsync(new("Qual dia do mês foi mais quente na sala?", "Tab1", "03-06-2026"), CancellationToken.None);

        Assert.Equal("local", resposta.Source);
        Assert.Contains("No calendário mensal de 06/2026", resposta.Answer);
        Assert.Contains("04/06/2026: 30,00°C", resposta.Answer);
    }

    [Fact]
    public async Task ResponderAsync_HeatmapHorario_AgregaPorHoraDoDiaNoMes()
    {
        var servico = CriarServico();

        var resposta = await servico.ResponderAsync(new("Qual hora costuma ser mais fria na sala?", "Tab1", "03-06-2026"), CancellationToken.None);

        Assert.Equal("local", resposta.Source);
        Assert.Contains("a hora com menor média por hora", resposta.Answer);
        Assert.Contains("foi 08:00: 20,00°C", resposta.Answer);
    }

    [Fact]
    public async Task ResponderAsync_HeatmapSemanal_UsaSemanaDaDataSelecionada()
    {
        var servico = CriarServico();

        var resposta = await servico.ResponderAsync(new("Qual dia/hora da semana teve pico de temperatura na sala?", "Tab1", "03-06-2026"), CancellationToken.None);

        Assert.Equal("local", resposta.Source);
        Assert.Contains("No mapa semanal", resposta.Answer);
        Assert.Contains("Quarta às 09:00: 28,00°C", resposta.Answer);
        Assert.DoesNotContain("04/06/2026", resposta.Answer);
    }

    [Fact]
    public async Task ResponderAsync_SolarDuracaoDia_UsaEventosSolaresDaDataSelecionada()
    {
        var servico = CriarServico();

        var resposta = await servico.ResponderAsync(new("Qual foi a duração do dia?", "Tab1", "03-06-2026"), CancellationToken.None);

        Assert.Equal("local", resposta.Source);
        Assert.Contains("A duração do dia em 03/06/2026 foi 11h15", resposta.Answer);
        Assert.Contains("de 06:40 até 17:55", resposta.Answer);
        Assert.Contains("Ciclo solar", resposta.Intent.Metrics);
    }

    [Fact]
    public async Task ResponderAsync_SolarMaiorDuracaoLuz_UsaAnoSelecionadoPorPadrao()
    {
        var servico = CriarServico();

        var resposta = await servico.ResponderAsync(new("Qual foi o dia mais longo?", "Tab1", "03-06-2026"), CancellationToken.None);

        Assert.Equal("local", resposta.Source);
        Assert.Contains("dia com maior duração de luz foi 04/06/2026", resposta.Answer);
        Assert.Contains("12h de luz", resposta.Answer);
    }

    [Fact]
    public async Task ResponderAsync_SolarTendenciaNascer_UsaSemanaSelecionada()
    {
        var servico = CriarServico();

        var resposta = await servico.ResponderAsync(new("O nascer do sol está ficando mais tarde nesta semana?", "Tab1", "03-06-2026"), CancellationToken.None);

        Assert.Equal("local", resposta.Source);
        Assert.Contains("nascer do sol ficou mais tarde", resposta.Answer);
        Assert.Contains("variação de 10 min", resposta.Answer);
    }

    [Fact]
    public async Task ResponderAsync_UltimosDias_UsaQuantidadeEDataSelecionada()
    {
        var servico = CriarServico();

        var resposta = await servico.ResponderAsync(new("Qual a média da temperatura da sala nos últimos 2 dias?", "Tab1", "03-06-2026"), CancellationToken.None);

        Assert.Equal("media", resposta.Intent.Operation);
        Assert.Contains("últimos 2 dias até 03/06/2026 (02/06/2026 a 03/06/2026)", resposta.Answer);
    }

    [Fact]
    public async Task ResponderAsync_UltimosDiasPorExtenso_UsaQuantidadeInformada()
    {
        var servico = CriarServico();

        var resposta = await servico.ResponderAsync(new("Qual a média da temperatura da sala nos últimos cinco dias?", "Tab1", "03-06-2026"), CancellationToken.None);

        Assert.Contains("últimos 5 dias até 03/06/2026 (30/05/2026 a 03/06/2026)", resposta.Answer);
    }

    [Fact]
    public async Task ResponderAsync_Ultimos45Dias_ExplicitaLimiteDe30Dias()
    {
        var servico = CriarServico();

        var resposta = await servico.ResponderAsync(new("Qual a média da temperatura da sala nos últimos 45 dias?", "Tab1", "03-06-2026"), CancellationToken.None);

        Assert.Contains("últimos 30 dias", resposta.Answer);
        Assert.Contains("solicitados 45 dias, limitado a 30 dias", resposta.Answer);
    }

    [Fact]
    public async Task ResponderAsync_Ultimas48Horas_NaoConverteParaDias()
    {
        var servico = CriarServico(() => new DateTime(2026, 6, 4, 23, 30, 0));

        var resposta = await servico.ResponderAsync(new("Qual a média da temperatura da sala nas últimas 48 horas?", "Tab1", "04-06-2026"), CancellationToken.None);

        Assert.Contains("últimas 48 horas", resposta.Answer);
        Assert.DoesNotContain("últimos 7 dias", resposta.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResponderAsync_MaximaEMinima_DestacamSomenteOperacaoSolicitada()
    {
        var servico = CriarServico();

        var maxima = await servico.ResponderAsync(new("Qual foi a temperatura mais alta da sala?", "Tab1", "03-06-2026"), CancellationToken.None);
        var minima = await servico.ResponderAsync(new("Qual foi a temperatura mais baixa da sala?", "Tab1", "03-06-2026"), CancellationToken.None);

        Assert.Contains("maior valor 28,00°C, em 03/06/2026 às 09:00", maxima.Answer);
        Assert.Contains("menor valor 19,00°C, em 03/06/2026 às 08:00", minima.Answer);
        Assert.DoesNotContain("média", maxima.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mínima", maxima.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResponderAsync_TendenciaEVariacao_UsamOperacaoSolicitada()
    {
        var servico = CriarServico();

        var tendencia = await servico.ResponderAsync(new("Qual a tendência da temperatura da sala nos últimos 2 dias?", "Tab1", "03-06-2026"), CancellationToken.None);
        var variacao = await servico.ResponderAsync(new("Quanto a temperatura da sala variou nos últimos 2 dias?", "Tab1", "03-06-2026"), CancellationToken.None);

        Assert.Contains("Tendência subindo", tendencia.Answer);
        Assert.Contains("diferença de 7,00°C", variacao.Answer);
        Assert.Equal("tendencia", tendencia.Intent.Operation);
        Assert.Equal("delta", variacao.Intent.Operation);
    }

    [Fact]
    public async Task ResponderAsync_FaixaSemComparacao_UsaDataSelecionada()
    {
        var servico = CriarServico();

        var resposta = await servico.ResponderAsync(new("A temperatura da sala ficou dentro da faixa?", "Tab1", "03-06-2026"), CancellationToken.None);

        Assert.Contains("ficou fora da faixa", resposta.Answer);
        Assert.DoesNotContain("pelo menos dois dias", resposta.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("status_faixa", resposta.Intent.Operation);
    }

    [Fact]
    public async Task ResponderAsync_DiaMaisQuenteNosUltimosDias_UsaJanelaSolicitada()
    {
        var servico = CriarServico();

        var resposta = await servico.ResponderAsync(new("Qual foi o dia mais quente da sala nos últimos 2 dias?", "Tab1", "03-06-2026"), CancellationToken.None);

        Assert.Contains("dia mais quente", resposta.Answer);
        Assert.Contains("03/06/2026", resposta.Answer);
        Assert.DoesNotContain("10/08/2026", resposta.Answer);
    }

    [Fact]
    public async Task ResponderAsync_AtalhoResumoDaEstacao_ConsultaVisaoGlobal()
    {
        var servico = CriarServico();

        var resposta = await servico.ResponderAsync(new("Resumo da data", "Tab0", "03-06-2026"), CancellationToken.None);

        Assert.Equal("resumo", resposta.Intent.Operation);
        Assert.Contains("Sala", resposta.Intent.Environments);
        Assert.Contains("Quarto", resposta.Intent.Environments);
        Assert.Contains("Aquário", resposta.Intent.Environments);
        Assert.DoesNotContain("Ciclo solar", resposta.Intent.Metrics);
    }

    [Fact]
    public async Task ResponderAsync_AtalhoMaximaDaEstacao_NaoUsaCicloSolarNemFallbackSilencioso()
    {
        var servico = CriarServico();

        var resposta = await servico.ResponderAsync(new("Máxima do dia", "Tab0", "03-06-2026"), CancellationToken.None);

        Assert.Equal("maxima", resposta.Intent.Operation);
        Assert.Contains("Sala", resposta.Intent.Environments);
        Assert.Contains("maior valor", resposta.Answer);
        Assert.DoesNotContain("Ciclo solar", resposta.Answer);
    }

    [Fact]
    public async Task ResponderAsync_ExtremoPorHorarioOntem_NaoViraComparacaoEntreDias()
    {
        var servico = CriarServico(() => new DateTime(2026, 6, 4, 12, 0, 0));

        var resposta = await servico.ResponderAsync(new("Qual foi o horário mais quente da sala ontem?", "Tab1", "04-06-2026"), CancellationToken.None);

        Assert.Equal("horario_maior_valor", resposta.Intent.Operation);
        Assert.Contains("03/06/2026 às 09:00", resposta.Answer);
        Assert.DoesNotContain("pelo menos dois dias", resposta.Answer, StringComparison.OrdinalIgnoreCase);
    }

    private static ServicoAssistenteClimatico CriarServico(Func<DateTime>? obterAgora = null)
    {
        var opcoes = new OpcoesIa { Enabled = false, MaxPeriodDays = 30, MaxPromptChars = 9000 };

        return obterAgora is null
            ? new ServicoAssistenteClimatico(new RepositorioDadosClimaticosSimulado(), new ClienteGeracaoTextoSimulado(), opcoes)
            : new ServicoAssistenteClimatico(new RepositorioDadosClimaticosSimulado(), new ClienteGeracaoTextoSimulado(), opcoes, obterAgora);
    }

    private sealed class RepositorioDadosClimaticosSimulado : IRepositorioDadosClimaticos
    {
        public Task<DadosDashboardFirebase> ObterDadosDashboardAsync(CancellationToken tokenCancelamento)
        {
            return Task.FromResult(new DadosDashboardFirebase(
                CriarJson(JsonQuarto),
                CriarJson(JsonSolar),
                CriarJson(JsonAquario),
                CriarJson(JsonSala)));
        }

        public Task<DadosDashboardFirebase> ObterDadosDashboardAsync(string? dataSelecionada, CancellationToken tokenCancelamento)
        {
            return ObterDadosDashboardAsync(tokenCancelamento);
        }

        private static JsonElement CriarJson(string json)
        {
            using var documento = JsonDocument.Parse(json);
            return documento.RootElement.Clone();
        }
    }

    private sealed class ClienteGeracaoTextoSimulado : IClienteGeracaoTexto
    {
        public Task<string> GerarTextoAsync(string instrucao, CancellationToken tokenCancelamento)
        {
            throw new InvalidOperationException("A IA nao deve ser chamada quando ArtificialIntelligence:Enabled=false.");
        }
    }

    private const string JsonQuarto = """
    {
      "02-06-2026": {
        "08-00": { "a": { "Temperatura": 20, "Sensacao termica": 21, "Umidade": 60 } }
      },
      "03-06-2026": {
        "08-00": { "a": { "Temperatura": 20, "Sensacao termica": 21, "Umidade": 60 } }
      }
    }
    """;

    private const string JsonSala = """
    {
      "02-06-2026": {
        "08-00": { "a": { "temperatura": 21, "sensacaoTermica": 22, "umidade": 55, "pressao": 1012, "CO": 1, "CO2": 400, "Aceton": 0, "Alcohol": 0, "NH4": 0, "Toluen": 0.4 } },
        "09-00": { "a": { "temperatura": 25, "sensacaoTermica": 26, "umidade": 57, "pressao": 1013, "CO": 1, "CO2": 410, "Aceton": 0, "Alcohol": 0, "NH4": 0, "Toluen": 0.6 } }
      },
      "03-06-2026": {
        "08-00": { "a": { "temperatura": 19, "sensacaoTermica": 19, "umidade": 35, "pressao": 1010, "CO": 1, "CO2": 400, "Aceton": 0, "Alcohol": 0, "NH4": 0, "Toluen": 0.4 } },
        "09-00": { "a": { "temperatura": 28, "sensacaoTermica": 28, "umidade": 65, "pressao": 1011, "CO": 1, "CO2": 410, "Aceton": 0, "Alcohol": 0, "NH4": 0, "Toluen": 0.6 } }
      },
      "04-06-2026": {
        "00-00": { "a": { "temperatura": 30, "sensacaoTermica": 30, "umidade": 50, "pressao": 1010, "CO": 1, "CO2": 400, "Aceton": 0, "Alcohol": 0, "NH4": 0, "Toluen": 0.4 } },
        "23-00": { "a": { "temperatura": 30, "sensacaoTermica": 30, "umidade": 50, "pressao": 1010, "CO": 1, "CO2": 400, "Aceton": 0, "Alcohol": 0, "NH4": 0, "Toluen": 0.4 } }
      }
    }
    """;

    private const string JsonAquario = """
    {
      "03-06-2026": {
        "08-00": { "a": { "temperaturaDS18B20": 26, "PH": 7, "TDS": 200, "Turbidez": 1200 } }
      }
    }
    """;

    private const string JsonSolar = """
    {
      "02-06-2026": {
        "08-00": { "HoraAmanhecer": 6, "MinutoAmanhecer": 0, "HoraNascerDoSol": 6, "MinutoNascerDoSol": 30, "HourPorDoSol": 17, "MinutePorDoSol": 50, "HoraAnoitecer": 18, "MinutoAnoitecer": 20 }
      },
      "03-06-2026": {
        "08-00": { "HoraAmanhecer": 6, "MinutoAmanhecer": 5, "HoraNascerDoSol": 6, "MinutoNascerDoSol": 40, "HourPorDoSol": 17, "MinutePorDoSol": 55, "HoraAnoitecer": 18, "MinutoAnoitecer": 25 }
      },
      "04-06-2026": {
        "08-00": { "HoraAmanhecer": 5, "MinutoAmanhecer": 50, "HoraNascerDoSol": 6, "MinutoNascerDoSol": 10, "HourPorDoSol": 18, "MinutePorDoSol": 10, "HoraAnoitecer": 18, "MinutoAnoitecer": 35 }
      }
    }
    """;
}
