# Roteiro de implementação das melhorias de UI/UX no projeto C#

Data: 22/09/2026

## 1. Objetivo

Levar para o projeto `EstacaoClimatica` em ASP.NET Core 8 as melhorias de UI/UX já aplicadas e validadas no projeto web original, preservando os contratos próprios da versão C#.

O trabalho é majoritariamente de frontend. Não há necessidade prevista de alterar Domain, Application, Infrastructure, controllers, endpoints ou os modelos JSON da API.

## 2. Projetos de referência

- Fonte visual atual: `D:\Documentos\03_Projetos\estacao climatica web\atualizacao claude`
- Projeto de destino: `D:\Documentos\03_Projetos\Estacao climatica C#`
- Frontend de destino: `src/EstacaoClimatica.Web/wwwroot`
- Testes de destino: `tests/EstacaoClimatica.Tests`

## 3. Situação atual

O projeto C# já possui boa paridade funcional com a versão web anterior, mas ainda não contém o último conjunto de melhorias responsivas. As lacunas confirmadas são:

1. O gráfico de chuva ainda mostra três séries, duas escalas e legenda ao mesmo tempo no celular.
2. Não existe o seletor móvel `Precipitação` / `Chance`.
3. O intervalo de tablet entre `641px` e `960px` não possui tratamento dedicado.
4. A assistente ainda usa o botão textual e pode cobrir conteúdo em telas menores.
5. O modo público mantém o hero e o formulário completos depois de uma consulta bem-sucedida.
6. Os gráficos e insights públicos continuam empilhados verticalmente no celular.
7. Os cards móveis ainda usam uma única coluna em vários pontos.
8. O modo público ainda usa `section` e `h2`, sem o landmark e a hierarquia de título atualizados.
9. Alguns controles continuam abaixo de `44px` de altura.
10. Tipografia auxiliar, tabelas e heatmaps ainda não receberam os últimos ajustes de legibilidade.
11. O número do AQI móvel fica sobre o medidor decorativo e perde contraste.
12. O gráfico de chuva móvel está mais alto que os demais mesmo após separar Precipitação e Chance.
13. Os pontos do ciclo solar possuem área de toque pequena no celular.
14. Os dias fixos do mapa semanal usam fundo translúcido e deixam as células aparecerem por trás durante a rolagem.
15. A sincronização reaplica o ponto ativo no gráfico de origem e pode produzir dois marcadores simultâneos.

## 4. Mapa de equivalência de arquivos

| Projeto web atual | Projeto C# | Ação |
| --- | --- | --- |
| `index.html` | `src/EstacaoClimatica.Web/wwwroot/index.html` | Adaptar a semântica e preservar IDs existentes |
| `scripts/charts/rain.js` | `src/EstacaoClimatica.Web/wwwroot/scripts/chuva.js` | Portar o modo responsivo mantendo `window.ClimaChuva` |
| `scripts/views/public-weather-view.js` | `src/EstacaoClimatica.Web/wwwroot/scripts/public-weather.js` | Adaptar o estado compacto à API C# |
| `styles/base.css` | `wwwroot/styles/base.css` | Adicionar utilitário acessível |
| `styles/header.css` | `wwwroot/styles/header.css` | Atualizar seletor da marca |
| `styles/stats.css` | `wwwroot/styles/stats.css` | Melhorar legibilidade |
| `styles/environment-insights.css` | `wwwroot/styles/environment-insights.css` | Melhorar legibilidade |
| `styles/charts.css` | `wwwroot/styles/charts.css` | Estilizar seletor do gráfico de chuva |
| `styles/advanced-views.css` | `wwwroot/styles/advanced-views.css` | Alvos de toque e contexto fixo |
| `styles/tables.css` | `wwwroot/styles/tables.css` | Coluna fixa e botões móveis |
| `styles/public-weather.css` | `wwwroot/styles/public-weather.css` | Compactar resultados públicos no mobile |
| `styles/responsive.css` | `wwwroot/styles/responsive.css` | Mobile e tablet |
| `styles/chat.css` | `wwwroot/styles/chat.css` | Geometria móvel da assistente e carregamento tardio |
| `style.css` | `src/EstacaoClimatica.Web/wwwroot/style.css` | Atualizar versões de cache |
| testes Node/Playwright do original | `FrontendStructureTests.cs` e `FrontendBrowserTests.cs` | Recriar as garantias em xUnit/Playwright |

