using EstacaoClimatica.Application.Abstractions;
using EstacaoClimatica.Domain;
using Microsoft.AspNetCore.Mvc;

namespace EstacaoClimatica.Web.Controllers;

[ApiController]
[Route("api/chat")]
public sealed class ChatController : ControllerBase
{
    private readonly IServicoAssistenteClimatico _servicoAssistente;

    public ChatController(IServicoAssistenteClimatico servicoAssistente)
    {
        _servicoAssistente = servicoAssistente;
    }

    [HttpPost]
    public async Task<ActionResult<ChatResponse>> Post(ChatRequest requisicao, CancellationToken tokenCancelamento)
    {
        if (string.IsNullOrWhiteSpace(requisicao.Question))
        {
            return BadRequest(new { message = "Pergunta obrigatória." });
        }

        return Ok(await _servicoAssistente.ResponderAsync(requisicao, tokenCancelamento));
    }
}
