using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using EstacaoClimatica.Application.Abstractions;
using EstacaoClimatica.Application.Configuration;

namespace EstacaoClimatica.Infrastructure.Authentication;

public sealed class VerificadorTokenAcessoFirebase : IVerificadorTokenAcessoInterno
{
    private static readonly TimeSpan DuracaoCache = TimeSpan.FromSeconds(30);
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
        var expiraTokenEm = ObterExpiracaoToken(idToken);
        if (expiraTokenEm <= DateTimeOffset.UtcNow) return null;
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
        if (usuario.TryGetProperty("validSince", out var validoDesde))
        {
            if (validoDesde.ValueKind != JsonValueKind.String
                || !long.TryParse(validoDesde.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var segundosValidos)
                || ObterTimestampToken(idToken, "auth_time") is not long autenticadoEm
                || autenticadoEm < segundosValidos) return null;
        }

        var email = emailJson.GetString()?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email)) return null;
        // A revogação remota volta a ser verificada em até 30 segundos.
        _cache[idToken] = new EntradaCache(email, Minimo(DateTimeOffset.UtcNow.Add(DuracaoCache), expiraTokenEm));
        return email;
    }

    private static DateTimeOffset Minimo(DateTimeOffset primeiro, DateTimeOffset segundo) =>
        primeiro < segundo ? primeiro : segundo;

    private static DateTimeOffset ObterExpiracaoToken(string token)
    {
        var segundos = ObterTimestampToken(token, "exp");
        if (segundos is null) return DateTimeOffset.MaxValue;
        try { return DateTimeOffset.FromUnixTimeSeconds(segundos.Value); }
        catch (ArgumentOutOfRangeException) { return DateTimeOffset.MaxValue; }
    }

    private static long? ObterTimestampToken(string token, string claim)
    {
        var partes = token.Split('.');
        if (partes.Length != 3) return null;
        try
        {
            var payload = partes[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
            using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
            return json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty(claim, out var valor)
                && valor.ValueKind == JsonValueKind.Number
                && valor.TryGetInt64(out var segundos)
                ? segundos : null;
        }
        catch (Exception erro) when (erro is FormatException or JsonException)
        {
            return null;
        }
    }

    private sealed record EntradaCache(string Email, DateTimeOffset ExpiraEm);
}