`app.js`, `auth.js`, controllers e serviços devem permanecer inalterados, salvo se algum teste revelar uma dependência não documentada.

## 5. Regras de preservação

- Preservar todos os IDs consumidos pelo JavaScript e pelos testes.
- Preservar `window.ClimaChuva`; a fonte web usa outro nome global e não deve ser copiada literalmente.
- Preservar as rotas `/api/public-weather/*`, o token Firebase e o fluxo de autorização do C#.
- Preservar a janela de chuva com 24 horas observadas e 12 horas previstas.
- Preservar valores ausentes como `null`; não converter lacunas em zero.
- Preservar sincronização temporal, marcador `Agora`, zoom, exportação PDF/JSON e restauração do cache de sessão.
- Não copiar o runtime loader do projeto web, pois o C# já entrega Chart.js localmente.
- Não alterar a regra de dados inválidos do Firebase durante este trabalho de UI.
- Usar nomenclatura em português no código novo, conforme a convenção do projeto C#.

## 6. Etapa 0 — Preparação e linha de base

### Execução

1. Criar uma branch dedicada, por exemplo `feat/ui-ux-responsiva`.
2. Registrar `git status` e não incluir arquivos de `outputs/`, `bin/`, `obj/` ou logs.
3. Executar:

```powershell
dotnet build EstacaoClimatica.sln
dotnet test EstacaoClimatica.sln
```

4. Capturar a interface atual nos viewports:
   - `1440x1000`: desktop;
   - `960x1024`: limite superior do tablet;
   - `768x1024`: tablet retrato;
   - `390x844`: celular principal;
   - `360x800`: celular estreito;
   - `720x360`: celular em paisagem.
5. Registrar medidas de `document.documentElement.scrollWidth`, largura das abas, posição da toolbar e dimensões do gráfico de chuva.

### Critério de saída

- Build e testes existentes aprovados.
- Evidência visual anterior disponível para comparação.
- Nenhum erro de console ou de página no estado inicial.

## 7. Etapa 1 — Semântica e estrutura básica

### Arquivos

- `src/EstacaoClimatica.Web/wwwroot/index.html`
- `src/EstacaoClimatica.Web/wwwroot/styles/base.css`
- `src/EstacaoClimatica.Web/wwwroot/styles/header.css`
- `src/EstacaoClimatica.Web/wwwroot/styles/public-weather.css`

### Implementação

1. Trocar o `h1` visual da marca por `span.header-brand__title`.
2. Trocar `section#publicApp` por `main#publicApp` com `aria-labelledby="publicPageTitle"`.
3. Tornar `Clima por localização` o `h1#publicPageTitle` do modo público.
4. Inserir um `h1.visually-hidden` dentro de `#privateApp` para o modo interno.
5. Adicionar `.visually-hidden` em `base.css` usando recorte acessível, sem `display:none`.
6. Atualizar em `header.css` o seletor que hoje estiliza o `h1` da marca.
7. Atualizar em `public-weather.css` o seletor do título público de `h2` para `h1`.
8. Remover a repetição visual de `Modo público` no estado inicial de login, mantendo apenas o eyebrow.

### Testes

- `#publicApp` deve ser um `main` quando o modo público estiver ativo.
- O modo público deve possuir exatamente um `h1` perceptível.
- O modo interno deve possuir um `h1` acessível mesmo com a marca oculta no celular.
- Os botões de login e logout devem continuar funcionando.

### Critério de saída

- Hierarquia de títulos válida nos dois modos.
- Nenhuma alteração nos IDs ou nos fluxos de autenticação.

## 8. Etapa 2 — Gráfico de chuva específico para mobile

### Arquivos

- `src/EstacaoClimatica.Web/wwwroot/scripts/chuva.js`
- `src/EstacaoClimatica.Web/wwwroot/styles/charts.css`
- `src/EstacaoClimatica.Web/wwwroot/styles/responsive.css`

### Implementação em `chuva.js`

1. Adicionar a media query central `const CONSULTA_MOVEL = '(max-width: 640px)'`.
2. Manter os três datasets já existentes:
   - precipitação observada;
   - precipitação prevista;
   - chance de chuva.
3. Criar um controle segmentado antes do canvas somente no mobile:
   - botão `Precipitação`, ativo por padrão;
   - botão `Chance`;
   - `role="group"`;
   - `aria-label="Dado exibido no gráfico de chuva"`;
   - estado exposto por `aria-pressed`.
