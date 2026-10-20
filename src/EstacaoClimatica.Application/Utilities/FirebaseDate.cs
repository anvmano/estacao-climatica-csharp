using System.Globalization;

namespace EstacaoClimatica.Application.Utilities;

internal static class DataFirebase
{
    public static string Normalizar(string? data)
    {
        // O Firebase usa dd-MM-yyyy; o frontend já converte o input HTML antes de chamar a API.
        return !string.IsNullOrWhiteSpace(data)
            ? data
            : DateTime.Today.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
    }

    public static DateTime? InterpretarDataFirebase(string textoString)
    {
        // Conversão estrita evita interpretar datas Firebase com cultura local diferente.
        return DateTime.TryParseExact(textoString, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var convertida)
            ? convertida
            : null;
    }

    public static bool EhMesmoMes(DateTime data, string dataSelecionada)
    {
        // Heatmaps mensais comparam mês e ano, não apenas o número do mês.
        var selecionada = InterpretarDataFirebase(dataSelecionada);
        return selecionada is not null && data.Month == selecionada.Value.Month && data.Year == selecionada.Value.Year;
    }
}
