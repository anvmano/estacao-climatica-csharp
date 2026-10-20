# AI_SYSTEM_PROMPT

Voce esta trabalhando no projeto `EstacaoClimatica`.

## Objetivo do sistema

Exibir um dashboard web de estacao climatica com aba global Estação, dados de Sala, Quarto, Aquario e eventos solares. O sistema le Firebase Realtime Database via backend .NET, processa registros historicos e entrega uma resposta JSON para o frontend estatico renderizar cards, graficos, tabelas, heatmaps, ciclo solar centralizado na Estação, indicadores contextuais no header, navegacao por toque entre abas e exportacao PDF/JSON da aba ativa.

## Tecnologias

- C# / .NET 8
- ASP.NET Core Controllers
- Firebase Realtime Database via REST
- HTML/CSS/JavaScript
- Chart.js 4.5.1 local em `wwwroot/vendor`
- html2canvas 1.4.1 local em `wwwroot/vendor`, carregado sob demanda ao exportar PDF
- jsPDF 2.5.2 local em `wwwroot/vendor`, carregado sob demanda ao exportar PDF
- xUnit

Nao ha Entity Framework, banco relacional, controllers MVC com views, background jobs ou escrita Firebase. Ha Firebase Auth opcional no frontend e autorizacao server-side para as APIs internas.

## Arquivos importantes

- `src/EstacaoClimatica.Web/Program.cs`: DI, controllers e static files.
- `src/EstacaoClimatica.Web/Controllers/DashboardController.cs`: endpoints `/api/dashboard` e `/api/health`.
- `src/EstacaoClimatica.Web/Controllers/ChatController.cs`: endpoint `/api/chat`.
- `src/EstacaoClimatica.Web/appsettings.json`: Firebase URL, paths e cores.
- `src/EstacaoClimatica.Domain/DashboardModels.cs`: contrato JSON da API.
- `src/EstacaoClimatica.Application/Services/ClimateDashboardService.cs`: orquestracao da montagem do dashboard.
- `src/EstacaoClimatica.Application/Services/ClimateChatService.cs`: assistente climatico com calculo local e IA opcional.
- `src/EstacaoClimatica.Application/Abstractions/IClimateDataRepository.cs`: contrato para dados brutos do dashboard.
- `src/EstacaoClimatica.Infrastructure/Repositories/FirebaseClimateDataRepository.cs`: repository Firebase do dashboard.
- `src/EstacaoClimatica.Infrastructure/Firebase/FirebaseDataSource.cs`: leitura Firebase via HTTP.
- `src/EstacaoClimatica.Application/Builders/EnvironmentDashboardBuilder.cs`: metricas/graficos/tabelas por ambiente.
- `src/EstacaoClimatica.Application/Builders/StatsBuilder.cs`: media, min, max, delta e tendencia.
- `src/EstacaoClimatica.Application/Builders/ChartSeriesBuilder.cs`: pontos de graficos ambientais.
- `src/EstacaoClimatica.Application/Builders/TableDataBuilder.cs`: linhas e cabecalhos de tabelas.
- `src/EstacaoClimatica.Application/Builders/HeatmapBuilder.cs`: heatmaps mensal, horario e semanal.
- `src/EstacaoClimatica.Application/Builders/SolarDashboardBuilder.cs`: historico e ciclo solar.
- `src/EstacaoClimatica.Application/Utilities/FirebaseRecordReader.cs`: enumeracao do JSON Firebase.
- `src/EstacaoClimatica.Application/Utilities/FirebaseDate.cs`: formato `dd-MM-yyyy`.
- `src/EstacaoClimatica.Application/Utilities/JsonNumberReader.cs`: parse numerico.
- `src/EstacaoClimatica.Web/wwwroot/app.js`: orquestracao do frontend e fetch.
- `src/EstacaoClimatica.Web/wwwroot/scripts/auth.js`: login/logout Google, allowlist e token das chamadas internas.
- `src/EstacaoClimatica.Web/wwwroot/scripts/public-weather.js`: modo publico por CEP/localizacao.
- `src/EstacaoClimatica.Infrastructure/Authentication/FirebaseAccessTokenVerifier.cs`: valida Firebase ID token no backend.
- `src/EstacaoClimatica.Web/wwwroot/scripts/*`: UI, Chart.js, aba Estação, estação/lua, zoom, heatmaps, indicador astronomico, AQI no header, navegacao touch e exportacao PDF/JSON.
- `src/EstacaoClimatica.Web/wwwroot/pdf-report.css`: estilos temporarios usados na renderizacao do PDF.
- `src/EstacaoClimatica.Web/wwwroot/vendor/*`: bibliotecas locais do frontend.
- `tests/EstacaoClimatica.Tests/ClimateDashboardServiceTests.cs`: testes das regras principais.
- `tests/EstacaoClimatica.Tests/ClimateChatServiceTests.cs`: testes do assistente climatico.
- `tests/EstacaoClimatica.Tests/ControllerTests.cs`: contratos dos controllers e mapeamento de erros HTTP.
- `tests/EstacaoClimatica.Tests/FirebaseDataSourceTests.cs`: URL, cache, retry e respostas Firebase.
- `tests/EstacaoClimatica.Tests/FirebaseClimateDataRepositoryTests.cs`: caminhos e janelas de data do repository.
- `tests/EstacaoClimatica.Tests/GeminiTextGenerationClientTests.cs`: configuracao, request e resposta da IA opcional.