4. No modo `Precipitação`:
   - mostrar os dois datasets em milímetros;
   - ocultar a linha de chance;
   - mostrar apenas `yMilimetros`.
5. No modo `Chance`:
   - ocultar os dois datasets em milímetros;
   - mostrar a linha de chance;
   - mostrar apenas `yProbabilidade`.
6. No mobile:
   - ocultar a legenda;
   - limitar o eixo X a cinco rótulos;
   - ocultar títulos laterais das escalas;
   - manter tooltip, marcador `Agora`, nulls e sincronização.
7. Em larguras acima de `640px`:
   - remover o controle segmentado;
   - restaurar os três datasets;
   - restaurar as duas escalas;
   - restaurar a legenda;
   - usar até treze rótulos no eixo X.
8. Usar `onResize` do Chart.js para sincronizar a transição entre retrato e paisagem.
9. Adicionar um plugin de ciclo de vida para remover o controle ao destruir o gráfico.
10. Garantir que recriações públicas e privadas não gerem controles duplicados.
11. Preservar a API pública atual de `window.ClimaChuva`.

### Implementação em CSS

1. Criar `.rain-chart-toggle`, inicialmente oculto.
2. Exibi-lo somente até `640px` como controle segmentado de duas colunas.
3. Aplicar altura mínima de `44px` aos botões.
4. Usar foco visível e estado ativo coerentes com os toggles existentes.
5. Definir `#plotRain` e `#publicChartRain` com aproximadamente `300px` de altura no mobile.

### Testes estruturais

Adicionar garantias em `FrontendStructureTests.cs` para:

- media query móvel;
- textos `Precipitação` e `Chance`;
- uso de `aria-pressed`;
- integração com `onResize`;
- preservação de `HORAS_OBSERVADAS = 24`, `HORAS_PREVISTAS = 12` e `window.ClimaChuva`.

### Testes no navegador

Adicionar um teste em `FrontendBrowserTests.cs` que valide:

1. Em `390x844`, o seletor existe e inicia em `Precipitação`.
2. O gráfico começa com dois datasets visíveis e somente `yMilimetros` visível.
3. Ao escolher `Chance`, somente o dataset percentual fica visível.
4. O controle não duplica após nova consulta.
5. Ao redimensionar para desktop ou paisagem acima de `640px`, o controle desaparece, três datasets ficam visíveis e as duas escalas retornam.
6. O mesmo comportamento funciona em `#plotRain` e `#publicChartRain`.

### Critério de saída

- O gráfico é legível em `360px` e `390px`.
- Não há sobreposição entre legenda, eixos e plotagem.
- Desktop mantém a apresentação combinada existente.

## 8.1 — Refinamentos móveis adicionais

### Arquivos

- `src/EstacaoClimatica.Web/wwwroot/styles/responsive.css`
- `src/EstacaoClimatica.Web/wwwroot/styles/advanced-views.css`
- `src/EstacaoClimatica.Web/wwwroot/scripts/solar.js`
- `src/EstacaoClimatica.Web/wwwroot/scripts/public-weather.js`
- `src/EstacaoClimatica.Web/wwwroot/scripts/sincronizacao-graficos.js`
- `tests/EstacaoClimatica.Tests/FrontendStructureTests.cs`
- `tests/EstacaoClimatica.Tests/FrontendBrowserTests.cs`

### AQI no cabeçalho móvel

1. Manter o medidor semicircular e a agulha como contexto visual.
2. Exibir o prefixo `AQI` no canto superior esquerdo do chip.
3. Exibir o valor em uma cápsula escura de alto contraste no canto superior direito.
4. Posicionar prefixo e valor acima dos pseudoelementos do medidor com `z-index` explícito.
5. Usar números tabulares, peso forte e largura mínima estável para valores de um a três dígitos.
6. Preservar o `aria-label` completo com valor, categoria, origem e dominante.

### Altura do gráfico de chuva

1. Definir `#plotRain` e `#publicChartRain` com `200px` no mobile.
2. Manter o seletor Precipitação/Chance acima do canvas, sem sobreposição.
3. Preservar a altura desktop e o zoom.
4. Confirmar que o card final fica próximo da altura dos demais, admitindo apenas o espaço adicional do seletor.

