# Roteiro de migracao das alteracoes recentes do C# para o Web original

Data da analise: 22/10/2026.

## Objetivo e limites

Migrar os ajustes recentes de chuva publica, cards, zoom e testes para o projeto Web original, preservando sua arquitetura e as melhorias que ja existem nele. Este documento e um plano: nenhuma alteracao no original foi executada nesta etapa.

- Origem das alteracoes: este repositorio C#.
- Destino: projeto Web original, mantido em outro repositorio; configure sua localizacao antes de executar os comandos dele.
- Migrar comportamentos, nao substituir arquivos completos nem portar classes C# para JavaScript.
- Preservar contratos externos, Firebase, autenticacao, exportacoes, IDs existentes e nomes globais do destino.
- Manter nomenclatura interna em PT-BR. Nao instalar/atualizar dependencias, formatar todo o projeto ou incluir refatoracoes alheias.

## 1. Situacao verificada no codigo

| Item | Situacao no original | Acao |
| --- | --- | --- |
| Seis graficos publicos, chuva separada e modos fixos | Ja implementados em `scripts/views/public-weather-view.js` e `scripts/charts/rain.js` | Preservar; testar, sem recriar |
| Previsao de 15 dias e tres graficos diarios | Ja implementados | Validar contrato, cores, nulos, incerteza e sincronizacao |
| Grade larga de tres colunas, intermediaria de duas | Ja implementada | Verificar alturas entre as duas linhas; aplicar apenas o ajuste necessario |
| Historico da chance de chuva | Original: 12h anteriores + 12h futuras. C#: 24h anteriores + 12h futuras | Migrar a janela de 24h apenas no publico, conforme o roteiro consolidado |
| Visibilidade no zoom | Original copia `serieGrafico.hidden`; C# usa `isDatasetVisible(indice)` | Migrar visibilidade efetiva, incluindo ocultacao pela legenda |
| Configuracao das series no zoom | Original usa lista de propriedades sem `segment` e `borderRadius` | Preservar propriedades necessarias, cores e segmentos da previsao diaria |
| Opcoes do zoom publico | Original reconstroi opcoes por ID; C# reutiliza opcoes do grafico | Adaptar sem perder callbacks, eixos ou metadados e sem reativar resize/sincronizacao de fundo |
| Cards da Estacao | Original ja coloca titulo/status em linhas separadas e permite quebra | Preservar essa melhoria; estender apenas onde faltar no componente compartilhado |
| Nulos nos cards publicos | Original ja usa `Number.isFinite(valor)` sem converter `null` em zero | Nao trocar o formatador; adicionar regressao |
| Foco e scroll do zoom | Original ja usa `preventScroll` e aguarda CSS sob demanda | Preservar o carregador e validar no primeiro e nos proximos usos |

Observacao: a documentacao do original descreve uma janela comum de 24h, mas `rain.js` define `HORAS_PROBABILIDADE_ANTERIORES = 12` e a view exibe 12h para chance. Corrigir codigo, titulo e documentacao juntos, sem alterar silenciosamente o privado/PDF.

## 2. Correspondencia de arquivos

Os caminhos nesta tabela sao relativos a cada raiz indicada acima.

| Referencia no C# | Destino no original |
| --- | --- |
| `src/EstacaoClimatica.Web/wwwroot/scripts/chuva.js` | `scripts/charts/rain.js` |
| `src/EstacaoClimatica.Web/wwwroot/scripts/public-weather.js` | `scripts/views/public-weather-view.js` |
| `src/EstacaoClimatica.Web/wwwroot/scripts/zoom.js` | `scripts/charts/zoom.js` e opcoes fornecidas pelas views/main |
| `src/EstacaoClimatica.Web/wwwroot/scripts/sincronizacao-graficos.js` | `scripts/charts/chart-sync.js` |
| `src/EstacaoClimatica.Web/wwwroot/styles/stats.css` | `styles/stats.css` |
| `src/EstacaoClimatica.Web/wwwroot/styles/public-weather.css` | `styles/public-weather.css` |
| `src/EstacaoClimatica.Infrastructure/External/PublicWeatherService.cs` | `scripts/external/external-weather-service.js`, somente como referencia de contrato |
| `tests/EstacaoClimatica.Tests/FrontendBrowserTests.cs` | Testes existentes em `tools/`, adaptando cenarios para JavaScript/Playwright |

Nao renomear `ClimateChuva`, `ClimateChartSync`, `ClimateZoom`, `ClimateCharts` ou `AppConfig.colors` para os nomes usados no C#. Preservar `publicChartRainPrecipitation` no original; o C# usa `publicChartRain`, mas isso nao justifica mudar o ID do destino. Preservar tambem `publicChartRainProbability`.

