using EstacaoClimatica.Application.Abstractions;
using EstacaoClimatica.Domain;
using Microsoft.AspNetCore.Mvc;

namespace EstacaoClimatica.Web.Controllers;

[ApiController]
[Route("api")]
public sealed class ClimateQueryController : ControllerBase
{
    private readonly IServicoConsultasClimaticas _servico;

    public ClimateQueryController(IServicoConsultasClimaticas servico)
    {
        _servico = servico;
    }

    [HttpGet("environments")]
    public IActionResult ObterAmbientes() => Ok(_servico.ObterAmbientes());

    [HttpGet("environments/{environment}/latest")]
    public Task<IActionResult> ObterUltimaMedicao(
        string environment,
        CancellationToken tokenCancelamento) =>
        ExecutarAsync(() => _servico.ObterUltimaMedicaoAsync(environment, tokenCancelamento));

    [HttpGet("environments/{environment}/measurements")]
    public Task<IActionResult> ObterMedicoes(
        string environment,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? metrics,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken tokenCancelamento = default) =>
        ExecutarAsync(() => _servico.ObterMedicoesAsync(
            environment,
            from,
            to,
            metrics,
            page,
            pageSize,
            tokenCancelamento));

    [HttpGet("metrics/{metric}/series")]
    public Task<IActionResult> ObterSerie(
        string metric,
        [FromQuery] string? date,
        [FromQuery] string? environments,
        CancellationToken tokenCancelamento) =>
        ExecutarAsync(() => _servico.ObterSerieAsync(metric, date, environments, tokenCancelamento));

    [HttpGet("dashboard/summary")]
    public Task<IActionResult> ObterResumo(
        [FromQuery] string? date,
        CancellationToken tokenCancelamento) =>
        ExecutarAsync(() => _servico.ObterResumoAsync(date, tokenCancelamento));

    [HttpGet("air-quality")]
    public Task<IActionResult> ObterQualidadeAr(
        [FromQuery] string? date,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken tokenCancelamento) =>
        ExecutarAsync(() => _servico.ObterQualidadeArAsync(date, from, to, tokenCancelamento));

    [HttpGet("solar")]
    public Task<IActionResult> ObterSolar(
        [FromQuery] string? date,
        CancellationToken tokenCancelamento) =>
        ExecutarAsync(() => _servico.ObterSolarAsync(date, tokenCancelamento));

    [HttpGet("alerts")]
    public Task<IActionResult> ObterAlertas(
        [FromQuery] string? date,
        [FromQuery] string? severity,
        [FromQuery] string? environment,
        CancellationToken tokenCancelamento) =>
        ExecutarAsync(() => _servico.ObterAlertasAsync(date, severity, environment, tokenCancelamento));

    [HttpPost("comparisons")]
    public Task<IActionResult> Comparar(
        [FromBody] ComparisonRequest requisicao,
        CancellationToken tokenCancelamento) =>
        ExecutarAsync(() => _servico.CompararAsync(requisicao, tokenCancelamento));

    [HttpGet("exports/{format}")]
    public async Task<IActionResult> Exportar(
        string format,
        [FromQuery] string? date,
        [FromQuery] string? tab,
        CancellationToken tokenCancelamento)
    {
        try
        {
            var arquivo = await _servico.ExportarAsync(format, date, tab, tokenCancelamento);
            return File(arquivo.Content, arquivo.ContentType, arquivo.FileName);
        }
        catch (ArgumentException erro)
        {
            return BadRequest(new { message = erro.Message });
        }
        catch (KeyNotFoundException erro)
        {
            return NotFound(new { message = erro.Message });
        }
    }

    private async Task<IActionResult> ExecutarAsync<T>(Func<Task<T>> acao)
    {
        try
        {
            return Ok(await acao());
        }
        catch (ArgumentException erro)
        {
            return BadRequest(new { message = erro.Message });
        }
        catch (KeyNotFoundException erro)
        {
            return NotFound(new { message = erro.Message });
        }
    }
}
