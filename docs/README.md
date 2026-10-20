# Estação Climática

Dashboard para acompanhamento da aba global Estação, dados climáticos de Sala, Quarto, Aquário e eventos solares. O projeto original era uma aplicação estática em HTML/CSS/JavaScript que lia o Firebase Realtime Database diretamente no navegador. A versão atual é uma solução C#/.NET 8 com backend ASP.NET Core usando Controllers, mantendo o comportamento visual/funcional do original sempre que possível.

## Requisitos

- .NET SDK 8 ou superior
- Acesso à internet para consultar o Firebase Realtime Database
- Navegador moderno
- Bibliotecas de frontend já versionadas em `src/EstacaoClimatica.Web/wwwroot/vendor`
- Microsoft Edge/Chromium disponível para os testes Playwright de frontend

## Estrutura

```text
src/
  EstacaoClimatica.Domain/          Modelos de resposta e DTOs de domínio
  EstacaoClimatica.Application/     Regras de datas, estatísticas, tabelas, gráficos e solar
  EstacaoClimatica.Infrastructure/  Cliente Firebase via REST
  EstacaoClimatica.Web/             ASP.NET Core + frontend estático modular em wwwroot
tests/
  EstacaoClimatica.Tests/           Testes automatizados das regras principais e estrutura do frontend
```

## Configuração

As configurações principais ficam em:

```text
src/EstacaoClimatica.Web/appsettings.json
```

Configurações migradas:

- `Firebase:DatabaseUrl`
- `Firebase:Paths:Room`
- `Firebase:Paths:Solar`
- `Firebase:Paths:Aquarium`
- `Firebase:Paths:LivingRoom`
- `Firebase:CacheDurationSeconds`
- `Firebase:MaxRetryAttempts`
- `ArtificialIntelligence:Enabled`
- `ArtificialIntelligence:Provider`
- `ArtificialIntelligence:Model`
- `ArtificialIntelligence:MaxPeriodDays`
- `ArtificialIntelligence:RequireAppCheck`
- `ArtificialIntelligence:AppCheck`
- `PublicWeather:BrasilApiCepUrl`
- `PublicWeather:ViaCepUrl`
- `PublicWeather:OpenMeteoGeocodingUrl`
- `PublicWeather:OpenMeteoForecastUrl`
- `PublicWeather:OpenMeteoAirQualityUrl`
- `InternalAccess:Enforce`
- `InternalAccess:AuthorizedEmails`
- `Colors`

O frontend usa Firebase Auth para login Google opcional. Todas as rotas internas em `/api`, incluindo dashboard, chat e consultas climaticas, validam o Firebase ID token no backend e aceitam somente os e-mails configurados em `InternalAccess:AuthorizedEmails`. Apenas `/api/health` e `/api/public-weather/*` sao anonimas.

A integração de IA fica desativada por padrão. O chat já funciona com cálculo local em C#; quando `ArtificialIntelligence:Enabled=true`, o backend pode usar o modelo configurado para classificar/redigir a resposta. Chaves secretas de IA devem ser configuradas por variáveis de ambiente, User Secrets ou cofre externo, nunca em `wwwroot` ou em `appsettings.json` versionado. O App Check está modelado como configuração, mas o enforcement só deve ser ativado depois de validar os tokens no domínio de produção.

## Como Rodar

```powershell
dotnet restore EstacaoClimatica.sln
dotnet run --project src/EstacaoClimatica.Web/EstacaoClimatica.Web.csproj
```

Depois, abra a URL indicada pelo ASP.NET Core. Em geral:

```text
http://localhost:5000
```

Também é possível consultar a API diretamente:

```text
GET /api/health
GET /api/dashboard?date=03-06-2026
GET /api/environments
GET /api/environments/sala/measurements?from=03-06-2026&to=03-06-2026
GET /api/dashboard/summary?date=03-06-2026
POST /api/comparisons
GET /api/public-weather/cep/01001000
GET /api/public-weather/cities?term=Gramado
POST /api/public-weather/city
GET /api/public-weather/location?latitude=-23.5505&longitude=-46.6333
POST /api/chat
```