### Toque nos eventos solares

1. Criar em `solar.js` um helper compartilhado para as opções dos pontos de eventos.
2. No mobile, usar raio visual `7px`, raio de toque adicional `24px` e raio de hover `9px`.
3. No desktop, preservar os valores atuais `5px`, `18px` e `7px`.
4. Reutilizar o helper tanto no ciclo solar interno quanto no público.
5. Manter o tooltip filtrado para `Eventos solares`, mostrando nome e horário do evento tocado.
6. Validar toques próximos, mas não exatamente sobre o centro, em Amanhecer, Nascer, Zênite, Pôr e Anoitecer.

### Coluna fixa do mapa semanal

1. Aumentar a primeira coluna para `48px` e manter largura fixa.
2. Tornar o canto superior e os sete rótulos de dia sticky com a mesma largura.
3. Usar fundo opaco `var(--bg-card)`; não usar `var(--bg-panel)` translúcido.
4. Cobrir o gap da grade com uma faixa sólida e um divisor discreto.
5. Manter os rótulos acima das células por `z-index`.
6. Remover o arredondamento esquerdo da primeira célula horária de cada linha para evitar o efeito de ponta ao lado do dia.
7. Validar o estado com `scrollLeft` intermediário e máximo.

### Marcador duplicado nos gráficos sincronizados

1. Alterar `sincronizacao-graficos.js` para receber a instância de origem ao sincronizar um horário.
2. Não chamar `setActiveElements` nem `tooltip.setActiveElements` no gráfico de origem.
3. Deixar o Chart.js controlar nativamente o marcador e o tooltip da origem.
4. Continuar ativando o horário correspondente nos demais gráficos do grupo.
5. Continuar limpando todos os gráficos no `mouseout`.
6. Preservar a correspondência por timestamp e a tolerância atual de 45 minutos.

### Testes

- AQI: prefixo e valor visíveis, valor dentro do chip, fundo contrastante e camada acima do medidor.
- Chuva: canvas móvel com `200px` nos modos público e interno.
- Solar: helper retornando os raios móveis e toque real próximo a um ponto abrindo o horário correto.
- Heatmap: dia alinhado à esquerda após rolagem, fundo opaco e rótulo acima das células.
- Sincronização: gráfico de origem não recebe ativação programática; gráficos irmãos continuam sincronizados.

### Critério de saída

- Valor do AQI legível imediatamente em `360px` e `390px`.
- Card de chuva proporcional aos demais cards móveis.
- Cada evento solar pode ser acionado confortavelmente por toque.
- Dias da semana permanecem íntegros durante toda a rolagem horizontal.
- Apenas um marcador aparece no gráfico manipulado em cada horário.

## 9. Etapa 3 — Responsividade do painel interno

### Arquivos

- `src/EstacaoClimatica.Web/wwwroot/styles/responsive.css`
- `src/EstacaoClimatica.Web/wwwroot/styles/stats.css`
- `src/EstacaoClimatica.Web/wwwroot/styles/environment-insights.css`
- `src/EstacaoClimatica.Web/wwwroot/styles/chat.css`
- `src/EstacaoClimatica.Web/wwwroot/scripts/ui.js`
- `src/EstacaoClimatica.Web/wwwroot/app.js`

### Implementação mobile até `640px`

1. Aumentar os chips do cabeçalho para pelo menos `44px`.
2. Manter o valor numérico do AQI visível e esconder apenas textos redundantes.
3. Configurar o cabeçalho para recolher ao rolar para baixo e reaparecer ao rolar para cima ou voltar ao topo.
4. Aplicar o recolhimento somente até `640px`; desktop e tablet mantêm o cabeçalho sticky atual.
5. Usar um limiar acumulado de movimento para impedir oscilação durante pequenos deslocamentos.
6. Manter o cabeçalho visível enquanto qualquer chip/popover estiver aberto ou receber foco.
7. Remover a transformação ao sair da media query, evitando estado oculto após rotação ou redimensionamento.
8. Aumentar as abas para pelo menos `44px` de altura.
9. Garantir `44px` para date picker, Hoje, PDF/JSON e Exportar.
10. Remover `min-height: 520px` de `#statsEstacao`.
11. Entre `360px` e `640px`, mostrar `#statsEstacao` em duas colunas.
12. Manter os demais resumos em duas colunas quando os valores couberem.
13. Usar padding inferior com `env(safe-area-inset-bottom)`.
14. Aumentar botões de zoom e fechamento para `44x44px`.

