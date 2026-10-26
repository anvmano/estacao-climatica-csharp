using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using EstacaoClimatica.Application.Abstractions;
using EstacaoClimatica.Application.Configuration;

namespace EstacaoClimatica.Infrastructure.Firebase;

public sealed class ObservadorAtualizacoesFirebase(HttpClient cliente, OpcoesFirebase opcoes) : IObservadorAtualizacoesFirebase
{
    public async IAsyncEnumerable<AtualizacaoFirebase> ObservarAsync([EnumeratorCancellation] CancellationToken tokenCancelamento)
    {
        if (!Uri.TryCreate(opcoes.DatabaseUrl, UriKind.Absolute, out var endereco) || endereco.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("Firebase:DatabaseUrl nao foi configurado corretamente.");
        var canal = Channel.CreateBounded<AtualizacaoFirebase>(new BoundedChannelOptions(32) { FullMode = BoundedChannelFullMode.DropOldest });
        using var controle = CancellationTokenSource.CreateLinkedTokenSource(tokenCancelamento);
        var caminhos = new Dictionary<string, string>
        {
            ["sala"] = opcoes.Paths.LivingRoom, ["quarto"] = opcoes.Paths.Room,
            ["aquario"] = opcoes.Paths.Aquarium, ["solar"] = opcoes.Paths.Solar
        };
        var tarefas = caminhos.Select(item => EscutarRamo(item.Key, item.Value, canal.Writer, controle.Token)).ToArray();
        try
        {
            await foreach (var atualizacao in canal.Reader.ReadAllAsync(tokenCancelamento)) yield return atualizacao;
        }
        finally
        {
            await controle.CancelAsync();
            try { await Task.WhenAll(tarefas); }
            catch (OperationCanceledException) when (controle.IsCancellationRequested) { }
        }
    }

    private async Task EscutarRamo(string ramo, string caminho, ChannelWriter<AtualizacaoFirebase> destino, CancellationToken token)
    {
        var url = $"{opcoes.DatabaseUrl.TrimEnd('/')}/{caminho.Trim('/')}.json";
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var pedido = new HttpRequestMessage(HttpMethod.Get, url);
                pedido.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                using var resposta = await cliente.SendAsync(pedido, HttpCompletionOption.ResponseHeadersRead, token);
                resposta.EnsureSuccessStatusCode();
                using var leitor = new StreamReader(await resposta.Content.ReadAsStreamAsync(token));
                string? evento = null;
                while (await leitor.ReadLineAsync(token) is { } linha)
                {
                    if (linha.StartsWith("event:", StringComparison.Ordinal)) evento = linha[6..].Trim();
                    if (linha.Length != 0) continue;
                    if (evento is "put" or "patch")
                    {
                        FonteDadosFirebase.InvalidarRamo(opcoes.DatabaseUrl, caminho);
                        await destino.WriteAsync(new(ramo, false), token);
                    }
                    else if (evento is "cancel" or "auth_revoked") throw new HttpRequestException("Stream Firebase indisponivel.");
                    evento = null;
                }
                throw new HttpRequestException("Stream Firebase encerrado.");
            }
            catch (Exception erro) when (!token.IsCancellationRequested && erro is HttpRequestException or IOException or TaskCanceledException)
            {
                await destino.WriteAsync(new(ramo, true), token);
                await Task.Delay(TimeSpan.FromSeconds(5), token);
            }
        }
    }
}
