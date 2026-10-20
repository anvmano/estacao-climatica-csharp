using System.Net;
using System.Text;
using EstacaoClimatica.Application.Configuration;
using EstacaoClimatica.Infrastructure.AI;

namespace EstacaoClimatica.Tests;

public sealed class TestesClienteGeracaoTextoGemini
{
    [Fact]
    public async Task GerarTextoAsync_DeveRejeitarQuandoIaEstaDesativada()
    {
        var manipulador = new ManipuladorCaptura(HttpStatusCode.OK, "{}");
        var cliente = new ClienteGeracaoTextoGemini(new HttpClient(manipulador), new OpcoesIa { Enabled = false });

        var erro = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cliente.GerarTextoAsync("Pergunta", CancellationToken.None));

        Assert.Equal("IA desativada.", erro.Message);
        Assert.Equal(0, manipulador.Requisicoes);
    }

    [Fact]
    public async Task GerarTextoAsync_DeveExigirChaveQuandoIaEstaAtiva()
    {
        var cliente = new ClienteGeracaoTextoGemini(new HttpClient(new ManipuladorCaptura(HttpStatusCode.OK, "{}")), new OpcoesIa { Enabled = true });

        var erro = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cliente.GerarTextoAsync("Pergunta", CancellationToken.None));

        Assert.Equal("Chave da IA não configurada.", erro.Message);
    }

    [Fact]
    public async Task GerarTextoAsync_DeveEnviarPromptEExtrairPrimeiroTexto()
    {
        var manipulador = new ManipuladorCaptura(HttpStatusCode.OK, """
        {"candidates":[{"content":{"parts":[{"text":""},{"text":"Resposta Gemini para 03/06/2026."}]}}]}
        """);
        var opcoes = new OpcoesIa
        {
            Enabled = true,
            ApiKey = "chave teste",
            Model = "modelo teste",
            Endpoint = "https://gemini.test/v1beta/"
        };
        var cliente = new ClienteGeracaoTextoGemini(new HttpClient(manipulador), opcoes);

        var resposta = await cliente.GerarTextoAsync("Resuma 03/06/2026", CancellationToken.None);

        Assert.Equal("Resposta Gemini para 03/06/2026.", resposta);
        Assert.Contains("/models/modelo%20teste:generateContent?key=chave%20teste", manipulador.Url);
        Assert.Contains("Resuma 03/06/2026", manipulador.Corpo);
    }

    [Fact]
    public async Task GerarTextoAsync_DevePropagarStatusHttpDeErro()
    {
        var manipulador = new ManipuladorCaptura(HttpStatusCode.TooManyRequests, "{}");
        var cliente = new ClienteGeracaoTextoGemini(
            new HttpClient(manipulador),
            new OpcoesIa { Enabled = true, ApiKey = "chave" });

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            cliente.GerarTextoAsync("Pergunta", CancellationToken.None));
    }

    private sealed class ManipuladorCaptura(HttpStatusCode status, string corpoResposta) : HttpMessageHandler
    {
        public int Requisicoes { get; private set; }
        public string Url { get; private set; } = "";
        public string Corpo { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage requisicao, CancellationToken tokenCancelamento)
        {
            Requisicoes++;
            Url = requisicao.RequestUri!.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped);
            Corpo = requisicao.Content is null ? "" : await requisicao.Content.ReadAsStringAsync(tokenCancelamento);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(corpoResposta, Encoding.UTF8, "application/json")
            };
        }
    }
}
