# Adequação da nomenclatura interna em PT-BR

A revisão posterior que alinhou os nomes com o projeto JavaScript original está documentada em `docs/EQUIVALENCIA_NOMES_ORIGINAL.md`.

Migração realizada em grupos, preservando a lógica e os contratos descritos em BUSINESS_RULES.md, AI_SYSTEM_PROMPT.md e ARCHITECTURE_AI.md. Fontes anteriores guardados em `outputs/nomenclatura-ptbr/antes`, pois a pasta não possui repositório Git.

## Escopo e resultado

- 1454 operações de renomeação documentadas em C# e JavaScript; 86 arquivos existentes alterados, incluindo referências na documentação.
- Domain: os modelos e propriedades da API permanecem intactos.
- Application: variáveis, parâmetros, métodos privados, serviços/interfaces, montadores, modelos estritamente internos e tipos auxiliares do assistente traduzidos.
- Infrastructure: cliente HTTP, consultas, cache, repositório e serviços traduzidos; campos e payloads externos mantidos.
- Web: variáveis do host/controladores e variáveis/funções locais dos scripts traduzidas; rotas, binding HTTP, DOM e chaves estruturais mantidos.
- Testes: variáveis, métodos auxiliares, classes e simuladores traduzidos; contratos Playwright, xUnit e .NET preservados.
- Arquivos físicos, projetos, assemblies, namespaces e scripts mantêm seus nomes existentes. Os nomes físicos são consumidos por referências de projetos, scripts, documentação e ferramentas; esta migração não altera esses caminhos.

## Grupos

1. Variáveis e métodos privados da montagem e das consultas climáticas.
2. Variáveis, parâmetros e métodos privados do assistente, AQI e solar.
3. Infraestrutura e inicialização Web.
4. Variáveis e auxiliares dos testes.
5. Serviços, interfaces, montadores e tipos de configuração internos.
6. Modelos internos de montagem/consulta e classes dos testes.
7. Tipos auxiliares do assistente, preservando propriedades dos payloads.
8. Variáveis locais de interface, indicadores e gráficos.
9. Variáveis locais de consultas públicas, autenticação, assistente e exportação.

Exemplos: `builder → construtor`, `request → requisicao`, `BuildDailyStats → MontarEstatisticasDiarias`, `ClimateDashboardService → ServicoDashboardClimatico`, `IClimateDataRepository → IRepositorioDadosClimaticos`, `FirebaseRecord → RegistroFirebase`, `ChartDefinition.Key → DefinicaoGrafico.Chave`.

## Verificações

Executados antes da migração e após cada grupo:

```powershell
dotnet build EstacaoClimatica.sln
dotnet test EstacaoClimatica.sln --logger "trx;LogFileName=grupo-NN.trx" --results-directory outputs/nomenclatura-ptbr/testes
```

Baseline: build sem erros/avisos e 178 testes aprovados.

| Grupo | Aprovados | Falhas | Total |
| --- | ---: | ---: | ---: |
| grupo-01 | 178 | 0 | 178 |
| grupo-02 | 178 | 0 | 178 |
| grupo-03 | 178 | 0 | 178 |
| grupo-04 | 178 | 0 | 178 |
| grupo-05 | 178 | 0 | 178 |
| grupo-06 | 178 | 0 | 178 |
| grupo-07 | 178 | 0 | 178 |
| grupo-08 | 178 | 0 | 178 |
| grupo-09 | 178 | 0 | 178 |

Logs completos: `outputs/nomenclatura-ptbr/NN-build.log`, `NN-test.log` e `testes/grupo-NN.trx`. Tentativas intermediárias malsucedidas dos grupos 02, 06 e 08 foram mantidas em arquivos `primeira-tentativa`/`segunda-tentativa`. No grupo 02, foram corrigidas a colisão de nome entre data e dados do ambiente e referências de tupla. No grupo 06, foram atualizadas propriedades geradas dos records e argumentos nomeados. No grupo 08, foi corrigida uma expectativa textual do teste estrutural para o identificador final `evento`. Nenhuma correção modificou cálculos ou condições.

Os testes de navegador existentes sobem Kestrel e executam Chromium/Playwright real em modo headless. Cobrem 03/06/2026, dashboard, abas, estatísticas, gráficos, tabelas, heatmaps, zoom, exportação PDF/JSON, consulta pública por CEP/cidade/localização, assistente, autenticação e responsividade. Serviços externos são interceptados com respostas fixas nos cenários de navegador. A paridade opcional com outro projeto (`RUN_LIVE_PARITY`) não foi habilitada e não é apresentada como validação ao vivo do Firebase.

Auditoria adicional: `auditoria-csharp.json` compara tokens não identificadores e literais com a cópia inicial. Em produção, todos permanecem idênticos; nos testes estruturais, foram atualizadas apenas as strings que verificam os identificadores locais renomeados do JavaScript. `auditoria-js.json` compara literais e nomes de propriedades dos scripts. `arquivos-protegidos.json` verifica identidade byte a byte de Domain, configurações, HTML, CSS, projetos e bibliotecas. Ferramentas auxiliares ficaram somente em `outputs/nomenclatura-ptbr`, fora da solução e das dependências da aplicação.

