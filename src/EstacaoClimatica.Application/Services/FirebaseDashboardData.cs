using System.Text.Json;

namespace EstacaoClimatica.Application.Services;

public sealed record DadosDashboardFirebase(
    JsonElement? Quarto,
    JsonElement? Solar,
    JsonElement? Aquario,
    JsonElement? Sala)
{
    public IReadOnlyList<string> RamosComFalha { get; init; } = [];
}