## Fluxos principais

```text
Browser
-> app.js
-> GET /api/dashboard?date=dd-MM-yyyy
-> DashboardController
-> IServicoDashboardClimatico.ObterDashboardAsync
-> ServicoDashboardClimatico
-> IRepositorioDadosClimaticos.ObterDadosDashboardAsync
-> RepositorioDadosClimaticosFirebase
-> IFonteDadosFirebase.ObterCaminhoAsync
-> FonteDadosFirebase
-> Firebase Realtime Database
-> Builders de Application
-> DashboardResponse
-> app.js renderiza DOM e Chart.js
-> app.js pode exportar a aba ativa para PDF ou JSON

Browser
-> scripts/chat.js
-> POST /api/chat
-> ChatController
-> IServicoAssistenteClimatico.ResponderAsync
-> ServicoAssistenteClimatico
-> IRepositorioDadosClimaticos.ObterDadosDashboardAsync
-> calculo local em C#
-> ClienteGeracaoTextoGemini apenas quando IA estiver habilitada
-> ChatResponse
-> scripts/chat.js renderiza a resposta
```

## Regras criticas

- Use nomenclatura PT-BR para novos metodos, funcoes e variaveis internas. Preserve nomes externos obrigatorios: campos Firebase, ids/classes DOM, contratos JSON ou HTTP ja publicados, propriedades de bibliotecas e APIs externas, configuracoes e chaves estruturais consumidas por outros modulos.
- Formato de data Firebase: `dd-MM-yyyy`.
- Input HTML usa `yyyy-MM-dd`, convertido por `app.js`.
- Estrutura Firebase esperada: data -> hora -> item -> campos.
- Caminhos Firebase atuais:
  - Room: `historico/Temperatura`
  - Solar: `historico/NascePorDoSol`
  - Aquarium: `historico/Aquario`
  - LivingRoom: `historico/AirQuality`
