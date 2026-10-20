using System.Net;
using System.Text;
using EstacaoClimatica.Application.Configuration;
using EstacaoClimatica.Infrastructure.Authentication;

namespace EstacaoClimatica.Tests;

public sealed class TestesVerificadorTokenAcessoFirebase
{
    [Fact]
    public async Task ObterEmailAsync_DeveNormalizarEmailRetornadoPeloFirebase()
    {
        var manipulador = new ManipuladorMensagemHttpSimulado(HttpStatusCode.OK, """{"users":[{"email":" USUARIO@EXAMPLE.COM "}]}""");
        var verificador = new VerificadorTokenAcessoFirebase(new HttpClient(manipulador), new OpcoesAcessoInterno());

        var email = await verificador.ObterEmailAsync("token-valido", CancellationToken.None);

        Assert.Equal("usuario@example.com", email);
        Assert.Contains("accounts:lookup", manipulador.UltimaUrl);
    }

    [Fact]
    public async Task ObterEmailAsync_DeveRejeitarTokenInvalido()
    {
        var manipulador = new ManipuladorMensagemHttpSimulado(HttpStatusCode.BadRequest, "{} ");
        var verificador = new VerificadorTokenAcessoFirebase(new HttpClient(manipulador), new OpcoesAcessoInterno());

        Assert.Null(await verificador.ObterEmailAsync("token-invalido", CancellationToken.None));
    }

    [Fact]
    public async Task ObterEmailAsync_DeveUsarCacheParaMesmoToken()
    {
        var manipulador = new ManipuladorMensagemHttpSimulado(HttpStatusCode.OK, """{"users":[{"email":"usuario@example.com"}]}""");
        var verificador = new VerificadorTokenAcessoFirebase(new HttpClient(manipulador), new OpcoesAcessoInterno());

        var primeiro = await verificador.ObterEmailAsync("token-cache", CancellationToken.None);
        var segundo = await verificador.ObterEmailAsync("token-cache", CancellationToken.None);

        Assert.Equal("usuario@example.com", primeiro);
        Assert.Equal(primeiro, segundo);
        Assert.Equal(1, manipulador.QuantidadeRequisicoes);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"users\":[]}")]
    [InlineData("{\"users\":[{}]}")]
    [InlineData("{\"users\":[{\"email\":\"  \"}]}")]
    public async Task ObterEmailAsync_DeveRejeitarRespostaSemEmail(string corpo)
    {
        var manipulador = new ManipuladorMensagemHttpSimulado(HttpStatusCode.OK, corpo);
        var verificador = new VerificadorTokenAcessoFirebase(new HttpClient(manipulador), new OpcoesAcessoInterno());

        Assert.Null(await verificador.ObterEmailAsync("token-sem-email", CancellationToken.None));
    }

    private sealed class ManipuladorMensagemHttpSimulado(HttpStatusCode codigoStatus, string corpo) : HttpMessageHandler
    {
        public string UltimaUrl { get; private set; } = string.Empty;
        public int QuantidadeRequisicoes { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage requisicao, CancellationToken tokenCancelamento)
        {
            UltimaUrl = requisicao.RequestUri?.ToString() ?? string.Empty;
            QuantidadeRequisicoes++;
            return Task.FromResult(new HttpResponseMessage(codigoStatus)
            {
                Content = new StringContent(corpo, Encoding.UTF8, "application/json")
            });
        }
    }
}
