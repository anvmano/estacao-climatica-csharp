using System.Text.Json;
using EstacaoClimatica.Application.Models;

namespace EstacaoClimatica.Application.Utilities;

internal static class LeitorRegistrosFirebase
{
    public static JsonElement? FiltrarDataSelecionada(JsonElement? raiz, string dataSelecionada)
    {
        // Recria a estrutura com a data na raiz para que os enumeradores continuem usando o mesmo formato.
        var elementoData = ObterElementoData(raiz, dataSelecionada);
        return elementoData is null ? null : EnvolverData(dataSelecionada, elementoData.Value);
    }

    public static JsonElement? FiltrarDadosPorDias(JsonElement? dados, int dias)
    {
        if (dados is null || dados.Value.ValueKind != JsonValueKind.Object) return null;

        // Filtro aplicado localmente porque o Firebase REST retorna o caminho inteiro configurado.
        var limite = RelogioClimatico.Hoje.AddDays(-dias).Date;
        var filtrado = EnumerarDatas(dados)
            .Where(par => DataFirebase.InterpretarDataFirebase(par.Date) is { } data && data >= limite)
            .ToDictionary(par => par.Date, par => par.DateData.Clone());

        return ParaElementoJson(filtrado);
    }

    public static JsonElement? FiltrarDadosPorJanelaHoras(JsonElement? dados, string dataSelecionada, int horas, DateTime dataReferenciaAtual)
    {
        if (dados is null || dados.Value.ValueKind != JsonValueKind.Object) return null;

        var dataBase = DataFirebase.InterpretarDataFirebase(dataSelecionada);
        if (dataBase is null) return null;

        // Replica o Web: aplica a hora atual sobre a data escolhida e olha as N horas anteriores.
        var fimJanela = new DateTime(
            dataBase.Value.Year,
            dataBase.Value.Month,
            dataBase.Value.Day,
            dataReferenciaAtual.Hour,
            dataReferenciaAtual.Minute,
            dataReferenciaAtual.Second,
            dataReferenciaAtual.Millisecond);
        var inicioJanela = fimJanela.AddHours(-horas);
        var filtrado = new Dictionary<string, Dictionary<string, JsonElement>>();

        foreach (var (dataFirebase, dadosData) in EnumerarDatas(dados))
        {
            var data = DataFirebase.InterpretarDataFirebase(dataFirebase);
            if (data is null) continue;

            foreach (var propriedadeHora in dadosData.EnumerateObject().Where(propriedade => propriedade.Value.ValueKind == JsonValueKind.Object))
            {
                var hora = ConverterHora(propriedadeHora.Name);
                if (hora is null) continue;

                var instante = new DateTime(
                    data.Value.Year,
                    data.Value.Month,
                    data.Value.Day,
                    hora.Value.Hour,
                    hora.Value.Minute,
                    hora.Value.Second ?? 0);

                if (instante < inicioJanela || instante > fimJanela) continue;

                if (!filtrado.TryGetValue(dataFirebase, out var horarios))
                {
                    horarios = new Dictionary<string, JsonElement>();
                    filtrado[dataFirebase] = horarios;
                }

                horarios[propriedadeHora.Name] = propriedadeHora.Value.Clone();
            }
        }

        return filtrado.Count == 0 ? null : ParaElementoJson(filtrado);
    }

    public static JsonElement? ObterElementoData(JsonElement? raiz, string data)
    {
        if (raiz is null || raiz.Value.ValueKind != JsonValueKind.Object) return null;
        return raiz.Value.TryGetProperty(data, out var elementoData) ? elementoData.Clone() : null;
    }

    public static IEnumerable<(string Date, JsonElement DateData)> EnumerarDatas(JsonElement? raiz)
    {
        if (raiz is null || raiz.Value.ValueKind != JsonValueKind.Object) yield break;

        // Datas inválidas ficam no fim da ordenação e são descartadas depois pelos builders que exigem data real.
        foreach (var propriedadeData in raiz.Value.EnumerateObject().OrderBy(propriedade => DataFirebase.InterpretarDataFirebase(propriedade.Name)))
        {
            if (propriedadeData.Value.ValueKind == JsonValueKind.Object)
            {
                yield return (propriedadeData.Name, propriedadeData.Value);
            }
        }
    }