A data da API usa o mesmo formato do Firebase: `dd-MM-yyyy`. Os dez endpoints de consulta estao documentados em [`API_CONSULTAS.md`](API_CONSULTAS.md).

## Como Testar

```powershell
dotnet test EstacaoClimatica.sln
```

Os testes incluem servicos e controllers dos dez endpoints de consulta, autorizacao HTTP, qualidade dos sensores, validacao estrutural do frontend e Playwright com browser real para renderizacao DOM, acessibilidade, navegacao, tabelas e downloads PDF/JSON. A validacao atual possui 157 testes.

## Como Compilar

```powershell
dotnet build EstacaoClimatica.sln
```

## Decisões Técnicas

- Foi usado .NET 8 com ASP.NET Core.
- As rotas principais ficam em `DashboardController`, `ClimateQueryController`, `ChatController` e `PublicWeatherController`.
- Acesso ao Firebase foi movido para o backend em `EstacaoClimatica.Infrastructure`.
- O carregamento dos dados brutos do dashboard passa por `IRepositorioDadosClimaticos` e `RepositorioDadosClimaticosFirebase`.
- O acesso Firebase usa cache curto por URL e retry/backoff simples em `FonteDadosFirebase`.
- Os nomes literais de campos Firebase ficam centralizados em `CamposFirebase`.
- A configuração de IA/App Check fica em `OpcoesIa`, sem chave secreta no frontend.
- O assistente climático fica em `/api/chat`; o frontend envia pergunta, aba ativa, data selecionada e, quando houver, a última intenção resolvida para memória curta de conversa.
- O assistente segue fluxo em duas etapas: classifica intenção, valida ambiente/data/métrica em C# e calcula localmente antes de usar IA apenas para redação quando habilitada.
- Regras principais foram migradas para C# em `ServicoDashboardClimatico`.
- Consultas historicas, series, resumos, AQI, solar, alertas, comparacoes e exportacoes server-side ficam centralizadas em `ServicoConsultasClimaticas`.
- O frontend novo em `wwwroot` renderiza dados já processados pela API e foi dividido em `scripts/` por responsabilidade, espelhando o projeto original.
- Chart.js foi mantido no cliente porque é uma dependência visual, não regra de negócio.
- Chart.js fica carregado na tela; html2canvas e jsPDF ficam em `wwwroot/vendor` e são carregados sob demanda apenas na exportação PDF.
- Entity Framework Core não foi usado porque o banco atual não é relacional; o projeto usa Firebase Realtime Database.
- Os arquivos HTML/JS/CSS originais foram removidos da raiz após a migração; a versão ativa fica em `src/EstacaoClimatica.Web/wwwroot`.

## Funcionalidades Migradas

- Seleção de data no formato compatível com Firebase.
- Consulta dos caminhos Firebase originais:
  - `historico/Temperatura`
  - `historico/NascePorDoSol`
  - `historico/Aquario`
  - `historico/AirQuality`
