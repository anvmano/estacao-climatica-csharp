# Equivalência de nomes com o projeto original

Data da revisão: 08/09/2026.

Projeto C#: `D:\Documentos\03_Projetos\Estacao climatica C#`  
Projeto original: `D:\Documentos\03_Projetos\estacao climatica web\atualizacao claude`

## Resultado

Os identificadores internos que implementam o mesmo comportamento foram alinhados ao nome PT-BR usado pelo projeto original. A rodada realizou 224 operações de renomeação de símbolos: 174 em JavaScript e 50 em C#. Foram alterados 33 arquivos de código ou teste no projeto C#. O código de produto do projeto original foi usado como referência e não precisou ser modificado.

O padrão léxico segue a convenção de cada linguagem: `criarGraficoLinha` em JavaScript corresponde a `CriarGraficoLinha` em C#. A grafia canônica do domínio nos dois projetos é `umidade`. `humidade` permanece somente como sinônimo textual aceito pela assistente, e `humidity` permanece quando é uma chave externa ou publicada.

## Método de comparação

Foram lidos `BUSINESS_RULES.md`, `AI_SYSTEM_PROMPT.md` e `ARCHITECTURE_AI.md` nos dois projetos. Em seguida, foram inventariadas 647 funções e 3.298 variáveis do JavaScript original e 317 funções e 1.352 variáveis do frontend C#. A comparação usa a árvore sintática com identificadores normalizados, o que permite localizar implementações equivalentes mesmo quando os nomes diferem.

Após as renomeações, há 88 correspondências exatas de implementação entre os frontends. Em 73 delas o nome também é igual. As 15 associações com nomes diferentes foram revisadas individualmente: 13 são falsos pares produzidos por corpos triviais iguais, como wrappers de formatação ou fechamento, e duas precisam dos sufixos `Pdf` e `Estacao` no C# para não declarar `formatarDataCompleta` duas vezes no escopo global dos scripts clássicos.

No backend C#, o vínculo semântico foi conferido por método e por todos os chamadores. A diferença de arquitetura entre JavaScript e .NET impede uma relação numérica de um para um para classes, records, injeção de dependência e controladores; nesses casos foi alinhado o vocabulário do comportamento equivalente, preservando as convenções da linguagem.

## Renomeações representativas

| Área | Nome anterior no C# | Nome alinhado ao original |
| --- | --- | --- |
| Dados Firebase | `Converter` | `InterpretarDataFirebase` |
| Dados Firebase | `FiltrarUltimosDias` | `FiltrarDadosPorDias` |
| Dados Firebase | `FiltrarJanelaMovelHoras` | `FiltrarDadosPorJanelaHoras` |
| Qualidade | `Analisar` | `AnalisarSerie` |
| Qualidade | `ObterChave` | `CriarChaveLeitura` |
| Qualidade | `EstaDesatualizado` | `VerificarDesatualizacao` |
| Qualidade | `ObterRotulo` | `ObterRotuloNivel` |
| Clima público | `NormalizarResposta` | `NormalizarRespostaPublica` |
| Clima público | `ObterCicloSolar` | `ObterEventosSolares` |
| Clima público | `NormalizarBrasilApi` | `NormalizarEnderecoBrasilApi` |
| Assistente | `ConstruirResultadoEstatistico` | `MontarResultadoEstatistico` |
| Assistente | `ParsearJsonIntencao` | `InterpretarJsonIntencao` |
| Assistente | `DetectarIntencaoUltimaMedicao` | `TemIntencaoUltimaMedicao` |
| Assistente | `ExtrairHorasJanela` | `ExtrairJanelaHoras` |
| Assistente | `ResolverMetricasAmbientais` | `ResolverMetricasAmbientes` |
| Frontend público | `consultarEntrada` | `buscarPorEntrada` |
| Frontend público | `consultarCep` | `buscarPorCep` |
| Frontend público | `pesquisarCidades` | `pesquisarPorCidade` |
| Frontend público | `consultarLocalizacao` | `buscarPorLocalizacao` |
| Frontend público | `consultarClima` | `executarBusca` |
| Frontend público | `criarGraficoHorario` | `renderizarGraficoLinha` |
| Relatório | `executarExportacaoComTimeout` | `executarComTimeout` |
| Relatório | `criarCabecalhoPdf` | `criarCabecalho` |
| Relatório | `criarSecaoResumoPdf` | `criarSecaoResumo` |
| Estação | `renderizarResumoEstacao` | `renderizarResumoGlobal` |
| Estação | `combinarVentilacao` | `recomendarVentilacaoCombinada` |
| Chat | `chatAberto` | `estaAberto` |
| Chat | `chatOcupado` | `estaOcupado` |
| Chat | `obterElementosChat` | `coletarElementos` |
| Chat | `adicionarMensagemChat` | `adicionarMensagem` |

Os arquivos `grupo-*.json` e `grupo-*-realizadas.json` em `outputs/equivalencia-nomes-original-csharp` guardam o inventário completo das operações e a quantidade de referências atualizadas.

## Contratos preservados

Não foram renomeados campos reais do Firebase, rotas, query strings, propriedades JSON, payloads, IDs/classes/atributos do DOM, chaves de configuração, APIs de bibliotecas, overrides, nomes exigidos pelo ASP.NET Core nem caminhos físicos. Os aliases públicos anteriores dos scripts continuam disponíveis quando já eram consumidos externamente.