O original usa `indiceUv` no modelo diario consumido pela view; o C# usa `indiceUvMaximo`. Adaptar os testes, sem transplantar a nomenclatura JSON de um projeto para o outro.

## 3. Etapa inicial: estabelecer uma base validada

1. Ler novamente `docs/AI_QUICK_CONTEXT.md`, `docs/BUSINESS_RULES.md`, a documentacao arquitetural aplicavel e `package.json` do destino. O `README.md` na raiz do original nao foi encontrado nesta analise; localizar o indice de documentacao existente, sem inventar comandos.
2. Registrar o estado dos arquivos que serao tocados e preservar alteracoes locais. Usar backup/snapshot ou controle de versao disponivel; nao fazer reset ou commit automatico.
3. Revalidar o C# antes de usa-lo como referencia. A afirmacao historica sobre falhas anteriores nao possui log associado neste roteiro; o estado da suite deve ser obtido pela execucao atual.
4. No C#, executar `dotnet build EstacaoClimatica.sln` e `dotnet test EstacaoClimatica.sln`.
5. No original, executar os comandos da etapa 8 e registrar falhas preexistentes separadamente.
6. Preparar a mesma massa deterministica em ambos: referencia `03/06/2026 16:00`, serie horaria de 24h anteriores + 12h futuras, 15 dias diarios a partir de 03/06, zeros reais, nulos e arrays parciais. Fixar relogio/fuso nos testes, sem alterar o relogio do sistema.

Aceite: compilacao e testes de origem concluidos; baseline do destino registrado; nenhuma falha preexistente mascarada.

## 4. Etapa 1: corrigir o zoom

1. Em `scripts/charts/zoom.js`, usar a visibilidade efetiva do Chart.js, nao apenas o campo `hidden` do dataset.
2. Preservar `segment`, `borderRadius`, cores, preenchimento, ordem, tipos, eixos, valores nulos e metadados usados pelos plugins. Copiar arrays/dados mutaveis para que interagir com o zoom nao altere o grafico original.
3. Adaptar `obterOpcoesZoomPublico` para conservar titulo/data do tooltip, unidades, ordenacao e escalas do grafico de origem. Se reutilizar opcoes, copia-las sem remover callbacks do objeto original.
4. Nao executar no overlay o resize que restaura series ou insere o seletor de chuva. Nao vincular o zoom ao grupo de sincronizacao da tela de fundo.
5. Preservar marcador Agora e horarios/tipos ativos. Manter os caminhos especificos dos graficos solares e das faixas de conforto.
6. Preservar carregamento assincrono do CSS, foco acessivel, Escape, fechamento pelo fundo, foco de retorno e `preventScroll`.

Aceite: ocultar uma serie pela legenda, ampliar e redimensionar nao a torna visivel; tracejado dos dias 8 a 15 continua no zoom; tooltips conservam data/unidades; pagina e carrossel nao mudam de posicao; fechar devolve o foco sem alterar o grafico de fundo.

## 5. Etapa 2: equalizar chuva publica e grade

1. Manter os dois cards e modos fixos ja existentes no original.
2. Ajustar somente o modo publico de chance para usar 24h anteriores, conservando as 12h futuras. Nao apenas trocar a constante global: `janela.chance` tambem participa do modo combinado e poderia mudar o privado/PDF.
3. Reutilizar a estrutura de janela existente e o parametro/modo publico para delimitar a mudanca. Manter o comportamento combinado padrao de chamadas privadas e de exportacao.
4. Atualizar o titulo da chance para refletir 24h anteriores + 12h futuras.
5. Preservar o estado vazio de cada grafico de forma independente. Uma serie totalmente nula nao deve apagar a outra, os cards atuais ou os graficos diarios. Zero e dado valido.
6. Sincronizar os seis graficos por horario, nao presumir que o mesmo indice representa a mesma hora em series parciais. Conferir marcador Agora, dados observados e futuros.
7. Validar tres colunas acima de 1100 px, duas de 641 a 1100 px e carrossel ate 640 px. Se as linhas tiverem alturas diferentes, aplicar a regra de alturas uniformes apenas na grade publica, preservando `margin-top: auto` dos canvases ja presente no original.

Aceite: seis cards na ordem Temperatura, Sensacao, Umidade, Pressao, Precipitacao, Chance; duas linhas completas no desktop; nenhuma alternancia Precipitacao/Chance no publico; privado/PDF iguais ao baseline.

## 6. Etapa 3: cards e previsao diaria

