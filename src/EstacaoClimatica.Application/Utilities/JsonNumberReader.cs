using System.Globalization;
using System.Text.Json;

namespace EstacaoClimatica.Application.Utilities;

internal static class LeitorNumerosJson
{
    public static double? ObterNumero(JsonElement item, string chave) => ObterNumero(item, new[] { chave });

    public static double? ObterNumero(JsonElement item, IReadOnlyList<string> chaves)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;

        // Aceita número real e strings numéricas gravadas por sensores em formatos invariant e pt-BR.
        foreach (var chave in chaves)
        {
            if (!item.TryGetProperty(chave, out var valor)) continue;

            if (valor.ValueKind == JsonValueKind.Number && valor.TryGetDouble(out var numero)) return numero;
            if (valor.ValueKind == JsonValueKind.String && double.TryParse(valor.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out numero)) return numero;
            if (valor.ValueKind == JsonValueKind.String && double.TryParse(valor.GetString(), NumberStyles.Any, CultureInfo.GetCultureInfo("pt-BR"), out numero)) return numero;
        }

        return null;
    }
}
