namespace EstacaoClimatica.Application.Models;

internal sealed record DefinicaoPoluenteAqi(
    string Chave,
    string Rotulo,
    string Unidade,
    IReadOnlyList<FaixaAqi> Faixas);

internal sealed record FaixaAqi(double LimiteInferior, double LimiteSuperior, int IndiceInferior, int IndiceSuperior);

internal sealed record CategoriaAqi(int Minimo, int Maximo, string Rotulo, string NomeClasse, string Impacto);
