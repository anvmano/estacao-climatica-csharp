using System.Text.Json;

namespace EstacaoClimatica.Application.Models;

internal sealed record RegistroFirebase(string Data, string Horario, JsonElement Item, string IdItem = "");
