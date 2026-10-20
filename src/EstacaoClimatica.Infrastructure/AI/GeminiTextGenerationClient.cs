using System.Net.Http.Json;
using System.Text.Json;
using EstacaoClimatica.Application.Abstractions;
using EstacaoClimatica.Application.Configuration;

namespace EstacaoClimatica.Infrastructure.AI;

public sealed class ClienteGeracaoTextoGemini : IClienteGeracaoTexto
{
    private readonly HttpClient _clienteHttp;
    private readonly OpcoesIa _opcoes;

    public ClienteGeracaoTextoGemini(HttpClient clienteHttp, OpcoesIa opcoes)
    {
        _clienteHttp = clienteHttp;
        _opcoes = opcoes;
    }

    public async Task<string> GerarTextoAsync(string instrucao, CancellationToken tokenCancelamento)
    {
        if (!_opcoes.Enabled)
        {
            throw new InvalidOperationException("IA desativada.");
        }

        if (string.IsNullOrWhiteSpace(_opcoes.ApiKey))
        {
            throw new InvalidOperationException("Chave da IA não configurada.");
        }

        var enderecoServico = (_opcoes.Endpoint ?? "").TrimEnd('/');
        var modelo = string.IsNullOrWhiteSpace(_opcoes.Model) ? "gemini-3.5-flash" : _opcoes.Model;
        var url = $"{enderecoServico}/models/{Uri.EscapeDataString(modelo)}:generateContent?key={Uri.EscapeDataString(_opcoes.ApiKey)}";

        var requisicao = new GeminiGenerateContentRequest(new[]
        {
            new GeminiContent(new[]
            {
                new GeminiPart(instrucao)
            })
        });

        using var resposta = await _clienteHttp.PostAsJsonAsync(url, requisicao, tokenCancelamento);
        resposta.EnsureSuccessStatusCode();

        using var fluxo = await resposta.Content.ReadAsStreamAsync(tokenCancelamento);
        using var documento = await JsonDocument.ParseAsync(fluxo, cancellationToken: tokenCancelamento);

        return documento.RootElement
            .GetProperty("candidates")
            .EnumerateArray()
            .SelectMany(candidato => candidato.GetProperty("content").GetProperty("parts").EnumerateArray())
            .Select(parte => parte.TryGetProperty("text", out var texto) ? texto.GetString() : null)
            .FirstOrDefault(texto => !string.IsNullOrWhiteSpace(texto))
            ?? "";
    }

    private sealed record GeminiGenerateContentRequest(IReadOnlyList<GeminiContent> Contents);
    private sealed record GeminiContent(IReadOnlyList<GeminiPart> Parts);
    private sealed record GeminiPart(string Text);
}
