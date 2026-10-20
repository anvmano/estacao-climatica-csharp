using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using EstacaoClimatica.Application.Abstractions;
using EstacaoClimatica.Application.Configuration;

namespace EstacaoClimatica.Infrastructure.Authentication;

public sealed class VerificadorTokenAcessoFirebase : IVerificadorTokenAcessoInterno
{
    private static readonly TimeSpan DuracaoCache = TimeSpan.FromMinutes(5);
    private readonly HttpClient _clienteHttp;
    private readonly OpcoesAcessoInterno _opcoes;
    private readonly ConcurrentDictionary<string, EntradaCache> _cache = new(StringComparer.Ordinal);

    public VerificadorTokenAcessoFirebase(HttpClient clienteHttp, OpcoesAcessoInterno opcoes)
    {
        _clienteHttp = clienteHttp;
        _opcoes = opcoes;
    }

    public async Task<string?> ObterEmailAsync(string idToken, CancellationToken tokenCancelamento)
    {
        if (string.IsNullOrWhiteSpace(idToken)) return null;
        if (_cache.TryGetValue(idToken, out var entrada) && entrada.ExpiraEm > DateTimeOffset.UtcNow)
        {
            return entrada.Email;
        }

        using var resposta = await _clienteHttp.PostAsJsonAsync(
            $"{_opcoes.TokenLookupUrl}?key={Uri.EscapeDataString(_opcoes.FirebaseApiKey)}",
            new { idToken },
            tokenCancelamento);
        if (!resposta.IsSuccessStatusCode) return null;

        await using var fluxo = await resposta.Content.ReadAsStreamAsync(tokenCancelamento);
        using var json = await JsonDocument.ParseAsync(fluxo, cancellationToken: tokenCancelamento);
        var usuario = json.RootElement.TryGetProperty("users", out var usuarios) && usuarios.ValueKind == JsonValueKind.Array
            ? usuarios.EnumerateArray().FirstOrDefault()
            : default;
        if (usuario.ValueKind != JsonValueKind.Object || !usuario.TryGetProperty("email", out var emailJson)) return null;

        var email = emailJson.GetString()?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email)) return null;
        _cache[idToken] = new EntradaCache(email, DateTimeOffset.UtcNow.Add(DuracaoCache));
        return email;
    }

    private sealed record EntradaCache(string Email, DateTimeOffset ExpiraEm);
}
