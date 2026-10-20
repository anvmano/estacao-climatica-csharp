# AI_QUICK_CONTEXT

## O que e o sistema

`EstacaoClimatica` possui modo publico sem login e painel interno autorizado. Visitantes consultam clima por CEP ou localizacao; somente `usuario@example.com` e `outra@example.com` acessam Estação, Sala, Quarto, Aquario, chat e exportacoes. O backend le o Firebase Realtime Database, processa os dados e protege todas as rotas internas `/api`, exceto saude e clima publico, com validacao de Firebase ID token.

## Tecnologias

- .NET 8
- ASP.NET Core Controllers
- C#
- Firebase Realtime Database via REST
- HTML/CSS/JavaScript estatico
- Chart.js 4.5.1 local em `wwwroot/vendor`
- html2canvas 1.4.1 local em `wwwroot/vendor`, carregado sob demanda ao exportar PDF
- jsPDF 2.5.2 local em `wwwroot/vendor`, carregado sob demanda ao exportar PDF
- xUnit para testes
- Microsoft Playwright para testes reais de browser/download

Nao existe no codigo atual:

- Entity Framework
- banco relacional
- migrations
- repositories
- background services
- jobs
- escrita Firebase

## Arquitetura Resumida

Projetos:

- `EstacaoClimatica.Domain`: records de resposta da API.
- `EstacaoClimatica.Application`: regras de montagem, builders, utilitarios e abstracoes.
- `EstacaoClimatica.Infrastructure`: acesso HTTP ao Firebase.
- `EstacaoClimatica.Web`: ASP.NET Core Controllers e arquivos estaticos.
- `EstacaoClimatica.Tests`: testes xUnit de servicos, controllers, infraestrutura, contratos HTTP e browser real.

Fluxo:

```text
Browser/app.js
  -> GET /api/dashboard?date=dd-MM-yyyy
  -> DashboardController
  -> ServicoDashboardClimatico
  -> IRepositorioDadosClimaticos
  -> RepositorioDadosClimaticosFirebase
  -> FonteDadosFirebase
  -> Firebase Realtime Database
  -> Builders da Application
  -> DashboardResponse
  -> app.js renderiza UI
```

## Fluxo Principal

1. `app.js` inicia no `DOMContentLoaded` e mostra `#publicApp`.
2. `scripts/auth.js` observa Firebase Auth sem bloquear a consulta publica.
3. Sem usuario interno autorizado, CEP/cidade/localizacao usam `/api/public-weather/*` e o dashboard nao e consultado.
4. Uma unica resposta publica fornece a serie horaria usada nos quatro graficos de linha e na chuva: ultimas 24 horas observadas ate o instante atual e 12 horas previstas a partir da proxima hora cheia.
5. Com usuario autorizado, o frontend envia Firebase ID token e chama `/api/dashboard?date=...`.
6. `DashboardController` injeta `IServicoDashboardClimatico`.
7. `ServicoDashboardClimatico.ObterDashboardAsync` normaliza a data.
8. O servico solicita os dados ao `IRepositorioDadosClimaticos`.
9. `RepositorioDadosClimaticosFirebase` busca quatro caminhos Firebase em paralelo:
   - `historico/Temperatura`
   - `historico/NascePorDoSol`
   - `historico/Aquario`
   - `historico/AirQuality`
10. Builders montam Sala, Quarto, Aquario e Solar.
11. O frontend renderiza:
   - aba Estação (`Tab0`) ativa por padrão
   - cards contextuais de estação do ano e fase da lua, calculados localmente no frontend
   - cards globais e gráficos comparativos de temperatura/umidade
   - stats
   - graficos ambientais
   - tabela
   - heatmaps de Sala e Quarto
   - historico solar e ciclo solar dentro da aba Estação
   - indicadores do header na ordem: estação do ano, AQI, ciclo solar, fase da lua e relógio
   - indicador astronomico atual no header, sem texto interno, com horarios solares no tooltip/aria-label e popover de ciclo solar
   - indicador AQI estimado no header, com popover proprio
   - navegacao touch entre `Tab0`, `Tab1`, `Tab2` e `Tab3`, ignorando tabelas/heatmaps/areas com rolagem horizontal
   - controle de exportacao PDF/JSON
   - chat climatico em `/api/chat`

