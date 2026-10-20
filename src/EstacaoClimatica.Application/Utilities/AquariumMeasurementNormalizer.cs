using EstacaoClimatica.Application.Configuration;

namespace EstacaoClimatica.Application.Utilities;

internal static class NormalizadorMedicoesAquario
{
    public static double? NormalizarValorMedicao(string chave, double? valor)
    {
        if (valor is null) return null;

        return chave switch
        {
            // Sensores do aquario gravam TDS em escala x10; a API entrega o valor ja em ppm.
            CamposFirebase.Aquario.Tds => valor / 10,

            // Turbidez e gravada em escala x1000; a API entrega o valor final em NTU.
            CamposFirebase.Aquario.Turbidez => valor / 1000,
            _ => valor
        };
    }
}
