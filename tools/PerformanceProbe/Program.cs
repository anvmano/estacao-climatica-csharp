using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using EstacaoClimatica.Application.Abstractions;
using EstacaoClimatica.Application.Configuration;
using EstacaoClimatica.Application.Services;
using EstacaoClimatica.Domain;
using EstacaoClimatica.Infrastructure.Firebase;
using EstacaoClimatica.Infrastructure.Authentication;
using EstacaoClimatica.Infrastructure.Repositories;

if (args.Length != 1) throw new ArgumentException("Informe o caminho do export RTDB como único argumento.");
using var fixture = JsonDocument.Parse(File.ReadAllBytes(args[0]));
var raiz = fixture.RootElement;
var historico = raiz.GetProperty("historico");
var perfil = new ManipuladorFixture(raiz);
using var cliente = new HttpClient(perfil);
var data = "22-10-2026";
var cenarios = new (string Endpoint, Func<IRepositorioDadosClimaticos, Task<object>> Executar)[]
{
    ("GET /api/dashboard?date", async r => await new ServicoDashboardClimatico(r, new CoresAplicacao()).ObterDashboardAsync(data, default)),
    ("GET /api/environments/sala/latest", async r => await new ServicoConsultasClimaticas(r, new ServicoDashboardClimatico(r, new CoresAplicacao())).ObterUltimaMedicaoAsync("sala", default)),
    ("GET /api/environments/sala/measurements", async r => await new ServicoConsultasClimaticas(r, new ServicoDashboardClimatico(r, new CoresAplicacao())).ObterMedicoesAsync("sala", data, data, null, 1, 100, default)),
    ("GET /api/air-quality?date", async r => await new ServicoConsultasClimaticas(r, new ServicoDashboardClimatico(r, new CoresAplicacao())).ObterQualidadeArAsync(data, null, null, default)),
    ("POST /api/chat", async r => await new ServicoAssistenteClimatico(r, new SemIa(), new OpcoesIa()).ResponderAsync(new ChatRequest("qual a temperatura da sala?", "Tab1", data, null), default)),
    ("GET /api/exports/csv", async r => await new ServicoConsultasClimaticas(r, new ServicoDashboardClimatico(r, new CoresAplicacao())).ExportarAsync("csv", data, "sala", default))
};

Console.WriteLine(JsonSerializer.Serialize(new
{
    ambiente = $"{Environment.OSVersion}; .NET {Environment.Version}; {Environment.ProcessorCount} CPUs; {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes} bytes GC limit",
    fixtureBytes = new FileInfo(args[0]).Length,
    dias = historico.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.EnumerateObject().Count()),
    dataSelecionada = data,
    condicoes = "Fixture local em memória; HttpClient simulado; 5 amostras sequenciais por condição; cold=URL nova por amostra; warm=mesma URL após aquecimento; sem rede, auth, servidor ASP.NET ou navegador",
    idadeLeituraMaisRecenteHoras = (DateTime.Now - historico.GetProperty("AirQuality").EnumerateObject()
        .SelectMany(dia => dia.Value.EnumerateObject().Select(hora =>
            DateTime.TryParseExact($"{dia.Name} {hora.Name}", "dd-MM-yyyy HH-mm", null, System.Globalization.DateTimeStyles.None, out var instante)
                ? instante : DateTime.MinValue)).Max()).TotalHours
}));