- Sala usa campos `temperatura`, `sensacaoTermica`, `umidade`, `pressao`, `CO`, `CO2`, `Aceton`, `Alcohol`, `NH4`, `Toluen`.
- O chat/assistente aceita Tolueno como metrica consultavel usando o campo Firebase `Toluen`.
- O chat/assistente redireciona metricas exclusivas para o ambiente correto: CO, CO2, Acetona, Alcool, Amonia, Tolueno e AQI/IAQ pertencem a Sala/MQ135 mesmo quando a aba ativa e outra.
- O chat/assistente aceita filtro por hora (`14h`, `14:00`, `as 14`) e deve retornar somente o valor daquele horario, sem resumo estatistico.
- O chat/assistente usa memoria curta via `PreviousIntent` para perguntas de continuidade, herdando ambiente, metrica, operacao, hora e faixa horaria quando a pergunta nova omitir esses dados.
- O chat/assistente trata `agora`, `valor atual`, `ultima medicao` e perguntas simples `qual o/a <metrica>` como consulta da ultima amostra disponivel no recorte consultado.
- O chat calcula AQI/IAQ/qualidade do ar em C# com os gases da Sala/MQ135, usando as mesmas faixas do indicador do header.
- O chat aceita faixa horaria, ultimas 24h reais, faixa de conforto, analise horaria e consultas equivalentes aos heatmaps.
- O chat calcula consultas solares em C#: ciclo do dia, duracao do dia, maior/menor duracao de luz, tendencia e comparacao de nascer/por do sol.
- Quarto usa `Temperatura`, `Sensacao termica`, `Umidade`.
- Aquario usa `temperaturaDS18B20`, `PH`, `TDS`, `Turbidez`.
- Tabela limita 24 registros mais recentes.
- Navegacao por toque troca entre `Tab0`, `Tab1`, `Tab2` e `Tab3`.
- Exportacao PDF/JSON usa dados ja carregados em memoria e graficos renderizados; nao consulta Firebase novamente.
- Exportacao JSON inclui metadados, resumo, graficos, tabela e dados processados da aba ativa.
- Exportacao PDF/JSON por aba: Estação inclui contexto anual/lunar, cards globais, graficos comparativos e ciclo solar, sem tabela; Sala inclui ambiente + tabela MQ135, sem solar; Quarto inclui ambiente + tabela ambiental, sem solar; Aquario inclui apenas Temperatura, PH, TDS e Turbidez, sem solar/zenite.
- Exportacao JSON inclui metadados, resumo, graficos, `tabela`, `tabelaResumida`, `tabelaDetalhada`, `dadosBrutos` e `dadosDaAba`.
- Ciclo solar no PDF deve ser gerado offscreen usando dados solares carregados e a mesma logica visual de `solar.js`; sem dados solares, mostrar estado vazio.
- Header usa chips na ordem estação do ano, AQI, ciclo solar, fase da lua e relogio; estação e lua sao calculadas localmente no frontend.
- Indicador astronomico usa `dashboard.solar.day` quando os dados solares carregados sao da data atual; se nao houver dados atuais, usa fallback 06:00-18:00. O chip nao tem texto interno e clique/toque abre popover com amanhecer, nascer, zenite, por do sol, anoitecer, estado atual e duracao do dia.
- Indicador AQI estimado da Sala/MQ135 fica no header e possui popover proprio. Popovers do header sao mutuamente exclusivos.
- Graficos comuns usam janela movel das ultimas 24h terminando no horario atual aplicado a data selecionada; cards e tabelas continuam por data selecionada.
- Heatmap semanal reinicia no domingo da semana da data selecionada e considera somente registros ate a data selecionada.
- FonteDadosFirebase usa cache curto por URL e retry/backoff simples para falhas transientes.
- Nomes de campos Firebase de ambiente e solar ficam em `CamposFirebase`.
- Configuracao de IA/App Check fica em `ArtificialIntelligence`/`OpcoesIa`, desativada por padrao e sem chave secreta versionada.
- O assistente usa resposta local quando IA esta desativada; quando IA esta ativada, a IA classifica/redige, mas os valores numericos sao calculados em C#.
- Tendencia: `abs(delta) < 0.05` estavel; positivo subindo; negativo caindo.
- Heatmaps so sao montados para Sala e Quarto.
- Solar usa ultimos 365 dias.
- Zenite solar tem fallback: medio entre nascer e por do sol.
- `DashboardModels.cs` e contrato com `app.js`.

## Nunca altere sem revisar impacto

