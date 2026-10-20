using System.Text.Json;
using EstacaoClimatica.Application.Configuration;
using EstacaoClimatica.Application.Models;
using EstacaoClimatica.Application.Utilities;
using EstacaoClimatica.Domain;

namespace EstacaoClimatica.Application.Builders;

internal static class MontadorDashboardSolar
{
    public static SolarDashboard Montar(JsonElement? raiz, string dataSelecionada, DateTime? agora = null)
    {
        // O histórico solar usa uma janela longa para mostrar variação sazonal sem carregar anos inteiros.
        var recente = LeitorRegistrosFirebase.FiltrarDadosPorDias(raiz, 365);
        var eventosPorData = LeitorRegistrosFirebase.EnumerarDatas(recente)
            .Select(par => (par.Date, Eventos: LerEventosSolares(ObterPrimeiroItemSolar(par.DateData))))
            .Where(par => par.Eventos is not null)
            .ToList();

        // O ciclo diário depende da data selecionada; quando incompleto, o frontend mostra mensagem de ausência.
        var eventosDoDia = raiz is null
            ? null
            : LerEventosSolares(ObterPrimeiroItemSolar(LeitorRegistrosFirebase.ObterElementoData(raiz, dataSelecionada)));
        var dataAtual = (agora ?? DateTime.Now).ToString("dd-MM-yyyy");
        var eventosAtuais = dataAtual == dataSelecionada
            ? eventosDoDia
            : raiz is null
                ? null
                : LerEventosSolares(ObterPrimeiroItemSolar(LeitorRegistrosFirebase.ObterElementoData(raiz, dataAtual)));

        return new SolarDashboard(
            MontarSerieSolar("sunHistoryDawn", "Amanhecer", "#fde68a", eventosPorData, evento => evento.Amanhecer),
            MontarSerieSolar("sunHistorySunrise", "Nascer do sol", "#fb923c", eventosPorData, evento => evento.NascerDoSol),
            MontarSerieSolar("sunHistorySunset", "Pôr do sol", "#f87171", eventosPorData, evento => evento.PorDoSol),
            MontarSerieSolar("sunHistoryDusk", "Anoitecer", "#818cf8", eventosPorData, evento => evento.Anoitecer),
            MontarDiaSolar(dataSelecionada, eventosDoDia),
            MontarDiaSolar(dataAtual, eventosAtuais));
    }

    private static SolarDay? MontarDiaSolar(string data, SegundosEventosSolares? eventos) => eventos is null
        ? null
        : new SolarDay(data, ConverterSegundosParaHoras(eventos.Amanhecer), ConverterSegundosParaHoras(eventos.NascerDoSol),
            ConverterSegundosParaHoras(eventos.Zenite), ConverterSegundosParaHoras(eventos.PorDoSol), ConverterSegundosParaHoras(eventos.Anoitecer));

    private static ChartSeries MontarSerieSolar(
        string id,
        string rotulo,
        string cor,
        IEnumerable<(string Date, SegundosEventosSolares? Eventos)> eventosPorData,
        Func<SegundosEventosSolares, int> seletor)
    {
        // O frontend recebe horas em formato decimal para alimentar diretamente o Chart.js.
        var pontos = eventosPorData
            .Where(par => par.Eventos is not null)
            .Select(par => new ChartPoint(par.Date.Replace("-", "/"), ConverterSegundosParaHoras(seletor(par.Eventos!))))
            .ToList();

        return new ChartSeries(id, rotulo, cor, "", pontos);
    }

    private static JsonElement? ObterPrimeiroItemSolar(JsonElement? dadosData)
    {
        if (dadosData is null || dadosData.Value.ValueKind != JsonValueKind.Object) return null;

        // Algumas datas solares vêm com níveis extras; procura o registro mais recente que tenha campos solares.
        foreach (var propriedade in dadosData.Value.EnumerateObject().OrderByDescending(par => par.Name))
        {
            if (PossuiCamposSolares(propriedade.Value)) return propriedade.Value;
            if (propriedade.Value.ValueKind != JsonValueKind.Object) continue;

            foreach (var aninhado in propriedade.Value.EnumerateObject().OrderByDescending(par => par.Name))
            {
                if (PossuiCamposSolares(aninhado.Value)) return aninhado.Value;
            }
        }

        return null;
    }

    private static bool PossuiCamposSolares(JsonElement item)
    {
        return item.ValueKind == JsonValueKind.Object &&
            PossuiAlgum(item, CamposFirebase.Solar.CamposIdentificacao);
    }

    private static SegundosEventosSolares? LerEventosSolares(JsonElement? item)
    {
        if (item is null) return null;

        // Os sensores já gravaram nomes em português e inglês; FirebaseFields mantém as variantes aceitas.
        var amanhecer = LerHorarioEmSegundos(item.Value, CamposFirebase.Solar.HoraAmanhecer, CamposFirebase.Solar.MinutoAmanhecer);
        var nascerSol = LerHorarioEmSegundos(item.Value, CamposFirebase.Solar.HoraNascerDoSol, CamposFirebase.Solar.MinutoNascerDoSol);
        var porDoSol = LerHorarioEmSegundos(item.Value, CamposFirebase.Solar.HoraPorDoSol, CamposFirebase.Solar.MinutoPorDoSol);
        var anoitecer = LerHorarioEmSegundos(item.Value, CamposFirebase.Solar.HoraAnoitecer, CamposFirebase.Solar.MinutoAnoitecer);
        var zenite = LerHorarioEmSegundos(
            item.Value,
            CamposFirebase.Solar.HoraZenite,
            CamposFirebase.Solar.MinutoZenite);

        if (amanhecer is null || nascerSol is null || porDoSol is null || anoitecer is null) return null;

        // Se o zênite não existir no Firebase, usa o ponto médio entre nascer e pôr do sol.
        return new SegundosEventosSolares(amanhecer.Value, nascerSol.Value, zenite ?? nascerSol.Value + ((porDoSol.Value - nascerSol.Value) / 2), porDoSol.Value, anoitecer.Value);
    }

    private static int? LerHorarioEmSegundos(JsonElement item, IReadOnlyList<string> nomesHora, IReadOnlyList<string> nomesMinuto)
    {
        var hora = LeitorNumerosJson.ObterNumero(item, nomesHora);
        var minuto = LeitorNumerosJson.ObterNumero(item, nomesMinuto) ?? 0;
        if (hora is null) return null;
        return (int)(hora.Value * 3600 + minuto * 60);
    }

    private static bool PossuiAlgum(JsonElement item, params string[] chaves)
    {
        return chaves.Any(chave => item.TryGetProperty(chave, out _));
    }

    private static double ConverterSegundosParaHoras(int segundos) => segundos / 3600d;
}
