using EstacaoClimatica.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

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
        if (date is not null && !DateOnly.TryParseExact(date, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return BadRequest(new { message = "Data inválida. Use o formato dd-MM-yyyy." });

        var dashboard = await _servico.ObterDashboardAsync(date, tokenCancelamento);
        if (HttpContext is not null) Response.Headers["X-Climate-Updates"] = "enabled";
        return Ok(dashboard);
    }

    [HttpGet("health")]
    public IActionResult ObterSaude()
    {
        // Endpoint simples para verificar se o host ASP.NET está respondendo sem depender do Firebase.
        return Ok(new { status = "ok" });
    }
}
