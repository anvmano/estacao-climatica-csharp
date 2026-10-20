using System.Text.Json;
using EstacaoClimatica.Application.Models;
using EstacaoClimatica.Application.Utilities;
using EstacaoClimatica.Domain;

namespace EstacaoClimatica.Application.Builders;

internal static class MontadorEstatisticas
{
    private static readonly TrendInfo TendenciaEstavel = new("Estável", "stable", "→");
    private static readonly TrendInfo TendenciaSemDados = new("--", "empty", "--");
    private static readonly TrendInfo TendenciaInsuficiente = new("Dados insuficientes", "insufficient", "--");

    public static IReadOnlyList<MetricSummary> Montar(JsonElement? dados, IEnumerable<DefinicaoMetrica> metricas)
    {
        return metricas.Select(metrica =>
        {
            var qualidade = MontadorQualidadeDados.AnalisarSerie(dados, metrica.Chave).Qualidade;
            // Valores ausentes ou inválidos são ignorados para não distorcer média, mínimo e máximo.
            var valores = LeitorRegistrosFirebase.EnumerarRegistros(dados)
                .Select(registro => LeitorNumerosJson.ObterNumero(registro.Item, metrica.Chave))
                .Select(valor => NormalizadorMedicoesAquario.NormalizarValorMedicao(metrica.Chave, valor))
                .OfType<double>()
                .ToList();

            if (valores.Count == 0)
            {
                return new MetricSummary(metrica.Chave, metrica.Rotulo, metrica.Sufixo, null, null, null, null, TendenciaSemDados, qualidade);
            }

            // Uma única leitura preserva o valor, mas não sustenta delta ou tendência.
            double? variacao = valores.Count >= 2 ? valores[^1] - valores[0] : null;
            return new MetricSummary(
                metrica.Chave,
                metrica.Rotulo,
                metrica.Sufixo,
                valores.Average(),
                valores.Min(),
                valores.Max(),
                variacao,
                variacao is null ? TendenciaInsuficiente : ObterTendencia(variacao.Value),
                qualidade);
        }).ToList();
    }

    private static TrendInfo ObterTendencia(double delta)
    {
        // Pequenas oscilações de sensor abaixo de 0,05 são tratadas como estabilidade visual.
        if (Math.Abs(delta) < 0.05) return TendenciaEstavel;
        return delta > 0 ? new TrendInfo("Subindo", "up", "↗") : new TrendInfo("Caindo", "down", "↘");
    }
}
