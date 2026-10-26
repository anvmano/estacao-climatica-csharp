using System.Text.Json;
using EstacaoClimatica.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace EstacaoClimatica.Web.Controllers;

[ApiController]
[Route("api/dashboard/updates")]
public sealed class DashboardUpdatesController(IObservadorAtualizacoesFirebase observador) : ControllerBase
{
    [HttpGet]
    public async Task Observar(CancellationToken cancelamento)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Accel-Buffering"] = "no";
        await Response.StartAsync(cancelamento);
        // Renova a autorizacao periodicamente sem manter uma conexao autenticada indefinidamente.
        using var sessao = CancellationTokenSource.CreateLinkedTokenSource(cancelamento);
        sessao.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            await foreach (var atualizacao in observador.ObservarAsync(sessao.Token))
            {
                var dados = JsonSerializer.Serialize(new { branch = atualizacao.Ramo, failed = atualizacao.Falhou });
                await Response.WriteAsync($"data: {dados}\n\n", sessao.Token);
                await Response.Body.FlushAsync(sessao.Token);
            }
        }
        catch (OperationCanceledException) when (sessao.IsCancellationRequested) { }
    }
}
