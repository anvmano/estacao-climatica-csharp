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
        var caminhos = _opcoesFirebase.Paths;

        // Os quatro ramos são independentes; buscar em paralelo reduz o tempo de atualização do dashboard.
        var tarefaQuarto = _firebase.ObterCaminhoAsync(caminhos.Room, tokenCancelamento);
        var tarefaSolar = _firebase.ObterCaminhoAsync(caminhos.Solar, tokenCancelamento);
        var tarefaAquario = _firebase.ObterCaminhoAsync(caminhos.Aquarium, tokenCancelamento);
        var tarefaSala = _firebase.ObterCaminhoAsync(caminhos.LivingRoom, tokenCancelamento);

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
        var ultimaData = await ObterUltimaDataDisponivelAsync(caminho, tokenCancelamento);
        var datasComUltima = ultimaData is null
            ? datas
            : datas.Concat(new[] { ultimaData }).Distinct(StringComparer.Ordinal).ToArray();

        return await ObterCaminhoPorDatasAsync(caminho, datasComUltima, tokenCancelamento);
    }

    private async Task<string?> ObterUltimaDataDisponivelAsync(string caminho, CancellationToken tokenCancelamento)
    {
        var chaves = await _firebase.ObterCaminhoComQueryAsync(caminho, "shallow=true", tokenCancelamento);
        if (chaves is null || chaves.Value.ValueKind != JsonValueKind.Object) return null;

        return chaves.Value
            .EnumerateObject()
            .Select(propriedade => new { propriedade.Name, Data = ConverterDataFirebase(propriedade.Name) })
            .Where(item => item.Data is not null && item.Data.Value.Date <= DateTime.Today)
            .OrderByDescending(item => item.Data)
            .Select(item => item.Name)
            .FirstOrDefault();
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