## Inventário e exceções

- `outputs/nomenclatura-ptbr/inventario-csharp-classificado.csv`: todas as declarações C# por arquivo/camada, com classificação e justificativa.
- `outputs/nomenclatura-ptbr/renomeacoes.csv`: nomes antigos/novos, arquivos, contextos e grupos.
- `outputs/nomenclatura-ptbr/nomes-preservados.csv`: lista individual de nomes preservados e motivos, incluindo propriedades JSON, campos Firebase, parâmetros HTTP, chaves de configuração, DOM e propriedades/globais JavaScript.
- `outputs/nomenclatura-ptbr/inventario-js-inicial.json`: declarações JavaScript com indicação de escopo local/global.
- `outputs/nomenclatura-ptbr/inventario-js-classificado.csv`: classificação das declarações JavaScript.
- `outputs/nomenclatura-ptbr/alteracoes.patch`: comparação revisável com os fontes iniciais.

Preservados por contrato: `Toluen`, `Aceton`, `Alcohol`, `CO2`, variantes `HourNascerDoSol`/`HoraNascerDoSol`; `date`, `from`, `to`, `pageSize`, `environment`; `Question`, `SelectedDate`, `Average`, `Points` e demais propriedades dos DTOs; `DatabaseUrl`, `Paths`, `Enabled`, `ApiKey` e outras chaves de configuração; `Contents`, `Parts`, `Text` do Gemini; `InitializeAsync`, `DisposeAsync`, `SendAsync`; IDs como `selectedDate`, `Tab0` a `Tab3`, `plotsTempSala`; Chart.js, Firebase Auth, html2canvas, jsPDF e APIs do navegador.

Casos duvidosos preservados: nomes de controladores/ação `Post` (metadados ASP.NET), propriedades dos records do assistente (grafo serializado no prompt), globais/chaves compartilhadas dos scripts clássicos e caminhos físicos. Termos técnicos adotados pelo projeto, como Dashboard, Async, JSON, Firebase, AQI, HTTP, CSS, Playwright e identificadores matemáticos, permanecem quando apropriados. O inventário registra as exceções em vez de afirmar que todo identificador em inglês desapareceu.

## Arquivos alterados