## Componentes Criticos

`src/EstacaoClimatica.Web/Program.cs`: registra DI, controllers e arquivos estaticos.

`src/EstacaoClimatica.Web/Controllers/DashboardController.cs`: expoe `/api/dashboard` e `/api/health`.

`src/EstacaoClimatica.Web/Controllers/ClimateQueryController.cs`: expoe catalogo, ultima medicao, historico paginado, series, resumo, AQI, solar, alertas, comparacoes e exportacoes JSON/CSV. Contrato completo em `docs/API_CONSULTAS.md`.

`src/EstacaoClimatica.Web/Controllers/ChatController.cs`: expoe `/api/chat` para o assistente climatico.

`src/EstacaoClimatica.Application/Services/ClimateDashboardService.cs`: orquestra leitura Firebase e montagem do dashboard.

`src/EstacaoClimatica.Application/Services/ClimateQueryService.cs`: centraliza validacao, aliases, paginacao e os dez endpoints de consulta sem duplicar as regras do dashboard.

`src/EstacaoClimatica.Application/Services/ClimateChatService.cs`: interpreta perguntas, calcula respostas com dados locais e usa IA apenas para classificar/redigir quando habilitada.

`src/EstacaoClimatica.Application/Abstractions/IClimateDataRepository.cs`: contrato para carregar os dados brutos do dashboard.

`src/EstacaoClimatica.Infrastructure/Repositories/FirebaseClimateDataRepository.cs`: busca os quatro ramos Firebase usados pelo dashboard.

`src/EstacaoClimatica.Infrastructure/Firebase/FirebaseDataSource.cs`: faz `GET {DatabaseUrl}/{path}.json` com cache curto por URL e retry/backoff simples.

`src/EstacaoClimatica.Application/Builders/EnvironmentDashboardBuilder.cs`: define metricas e graficos de Sala, Quarto e Aquario.

`src/EstacaoClimatica.Application/Builders/StatsBuilder.cs`: calcula media, minimo, maximo, delta e tendencia.

`src/EstacaoClimatica.Application/Builders/HeatmapBuilder.cs`: monta calendario mensal, heatmap horario e mapa semanal.

`src/EstacaoClimatica.Application/Builders/SolarDashboardBuilder.cs`: interpreta campos solares e gera historico/ciclo solar.

`src/EstacaoClimatica.Application/Configuration/FirebaseFields.cs`: centraliza nomes literais dos campos Firebase por dominio.

`src/EstacaoClimatica.Application/Configuration/AiOptions.cs`: configura IA/App Check; nenhuma chave secreta deve ser versionada.

`src/EstacaoClimatica.Infrastructure/AI/GeminiTextGenerationClient.cs`: cliente HTTP para gerar texto pelo modelo Gemini quando `ArtificialIntelligence:Enabled=true`.

`src/EstacaoClimatica.Application/Utilities/FirebaseRecordReader.cs`: interpreta a estrutura dinamica do Firebase.

`src/EstacaoClimatica.Domain/DashboardModels.cs`: contrato JSON consumido pelo frontend.

`src/EstacaoClimatica.Web/wwwroot/app.js`: orquestrador do frontend.

`src/EstacaoClimatica.Web/wwwroot/scripts/*`: modulos do frontend para configuracao, utilitarios, UI, PDF/JSON, estação/lua, solar, heatmaps, zoom e views.

`src/EstacaoClimatica.Web/wwwroot/scripts/views/station-view.js`: monta a aba Estação a partir de Sala, Quarto, Aquario, Solar e AQI ja retornados pela API.