### Implementação tablet de `641px` a `960px`

1. Criar um breakpoint intermediário explícito.
2. Colocar as quatro abas em uma linha de largura total.
3. Colocar a toolbar em uma linha própria abaixo das abas.
4. Manter cards e gráficos em duas colunas quando houver espaço.
5. Impedir rolagem horizontal interna nas abas.
6. Aplicar alvos de toque de `44px`.

### Assistente em telas menores

1. Transformar o acionador em botão circular de `48x48px`.
2. Manter apenas o ícone visível e esconder o rótulo visualmente, preservando o nome acessível.
3. Manter as regras de botão e espaçamento geral em `responsive.css`.
4. Colocar a geometria do painel móvel no final de `chat.css`, dentro de `@media (max-width: 640px)`, porque esse arquivo é carregado depois do agregador principal.
5. Usar `.ai-chat .ai-chat__panel` para abrir o painel como bottom sheet ancorado ao viewport, com `position: fixed`, `width:auto`, `max-width:none` e limites laterais explícitos.
6. Calcular `left`, `right` e `bottom` com `env(safe-area-inset-*)`.
7. Limitar a altura com fallback em `vh` e preferência por `dvh`, acompanhando a viewport visual quando o teclado móvel abrir.
8. Não manter a geometria móvel somente em `responsive.css`: a regra desktop de `chat.css`, carregada posteriormente, voltaria a definir `position:absolute` e deslocaria o painel para fora da tela.
9. Reservar espaço inferior para que o último card ou controle não fique encoberto.
10. Preservar trava de rolagem, foco inicial, fechamento e devolução de foco.

### Paisagem

1. Preservar o breakpoint existente para `720x360`.
2. Elevar alvos das abas para `44px` sem quebrar a toolbar.
3. Confirmar que a nova regra de tablet não sobrescreve indevidamente a regra de paisagem.

### Critério de saída

- Nenhuma aba cortada entre `320px` e `1024px`.
- Toolbar sempre abaixo das abas em tablet retrato.
- Cabeçalho móvel recolhe na descida, reaparece na subida e permanece visível com popover aberto.
- Assistente não cobre controles nem o último conteúdo.
- Painel da assistente permanece inteiramente entre as quatro bordas do viewport após a trava de rolagem.
- Nenhum overflow horizontal global.

## 10. Etapa 4 — Modo público compacto após a consulta

### Arquivos

- `src/EstacaoClimatica.Web/wwwroot/scripts/public-weather.js`
- `src/EstacaoClimatica.Web/wwwroot/styles/public-weather.css`

### Implementação JavaScript

1. Em `renderizarEstado`, remover a classe `has-results` de `#publicApp`.
2. Em `renderizarResultado`, adicionar à faixa de localização o botão `Alterar local`.
3. Depois de renderizar com sucesso, adicionar `has-results` em `#publicApp`.
4. Ao clicar em `Alterar local`:
   - remover `has-results`;
   - rolar suavemente até `.public-hero`;
   - colocar foco no campo de busca após a animação.
5. Adicionar a classe `.public-metric-charts` ao grupo dos quatro gráficos principais.
6. Tornar o grupo focável com `tabindex="0"` e nome acessível que informe a rolagem horizontal no celular.
7. Tornar o grupo de insights focável e nomeado quando ele adotar carrossel horizontal.
8. Preservar o tratamento C# de concorrência, cache de sessão, endpoints e mensagens de erro.

### Implementação CSS

1. Remover borda e fundo redundantes do formulário aninhado.
2. Em mobile com `.has-results`, ocultar o hero completo.
3. Exibir Temperatura, Sensação, Umidade e Pressão em duas colunas.
4. Fazer o card AQI ocupar a largura total.
5. Transformar insights em faixa horizontal com `overflow-x:auto` e `scroll-snap`.
6. Transformar os quatro gráficos meteorológicos em faixa horizontal com `scroll-snap`.
7. Manter Chuva e Ciclo Solar como blocos independentes de largura total.
8. Estilizar `Alterar local` como ação compacta com altura mínima de `44px`.
9. Aplicar padding inferior com safe area.
10. Elevar o tamanho mínimo dos textos auxiliares e metadados.

### Testes no navegador