- `src/EstacaoClimatica.Web/Program.cs`
- `src/EstacaoClimatica.Web/Controllers/ChatController.cs`
- `src/EstacaoClimatica.Web/Controllers/ClimateQueryController.cs`
- `src/EstacaoClimatica.Web/Controllers/DashboardController.cs`
- `src/EstacaoClimatica.Web/Controllers/PublicWeatherController.cs`
- `src/EstacaoClimatica.Web/wwwroot/app.js`
- `src/EstacaoClimatica.Web/wwwroot/scripts/auth.js`
- `src/EstacaoClimatica.Web/wwwroot/scripts/chat.js`
- `src/EstacaoClimatica.Web/wwwroot/scripts/config.js`
- `src/EstacaoClimatica.Web/wwwroot/scripts/heatmaps.js`
- `src/EstacaoClimatica.Web/wwwroot/scripts/moon.js`
- `src/EstacaoClimatica.Web/wwwroot/scripts/pdf-report.js`
- `src/EstacaoClimatica.Web/wwwroot/scripts/public-weather.js`
- `src/EstacaoClimatica.Web/wwwroot/scripts/season.js`
- `src/EstacaoClimatica.Web/wwwroot/scripts/solar.js`
- `src/EstacaoClimatica.Web/wwwroot/scripts/ui.js`
- `src/EstacaoClimatica.Web/wwwroot/scripts/utils.js`
- `src/EstacaoClimatica.Web/wwwroot/scripts/zoom.js`
- `src/EstacaoClimatica.Web/wwwroot/scripts/views/dashboard-view.js`
- `src/EstacaoClimatica.Web/wwwroot/scripts/views/station-view.js`
- `src/EstacaoClimatica.Infrastructure/AI/GeminiTextGenerationClient.cs`
- `src/EstacaoClimatica.Infrastructure/Authentication/FirebaseAccessTokenVerifier.cs`
- `src/EstacaoClimatica.Infrastructure/External/PublicWeatherService.cs`
- `src/EstacaoClimatica.Infrastructure/Firebase/FirebaseDataSource.cs`
- `src/EstacaoClimatica.Infrastructure/Repositories/FirebaseClimateDataRepository.cs`
- `src/EstacaoClimatica.Application/Abstractions/IClimateChatService.cs`
- `src/EstacaoClimatica.Application/Abstractions/IClimateDashboardService.cs`
- `src/EstacaoClimatica.Application/Abstractions/IClimateDataRepository.cs`
- `src/EstacaoClimatica.Application/Abstractions/IClimateQueryService.cs`
- `src/EstacaoClimatica.Application/Abstractions/IFirebaseDataSource.cs`
- `src/EstacaoClimatica.Application/Abstractions/IInternalAccessTokenVerifier.cs`
- `src/EstacaoClimatica.Application/Abstractions/IPublicWeatherService.cs`
- `src/EstacaoClimatica.Application/Abstractions/ITextGenerationClient.cs`
- `src/EstacaoClimatica.Application/Builders/AqiDashboardBuilder.cs`
- `src/EstacaoClimatica.Application/Builders/ChartSeriesBuilder.cs`
- `src/EstacaoClimatica.Application/Builders/DataQualityBuilder.cs`
- `src/EstacaoClimatica.Application/Builders/EnvironmentDashboardBuilder.cs`
- `src/EstacaoClimatica.Application/Builders/HeatmapBuilder.cs`
- `src/EstacaoClimatica.Application/Builders/SolarDashboardBuilder.cs`
- `src/EstacaoClimatica.Application/Builders/StationLatestDashboardBuilder.cs`
- `src/EstacaoClimatica.Application/Builders/StatsBuilder.cs`
- `src/EstacaoClimatica.Application/Builders/TableDataBuilder.cs`
- `src/EstacaoClimatica.Application/Configuration/AiOptions.cs`
- `src/EstacaoClimatica.Application/Configuration/AppColors.cs`
- `src/EstacaoClimatica.Application/Configuration/FirebaseFields.cs`
- `src/EstacaoClimatica.Application/Configuration/FirebaseOptions.cs`
- `src/EstacaoClimatica.Application/Configuration/InternalAccessOptions.cs`
- `src/EstacaoClimatica.Application/Configuration/PublicWeatherOptions.cs`
- `src/EstacaoClimatica.Application/Models/AqiPollutantDefinition.cs`
- `src/EstacaoClimatica.Application/Models/ChartDefinition.cs`
- `src/EstacaoClimatica.Application/Models/ClimateRecord.cs`
- `src/EstacaoClimatica.Application/Models/FirebaseRecord.cs`
- `src/EstacaoClimatica.Application/Models/MetricDefinition.cs`
- `src/EstacaoClimatica.Application/Models/SolarEventSeconds.cs`
- `src/EstacaoClimatica.Application/Services/ClimateChatService.Aqi.cs`
- `src/EstacaoClimatica.Application/Services/ClimateChatService.cs`
- `src/EstacaoClimatica.Application/Services/ClimateChatService.Solar.cs`
- `src/EstacaoClimatica.Application/Services/ClimateDashboardService.cs`
- `src/EstacaoClimatica.Application/Services/ClimateQueryService.cs`
- `src/EstacaoClimatica.Application/Services/EnvironmentalInsightsService.cs`
- `src/EstacaoClimatica.Application/Services/FirebaseDashboardData.cs`
- `src/EstacaoClimatica.Application/Utilities/AquariumMeasurementNormalizer.cs`
- `src/EstacaoClimatica.Application/Utilities/FirebaseDate.cs`
- `src/EstacaoClimatica.Application/Utilities/FirebaseRecordReader.cs`
- `src/EstacaoClimatica.Application/Utilities/JsonNumberReader.cs`
- `tests/EstacaoClimatica.Tests/ClimateChatServiceTests.cs`
- `tests/EstacaoClimatica.Tests/ClimateDashboardServiceTests.cs`
- `tests/EstacaoClimatica.Tests/ClimateQueryControllerTests.cs`
- `tests/EstacaoClimatica.Tests/ClimateQueryServiceTests.cs`
- `tests/EstacaoClimatica.Tests/ControllerTests.cs`
- `tests/EstacaoClimatica.Tests/DataQualityBuilderTests.cs`
- `tests/EstacaoClimatica.Tests/EnvironmentalInsightsServiceTests.cs`
- `tests/EstacaoClimatica.Tests/FirebaseAccessTokenVerifierTests.cs`
- `tests/EstacaoClimatica.Tests/FirebaseClimateDataRepositoryTests.cs`
- `tests/EstacaoClimatica.Tests/FirebaseDataSourceTests.cs`
- `tests/EstacaoClimatica.Tests/FrontendBrowserTests.cs`
- `tests/EstacaoClimatica.Tests/FrontendStructureTests.cs`
- `tests/EstacaoClimatica.Tests/GeminiTextGenerationClientTests.cs`
- `tests/EstacaoClimatica.Tests/LiveOriginalParityTests.cs`
- `tests/EstacaoClimatica.Tests/PublicWeatherServiceTests.cs`
- `docs/AI_QUICK_CONTEXT.md`
- `docs/AI_SYSTEM_PROMPT.md`
- `docs/ARCHITECTURE_AI.md`
- `docs/BUSINESS_RULES.md`
- `docs/DEPENDENCY_MAP.md`
- `docs/README.md`

Mensagem de commit sugerida: `refactor(nomenclatura): adequar nomes internos ao português brasileiro`