`src/EstacaoClimatica.Web/wwwroot/style.css`: agregador CSS que importa os arquivos modulares de `wwwroot/styles`.

`src/EstacaoClimatica.Web/wwwroot/styles/*`: estilos modulares seguindo a estrutura do projeto original: tokens, base, header, layout, abas/toolbar, feedback, stats, charts, views avancadas, zoom, tabelas e responsivo.

`styles/responsive.css` preserva quatro colunas equivalentes de abas em uma linha exclusiva para celulares em paisagem com `orientation: landscape`, `max-height: 540px` e `max-width: 900px`. A toolbar de data, Hoje, formato PDF/JSON e exportacao ocupa a linha seguinte, sem rolagem horizontal. O comportamento de swipe, teclado e ARIA das abas nao muda. A regressao usa viewport `720x360` e data `03/06/2026` em `FrontendBrowserTests.cs`.

`src/EstacaoClimatica.Web/wwwroot/pdf-report.css`: estilos usados apenas na montagem do relatorio PDF.

`src/EstacaoClimatica.Web/wwwroot/vendor`: dependencias locais do frontend (`chart.umd.min.js`, `html2canvas.min.js`, `jspdf.umd.min.js`).

## Regras Principais

- Data Firebase usa formato `dd-MM-yyyy`.
- O input HTML usa `yyyy-MM-dd`; `app.js` converte para Firebase antes da chamada.
- Registros Firebase esperados: data -> hora -> item -> campos.
- Tabelas mostram no maximo 24 registros mais recentes.
- Valor nulo em tabela vira `--`.
- Sem metricas: media/min/max/delta nulos e tendencia estavel.
- Tendencia e calculada por delta:
  - sem registros suficientes: `--`.
  - `abs(delta) < 0.05`: estavel.
  - delta positivo: subindo.
  - delta negativo: caindo.
- Faixas de conforto:
  - temperatura/sensacao ambiental: 20°C a 26°C.
  - umidade: 40% a 60%.
  - temperatura do aquario: 25°C a 27°C.
