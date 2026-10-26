using EstacaoClimatica.Application.Abstractions;
using EstacaoClimatica.Application.Builders;
using EstacaoClimatica.Application.Configuration;
using EstacaoClimatica.Application.Utilities;
using EstacaoClimatica.Domain;

namespace EstacaoClimatica.Application.Services;

public sealed class ServicoDashboardClimatico : IServicoDashboardClimatico
{
    private readonly MontadorDashboardAmbiente _montadorAmbiente;
    private readonly ServicoAnalisesAmbientais _servicoInsights = new();
    private readonly IRepositorioDadosClimaticos _repositorio;
    private readonly Func<DateTime> _obterAgora;

    public ServicoDashboardClimatico(
        IRepositorioDadosClimaticos repositorio,
        CoresAplicacao cores)
        : this(repositorio, cores, () => RelogioClimatico.Agora)
    {
    }

    internal ServicoDashboardClimatico(
        IRepositorioDadosClimaticos repositorio,
        CoresAplicacao cores,
        Func<DateTime> obterAgora)
    {
        _repositorio = repositorio;
        _obterAgora = obterAgora;
        _montadorAmbiente = new MontadorDashboardAmbiente(cores, obterAgora);
    }

    public async Task<DashboardResponse> ObterDashboardAsync(string? dataFirebase, CancellationToken tokenCancelamento)
    {
        // Todo o contrato do dashboard usa a data no formato do Firebase: dd-MM-yyyy.
        var dataSelecionada = DataFirebase.Normalizar(dataFirebase);

        // Carrega os ramos brutos uma vez e delega a tradução de cada domínio aos builders.
        var dados = await _repositorio.ObterDadosDashboardAsync(dataSelecionada, tokenCancelamento);

        var aqi = MontadorDashboardAqi.Montar(dados.Sala);
        var ultimasMedicoes = MontadorUltimasMedicoesEstacao.Montar(dados.Quarto, dados.Sala, dados.Aquario, _obterAgora());
        var riscoSala = _servicoInsights.AvaliarRiscoMofo(
            ultimasMedicoes.SalaTemperatura?.Value,
            ultimasMedicoes.SalaUmidade?.Value);
        var riscoQuarto = _servicoInsights.AvaliarRiscoMofo(
            ultimasMedicoes.QuartoTemperatura?.Value,
            ultimasMedicoes.QuartoUmidade?.Value);
        var ventilacao = _servicoInsights.RecomendarVentilacaoInterna(
            ultimasMedicoes.SalaTemperatura?.Value,
            ultimasMedicoes.SalaUmidade?.Value,
            aqi?.Value,
            riscoSala);

        return new DashboardResponse(
            dataSelecionada,
            _montadorAmbiente.MontarQuarto(dados.Quarto, dataSelecionada),
            _montadorAmbiente.MontarSala(dados.Sala, dataSelecionada),
            _montadorAmbiente.MontarAquario(dados.Aquario, dataSelecionada),
            MontadorDashboardSolar.Montar(dados.Solar, dataSelecionada, _obterAgora()),
            aqi,
            ultimasMedicoes,
            new InternalEnvironmentalInsights(ventilacao, riscoSala, riscoQuarto))
        { FailedBranches = dados.RamosComFalha };
    }
}
