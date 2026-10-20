using System.Text.Json;
using EstacaoClimatica.Application.Configuration;
using EstacaoClimatica.Application.Models;
using EstacaoClimatica.Application.Utilities;
using EstacaoClimatica.Domain;

namespace EstacaoClimatica.Application.Builders;

internal static class MontadorDashboardAqi
{
    private static readonly CategoriaAqi[] Categorias =
    {
        new(0, 50, "Boa", "good", "Ar satisfatório, risco mínimo."),
        new(51, 100, "Moderado", "moderate", "Aceitável, mas pode haver risco moderado para grupos sensíveis."),
        new(101, 150, "Insalubre para grupos sensíveis", "sensitive", "Grupos de risco podem apresentar sintomas."),
        new(151, 200, "Insalubre", "unhealthy", "Toda a população pode começar a sentir efeitos na saúde."),
        new(201, 300, "Muito insalubre", "very-unhealthy", "Alerta de saúde; riscos aumentados para todos."),
        new(301, 500, "Perigoso", "hazardous", "Condições de emergência; toda a população é severamente afetada.")
    };

    private static readonly DefinicaoPoluenteAqi[] Poluentes =
    {
        new(CamposFirebase.Sala.Co, "CO", "ppm", new[]
        {
            new FaixaAqi(0, 4.4, 0, 50),
            new FaixaAqi(4.5, 9.4, 51, 100),
            new FaixaAqi(9.5, 12.4, 101, 150),
            new FaixaAqi(12.5, 15.4, 151, 200),
            new FaixaAqi(15.5, 30.4, 201, 300),
            new FaixaAqi(30.5, 50.4, 301, 500)
        }),
        new(CamposFirebase.Sala.Co2, "CO2", "ppm", new[]
        {
            new FaixaAqi(0, 700, 0, 50),
            new FaixaAqi(701, 1000, 51, 100),
            new FaixaAqi(1001, 1500, 101, 150),
            new FaixaAqi(1501, 2000, 151, 200),
            new FaixaAqi(2001, 5000, 201, 300),
            new FaixaAqi(5001, 10000, 301, 500)
        }),
        CriarPoluenteGenerico(CamposFirebase.Sala.Tolueno, "Tolueno"),
        CriarPoluenteGenerico(CamposFirebase.Sala.Amonia, "Amônia"),
        CriarPoluenteGenerico(CamposFirebase.Sala.Acetona, "Acetona"),
        CriarPoluenteGenerico(CamposFirebase.Sala.Alcool, "Álcool")
    };

    public static AqiDashboard? Montar(JsonElement? raizSala)
    {
        var ultimo = LeitorRegistrosFirebase.EnumerarRegistros(raizSala, maisRecentesPrimeiro: true)
            .Select(registro => new
            {
                Registro = registro,
                Timestamp = CriarTimestamp(registro.Data, registro.Horario)
            })
            .Where(item => item.Timestamp is not null)
            .OrderByDescending(item => item.Timestamp)
            .FirstOrDefault();

        if (ultimo is null) return null;

        return MontarRegistro(ultimo.Registro.Item, ultimo.Timestamp!.Value);
    }

    public static AqiDashboard? MontarRegistro(JsonElement registro, DateTime instante)
    {
        var subindices = Poluentes
            .Select(poluente => CalcularSubindice(poluente, registro))
            .OfType<AqiSubIndex>()
            .OrderByDescending(item => item.Value)
            .ToArray();

        if (subindices.Length == 0) return null;

        var dominante = subindices[0];
        var valor = Math.Clamp(dominante.Value, 0, 500);
        var categoria = Categorias.FirstOrDefault(item => valor >= item.Minimo && valor <= item.Maximo) ?? Categorias[^1];

        return new AqiDashboard(
            valor,
            categoria.Rotulo,
            categoria.NomeClasse,
            categoria.Impacto,
            dominante.Label,
            FormatarInstante(instante),
            subindices);
    }

    private static DefinicaoPoluenteAqi CriarPoluenteGenerico(string chave, string rotulo)
    {
        return new DefinicaoPoluenteAqi(chave, rotulo, "ppm", new[]
        {
            new FaixaAqi(0, 1, 0, 50),
            new FaixaAqi(1.1, 5, 51, 100),
            new FaixaAqi(5.1, 10, 101, 150),
            new FaixaAqi(10.1, 25, 151, 200),
            new FaixaAqi(25.1, 50, 201, 300),
            new FaixaAqi(50.1, 100, 301, 500)
        });
    }

    private static AqiSubIndex? CalcularSubindice(DefinicaoPoluenteAqi poluente, JsonElement item)
    {
        var concentracao = LeitorNumerosJson.ObterNumero(item, poluente.Chave);
        if (concentracao is null) return null;

        var faixa = poluente.Faixas.FirstOrDefault(faixaIndice => concentracao >= faixaIndice.LimiteInferior && concentracao <= faixaIndice.LimiteSuperior)
            ?? poluente.Faixas[^1];
        var limitada = Math.Min(faixa.LimiteSuperior, Math.Max(faixa.LimiteInferior, concentracao.Value));
        var indice = ((faixa.IndiceSuperior - faixa.IndiceInferior) / (faixa.LimiteSuperior - faixa.LimiteInferior)) * (limitada - faixa.LimiteInferior) + faixa.IndiceInferior;

        return new AqiSubIndex(poluente.Rotulo, poluente.Unidade, Math.Round(concentracao.Value, 2), (int)Math.Round(indice));
    }

    private static DateTime? CriarTimestamp(string dataFirebase, string horaFirebase)
    {
        var data = DataFirebase.InterpretarDataFirebase(dataFirebase);
        var hora = LeitorRegistrosFirebase.ConverterHora(horaFirebase);
        return data is null || hora is null
            ? null
            : new DateTime(data.Value.Year, data.Value.Month, data.Value.Day, hora.Value.Hour, hora.Value.Minute, hora.Value.Second ?? 0);
    }

    private static string FormatarInstante(DateTime instante)
    {
        return $"{instante:dd/MM HH:mm}";
    }
}