- Unidades do aquario: temperatura em °C, TDS em ppm e Turbidez em NTU.
- Leituras brutas do aquario: TDS e dividido por 10; Turbidez e dividida por 1000 antes de stats, graficos e tabela.
- Sala/MQ135 usa `CO`, `CO2`, `Aceton`, `Alcohol`, `NH4` e `Toluen`; `Toluen` e exibido como Tolueno, em ppm e tambem e metrica consultavel pelo assistente.
- O assistente climatico usa `/api/chat`; com IA desativada responde pelo calculo local, e com IA ativada usa o Gemini apenas para classificar/redigir a partir dos resultados calculados em C#.
- O chat aceita filtro por hora (`14h`, `14:00`, `as 14`) e compara com chaves Firebase `HH-MM`; consultas pontuais retornam somente o valor da metrica naquele horario.
- O frontend envia `previousIntent` no `/api/chat` com a ultima intencao resolvida; o backend usa essa memoria curta apenas em perguntas de continuidade como `e no quarto?`, `e ontem?` ou `e a umidade?`, herdando ambiente, metrica, operacao, periodo, hora e faixa horaria quando fizer sentido.
- Perguntas de valor atual/agora/ultima medicao ou perguntas simples `qual o/a <metrica>` sem pedir media, maxima, minima, tendencia, faixa ou periodo inteiro usam operacao `ultima_medicao` e retornam a amostra mais recente do recorte consultado.
- O chat reconhece metricas exclusivas de ambiente: CO, CO2, Acetona, Alcool, Amonia, Tolueno e AQI/IAQ/qualidade do ar consultam a Sala/MQ135 mesmo quando a aba ativa e outra.
- O chat calcula AQI estimado no backend C# a partir dos gases da Sala/MQ135, usando as mesmas faixas do indicador do header; respostas podem incluir classificacao, impacto, dominante e subindices principais.
- O chat aceita faixa horaria (`entre 8h e 18h`) e limita medias, maximas, minimas, tendencia, faixa de conforto e analise horaria ao intervalo informado.
- O chat entende `ultimas 24 horas`/`ultimas 24h` como janela movel real terminando no horario atual aplicado a data selecionada.
- O chat entende `ultimos X dias` e numeros por extenso, calcula a janela a partir da data selecionada e limita pedidos acima de 30 dias com indicacao explicita.
- O chat entende `ultimas X horas`, inclusive 48 horas, como janela movel real; a quantidade nao e convertida em uma janela fixa de sete dias.
- Media, maxima, minima, delta e tendencia usam resultados locais especificos para a operacao pedida, sem retornar o bloco generico de media/min/max.
- Perguntas de faixa/conforto, extremos por horario e dia mais quente/frio sao resolvidas deterministicamente antes do classificador, evitando a classificacao indevida como comparacao entre dias.
- Os atalhos rapidos respeitam o ambiente ativo; na aba Estacao, Resumo, Maxima e Alertas consultam os ambientes internos e nao o ciclo solar.
- O chat entende comparacoes simples entre dias mencionados, como `ontem ou hoje`, `ontem e anteontem`, `maior que hoje` ou `mais quente que hoje`, e responde `comparar_dias` com o dia vencedor por maior media diaria, o dia de referencia e a diferenca entre medias.
- O chat responde perguntas de faixa/status/conforto para temperatura/sensacao, umidade e temperatura do Aquario, informando faixa usada, horas fora e pior horario fora.
- O chat responde consultas equivalentes aos heatmaps: dia do mes com maior/menor media, hora do dia mais alta/baixa no mes e ponto dia/hora do mapa semanal.
- O chat responde consultas solares: duracao do dia, ciclo solar do dia, maior/menor duracao de luz no ano por padrao, maior/menor duracao no mes quando mes for mencionado, tendencia e comparacao de nascer/por do sol na semana.
- Quando a pergunta tiver intencao solar, o chat forca a metrica `ciclo_solar`, mesmo se a classificacao da IA sugerir outra metrica.
- Exportacao PDF/JSON respeita a aba ativa: Estação exporta cards contextuais, 6 cards globais, gráficos comparativos e ciclo solar, sem tabela; Sala exporta resumo/graficos ambientais e tabela MQ135, sem solar; Quarto exporta resumo/graficos ambientais e tabela ambiental, sem solar; Aquario exporta apenas temperatura, PH, TDS e Turbidez, sem card/grafico solar.
- JSON inclui metadados, resumo, graficos, `tabela`, `tabelaResumida`, `tabelaDetalhada`, `dadosBrutos` e `dadosDaAba`.
- O ciclo solar do PDF e gerado em canvas offscreen usando os dados solares carregados e a mesma logica visual de `solar.js`; se nao houver dados solares, o card exibe estado vazio.
- Graficos com faixa de conforto usam escala Y derivada dos dados medidos; a faixa e recortada ao trecho visivel e nao altera zoom/escala.
- O overlay de zoom usa semantica de dialogo, nome acessivel, foco inicial e retorno do foco ao botao de origem, alem de fechamento por Esc, botao e fundo.
- Heatmaps usam medias por dia, hora e dia da semana/hora; o mapa semanal usa a semana da data selecionada, do domingo ate a data selecionada.
- Heatmaps so existem para Sala e Quarto.
- Graficos comuns de Sala, Quarto e Aquario usam janela movel das ultimas 24h terminando no horario atual aplicado a data selecionada; cards e tabelas continuam por data selecionada.
- A aba Estação (`Tab0`) e a aba padrao e centraliza solar, contexto anual/lunar e comparativos globais.
- O indicador de estação do ano e a fase da lua sao calculados localmente no frontend, sem API externa.
- O indicador astronomico fica sem texto interno; clique/toque abre popover com amanhecer, nascer do sol, zenite, por do sol, anoitecer, estado atual e duracao do dia.
- O chat trava a rolagem da pagina de fundo enquanto o painel esta aberto; apenas a area de mensagens deve rolar.
- Popovers do header sao mutuamente exclusivos: abrir qualquer popover contextual fecha os demais.
- Solar usa ultimos 365 dias.
- Zenite solar e calculado como ponto medio entre nascer e por do sol quando campo de zenite nao existe.

