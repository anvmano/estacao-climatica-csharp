using EstacaoClimatica.Application.Abstractions;
using EstacaoClimatica.Application.Configuration;
using EstacaoClimatica.Application.Services;
using EstacaoClimatica.Infrastructure.AI;
using EstacaoClimatica.Infrastructure.Authentication;
using EstacaoClimatica.Infrastructure.External;
using EstacaoClimatica.Infrastructure.Firebase;
using EstacaoClimatica.Infrastructure.Repositories;

var construtor = WebApplication.CreateBuilder(args);

construtor.Services.AddControllers();

// Configurações vêm do appsettings para manter URL/caminhos Firebase e paleta fora do código.
construtor.Services.AddSingleton(construtor.Configuration.GetSection("Firebase").Get<OpcoesFirebase>() ?? new OpcoesFirebase());
construtor.Services.AddSingleton(construtor.Configuration.GetSection("Colors").Get<CoresAplicacao>() ?? new CoresAplicacao());
construtor.Services.AddSingleton(construtor.Configuration.GetSection("ArtificialIntelligence").Get<OpcoesIa>() ?? new OpcoesIa());
construtor.Services.AddSingleton(construtor.Configuration.GetSection("PublicWeather").Get<OpcoesClimaPublico>() ?? new OpcoesClimaPublico());
construtor.Services.AddSingleton(construtor.Configuration.GetSection("InternalAccess").Get<OpcoesAcessoInterno>() ?? new OpcoesAcessoInterno());

// O controller conhece apenas o serviço; leitura externa fica isolada no repository/data source.
construtor.Services.AddScoped<IServicoDashboardClimatico, ServicoDashboardClimatico>();
construtor.Services.AddScoped<IServicoConsultasClimaticas, ServicoConsultasClimaticas>();
construtor.Services.AddScoped<IServicoAssistenteClimatico, ServicoAssistenteClimatico>();
construtor.Services.AddSingleton<ServicoAnalisesAmbientais>();
construtor.Services.AddScoped<IRepositorioDadosClimaticos, RepositorioDadosClimaticosFirebase>();
construtor.Services.AddHttpClient<IFonteDadosFirebase, FonteDadosFirebase>();
construtor.Services.AddHttpClient<IClienteGeracaoTexto, ClienteGeracaoTextoGemini>();
construtor.Services.AddHttpClient<IServicoClimaPublico, ServicoClimaPublico>();
construtor.Services.AddHttpClient<IVerificadorTokenAcessoInterno, VerificadorTokenAcessoFirebase>();

var aplicacao = construtor.Build();

aplicacao.UseDefaultFiles();
aplicacao.UseStaticFiles();

aplicacao.Use(async (contexto, proximo) =>
{
    var opcoes = contexto.RequestServices.GetRequiredService<OpcoesAcessoInterno>();
    var caminhoApi = contexto.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);
    var caminhoPublico = contexto.Request.Path.Equals("/api/health", StringComparison.OrdinalIgnoreCase)
        || contexto.Request.Path.StartsWithSegments("/api/public-weather", StringComparison.OrdinalIgnoreCase);
    var caminhoProtegido = caminhoApi && !caminhoPublico;
    if (!opcoes.Enforce || !caminhoProtegido)
    {
        await proximo();
        return;
    }

    var autorizacao = contexto.Request.Headers.Authorization.ToString();
    var token = autorizacao.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? autorizacao["Bearer ".Length..].Trim()
        : string.Empty;
    var verificador = contexto.RequestServices.GetRequiredService<IVerificadorTokenAcessoInterno>();
    var email = await verificador.ObterEmailAsync(token, contexto.RequestAborted);
    var autorizado = opcoes.AuthorizedEmails.Any(item => string.Equals(item.Trim(), email, StringComparison.OrdinalIgnoreCase));
    if (!autorizado)
    {
        contexto.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await contexto.Response.WriteAsJsonAsync(new { message = "Acesso interno não autorizado." }, contexto.RequestAborted);
        return;
    }

    await proximo();
});

aplicacao.MapControllers();

aplicacao.Run();