- Estatísticas de média, mínimo, máximo, delta e tendência.
- Qualidade operacional por sensor, com cobertura, criticidade, repeticao, saltos, desatualizacao e estado offline.
- Tabelas com até 24 registros, inversao de ordem, contagem, CSV e destaque de leituras suspeitas/criticas.
- Séries para gráficos de Sala, Quarto e Aquário.
- Aba `Estação` (`Tab0`) ativa por padrão, com cards contextuais de estação do ano/fase da lua, 6 cards globais, gráficos comparativos e ciclo solar centralizado.
- Histórico solar e ciclo solar do dia na aba Estação.
- Header com chips na ordem: estação do ano, AQI, ciclo solar, fase da lua e relógio.
- Indicador astronômico no header com chip sem texto interno, tooltip/acessibilidade, popover de ciclo solar e fallback 06:00-18:00.
- Estação do ano e fase da lua calculadas localmente no frontend, sem API externa.
- Indicador AQI estimado da Sala/MQ135 no header, com classificação, dominante, horário e subíndices principais em popover.
- Popovers do header são mutuamente exclusivos: abrir AQI fecha o ciclo solar e abrir o ciclo solar fecha AQI.
- Visualizações climáticas avançadas da Sala e do Quarto: calendário mensal, heatmap horário e mapa semanal reiniciando no domingo da semana selecionada.
- Gráficos comuns de Sala, Quarto e Aquário usam janela móvel das últimas 24h, terminando no horário atual aplicado à data selecionada; cards e tabelas seguem filtrados apenas pela data selecionada.
- Zoom dos gráficos, faixas de conforto e visualizações solares.
- Faixas de conforto: temperatura/sensação 20°C a 26°C, umidade 40% a 60% e aquário 25°C a 27°C.
- Navegação entre abas por gesto touch horizontal no fluxo Estação, Sala, Quarto e Aquário.
- Navegacao acessivel entre abas por setas, `Home` e `End`, com foco movel e paineis associados por ARIA.
- Exportação da aba ativa para PDF ou JSON: Estação exporta contexto global/solar sem tabela; Sala e Quarto exportam ambiente+tabela sem solar; Aquário exporta apenas temperatura, PH, TDS e Turbidez.
- Exportacao com progresso anunciado, timeout de 45 segundos, mensagem de erro e tentativa novamente.
- Ciclo solar do PDF gerado offscreen pela mesma lógica visual do gráfico da tela, com estado vazio quando não houver dados solares.
- Cache curto nas leituras Firebase.
- Retry/backoff simples no HTTP Firebase.
- Campos Firebase centralizados em constantes.
- Sala/MQ135 exibe `CO`, `CO2`, `Aceton`, `Alcohol`, `NH4` e `Toluen`; `Toluen` aparece como Tolueno em `ppm`.
- Assistente climático migrado para C#/.NET, com resposta local por padrão e IA opcional no backend.
- O chat aceita filtro por hora específica, como `14h` ou `14:00`, comparando com chaves Firebase `HH-MM` e retornando somente o valor daquele horário.
- O chat mantém memória curta de ambiente, métricas, operação, hora e faixa horária para perguntas de continuidade como `e no quarto?`, `e ontem?` ou `e a umidade?`.
- O chat interpreta `agora`, `valor atual`, `última medição` e perguntas simples `qual o/a <métrica>` como consulta da última amostra disponível no recorte consultado.
- O chat redireciona métricas exclusivas para o ambiente correto: CO, CO2, Acetona, Álcool, Amônia, Tolueno e AQI/IAQ pertencem à Sala/MQ135 mesmo quando a aba ativa é outra.
- O chat calcula AQI/IAQ/qualidade do ar em C# com os gases da Sala/MQ135, incluindo classificação, impacto, dominante e subíndices.
- O chat responde consultas de faixa horária, últimas 24h reais, status de faixa de conforto, análise por horário e perguntas equivalentes aos heatmaps.
- O chat responde ciclo solar e comparações solares: duração do dia, maior/menor duração de luz, tendência e comparação de nascer/pôr do sol.
- Preparação segura para Firebase AI Logic/App Check, sem chave secreta no frontend.
- Base backend do modo público com BrasilAPI, fallback ViaCEP, Open-Meteo Forecast, Air Quality e Geocoding.
- Contrato público normalizado para cards, séries horárias, AQI, ciclo solar e previsão diária de 15 dias, sem persistência de coordenadas.
- Consulta publica protegida contra respostas fora de ordem, com mascara/validacao acessivel de CEP/cidade, bloqueio durante carregamento e cache de sessao sanitizado.
- Tela pública inicial por CEP, cidade brasileira ou localização, com os mesmos cards, contexto sazonal/lunar e cinco gráficos do original.
- Pesquisa de cidade retorna ate cinco candidatos brasileiros, prioriza correspondencia exata sem diferenciar acentos/caixa e apresenta a regiao administrativa para desambiguar homonimos.
- Os graficos publicos de Temperatura, Sensacao Termica, Umidade e Pressao combinam as ultimas 24 horas observadas com as proximas 12 horas previstas, iniciadas na proxima hora cheia.
- A chamada Open-Meteo usa `past_hours=24`, `forecast_hours=13`, `forecast_days=15` e os agregados diarios de temperatura minima/maxima, chuva, UV e codigo meteorologico.
- A previsao publica de 15 dias inclui faixa compacta e graficos sincronizados de temperatura, chuva e UV. Do 8o dia em diante, fundo, opacidade e tracejado indicam maior incerteza sem alterar valores.
- Em desktop largo, os cinco graficos curtos usam grade 3 + 2; tablet usa duas colunas; celular usa carrossel. Os tres graficos diarios usam tres colunas acima de 640 px e carrossel no celular.
- Medido e Previsao possuem estilos, legenda e tooltips distintos, separados pelo marcador `Agora`; o zoom preserva essa semantica e o mobile mantem todos os pontos com menos rotulos no eixo X.
- Login/logout Google opcional; usuário não autorizado permanece no modo público.
- Dashboard, chat e exportações só são inicializados para usuário interno autorizado.
- Proteção server-side de `/api/dashboard` e `/api/chat` por Firebase ID token e allowlist.
- Diferença arquitetural aceita em relação ao projeto original: o original pode usar frontend estático/Firebase AI Logic; esta versão mantém backend ASP.NET Core, repository Firebase e chat via `/api/chat`.
- Chip responsivo de duração do dia nos ciclos solares interno e público.
- Cards ambientais de ventilação, chuva nas próximas 6 horas, índice UV e ponto de orvalho/risco estimado de mofo.
- Chuva inclui maior probabilidade, intensidade máxima, acumulado e horário aproximado do pico; ausência de previsão permanece indisponível.
- UV inclui valor atual, máxima diária, faixa de risco e recomendação proporcional; máxima usada como fallback é identificada.