## Onde Estao as Coisas

- API: `src/EstacaoClimatica.Web/Controllers/DashboardController.cs`
- Configuracao Firebase: `src/EstacaoClimatica.Web/appsettings.json`
- Contrato da API: `src/EstacaoClimatica.Domain/DashboardModels.cs`
- Orquestracao: `src/EstacaoClimatica.Application/Services/ClimateDashboardService.cs`
- Repository: `src/EstacaoClimatica.Infrastructure/Repositories/FirebaseClimateDataRepository.cs`
- Acesso Firebase: `src/EstacaoClimatica.Infrastructure/Firebase/FirebaseDataSource.cs`
- Regras ambientais: `src/EstacaoClimatica.Application/Builders/EnvironmentDashboardBuilder.cs`
- Estatisticas: `src/EstacaoClimatica.Application/Builders/StatsBuilder.cs`
- Graficos: `src/EstacaoClimatica.Application/Builders/ChartSeriesBuilder.cs`
- Tabelas: `src/EstacaoClimatica.Application/Builders/TableDataBuilder.cs`
- Heatmaps: `src/EstacaoClimatica.Application/Builders/HeatmapBuilder.cs`
- Solar: `src/EstacaoClimatica.Application/Builders/SolarDashboardBuilder.cs`
- Frontend: `src/EstacaoClimatica.Web/wwwroot`
- Modulos JS: `src/EstacaoClimatica.Web/wwwroot/scripts`
- Testes: `tests/EstacaoClimatica.Tests/ClimateDashboardServiceTests.cs`, `tests/EstacaoClimatica.Tests/ClimateChatServiceTests.cs`, `tests/EstacaoClimatica.Tests/FrontendStructureTests.cs` e `tests/EstacaoClimatica.Tests/FrontendBrowserTests.cs`

## Arquivos Criticos

1. `DashboardModels.cs`: contrato backend/frontend.
2. `DashboardController.cs`: rotas publicas da API.
3. `IClimateDataRepository.cs` e `FirebaseClimateDataRepository.cs`: carregamento dos dados Firebase.
4. `app.js` e `scripts/*`: consomem contrato JSON, renderizam Estação/Sala/Quarto/Aquario, controlam indicadores do header, touch e geram PDF/JSON.
5. `EnvironmentDashboardBuilder.cs`: nomes de campos Firebase por ambiente.
6. `SolarDashboardBuilder.cs`: nomes alternativos de campos solares.
7. `FirebaseRecordReader.cs`: formato de dados Firebase.
8. `appsettings.json`: URL, caminhos Firebase, cache curto e retry.
9. `style.css`, `styles/*`, `pdf-report.css` e `vendor/*`: visual, exportacao PDF/JSON e bibliotecas visuais.

## Resumo para IA

Antes de alterar codigo, verifique se a mudanca afeta o contrato JSON entre `DashboardModels.cs` e `app.js`. Preserve nomes de campos do Firebase exatamente como aparecem nos builders. Para mudancas de calculo, atualize ou adicione testes em `ClimateDashboardServiceTests.cs`. Nao introduza EF ou banco relacional: o banco atual e Firebase Realtime Database via REST. Nao mova logica de negocio para controllers ou `Program.cs`; use builders, servicos da camada Application ou repositories de Infrastructure.

## Insights ambientais compartilhados

