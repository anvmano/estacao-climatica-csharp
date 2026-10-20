using System.Text.Json;
using System.Collections.Concurrent;
using EstacaoClimatica.Application.Abstractions;
using EstacaoClimatica.Application.Configuration;

namespace EstacaoClimatica.Infrastructure.Firebase;

public sealed class FonteDadosFirebase : IFonteDadosFirebase
{
    private static readonly ConcurrentDictionary<string, EntradaCache> EntradasCache = new();
    private readonly HttpClient _clienteHttp;
    private readonly OpcoesFirebase _opcoes;

    public FonteDadosFirebase(HttpClient clienteHttp, OpcoesFirebase opcoes)
    {
        _clienteHttp = clienteHttp;
        _opcoes = opcoes;
    }

    public async Task<JsonElement?> ObterCaminhoAsync(string caminho, CancellationToken tokenCancelamento)
    {
        return await ObterUrlAsync(MontarUrl(caminho, null), tokenCancelamento);
    }

    public async Task<JsonElement?> ObterCaminhoComQueryAsync(string caminho, string parametrosConsulta, CancellationToken tokenCancelamento)
    {
        return await ObterUrlAsync(MontarUrl(caminho, parametrosConsulta), tokenCancelamento);
    }

    private async Task<JsonElement?> ObterUrlAsync(string url, CancellationToken tokenCancelamento)
    {
        if (string.IsNullOrWhiteSpace(_opcoes.DatabaseUrl))
        {
            throw new InvalidOperationException("Firebase:DatabaseUrl não foi configurado.");
        }

        // Cache curto por URL evita baixar os mesmos ramos Firebase em atualizações muito próximas.
        if (TentarObterDoCache(url, out var valorCache))
        {
            return Clonar(valorCache);
        }

        var valor = await ObterComRepeticaoAsync(url, tokenCancelamento);
        GuardarNoCache(url, valor);
        return Clonar(valor);
    }

    private string MontarUrl(string caminho, string? parametrosConsulta)
    {
        var urlBase = _opcoes.DatabaseUrl.TrimEnd('/');
        var caminhoNormalizado = caminho.Trim('/').Replace(" ", "%20", StringComparison.Ordinal);
        var consulta = string.IsNullOrWhiteSpace(parametrosConsulta) ? "" : $"?{parametrosConsulta.TrimStart('?')}";
        return $"{urlBase}/{caminhoNormalizado}.json{consulta}";
    }

    private async Task<JsonElement?> ObterComRepeticaoAsync(string url, CancellationToken tokenCancelamento)
    {
        var tentativas = Math.Max(1, _opcoes.MaxRetryAttempts);
        Exception? ultimoErro = null;

        // Retry simples cobre indisponibilidade transitória, rate limit e timeouts não causados pelo usuário.
        for (var tentativa = 1; tentativa <= tentativas; tentativa++)
        {
            try
            {
                using var resposta = await _clienteHttp.GetAsync(url, tokenCancelamento);
                if (DeveTentarNovamente(resposta.StatusCode) && tentativa < tentativas)
                {
                    // Aguarda antes de repetir para não pressionar o Firebase quando ele sinaliza instabilidade.
                    await AguardarIntervaloTentativaAsync(tentativa, tokenCancelamento);
                    continue;
                }

                resposta.EnsureSuccessStatusCode();

                await using var fluxo = await resposta.Content.ReadAsStreamAsync(tokenCancelamento);
                var documento = await JsonDocument.ParseAsync(fluxo, cancellationToken: tokenCancelamento);

                return documento.RootElement.ValueKind == JsonValueKind.Null
                    ? null
                    : documento.RootElement.Clone();
            }
            catch (Exception erro) when (DeveTentarNovamente(erro, tokenCancelamento) && tentativa < tentativas)
            {
                ultimoErro = erro;
                await AguardarIntervaloTentativaAsync(tentativa, tokenCancelamento);
            }
        }

        throw ultimoErro ?? new InvalidOperationException("Falha ao consultar Firebase.");
    }

    private bool TentarObterDoCache(string url, out JsonElement? valor)
    {
        valor = null;
        if (_opcoes.CacheDurationSeconds <= 0) return false;

        if (!EntradasCache.TryGetValue(url, out var entrada)) return false;
        if (entrada.ExpiraEm <= DateTimeOffset.UtcNow)
        {
            // Remove entradas vencidas para manter o dicionário pequeno durante longas sessões.
            EntradasCache.TryRemove(url, out _);
            return false;
        }

        valor = entrada.Valor;
        return true;
    }

    private void GuardarNoCache(string url, JsonElement? valor)
    {
        if (_opcoes.CacheDurationSeconds <= 0) return;

        EntradasCache[url] = new EntradaCache(
            valor,
            DateTimeOffset.UtcNow.AddSeconds(_opcoes.CacheDurationSeconds));
    }

    private static JsonElement? Clonar(JsonElement? valor)
    {
        // JsonElement é ligado ao documento de origem; clones evitam uso acidental de instâncias descartadas.
        return valor is null
            ? null
            : valor.Value.Clone();
    }

    private static bool DeveTentarNovamente(System.Net.HttpStatusCode codigoStatus)
    {
        var codigo = (int)codigoStatus;
        return codigoStatus is System.Net.HttpStatusCode.RequestTimeout or (System.Net.HttpStatusCode)429 || codigo >= 500;
    }

    private static bool DeveTentarNovamente(Exception erro, CancellationToken tokenCancelamento)
    {
        return erro is HttpRequestException || erro is TaskCanceledException && !tokenCancelamento.IsCancellationRequested;
    }

    private static Task AguardarIntervaloTentativaAsync(int tentativa, CancellationToken tokenCancelamento)
    {
        var atrasoMs = tentativa switch
        {
            1 => 200,
            2 => 500,
            _ => 1000
        };

        return Task.Delay(atrasoMs, tokenCancelamento);
    }

    private sealed record EntradaCache(JsonElement? Valor, DateTimeOffset ExpiraEm);
}