1. Manter as correcoes ja presentes em `.station-summary-card`; nao substituir `styles/stats.css` pelo arquivo do C#.
2. Testar os demais `.stats-card__header`, titulos, status e valores com texto longo. Onde faltar, aplicar quebra/linha propria ao componente compartilhado no mobile, sem alterar o desktop.
3. Testar AQI com `Insalubre para grupos sensiveis com exposicao prolongada` em 320, 360 e 390 px. Nenhum titulo/status/valor pode sobrepor ou ficar cortado.
4. Manter os formatadores do original que ja distinguem `null` de zero; testar temperatura e AQI ausentes.
5. Validar o contrato Open-Meteo existente: `timezone=auto`, `past_hours=24`, `forecast_hours=13`, `forecast_days=15`; campos diarios de min/max, precipitacao, probabilidade maxima, UV e codigo. Nao criar uma segunda consulta desnecessaria.
6. Validar 15 dias ordenados, arrays desiguais sem deslocar datas/valores, nulos preservados e incerteza a partir do oitavo dia. Nao alterar valores para representar incerteza.
7. Validar selecao entre faixa e tres graficos por mouse/toque/teclado, limpeza ao sair, cores definidas e tooltips ordenados. Corrigir apenas divergencias reproduzidas.
8. Manter ordem: busca, cards atuais, estacao/lua, recomendacoes, seis graficos curtos, 15 dias, ciclo solar. Falha/ausencia da previsao diaria nao pode apagar dados atuais.

Aceite: cards sem sobreposicao; previsao diaria completa ou parcial sem dados inventados; funcionalidades existentes preservadas.

## 7. Etapa 4: portar os cenarios de teste

Reutilizar `tools/testar-modo-publico.mjs`, `testar-chuva.mjs`, `testar-sincronizacao-graficos.mjs`, `testar-zoom-geometria.mjs`, `testar-ui-ux.mjs` e os testes de exportacao. Nao copiar xUnit/C# literalmente e nao trocar testes comportamentais por verificacoes apenas de strings.

- Seis graficos e alturas uniformes no desktop; duas colunas no tablet; carrossel mobile sem overflow da pagina.
- Sincronizacao por horario a partir de ambos os graficos de chuva, incluindo series parciais, mouse e toque.
- Precipitacao inteiramente nula com chance disponivel e o inverso; zeros reais continuam visiveis.
- Zoom apos ocultar serie na legenda, com comparacao de dados, cores, eixos, segmentos, marcador e scroll antes/depois.
- Texto longo nos cards compartilhados e AQI publico; temperatura/AQI nulos exibem `--`.
- Previsao de 15 dias: limites, arrays desalinhados, incerteza, teclado, tooltips, ausencia, erro e nova tentativa.
- Fluxos de CEP, cidade, candidatos homonimos e localizacao; permissao recusada e falha de conexao.
- Modo privado autenticado de teste: abas, swipe, teclado, data, chuva combinada e exportacoes PDF/JSON.
- PDF sem previsao diaria; JSON preserva medicao/previsao e nulos, sem vazar localizacao.

Capturar screenshots em 1440x900, 1024x768, 390x844, 360x800 e 320x568. Incluir 720x360 para proteger as abas privadas em paisagem. Armazenar evidencias por projeto e viewport, identificando dados simulados e data-base 03/06/2026.

## 8. Comandos e criterio de conclusao

Comandos existentes em `package.json`, executar na raiz do original. Comecar pelos testes do grupo modificado, depois executar a regressao completa:

```powershell
npm run validate
npm run lint
npm run test:public
npm run test:rain
npm run test:chart-sync
npm run test:zoom
npm run test:ui-ux
npm run test:accessibility
npm run test:pdf-rain
npm run test:export
npm test
```

Confirmar nos scripts quais testes usam navegador real e quais verificam apenas contratos. Executar tambem as jornadas visuais com Playwright ja instalado; nao considerar uma verificacao estatica equivalente a teste no navegador. Se faltar navegador/dependencia, registrar o bloqueio, sem instalar automaticamente.

Depois de cada grupo: executar os testes associados, conferir as evidencias e revisar o diff. Em regressao, corrigir ou desfazer apenas o grupo da migracao, preservando mudancas anteriores do usuario. Nao avancar acumulando falhas desconhecidas.

## 9. Documentacao e entrega

Atualizar no destino `docs/AI_QUICK_CONTEXT.md`, `docs/BUSINESS_RULES.md` e os mapas arquiteturais/de dependencias que existirem. Registrar janela publica de chance 24h + 12h, preservacao privada/PDF, metadados/visibilidade do zoom, cards mobile, IDs mantidos e testes realmente executados.

Entregar lista dos arquivos alterados, comparacao antes/depois, resultados dos comandos, screenshots e eventuais diferencas intencionais. Nao afirmar equivalencia total apenas por compilacao ou testes com mocks.

## Limites desta analise

Este roteiro foi produzido por leitura do codigo/documentacao dos dois projetos em 22/10/2026. Nao houve migracao para o original nesta etapa. Os comandos do projeto Web original dependem do outro repositorio e nao sao executaveis apenas nesta raiz; autenticacao real, servicos externos e dispositivos fisicos continuam dependendo de verificacao especifica.