- `ServicoAnalisesAmbientais` centraliza ponto de orvalho por Magnus, risco estimado de mofo/condensacao, chuva nas proximas 6h, indice UV e recomendacoes de ventilacao.
- `ServicoClimaPublico` solicita da Open-Meteo ponto de orvalho, precipitacao, probabilidade de chuva, vento/rajadas, UV atual e agregados diarios de 15 dias; a resposta normalizada inclui `Insights` ja classificados e `PrevisaoQuinzeDias`.
- `DashboardResponse.Insights` traz ventilacao interna e risco estimado para Sala e Quarto. Temperatura e umidade usam as ultimas medicoes globais, nao apenas a data selecionada.
- `public-weather.js` monta uma unica janela temporal compartilhada por Temperatura, Sensacao Termica, Umidade e Pressao. A parte medida e solida, a previsao e mais clara e tracejada, e o limite recebe o marcador `Agora`.
- O inicio da previsao e sempre a proxima hora cheia, sem duplicar a medicao atual. Valores ausentes continuam `null`; no mobile apenas a quantidade de rotulos do eixo X e reduzida.
- A chamada Forecast usa `timezone=auto`, `past_hours=24`, `forecast_hours=13` e `forecast_days=15`. Os campos diarios sao temperatura minima/maxima, precipitacao, probabilidade maxima de chuva, UV maximo e codigo meteorologico.
- `PrevisaoDiaria` usa nomes internos PT-BR, limita a colecao a 15 datas ordenadas e marca `MaiorIncerteza` do 8o dia em diante. Arrays externos menores produzem `null`, nunca zero.
- No modo publico, a ordem e: busca, cards atuais, estacao/lua, recomendacoes, cinco graficos curtos, previsao de 15 dias e ciclo solar. Os tres graficos diarios e a faixa compacta compartilham selecao por mouse, toque e teclado.
- `zoom.js` preserva datasets, tracejado, origem Medido/Previsao, marcador, tooltips e unidades. O grafico solar nao participa dessa regra.
- A busca por cidade usa `GET /api/public-weather/cities?term=...`; a cidade escolhida segue por `POST /api/public-weather/city` e reutiliza `BuscarPorCoordenadasAsync`.
- `CidadePublica` representa id, nome, estado, regiao administrativa, pais, codigo do pais, coordenadas, fuso e rotulo. Homonimos incluem a regiao entre parenteses quando ela difere da cidade e do estado.
- O frontend preserva separadamente os valores digitados em CEP e Cidade, move o foco para a primeira opcao ambigua e usa a mesma sequencia para impedir que respostas antigas substituam a pesquisa atual.
- No modo interno, chuva e UV permanecem indisponiveis ate o clique em `Usar localizacao`; depois, a ventilacao combina sensores da Sala com as condicoes externas.
- Localizacao nunca e solicitada automaticamente e latitude/longitude ficam somente em memoria durante a pagina atual.
- Ausencia de previsao, UV ou ponto de orvalho permanece `null`/`Indisponivel`; nao deve virar zero.
- Risco de mofo, ponto de orvalho e ventilacao sao estimativas orientativas, nao diagnostico nem medicao certificada.
- O card `Ciclo Solar do Dia` mostra `Duracao do dia: <h>h<mm>` quando nascer e por do sol estao disponiveis. No clima publico usa `daylightDuration` da mesma resposta solar e recorre a diferenca entre por e nascer; no interno usa a diferenca dos eventos.
- Os cards ambientais comecam diretamente apos o bloco anterior, sem faixa `Leitura pratica`/`Condicoes e recomendacoes`/`Clima externo atual`. A acao privada de localizacao fica compacta no card de chuva.
IA/App Check ficam em `ArtificialIntelligence`/`OpcoesIa`; segredo de IA deve vir de ambiente/User Secrets/cofre externo.

## Sincronizacao e chuva (2026-09)

