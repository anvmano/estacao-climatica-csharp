# Contrato do modo público

## Fonte da verdade

O comportamento é derivado de `scripts/external/external-weather-service.js`, `scripts/external/browser-location-service.js` e `scripts/views/public-weather-view.js` do projeto Web original.

## Endpoints C#/.NET

```text
GET /api/public-weather/cep/{cep}
GET /api/public-weather/cities?term={cidade}
POST /api/public-weather/city
GET /api/public-weather/location?latitude={lat}&longitude={lon}&precisao={metros}
```

O modo público não depende do seletor de data do dashboard interno. A data `03/06/2026` continua obrigatória para o dashboard, gráficos, tabelas, exportações e chat que dependem da data selecionada.

## Fluxo por CEP

1. Remover caracteres não numéricos.
2. Exigir exatamente oito dígitos.
3. Consultar BrasilAPI.
4. Em indisponibilidade ou resposta inválida, consultar ViaCEP.
5. Usar coordenadas da BrasilAPI quando presentes.
6. Sem coordenadas, consultar Open-Meteo Geocoding por cidade, cidade/UF e cidade/UF/Brasil.
7. Consultar clima e AQI em paralelo.
8. Falha de AQI não deve bloquear clima.

## Fluxo por localização

O frontend é responsável por solicitar permissão ao navegador. O backend recebe latitude, longitude e precisão, consulta as APIs externas e não persiste as coordenadas.

## Resposta

```json
{
  "origem": {
    "tipo": "cep",
    "rotulo": "São Paulo - SP",
    "latitude": -23.5505,
    "longitude": -46.6333,
    "cep": "01001000",
    "precisao": null
  },
  "atualizadoEm": "2026-06-03T12:00",
  "climaAtual": {
    "temperatura": 21.4,
    "sensacaoTermica": 20.8,
    "umidade": 63,
    "pressao": 1017.2
  },
  "seriesHorarias": {
    "horarios": [],
    "temperatura": [],
    "sensacaoTermica": [],
    "umidade": [],
    "pressao": []
  },
  "aqi": {
    "valor": 42,
    "horarios": [],
    "serie": []
  },
  "cicloSolar": {
    "dawn": 5.5,
    "sunrise": 6.5,
    "zenith": 12.125,
    "sunset": 17.75,
    "dusk": 18.75,
    "daylightDuration": 40500
  },
  "previsaoQuinzeDias": [
    {
      "data": "2026-06-03",
      "temperaturaMinima": 13.2,
      "temperaturaMaxima": 22.4,
      "precipitacao": 2.5,
      "probabilidadeChuva": 70,
      "indiceUvMaximo": 5.8,
      "codigoClimatico": 61,
      "maiorIncerteza": false
    }
  ]
}
```

## Contrato Open-Meteo Forecast

- Parametros: `timezone=auto`, `past_hours=24`, `forecast_hours=13`, `forecast_days=15`.
- Campos diarios: `temperature_2m_min`, `temperature_2m_max`, `precipitation_sum`, `precipitation_probability_max`, `uv_index_max` e `weather_code`, alem de nascer/por do sol e duracao do dia.
- Arrays diarios sao combinados pela posicao de `daily.time`, limitados a 15 datas e ordenados cronologicamente.
- Campos ausentes permanecem `null`; do 8o ao 15o dia, `maiorIncerteza` e `true`.
- Resposta sem agregados diarios nao invalida clima atual, series horarias, AQI, insights ou ciclo solar.

## Erros funcionais

| Situação | Status | Mensagem |
|---|---:|---|
| CEP com tamanho inválido | 400 | `Informe um CEP com 8 dígitos.` |
| CEP não encontrado | 404 | `CEP não encontrado.` |
| Coordenadas do CEP não encontradas | 404 | `Não foi possível encontrar coordenadas para o CEP.` |
| Falha de consulta do CEP | 502 | `Não foi possível consultar o CEP.` |
| Falha de clima externo | 502 | `Não foi possível consultar o clima externo.` |
| Resposta externa inválida | 502 | `Resposta inválida do serviço externo.` |

## Privacidade

- Coordenadas não devem ser gravadas em banco, arquivo, cookie ou `localStorage`.
- Coordenadas não devem ser incluídas em prompts do chat/IA.
- O modo público não deve consultar Firebase interno, dashboard, chat ou exportações.
- Logs não devem registrar coordenadas precisas nem payloads completos das APIs externas.

## Separação do painel interno

- `GET /api/dashboard` e `POST /api/chat` exigem `Authorization: Bearer <Firebase ID token>`.
- O backend valida o token no Firebase Identity Toolkit e aplica `InternalAccess:AuthorizedEmails`.
- Visitante sem login e usuário autenticado fora da allowlist permanecem em `#publicApp`.
- Chat, exportações e carregamento do dashboard só são inicializados após autorização.
