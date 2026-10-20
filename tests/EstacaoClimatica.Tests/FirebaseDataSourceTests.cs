using System.Net;
using System.Text;
using EstacaoClimatica.Application.Configuration;
using EstacaoClimatica.Infrastructure.Firebase;

namespace EstacaoClimatica.Tests;

public sealed class TestesFonteDadosFirebase
{
    [Fact]
    public async Task ObterCaminhoAsync_DeveExigirDatabaseUrl()
    {
        using var cliente = new HttpClient(new ManipuladorSequencial([]));
        var fonte = new FonteDadosFirebase(cliente, new OpcoesFirebase());

        var erro = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fonte.ObterCaminhoAsync("historico/Temperatura", CancellationToken.None));

        Assert.Equal("Firebase:DatabaseUrl não foi configurado.", erro.Message);
    }

    [Fact]
    public async Task ObterCaminhoComQueryAsync_DeveNormalizarUrlEQuery()
    {
        var manipulador = new ManipuladorSequencial([CriarJson(HttpStatusCode.OK, "{\"03-06-2026\":true}")]);
        using var cliente = new HttpClient(manipulador);
        var fonte = new FonteDadosFirebase(cliente, Opcoes(cache: 0));

        var resultado = await fonte.ObterCaminhoComQueryAsync("/historico/Com Espaco/", "?shallow=true", CancellationToken.None);

        Assert.True(resultado?.GetProperty("03-06-2026").GetBoolean());
        Assert.Equal("https://firebase.test/historico/Com%20Espaco.json?shallow=true", manipulador.Urls.Single());
    }

    [Fact]
    public async Task ObterCaminhoAsync_DeveUsarCachePorUrl()
    {
        var identificador = Guid.NewGuid().ToString("N");
        var manipulador = new ManipuladorSequencial([CriarJson(HttpStatusCode.OK, "{\"valor\":42}")]);
        using var cliente = new HttpClient(manipulador);
        var fonte = new FonteDadosFirebase(cliente, Opcoes(cache: 60));

        var primeiro = await fonte.ObterCaminhoAsync($"teste/{identificador}", CancellationToken.None);
        var segundo = await fonte.ObterCaminhoAsync($"teste/{identificador}", CancellationToken.None);

        Assert.Equal(42, primeiro?.GetProperty("valor").GetInt32());
        Assert.Equal(42, segundo?.GetProperty("valor").GetInt32());
        Assert.Single(manipulador.Urls);
    }

    [Fact]
    public async Task ObterCaminhoAsync_DeveRepetirAposErroTransitorio()
    {
        var manipulador = new ManipuladorSequencial([
            CriarJson(HttpStatusCode.ServiceUnavailable, "{}"),
            CriarJson(HttpStatusCode.OK, "{\"data\":\"03-06-2026\"}")
        ]);
        using var cliente = new HttpClient(manipulador);
        var fonte = new FonteDadosFirebase(cliente, Opcoes(cache: 0, tentativas: 2));

        var resultado = await fonte.ObterCaminhoAsync("historico/Temperatura", CancellationToken.None);

        Assert.Equal("03-06-2026", resultado?.GetProperty("data").GetString());
        Assert.Equal(2, manipulador.Urls.Count);
    }

    [Fact]
    public async Task ObterCaminhoAsync_DeveRetornarNuloParaJsonNull()
    {
        var manipulador = new ManipuladorSequencial([CriarJson(HttpStatusCode.OK, "null")]);
        using var cliente = new HttpClient(manipulador);
        var fonte = new FonteDadosFirebase(cliente, Opcoes(cache: 0));

        Assert.Null(await fonte.ObterCaminhoAsync("historico/Aquario", CancellationToken.None));
    }

    private static OpcoesFirebase Opcoes(int cache, int tentativas = 1) => new()
    {
        DatabaseUrl = "https://firebase.test/",
        CacheDurationSeconds = cache,
        MaxRetryAttempts = tentativas
    };

    private static HttpResponseMessage CriarJson(HttpStatusCode status, string corpo) => new(status)
    {
        Content = new StringContent(corpo, Encoding.UTF8, "application/json")
    };

    private sealed class ManipuladorSequencial(IEnumerable<HttpResponseMessage> respostas) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _respostas = new(respostas);
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage requisicao, CancellationToken tokenCancelamento)
        {
            Urls.Add(requisicao.RequestUri!.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped));
            return Task.FromResult(_respostas.Dequeue());
        }
    }
}