- Nomes de propriedades em `DashboardModels.cs`.
- Nomes de campos Firebase nos builders.
- IDs de graficos retornados pelo backend e usados no DOM.
- Formato `dd-MM-yyyy`.
- Chaves de configuracao `Firebase` e `Colors`.
- Chaves/configuracoes de IA: segredo nunca deve ser colocado em frontend ou `appsettings.json` versionado.
- Disponibilidade dos scripts locais `vendor/chart.umd.min.js`, `vendor/html2canvas.min.js` e `vendor/jspdf.umd.min.js`; html2canvas/jsPDF sao carregados sob demanda no PDF.

## Pontos de atencao

- `app.js` orquestra fetch/inicializacao, mas a UI principal fica em `scripts/*`; os scripts ainda sao classicos e dependem de ordem/global `window`.
- `FonteDadosFirebase` possui cache curto por URL e retry/backoff simples, mas nao possui autenticacao.
- O backend tenta leituras Firebase mais estreitas por data/path para o dashboard quando aplicavel; algumas consultas historicas continuam filtrando em memoria apos o carregamento.
- Campos Firebase sao literais; erros de acento/case quebram dados.
- Existem testes estruturais de frontend em `FrontendStructureTests.cs`, cobrindo scripts locais, IDs principais, controle PDF/JSON e containers de graficos/tabelas.
- Existem testes reais de browser em `FrontendBrowserTests.cs`, cobrindo modo publico, geolocalizacao, allowlist, logout, data `03/06/2026`, abas, colapsaveis, zoom, responsividade, renderizacao DOM, downloads PDF/JSON e chat via Playwright.
- Alterar contrato JSON exige alterar `app.js` e/ou `scripts/*`.
- Exportacao PDF/JSON depende dos IDs de graficos e secoes DOM usados pelos scripts frontend.

## Estrategia recomendada para alteracoes

1. Identifique se a mudanca e de API, negocio, infraestrutura ou UI.
2. Leia o arquivo critico correspondente.
3. Preserve contratos externos: Firebase, JSON da API, DOM/IDs.
4. Coloque regras de negocio em Application, nao em controllers ou `Program.cs`.
5. Para calculos novos, crie builder/utilitario ou expanda builder existente.
6. Atualize testes xUnit quando regras forem alteradas.
7. Valide build, testes, endpoint e tela.

## Checklist antes de modificar codigo

- Li `DashboardModels.cs` se a mudanca afeta resposta da API?
- Li `app.js` se a mudanca afeta frontend?
- Li `EnvironmentDashboardBuilder.cs` se a mudanca afeta metricas ambientais?
- Li `SolarDashboardBuilder.cs` se a mudanca afeta solar?
- Li `FirebaseRecordReader.cs` se a mudanca afeta estrutura Firebase?
- Verifiquei nomes exatos dos campos do Firebase?
- Entendi se a data deve estar em `dd-MM-yyyy` ou `yyyy-MM-dd`?

## Checklist antes de finalizar alteracoes

- Rodar `dotnet build EstacaoClimatica.sln`.
- Rodar `dotnet test EstacaoClimatica.sln`.
- Rodar app com `dotnet run --project src/EstacaoClimatica.Web/EstacaoClimatica.Web.csproj`.
- Verificar `GET /api/health`.
- Verificar `GET /api/dashboard?date=...`.
- Abrir `http://127.0.0.1:5288/` ou URL equivalente.
- Conferir console do navegador.
- Conferir stats, graficos, tabelas, heatmaps e solar se foram afetados.
- Conferir indicador astronomico do header se dados solares ou header forem afetados.
- Conferir swipe/touch em dispositivo ou navegador com suporte a eventos touch quando essa area for alterada.
- Conferir controle PDF/JSON, rotulo do botao e ausencia de erros no console quando essa area for alterada.

## Documentacao e commits

- Atualize somente os arquivos `.md` diretamente impactados pela alteracao. Nao altere documentacao sem impacto real.
- Para cada entrega logica concluida, gere uma mensagem de commit em portugues no formato:

  ```text
  <tipo>(<escopo>): <resumo no infinitivo e sem ponto>