1. Depois da consulta bem-sucedida em `390x844`, `.public-app` deve possuir `has-results`.
2. O hero deve ficar oculto no mobile e continuar disponível no desktop.
3. O botão `Alterar local` deve reabrir a busca e focar o input.
4. O resumo deve ter duas colunas e AQI em largura total.
5. Insights e gráficos devem rolar internamente sem aumentar `scrollWidth` da página.
6. CEP, cidade única, cidades homônimas, geolocalização, resposta antiga e restauração de sessão devem continuar passando.

### Critério de saída

- Resultados essenciais aparecem rapidamente após a consulta.
- A página pública móvel reduz significativamente sua altura.
- Nenhum dado, gráfico ou fluxo de pesquisa é removido.

## 11. Etapa 5 — Legibilidade e componentes secundários

### Arquivos

- `styles/stats.css`
- `styles/environment-insights.css`
- `styles/advanced-views.css`
- `styles/tables.css`
- `styles/responsive.css`

### Implementação

1. Usar `0.75rem` como piso geral para texto auxiliar importante.
2. Usar aproximadamente `0.8125rem` para informações operacionais.
3. Evitar ampliar cabeçalhos de cards; o ganho deve estar nos detalhes pequenos.
4. Aplicar `min-height:44px` ao acionador das seções recolhíveis.
5. Aumentar a área clicável do ícone de expansão para cerca de `36px`.
6. Fixar os rótulos de dia do mapa semanal durante rolagem horizontal.
7. Em tabelas móveis, fixar a primeira coluna de contexto.
8. Aplicar `44px` aos botões de ordenação e CSV no mobile.
9. Preservar larguras estáveis das células e a máscara de rolagem existente.

### Critério de saída

- Informações auxiliares podem ser lidas sem zoom em `390px`.
- Cabeçalhos fixos não cobrem células.
- Rolagem de tabelas e heatmaps continua compatível com o swipe entre abas.

## 12. Etapa 6 — Cache, testes e regressões

### Cache de arquivos estáticos

Atualizar os sufixos de versão em:

- `src/EstacaoClimatica.Web/wwwroot/index.html` para `style.css`, `chuva.js` e `public-weather.js`;
- `src/EstacaoClimatica.Web/wwwroot/style.css` para cada CSS alterado.

Usar uma única versão coerente para esta entrega, evitando que o navegador combine JS novo com CSS antigo.

### Testes obrigatórios

```powershell
dotnet build EstacaoClimatica.sln
dotnet test EstacaoClimatica.sln
```

Executar também os testes de navegador isoladamente durante o desenvolvimento:

```powershell
dotnet test EstacaoClimatica.sln --filter "FullyQualifiedName~FrontendStructureTests"
dotnet test EstacaoClimatica.sln --filter "FullyQualifiedName~FrontendBrowserTests"
```

### Matriz visual final

| Estado | 1440x1000 | 960x1024 | 768x1024 | 390x844 | 360x800 | 720x360 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Público inicial | Sim | Sim | Sim | Sim | Sim | Sim |
| Público preenchido | Sim | Sim | Sim | Sim | Sim | Sim |
| Estação sem localização | Sim | Sim | Sim | Sim | Sim | Sim |
| Estação com chuva | Sim | Sim | Sim | Sim | Sim | Sim |
| Sala com tabela aberta | Sim | - | Sim | Sim | - | Sim |
| Assistente aberta | Sim | Sim | Sim | Sim | - | Sim |

### Verificações automáticas sugeridas

- `scrollWidth <= clientWidth` no documento.
- Nenhuma aba com `scrollWidth > clientWidth`.
- Toolbar posicionada abaixo da navegação entre `641px` e `960px`.
- Controles principais com área clicável mínima de `44px`.
- Cabeçalho móvel alternando visibilidade conforme a direção da rolagem, sem atuar no desktop.
- Botão da assistente com `48x48px` em telas menores.
- Painel móvel da assistente com `position:fixed`, limites laterais seguros e nenhuma borda fora do viewport.
- `main` e `h1` corretos no modo público.
- Ausência de controles duplicados no gráfico de chuva.
- Três datasets e duas escalas restaurados no desktop.
- Nenhum erro de console, request ou página.

### Regressões funcionais

Confirmar explicitamente:

