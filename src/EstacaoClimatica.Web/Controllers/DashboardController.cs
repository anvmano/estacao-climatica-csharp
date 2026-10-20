using EstacaoClimatica.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace EstacaoClimatica.Web.Controllers;

[ApiController]
[Route("api")]
public sealed class DashboardController : ControllerBase
{
    private readonly IServicoDashboardClimatico _servico;

    public DashboardController(IServicoDashboardClimatico servico)
    {
        _servico = servico;
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> ObterDashboard([FromQuery] string? date, CancellationToken tokenCancelamento)
    {
        // A data chega no formato usado pelo Firebase; se vier vazia, a camada Application aplica o padrão do dia atual.
        var dashboard = await _servico.ObterDashboardAsync(date, tokenCancelamento);
        return Ok(dashboard);
    }

    [HttpGet("health")]
    public IActionResult ObterSaude()
    {
        // Endpoint simples para verificar se o host ASP.NET está respondendo sem depender do Firebase.
        return Ok(new { status = "ok" });
    }
}