Regressões da Assistente cobertas:

- Últimos X dias usam a quantidade escrita e a data selecionada, com limite explícito de 30 dias.
- Últimas X horas usam janela móvel real, inclusive 48 horas.
- Média, máxima, mínima, tendência e variação respeitam a operação solicitada.
- Faixa de conforto, extremos por horário/dia e atalhos da aba Estação não são tratados como comparação ou ciclo solar por engano.
- O zoom dos gráficos possui semântica de diálogo e gerenciamento de foco.
- Sala e Quarto calculam ponto de orvalho pela equação de Magnus e apresentam estimativa orientativa de condensação/mofo.
- A aba Estação começa com insights internos e só combina clima externo após clique do usuário. A localização não é solicitada automaticamente nem persistida.
- O cabeçalho vazio dos insights foi removido nos modos público e privado; a ação privada de localização fica no card de chuva.

## Pendências Conhecidas

- O frontend preserva o comportamento do original, mas mantém a arquitetura C#/.NET com API própria e scripts clássicos em `window`; uma migração futura para ES modules reduziria dependência de ordem de carregamento.
- A autenticação da API protege o acesso pelo navegador, mas regras do Realtime Database e credenciais de escrita dos dispositivos devem continuar sendo revisadas separadamente.
- A API usa cache curto para os caminhos Firebase; ajuste `Firebase:CacheDurationSeconds` se precisar reduzir ou ampliar a janela.
- Alterações visuais finas no PDF ainda devem ser conferidas manualmente no navegador, embora os testes automatizados validem downloads PDF/JSON e o contrato do relatório por aba, incluindo Estação.
- Login Google real depende de o domínio de implantação estar autorizado no Firebase Authentication.