- `ChartPoint.Instante` transporta o instante Firebase em ISO local (`yyyy-MM-ddTHH:mm:ss`); entrada invalida produz `null`, sem remover o ponto ou converter valor ausente em zero.
- `scripts/sincronizacao-graficos.js` expoe `window.SincronizadorGraficos`. Os grupos `sala`, `quarto`, `aquario`, `estacao` e `publico` correlacionam tooltip por instante exato ou pelo ponto mais proximo, limitado a 45 minutos. Solar e zoom ficam fora dos grupos.
- `scripts/chuva.js` expoe `window.ClimaChuva`: 24 horas observadas, 12 horas previstas desde a proxima hora cheia, barras observadas/previstas, chance tracejada e marcador atual.
- A consulta publica organiza Temperatura/Sensacao/Umidade na primeira linha e Pressao/Chuva na segunda em telas acima de 1100 px; de 641 a 1100 px usa duas colunas e ate 640 px usa carrossel. A Estacao mantem o card de chuva visivel, mas so consulta dados externos por acao explicita.
- PDF/JSON da Estacao incluem clima externo apenas quando ja existe em memoria, sem nova API. O JSON remove coordenadas, precisao e CEP; sem consulta, o contrato anterior permanece igual.
- Tooltip do historico solar usa modo `index` e ordem vertical real. Em ate 640 px, o chip de duracao fica no fluxo entre rotulo e canvas.
- Regressao obrigatoria: `03/06/2026`, desktop, larguras 320/360/390 e paisagem `720x360`.

## Revisao responsiva de UI/UX (2026-09-22)

- O modo publico usa `main#publicApp`, `h1#publicPageTitle` e estado compacto `has-results`. Em ate 640 px, o hero some depois de uma consulta; `Alterar local` restaura o formulario e devolve o foco ao campo de busca.
- Cards publicos usam duas colunas no celular, com AQI em largura total. Insights, os cinco graficos curtos e os tres graficos diarios usam faixas horizontais acessiveis com `scroll-snap`; ciclo solar continua em bloco de largura total.
- `scripts/chuva.js` cria no celular o seletor `Precipitacao`/`Chance`. Precipitacao mostra as duas barras e a escala em mm; Chance mostra apenas a linha e a escala percentual. Acima de 640 px, o controle e removido e os tres datasets, legenda, duas escalas e 13 ticks sao restaurados.
- O controle de chuva e removido no `beforeDestroy`, nao duplica em novas consultas e preserva `window.ClimaChuva`, marcador atual, nulos, sincronizacao, zoom e PDF.
- Em ate 640 px, o header recolhe ao rolar para baixo e reaparece ao subir, voltar ao topo, focar um item ou abrir um popover. O limiar e 8 px e o estado oculto e limpo ao sair da media query.
- O breakpoint de tablet e `641px` a `960px`: quatro abas em uma linha e toolbar em uma linha separada, com alvos de toque de pelo menos 44 px.
- A assistente usa acionador circular de 48 px em celular/tablet. Em celular, o painel e um bottom sheet fixo definido no fim de `chat.css`, com safe area e limite em `dvh`.
- O chip AQI movel mantem o medidor e sobrepoe prefixo e valor em camadas de alto contraste; o valor externo do modo publico tambem aparece no chip.
- Os canvases de chuva publico e interno usam 200 px no celular. Os eventos solares usam raio visual 7 px, area adicional de toque 24 px e hover 9 px, por helper compartilhado entre os dois modos.
- O mapa semanal usa primeira coluna sticky opaca de 48 px, incluindo o canto superior, faixa que cobre o gap e divisor. A primeira celula horaria nao arredonda o lado junto ao dia.
- A sincronizacao temporal ignora ativacao programatica no grafico de origem; apenas os graficos irmaos recebem `setActiveElements`, enquanto `mouseout` continua limpando todo o grupo.
- Regressao visual obrigatoria: `03/06/2026` em `1440x1000`, `960x1024`, `768x1024`, `390x844`, `360x800` e `720x360`; `320x700` permanece como limite adicional.
