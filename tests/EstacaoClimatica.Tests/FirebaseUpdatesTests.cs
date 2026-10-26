using System.Net;
using EstacaoClimatica.Application.Configuration;
using EstacaoClimatica.Infrastructure.Firebase;

namespace EstacaoClimatica.Tests;

public sealed class TestesAtualizacoesFirebase
{
    [Fact]
    public async Task Eventos_DeveNotificarQuatroRamosSemExporMedicoesEEncerrarAoCancelar()
    {
        using var cliente = new HttpClient(new ManipuladorEventos());
        var observador = new ObservadorAtualizacoesFirebase(cliente, new OpcoesFirebase { DatabaseUrl = "https://firebase.test" });
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var ramos = new HashSet<string>();
        await foreach (var evento in observador.ObservarAsync(limite.Token))
        {
            if (!evento.Falhou) ramos.Add(evento.Ramo);
            if (ramos.Count == 4) break;
        }
        Assert.Equal(new[] { "aquario", "quarto", "sala", "solar" }, ramos.Order());
        limite.Cancel();
        await using var fluxo = observador.ObservarAsync(limite.Token).GetAsyncEnumerator();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await fluxo.MoveNextAsync());
    }

    [Fact]
    public async Task FalhaDeUmStream_DeveSerSinalizadaSemBloquearDemaisRamos()
    {
        using var cliente = new HttpClient(new ManipuladorEventos(falharSala: true));
        var observador = new ObservadorAtualizacoesFirebase(cliente, new OpcoesFirebase { DatabaseUrl = "https://firebase.test" });
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var salaFalhou = false;
        var quartoAtualizou = false;
        await foreach (var evento in observador.ObservarAsync(limite.Token))
        {
            salaFalhou |= evento is { Ramo: "sala", Falhou: true };
            quartoAtualizou |= evento is { Ramo: "quarto", Falhou: false };
            if (salaFalhou && quartoAtualizou) break;
        }
        Assert.True(salaFalhou && quartoAtualizou);
    }

    private sealed class ManipuladorEventos(bool falharSala = false) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Assert.Contains(pedido.Headers.Accept, item => item.MediaType == "text/event-stream");
            return Task.FromResult(falharSala && pedido.RequestUri!.AbsolutePath.Contains("AirQuality")
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("event: keep-alive\ndata: null\n\nevent: patch\ndata: {\"path\":\"03-06-2026\",\"data\":{}}\n\n")
                });
        }
    }
}
