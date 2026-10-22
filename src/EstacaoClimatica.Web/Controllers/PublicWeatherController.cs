using EstacaoClimatica.Application.Abstractions;
using EstacaoClimatica.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json;

namespace EstacaoClimatica.Web.Controllers;

[ApiController]
[Route("api/public-weather")]
[EnableRateLimiting("clima-publico")]
public sealed class PublicWeatherController : ControllerBase
{
    private readonly IServicoClimaPublico _servico;

    public PublicWeatherController(IServicoClimaPublico servico)
    {
        _servico = servico;
    }

    [HttpGet("cep/{cep}")]
    public Task<ActionResult<PublicWeatherData>> BuscarPorCep(string cep, CancellationToken tokenCancelamento)
    {
        return ExecutarAsync(() => _servico.BuscarPorCepAsync(cep, tokenCancelamento));
    }

    [HttpGet("cities")]
    public Task<ActionResult<IReadOnlyList<CidadePublica>>> PesquisarCidades(
        [FromQuery(Name = "term")] string termo,
        CancellationToken tokenCancelamento)
    {
        return ExecutarAsync(() => _servico.PesquisarCidadesAsync(termo, tokenCancelamento));
    }

    [HttpPost("city")]
    public Task<ActionResult<PublicWeatherData>> BuscarPorCidade(
        [FromBody] CidadePublica cidade,
        CancellationToken tokenCancelamento)
    {
        return ExecutarAsync(() => _servico.BuscarPorCidadeAsync(cidade, tokenCancelamento));
    }

    [HttpGet("location")]
    public Task<ActionResult<PublicWeatherData>> BuscarPorLocalizacao(
        [FromQuery] double latitude,
        [FromQuery] double longitude,
        [FromQuery] double? precisao,
        CancellationToken tokenCancelamento)
    {
        return ExecutarAsync(() => _servico.BuscarPorCoordenadasAsync(
            latitude,
            longitude,
            "localizacao",
            "Localização atual",
            null,
            precisao,
            tokenCancelamento));
    }

    private async Task<ActionResult<T>> ExecutarAsync<T>(Func<Task<T>> acao)
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
        catch (HttpRequestException erro)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = erro.Message });
        }
        catch (JsonException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "Resposta inválida do serviço externo." });
        }
    }
}