foreach (var (endpoint, executar) in cenarios)
{
    foreach (var quente in new[] { false, true })
    {
        var tempos = new List<double>();
        var gets = 0;
        long bytes = 0, alocacoes = 0;
        double cpuMs = 0;
        long heap = 0;
        string? digest = null;
        var idQuente = Guid.NewGuid().ToString("N");
        for (var i = 0; i < 5; i++)
        {
            var opcoes = new OpcoesFirebase { DatabaseUrl = $"https://fixture-{(quente ? idQuente : Guid.NewGuid().ToString("N"))}.local", CacheDurationSeconds = 30 };
            var fonte = new FonteDadosFirebase(cliente, opcoes);
            var repositorio = new RepositorioDadosClimaticosFirebase(fonte, opcoes);
            if (quente && i == 0) await executar(repositorio);
            perfil.Zerar();
            var alocadoAntes = GC.GetTotalAllocatedBytes(true);
            var cpuAntes = Process.GetCurrentProcess().TotalProcessorTime;
            var relogio = Stopwatch.StartNew();
            var resposta = await executar(repositorio);
            relogio.Stop();
            tempos.Add(relogio.Elapsed.TotalMilliseconds);
            gets += perfil.Gets;
            bytes += perfil.Bytes;
            alocacoes += GC.GetTotalAllocatedBytes(true) - alocadoAntes;
            cpuMs += (Process.GetCurrentProcess().TotalProcessorTime - cpuAntes).TotalMilliseconds;
            heap = Math.Max(heap, GC.GetTotalMemory(false));
            var atual = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(resposta)));
            if (digest is not null && digest != atual) throw new InvalidOperationException($"Resposta variável: {endpoint}");
            digest = atual;
        }
        tempos.Sort();
        Console.WriteLine(JsonSerializer.Serialize(new { endpoint, cache = quente ? "warm" : "cold", n = tempos.Count, p50Ms = tempos[2], p95Ms = tempos[4], getsMedios = gets / 5.0, bytesMedios = bytes / 5.0, cpuMsMedios = cpuMs / 5, alocacoesBytesMedias = alocacoes / 5.0, heapMaxBytes = heap, digest }));
    }
}

foreach (var compartilhado in new[] { false, true })
{
    perfil.Zerar();
    var opcoes = new OpcoesAcessoInterno();
    var verificador = new VerificadorTokenAcessoFirebase(cliente, opcoes);
    var token = Guid.NewGuid().ToString("N");
    for (var i = 0; i < 5; i++)
    {
        var instancia = compartilhado ? verificador : new VerificadorTokenAcessoFirebase(cliente, opcoes);
        if (await instancia.ObterEmailAsync(token, default) != "probe@example.test") throw new InvalidOperationException("Resposta de autenticação mudou.");
    }
    Console.WriteLine(JsonSerializer.Serialize(new { auth = compartilhado ? "singleton" : "transient", requests = 5, lookupsRemotos = perfil.Posts }));
}

sealed class SemIa : IClienteGeracaoTexto
{
    public Task<string> GerarTextoAsync(string instrucao, CancellationToken tokenCancelamento) => throw new NotSupportedException();
}

sealed class ManipuladorFixture(JsonElement raiz) : HttpMessageHandler
{
    public int Gets { get; private set; }
    public int Posts { get; private set; }
    public long Bytes { get; private set; }
    public void Zerar() { Gets = 0; Posts = 0; Bytes = 0; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage requisicao, CancellationToken cancelamento)
    {
        if (requisicao.Method == HttpMethod.Post)
        {
            Posts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"users":[{"email":"probe@example.test"}]}""")
            });
        }
        var caminho = Uri.UnescapeDataString(requisicao.RequestUri!.AbsolutePath.Trim('/'));
        caminho = caminho[..^".json".Length];
        JsonElement? valor = raiz;
        foreach (var parte in caminho.Split('/'))
            valor = valor is { ValueKind: JsonValueKind.Object } objeto && objeto.TryGetProperty(parte, out var filho) ? filho : null;
        var bytes = requisicao.RequestUri.Query.Contains("shallow=true", StringComparison.Ordinal)
            ? JsonSerializer.SerializeToUtf8Bytes(valor is { ValueKind: JsonValueKind.Object } v ? v.EnumerateObject().ToDictionary(x => x.Name, _ => true) : null)
            : JsonSerializer.SerializeToUtf8Bytes(valor);
        Gets++;
        Bytes += bytes.Length;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }
}
