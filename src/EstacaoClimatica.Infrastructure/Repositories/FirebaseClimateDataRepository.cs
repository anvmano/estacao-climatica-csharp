using EstacaoClimatica.Application.Abstractions;
using EstacaoClimatica.Application.Configuration;
using EstacaoClimatica.Application.Services;
using System.Globalization;
using System.Text.Json;

namespace EstacaoClimatica.Infrastructure.Repositories;

public sealed class RepositorioDadosClimaticosFirebase : IRepositorioDadosClimaticos
{
    private readonly IFonteDadosFirebase _firebase;
    private readonly OpcoesFirebase _opcoesFirebase;

    public RepositorioDadosClimaticosFirebase(IFonteDadosFirebase firebase, OpcoesFirebase opcoesFirebase)
    {
        _firebase = firebase;
        _opcoesFirebase = opcoesFirebase;
    }

    public async Task<DadosDashboardFirebase> ObterDadosDashboardAsync(CancellationToken tokenCancelamento)
    {
        return await ObterDadosPorRamosAsync(true, true, true, true, tokenCancelamento);
    }

    public async Task<DadosDashboardFirebase> ObterDadosPorRamosAsync(bool quarto, bool solar, bool aquario, bool sala, CancellationToken tokenCancelamento)
    {
        var caminhos = _opcoesFirebase.Paths;
        var tarefaQuarto = quarto ? _firebase.ObterCaminhoAsync(caminhos.Room, tokenCancelamento) : Task.FromResult<JsonElement?>(null);
        var tarefaSolar = solar ? _firebase.ObterCaminhoAsync(caminhos.Solar, tokenCancelamento) : Task.FromResult<JsonElement?>(null);
        var tarefaAquario = aquario ? _firebase.ObterCaminhoAsync(caminhos.Aquarium, tokenCancelamento) : Task.FromResult<JsonElement?>(null);
        var tarefaSala = sala ? _firebase.ObterCaminhoAsync(caminhos.LivingRoom, tokenCancelamento) : Task.FromResult<JsonElement?>(null);

        await Task.WhenAll(tarefaQuarto, tarefaSolar, tarefaAquario, tarefaSala);

        return new DadosDashboardFirebase(
            tarefaQuarto.Result,
            tarefaSolar.Result,
            tarefaAquario.Result,
            tarefaSala.Result);
    }

    public async Task<DadosDashboardFirebase> ObterDadosDashboardAsync(string? dataSelecionada, CancellationToken tokenCancelamento)
    {
        if (!TentarConverterDataFirebase(dataSelecionada, out var dataBase))
        {
            return await ObterDadosDashboardAsync(tokenCancelamento);
        }

        var caminhos = _opcoesFirebase.Paths;
        var datasMes = CriarDatasDoMes(dataBase).ToArray();
        var datasJanela = new[] { FormatarDataFirebase(dataBase), FormatarDataFirebase(dataBase.AddDays(-1)) };
        var datasClimaticas = datasMes.Concat(datasJanela).Distinct(StringComparer.Ordinal).ToArray();

        // Solar permanece completo: o gráfico histórico usa 365 dias e buscar dia a dia seria mais caro que o ramo em cache.
        var tarefaQuarto = ObterCaminhoComUltimaDataAsync(caminhos.Room, datasClimaticas, tokenCancelamento);
        var tarefaSolar = _firebase.ObterCaminhoAsync(caminhos.Solar, tokenCancelamento);
        var tarefaAquario = ObterCaminhoComUltimaDataAsync(caminhos.Aquarium, datasJanela, tokenCancelamento);
        var tarefaSala = ObterCaminhoComUltimaDataAsync(caminhos.LivingRoom, datasClimaticas, tokenCancelamento);

        await Task.WhenAll(tarefaQuarto, tarefaSolar, tarefaAquario, tarefaSala);

        return new DadosDashboardFirebase(
            tarefaQuarto.Result,
            tarefaSolar.Result,
            tarefaAquario.Result,
            tarefaSala.Result);
    }

    private async Task<JsonElement?> ObterCaminhoComUltimaDataAsync(string caminho, IReadOnlyList<string> datas, CancellationToken tokenCancelamento)
    {
        var chaves = await _firebase.ObterCaminhoComQueryAsync(caminho, "shallow=true", tokenCancelamento);
        var datasDisponiveis = chaves is { ValueKind: JsonValueKind.Object } valor
            ? valor.EnumerateObject().Select(item => item.Name).ToHashSet(StringComparer.Ordinal)
            : null;
        var ultimaData = datasDisponiveis?
            .Select(nome => new { Nome = nome, Data = ConverterDataFirebase(nome) })
            .Where(item => item.Data is not null && item.Data.Value.Date <= DateTime.Today)
            .OrderByDescending(item => item.Data)
            .Select(item => item.Nome)
            .FirstOrDefault();
        // O dia atual pode surgir enquanto o shallow ainda está no cache de 30 segundos.
        var hoje = FormatarDataFirebase(DateTime.Today);
        var datasExistentes = datasDisponiveis is null
            ? datas
            : datas.Where(data => datasDisponiveis.Contains(data) || data == hoje).ToArray();
        var datasComUltima = ultimaData is null
            ? datasExistentes
            : datasExistentes.Concat(new[] { ultimaData }).Distinct(StringComparer.Ordinal).ToArray();

        return await ObterCaminhoPorDatasAsync(caminho, datasComUltima, tokenCancelamento);
    }

    private async Task<JsonElement?> ObterCaminhoPorDatasAsync(string caminho, IReadOnlyList<string> datas, CancellationToken tokenCancelamento)
    {
        var datasUnicas = datas
            .Where(data => !string.IsNullOrWhiteSpace(data))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (datasUnicas.Length == 0) return null;

        var tarefas = datasUnicas.ToDictionary(
            data => data,
            data => _firebase.ObterCaminhoAsync($"{caminho}/{data}", tokenCancelamento),
            StringComparer.Ordinal);

        await Task.WhenAll(tarefas.Values);

        var dados = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (data, tarefa) in tarefas)
        {
            if (tarefa.Result is { } valor)
            {
                dados[data] = valor.Clone();
            }
        }

        return dados.Count == 0 ? null : ParaElementoJson(dados);
    }

    private static IEnumerable<string> CriarDatasDoMes(DateTime dataBase)
    {
        var diasNoMes = DateTime.DaysInMonth(dataBase.Year, dataBase.Month);
        for (var dia = 1; dia <= diasNoMes; dia++)
        {
            yield return FormatarDataFirebase(new DateTime(dataBase.Year, dataBase.Month, dia));
        }
    }

    private static bool TentarConverterDataFirebase(string? valor, out DateTime data)
    {
        data = default;
        return !string.IsNullOrWhiteSpace(valor)
            && DateTime.TryParseExact(valor, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out data);
    }

    private static DateTime? ConverterDataFirebase(string valor)
    {
        return DateTime.TryParseExact(valor, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data)
            ? data
            : null;
    }

    private static string FormatarDataFirebase(DateTime data)
    {
        return data.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
    }

    private static JsonElement ParaElementoJson<T>(T valor)
    {
        return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(valor));
    }
}
