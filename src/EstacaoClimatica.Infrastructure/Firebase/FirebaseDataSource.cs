using System.Text.Json;
using System.Collections.Concurrent;
using EstacaoClimatica.Application.Abstractions;
using EstacaoClimatica.Application.Configuration;

namespace EstacaoClimatica.Infrastructure.Firebase;

public sealed class FonteDadosFirebase : IFonteDadosFirebase
{
    private static readonly ConcurrentDictionary<string, EntradaCache> EntradasCache = new();
    private static readonly SemaphoreSlim LimiteConsultas = new(4, 4);
    private static readonly SemaphoreSlim[] TravasUrl = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private static long _geracaoCache;

    public static void InvalidarRamo(string urlBase, string caminho)
    {
        Interlocked.Increment(ref _geracaoCache);
        var prefixo = $"{urlBase.TrimEnd('/')}/{caminho.Trim('/').Replace(" ", "%20", StringComparison.Ordinal)}";
        foreach (var chave in EntradasCache.Keys)
            if (chave.StartsWith(prefixo + "/", StringComparison.Ordinal) || chave.StartsWith(prefixo + ".json", StringComparison.Ordinal))
                EntradasCache.TryRemove(chave, out _);
    }
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
            return valorCache;
        }

        // Travas fixas evitam um dicionario ilimitado de consultas e compartilham o cache entre pedidos simultaneos.
        var trava = TravasUrl[(uint)StringComparer.Ordinal.GetHashCode(url) % (uint)TravasUrl.Length];
        await trava.WaitAsync(tokenCancelamento);
        try
        {
            if (TentarObterDoCache(url, out valorCache)) return valorCache;
            await LimiteConsultas.WaitAsync(tokenCancelamento);
            try
            {
                var geracao = Interlocked.Read(ref _geracaoCache);
                var valor = await ObterComRepeticaoAsync(url, tokenCancelamento);
                if (geracao == Interlocked.Read(ref _geracaoCache)) GuardarNoCache(url, valor);
                return valor;
            }
            finally { LimiteConsultas.Release(); }
        }
        finally { trava.Release(); }
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
                using var documento = await JsonDocument.ParseAsync(fluxo, cancellationToken: tokenCancelamento);

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

        var agora = DateTimeOffset.UtcNow;
        foreach (var entrada in EntradasCache)
            if (entrada.Value.ExpiraEm <= agora) EntradasCache.TryRemove(entrada.Key, out _);
        // Limita historicos de URLs nunca revisitadas, inclusive em processos de longa duracao.
        foreach (var chave in EntradasCache.OrderBy(item => item.Value.ExpiraEm).Take(Math.Max(0, EntradasCache.Count - 255)).Select(item => item.Key))
            EntradasCache.TryRemove(chave, out _);

        EntradasCache[url] = new EntradaCache(
            valor,
            DateTimeOffset.UtcNow.AddSeconds(_opcoes.CacheDurationSeconds));
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