A lista classificada está em `outputs/equivalencia-nomes-original-csharp/nomes-preservados-equivalencia.csv`. Exemplos relevantes:

- `humidity`, `temperature` e `feelsLike` permanecem como chaves publicadas; vínculos internos usam `umidade`, `temperatura` e `sensacaoTermica`;
- `Question`, `SelectedDate`, `ActiveTab`, `Average` e `Points` permanecem como propriedades serializadas;
- `date`, `from`, `to`, `pageSize`, `environment`, `latitude` e `longitude` permanecem em contratos HTTP;
- `selectedDate`, `Tab0` a `Tab3`, `publicChartHumidity` e os demais IDs do DOM permanecem iguais;
- `setup`, `update`, `getState` e os nomes globais antigos de zoom permanecem como aliases públicos;
- `formatarDataCompletaPdf` e `formatarDataCompletaEstacao` permanecem distintos para evitar colisão no escopo global.

## Arquivos alterados nesta rodada

### Application

- `Builders/AqiDashboardBuilder.cs`
- `Builders/ChartSeriesBuilder.cs`
- `Builders/DataQualityBuilder.cs`
- `Builders/EnvironmentDashboardBuilder.cs`
- `Builders/HeatmapBuilder.cs`
- `Builders/SolarDashboardBuilder.cs`
- `Builders/StationLatestDashboardBuilder.cs`
- `Builders/StatsBuilder.cs`
- `Builders/TableDataBuilder.cs`
- `Services/ClimateChatService.cs`
- `Services/ClimateChatService.Aqi.cs`
- `Services/ClimateChatService.Solar.cs`
- `Services/ClimateQueryService.cs`
- `Services/EnvironmentalInsightsService.cs`
- `Utilities/AquariumMeasurementNormalizer.cs`
- `Utilities/FirebaseDate.cs`
- `Utilities/FirebaseRecordReader.cs`

### Infrastructure

- `External/PublicWeatherService.cs`

### Web

- `wwwroot/app.js`
- `wwwroot/scripts/chat.js`
- `wwwroot/scripts/heatmaps.js`
- `wwwroot/scripts/moon.js`
- `wwwroot/scripts/pdf-report.js`
- `wwwroot/scripts/public-weather.js`
- `wwwroot/scripts/season.js`
- `wwwroot/scripts/solar.js`
- `wwwroot/scripts/ui.js`
- `wwwroot/scripts/utils.js`
- `wwwroot/scripts/views/dashboard-view.js`
- `wwwroot/scripts/views/station-view.js`
- `wwwroot/scripts/zoom.js`

### Testes

- `DataQualityBuilderTests.cs`
- `FrontendStructureTests.cs`

Domain não recebeu alteração nesta rodada porque seus nomes remanescentes são modelos e propriedades de contrato. O arquivo completo com caminhos relativos está em `outputs/equivalencia-nomes-original-csharp/arquivos-fonte-alterados.txt`.

## Validação e preservação de comportamento

Cada grupo coeso foi seguido por `dotnet build EstacaoClimatica.sln` e `dotnet test EstacaoClimatica.sln`. Houve uma falha intermediária esperada no grupo 2: um teste estrutural ainda procurava o nome antigo de uma função JavaScript. A expectativa foi atualizada para o identificador novo e a suíte voltou a 178 aprovações. Nenhuma correção mudou lógica.

Validação final do C#:

- `dotnet build EstacaoClimatica.sln`: aprovado, zero avisos e zero erros;
- `dotnet test EstacaoClimatica.sln`: 178 aprovados, zero falhas e zero ignorados;
- auditoria de literais JavaScript: 15 de 15 arquivos idênticos ao estado anterior;
- auditoria de literais C#: 70 de 71 arquivos idênticos; o único arquivo diferente é `FrontendStructureTests.cs`, com quatro strings de asserção atualizadas para os nomes novos. Em produção, 100% dos literais ficaram iguais.

Validação final do projeto original:

- `npm run validate`: 88 arquivos JS e contratos validados;
- `npm run lint`: aprovado;
- `npm run test:assistant`: aprovado;
- `npm run test:report`: aprovado;
- `npm run test:pdf-artifact`: PDF aprovado com 4 páginas e 7 blocos visuais;
- `npm run test:export`: aprovado;
- `npm run test:accessibility`: aprovado;
- `npm run test:data-quality`: aprovado;
- `npm run test:public`: aprovado;
- `npm run test:axe`: aprovado;
- `npm run test:tables`: aprovado.

Os 178 testes C# incluem os cenários Playwright existentes para a data 03/06/2026: dashboard, abas, gráficos, tabelas, exportações PDF/JSON, modo público por CEP, cidade e localização, assistente, autenticação e responsividade. As integrações externas são simuladas pelos próprios testes.

Logs finais: `outputs/equivalencia-nomes-original-csharp/validacao-csharp-final.log` e `validacao-original-final.log`. Auditorias: `auditoria-csharp.json`, `auditoria-js.json`, `correspondencias-exatas-js.json` e `correspondencias-provaveis-js.json`.

## Observação operacional

Durante a criação da cópia de segurança do original, uma tentativa apontou por engano para uma subpasta da própria origem e criou `docs/equivalencia-nomes-csharp/antes-original`. O conteúdo é somente uma cópia gerada e está fora do código carregado e validado pela aplicação. A política local bloqueou a exclusão recursiva; a cópia de referência válida usada nas auditorias está em `outputs/equivalencia-nomes-original-csharp/antes-original` no projeto C#.