    public static IEnumerable<RegistroFirebase> EnumerarRegistros(JsonElement? raiz, bool maisRecentesPrimeiro = false)
    {
        // Estrutura esperada no Firebase: data -> hora -> item -> campos de sensor.
        var datas = EnumerarDatas(raiz);
        datas = maisRecentesPrimeiro ? datas.OrderByDescending(par => DataFirebase.InterpretarDataFirebase(par.Date)) : datas.OrderBy(par => DataFirebase.InterpretarDataFirebase(par.Date));

        foreach (var (data, dadosData) in datas)
        {
            if (DataFirebase.InterpretarDataFirebase(data) is null) continue;
            var horarios = dadosData.EnumerateObject().Where(propriedade => propriedade.Value.ValueKind == JsonValueKind.Object);
            horarios = maisRecentesPrimeiro ? horarios.OrderByDescending(propriedade => ChaveOrdenacaoHora(propriedade.Name)) : horarios.OrderBy(propriedade => ChaveOrdenacaoHora(propriedade.Name));

            foreach (var propriedadeHora in horarios)
            {
                if (ConverterHora(propriedadeHora.Name) is null) continue;
                var itens = propriedadeHora.Value.EnumerateObject().Where(propriedade => propriedade.Value.ValueKind == JsonValueKind.Object);
                itens = maisRecentesPrimeiro ? itens.OrderByDescending(propriedade => propriedade.Name) : itens.OrderBy(propriedade => propriedade.Name);

                foreach (var item in itens)
                {
                    yield return new RegistroFirebase(data, propriedadeHora.Name, item.Value, item.Name);
                }
            }
        }
    }

    public static string FormatarRotuloHora(string horario)
    {
        var convertido = ConverterHora(horario);
        if (convertido is null) return horario;
        return convertido.Value.Second is null
            ? $"{convertido.Value.Hour:00}:{convertido.Value.Minute:00}"
            : $"{convertido.Value.Hour:00}:{convertido.Value.Minute:00}:{convertido.Value.Second:00}";
    }

    public static (int Hour, int Minute, int? Second)? ConverterHora(string horario)
    {
        var partes = horario.Split('-', ':');
        if (partes.Length is < 1 or > 3 || !int.TryParse(partes[0], out var hora) || hora is < 0 or > 23) return null;
        var minuto = 0;
        var segundoConvertido = 0;
        if (partes.Length > 1 && (!int.TryParse(partes[1], out minuto) || minuto is < 0 or > 59)) return null;
        if (partes.Length > 2 && (!int.TryParse(partes[2], out segundoConvertido) || segundoConvertido is < 0 or > 59)) return null;
        int? segundo = partes.Length > 2 ? segundoConvertido : null;
        return (hora, minuto, segundo);
    }

    public static bool LeituraNoPassadoValido(string data, string horario, DateTime agora)
    {
        var dia = DataFirebase.InterpretarDataFirebase(data);
        var hora = ConverterHora(horario);
        return dia is not null && hora is not null
            && dia.Value.AddHours(hora.Value.Hour).AddMinutes(hora.Value.Minute).AddSeconds(hora.Value.Second ?? 0) <= agora;
    }

    private static JsonElement EnvolverData(string data, JsonElement dadosData)
    {
        return ParaElementoJson(new Dictionary<string, JsonElement> { [data] = dadosData.Clone() });
    }

    private static JsonElement ParaElementoJson<T>(T valor)
    {
        // Serializa e desserializa para criar um JsonElement independente do documento original.
        return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(valor));
    }

    private static int ChaveOrdenacaoHora(string horario)
    {
        var convertido = ConverterHora(horario);
        return convertido is null ? -1 : convertido.Value.Hour * 3600 + convertido.Value.Minute * 60 + (convertido.Value.Second ?? 0);
    }
}