- dashboard interno e autenticação;
- consulta pública por CEP, cidade e geolocalização;
- cache da consulta pública;
- sincronização dos gráficos;
- zoom e retorno de foco;
- exportação PDF, JSON e CSV;
- PDF com proporções preservadas;
- chat e memória curta;
- teclado nas abas e seções recolhíveis;
- swipe sem capturar tabelas, heatmaps e carrosséis públicos.

## 13. Etapa 7 — Documentação

Após a implementação:

1. Atualizar `docs/AI_QUICK_CONTEXT.md` com o comportamento móvel da chuva, o breakpoint de tablet e o modo público compacto.
2. Atualizar `docs/DEPENDENCY_MAP.md` com a dependência entre `chuva.js`, `charts.css`, `responsive.css`, modo público, zoom e testes.
3. Registrar no documento de paridade que o frontend C# alcançou a revisão visual de 22/09/2026.
4. Não alterar o resultado histórico da auditoria de dados inválidos; adicionar uma nota nova, se necessário.

## 14. Sequência recomendada de commits

1. `test(ui): registrar regressões responsivas atuais`
2. `feat(ui): melhorar gráfico de chuva no mobile`
3. `feat(ui): ajustar navegação e assistente em telas menores`
4. `feat(ui): compactar resultados do modo público`
5. `refactor(ui): melhorar semântica e legibilidade responsiva`
6. `docs(ui): atualizar contexto da revisão responsiva`

Cada commit deve manter `dotnet build` e os testes diretamente relacionados aprovados.

## 15. Critérios finais de aceite

- [ ] Build sem erros e sem novos avisos.
- [ ] Suíte xUnit completa aprovada.
- [ ] Nenhum overflow horizontal global de `320px` a `1440px`.
- [ ] Abas completas e toolbar em linha separada no tablet.
- [ ] Cabeçalho recolhe ao descer, reaparece ao subir e não se oculta com popover aberto no celular.
- [ ] Gráfico de chuva móvel alterna entre Precipitação e Chance.
- [ ] Gráfico de chuva móvel mantém `200px` de altura e proporção visual equivalente aos demais cards.
- [ ] Desktop restaura três datasets, legenda e duas escalas.
- [ ] Chip móvel do AQI exibe prefixo e valor com contraste suficiente sobre o medidor.
- [ ] Pontos do ciclo solar possuem área de toque ampliada e exibem o evento correto ao tocar próximo deles.
- [ ] Dias do mapa semanal permanecem opacos, alinhados e legíveis durante a rolagem horizontal.
- [ ] Sincronização exibe apenas um marcador ativo no gráfico de origem e mantém os gráficos irmãos sincronizados.
- [ ] Assistente não cobre conteúdo acionável.
- [ ] Assistente aberta permanece totalmente dentro do viewport em `390x844` e `720x360`.
- [ ] Modo público usa `main` e `h1` válidos.
- [ ] Hero público recolhe após consulta no celular.
- [ ] Cards públicos usam duas colunas e AQI ocupa largura total.
- [ ] Insights e gráficos públicos usam scroll-snap sem gerar overflow da página.
- [ ] Controles touch principais possuem pelo menos `44px`.
- [ ] Zoom, sincronização, PDF, JSON, CSV, chat e autenticação permanecem funcionais.
- [ ] PDF continua com gráficos proporcionais e legíveis.
- [ ] Documentação técnica atualizada.

## 16. Pontos de atenção durante a execução

- O projeto C# já possui diferenças arquiteturais legítimas em relação ao original. A portabilidade deve ser feita por comportamento, usando os nomes e contratos locais.
- `public-weather.js` consome endpoints C# e possui tratamento de concorrência próprio; incorporar apenas a composição visual e o estado `has-results`.
- `chuva.js` participa do modo público, do modo interno, do zoom e da sincronização. Toda mudança deve ser testada nos dois canvases.
- A regra de paisagem já existente é importante e deve ser reconciliada com o novo breakpoint de tablet.
- Alterações em `index.html` exigem revisão dos testes estruturais e dos seletores usados por Playwright.
- Alterações de versão dos assets são parte funcional da entrega por causa do cache do navegador.

## 17. Definição de concluído

A implementação estará concluída quando o projeto C# reproduzir o comportamento responsivo atual do projeto web nos estados público e interno, com testes automatizados cobrindo chuva, tablet, semântica, alvos de toque e ausência de overflow, sem mudanças nos contratos de backend e sem regressões nas exportações ou na autenticação.
