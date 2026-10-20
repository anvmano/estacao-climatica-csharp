# API de consultas climaticas

## Autorizacao

Todos os endpoints deste documento sao internos. Quando `InternalAccess:Enforce=true`, envie:

```http
Authorization: Bearer <Firebase ID token>
```

O token precisa pertencer a um e-mail configurado em `InternalAccess:AuthorizedEmails`. Somente `/api/health` e `/api/public-weather/*` permanecem anonimos.

## Datas, listas e erros

- Datas usam estritamente `dd-MM-yyyy`.
- A data de regressao e `03-06-2026`.
- Listas em query string usam virgula, por exemplo `sala,quarto`.
- Entrada invalida retorna HTTP 400 com `{ "message": "..." }`.
- Ambiente ou dados inexistentes retornam HTTP 404.
- Requisicao interna sem autorizacao retorna HTTP 401.

## Endpoints

### `GET /api/environments`

Lista `sala`, `quarto` e `aquario`, com as metricas, rotulos e unidades aceitas por cada ambiente.

### `GET /api/environments/{environment}/latest`

Retorna a medicao mais recente do ambiente, com data, hora, timestamp ISO e valores normalizados. No aquario, TDS e turbidez usam as mesmas escalas do dashboard.

### `GET /api/environments/{environment}/measurements`

Query:

- `from` e `to`: intervalo inclusivo; se omitidos, usa o dia atual.
- `metrics`: lista opcional de metricas.
- `page`: padrao 1.
- `pageSize`: padrao 100; minimo 1 e maximo 500.

O intervalo maximo e de 366 dias. A resposta inclui `totalItems` e `totalPages`.

Exemplo:

```http
GET /api/environments/sala/measurements?from=03-06-2026&to=03-06-2026&metrics=temperatura,CO2&page=1&pageSize=100
```

### `GET /api/metrics/{metric}/series`

Query:

- `date`: data da serie.
- `environments`: ambientes opcionais separados por virgula.

Aliases ignoram maiusculas, espacos e acentos. `temperatura`, por exemplo, pode retornar Sala, Quarto e Aquario.

### `GET /api/dashboard/summary`

Query `date`. Retorna apenas os resumos por ambiente, ciclo solar do dia e AQI, sem tabelas, heatmaps ou series completas.

### `GET /api/air-quality`

Aceita `date` ou o par `from`/`to`. Os formatos nao podem ser combinados. Retorna AQI, categoria, poluente dominante e subindices para cada registro da Sala.

### `GET /api/solar`

Query `date`. Retorna amanhecer, nascer do sol, zenite, por do sol, anoitecer e `daylightSeconds`. Retorna 404 quando o dia nao possui dados solares completos.

### `GET /api/alerts`

Query:

- `date`: data analisada.
- `severity`: `info`, `warning` ou `critical`.
- `environment`: `sala`, `quarto` ou `aquario`.

As faixas seguem a fonte Web original: temperatura e sensacao de Sala/Quarto entre 20 e 26 °C, umidade entre 40 e 60%, e temperatura do Aquario entre 25 e 27 °C. AQI acima de 100 gera alerta; a partir de 151, alerta critico. Ausencia total de dados gera aviso. Nao foram inventados limites para pH, TDS, turbidez ou gases individuais.

### `POST /api/comparisons`

Compara de 2 a 31 datas. `environments` e `metrics` sao opcionais.

```json
{
  "dates": ["02-06-2026", "03-06-2026"],
  "environments": ["sala", "quarto"],
  "metrics": ["temperatura", "umidade"]
}
```

Cada serie retorna media, minimo e maximo por data, alem da diferenca absoluta e percentual entre a primeira e a ultima data.

### `GET /api/exports/{format}`

- `format`: `json` ou `csv`.
- `date`: data exportada.
- `tab`: `Tab0`, `Tab1`, `Tab2`, `Tab3` ou os aliases `estacao`, `sala`, `quarto`, `aquario`.

JSON retorna o contrato completo da aba. CSV retorna a tabela do ambiente; para Estacao, retorna o resumo das metricas. PDF continua sendo gerado no navegador.

## Implementacao e testes

- Controller: `src/EstacaoClimatica.Web/Controllers/ClimateQueryController.cs`
- Servico: `src/EstacaoClimatica.Application/Services/ClimateQueryService.cs`
- Contratos: `src/EstacaoClimatica.Domain/ClimateQueryModels.cs`
- Testes de negocio: `tests/EstacaoClimatica.Tests/ClimateQueryServiceTests.cs`
- Testes HTTP/controller: `tests/EstacaoClimatica.Tests/ClimateQueryControllerTests.cs` e `FrontendBrowserTests.cs`
