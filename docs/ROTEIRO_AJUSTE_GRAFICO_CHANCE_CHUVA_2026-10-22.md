# Roteiro incremental - grafico publico de chance de chuva

Data: 22/10/2026.

## Objetivo

Aplicar no projeto C# o ajuste validado no projeto Web original para que o grafico publico de chance de chuva siga o mesmo padrao visual dos demais graficos meteorologicos:

- escala percentual no lado esquerdo;
- legenda com `Medido` e `Previsao`;
- trecho medido continuo;
- proximas 12h em linha mais clara e tracejada;
- continuidade visual no marcador `Agora`.

Este roteiro complementa `ROTEIRO_MIGRACAO_PARA_ORIGINAL_2026-10-22.md`. Nao repetir a migracao anterior nem substituir arquivos completos entre os projetos.

## Limites

- Alterar somente o card publico de chance de chuva.
- Preservar a janela publica de 24h anteriores + 12h futuras.
- Preservar o grafico publico de precipitacao.
- Preservar o grafico combinado do modo privado e do PDF, incluindo eixos, janela e datasets atuais.
- Preservar sincronizacao por horario, zoom, marcador `Agora`, estados vazios e valores `null`.
- Nao alterar APIs, DTOs, endpoints, autenticacao ou dependencias.

## Arquivos provaveis

- `src/EstacaoClimatica.Web/wwwroot/scripts/chuva.js`
- `src/EstacaoClimatica.Web/wwwroot/scripts/public-weather.js`, apenas se titulo ou registro do grafico ainda divergir
- `tests/EstacaoClimatica.Tests/FrontendBrowserTests.cs`
- documentacao de comportamento do frontend, se existir regra equivalente

## Implementacao

1. Localizar a criacao do grafico com `modo: 'probabilidade'` e todos os seus chamadores.
2. Manter a serie horaria normalizada atual e derivar duas listas com o mesmo comprimento:
   - `probabilidadeObservada`: valor somente em horarios observados;
   - `probabilidadePrevista`: valor somente em horarios previstos.
3. Copiar o ultimo valor observado para o indice `Agora` da serie prevista somente quando existir previsao posterior. Isso conecta as linhas sem inventar uma nova medicao.
4. No modo publico fixo de probabilidade, criar dois datasets:
   - `Medido`: cor principal, linha continua;
   - `Previsao`: mesma familia de cor com menor opacidade e `borderDash: [6, 4]`.
5. Posicionar o eixo de porcentagem a esquerda e manter limites de 0% a 100%.
6. Evitar tooltip duplicado no indice de transicao: o valor previsto copiado para `Agora` serve apenas para continuidade visual.
7. Manter o dataset unico `Chance de chuva` no grafico combinado privado/PDF.
8. Confirmar que zoom copia os dois datasets e respeita legenda, tracejado, eixo e serie ocultada.

## Testes obrigatorios

- O card publico possui 37 pontos: 25 observados, incluindo `Agora`, e 12 futuros.
- A legenda publica apresenta exatamente `Medido` e `Previsao`.
- O dataset `Previsao` usa linha mais clara e tracejada.
- O eixo percentual publico fica a esquerda e continua entre 0% e 100%.
- O tooltip em `Agora` nao repete o mesmo valor nas duas series.
- Hover/toque continua sincronizando o horario com os outros cinco graficos publicos.
- Zoom mantem as duas legendas e o estilo da previsao.
- Ausencia parcial de dados continua representada por `null`, nunca por zero.
- O grafico combinado privado e o PDF permanecem iguais ao baseline.
- Validar desktop, mobile e zoom pelo menos em 1440x900, 390x844 e 320x568.

## Ordem de execucao

1. Registrar baseline visual e executar os testes atuais.
2. Ajustar somente `chuva.js`.
3. Atualizar o teste de browser para legenda, eixo, estilos e transicao.
4. Executar testes focados de chuva, modo publico, zoom e PDF.
5. Executar a regressao completa.
6. Atualizar a documentacao somente apos todos os testes passarem.

## Comandos

```powershell
dotnet build EstacaoClimatica.sln
dotnet test EstacaoClimatica.sln
```

## Criterio de aceite

O ajuste esta concluido quando o grafico publico de chance exibir eixo percentual a esquerda e legenda `Medido`/`Previsao`, com previsao clara e tracejada, sem regressao no privado, PDF, sincronizacao, zoom ou responsividade.
