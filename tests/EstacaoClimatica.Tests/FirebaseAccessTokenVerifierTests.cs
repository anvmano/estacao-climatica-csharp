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

    [Fact]
    public async Task ObterEmailAsync_DeveRejeitarJwtExpiradoMesmoComRespostaRemotaPositiva()
    {
        var manipulador = new ManipuladorMensagemHttpSimulado(HttpStatusCode.OK, """{"users":[{"email":"usuario@example.com"}]}""");
        var verificador = new VerificadorTokenAcessoFirebase(new HttpClient(manipulador), new OpcoesAcessoInterno());
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"exp":1}""")).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.Null(await verificador.ObterEmailAsync($"header.{payload}.signature", CancellationToken.None));
        Assert.Equal(0, manipulador.QuantidadeRequisicoes);
    }

    [Fact]
    public async Task ObterEmailAsync_JwtMalformadoNaoDeveCausarErroAntesDaVerificacaoRemota()
    {
        var manipulador = new ManipuladorMensagemHttpSimulado(HttpStatusCode.BadRequest, "{}");
        var verificador = new VerificadorTokenAcessoFirebase(new HttpClient(manipulador), new OpcoesAcessoInterno());
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("[]")).TrimEnd('=');

        Assert.Null(await verificador.ObterEmailAsync($"header.{payload}.signature", CancellationToken.None));
        Assert.Equal(1, manipulador.QuantidadeRequisicoes);
    }

    [Fact]
    public async Task ObterEmailAsync_DeveRejeitarTokenRevogadoPeloValidSince()
    {
        var agora = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var manipulador = new ManipuladorMensagemHttpSimulado(HttpStatusCode.OK,
            $$"""{"users":[{"email":"usuario@example.com","validSince":"{{agora - 30}}"}]}""");
        var verificador = new VerificadorTokenAcessoFirebase(new HttpClient(manipulador), new OpcoesAcessoInterno());

        Assert.Null(await verificador.ObterEmailAsync(CriarJwt(agora - 60, agora + 3600), CancellationToken.None));
        Assert.Equal("usuario@example.com", await verificador.ObterEmailAsync(CriarJwt(agora - 20, agora + 3600), CancellationToken.None));
        Assert.Equal(2, manipulador.QuantidadeRequisicoes);
    }

    private static string CriarJwt(long autenticadoEm, long expiraEm)
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $$"""{"auth_time":{{autenticadoEm}},"exp":{{expiraEm}}}"""))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"header.{payload}.signature";
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
