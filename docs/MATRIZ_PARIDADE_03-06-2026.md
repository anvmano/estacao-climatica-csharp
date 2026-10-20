# Matriz de paridade - 03/06/2026

## Objetivo

Esta matriz usa o projeto Web HTML/CSS/JavaScript como fonte da verdade e separa diferenças funcionais de diferenças arquiteturais aceitáveis. A data obrigatória para todos os fluxos internos dependentes de data é `03/06/2026`.

## Estados

- `Igual`: comportamento equivalente confirmado.
- `Parcial`: existe nos dois projetos, mas ainda há diferença de contrato ou interface.
- `Ausente`: não existe no C#/.NET.
- `Arquitetura aceita`: implementação diferente sem alterar o comportamento observável.

## Matriz

| Área | Web esperado | C#/.NET atual | Estado | Próxima validação |
|---|---|---|---|---|
| Tela inicial | Modo público sem login | `#publicApp` abre sem consultar o dashboard | Igual | Regressão desktop/mobile |
| Consulta por CEP | BrasilAPI e fallback ViaCEP | Formulário, mensagens, fallback e endpoint validados | Igual | Manter teste real e mockado |
| Geocodificação | Open-Meteo quando CEP não possui coordenadas | Implementada no backend | Igual | Monitorar indisponibilidade externa |
| Clima externo | Forecast atual e séries horárias | Cards e quatro gráficos em janela móvel de 24h | Igual | Monitorar contrato Open-Meteo |
| AQI externo | Falha de AQI não bloqueia clima; popover informa impacto, origem, horário e proveniência | Card, chip, popover completo e estado indisponível | Igual | Coberto por browser test |
| Ciclo solar público | Amanhecer, nascer, zênite, pôr e anoitecer da localização consultada | Gráfico, chip e popover compartilham os mesmos eventos externos | Igual | Coberto por browser test |
| Geolocalização | Coordenadas somente em memória e três tentativas | Cache, tentativa normal e alta precisão implementados | Igual | Permissão depende do navegador |
| Login Google | Login opcional via Firebase Auth | Implementado em `scripts/auth.js` | Igual | Validar domínio de produção |
| Usuários internos | Dois e-mails autorizados | Allowlist no cliente e no backend | Igual | Manter listas sincronizadas |
| Usuário não autorizado | Permanece no modo público | Mantém tela pública | Igual | Regressão com conta externa |
| Logout | Limpa estado interno e volta ao público | Oculta painel/chat, destrói gráficos e limpa dashboard | Igual | Coberto por sessão automatizada; validar conta real em produção |
| Inicialização privada | Dashboard só inicia após autorização | `/api/dashboard` é adiado até autorização | Igual | Coberto por browser test |
| Proteção das APIs internas | Token e allowlist | Firebase ID token validado no ASP.NET Core para dashboard, chat e dez endpoints de consulta | Igual | Todos os endpoints internos sem token retornam HTTP 401 |
| Chat | Somente usuário interno | UI oculta e endpoint protegido | Igual | Regressão em 03/06/2026 |
| Exportação | Somente dashboard interno | Controles existem apenas no painel oculto/autorizado | Igual | Downloads cobertos por Playwright |
| Dashboard em 03/06/2026 | Data controla cards/tabelas e janela dos gráficos | Mock e API usam `03-06-2026` | Igual | Manter data fixa na regressão |
| Abas internas | Estação, Sala, Quarto e Aquário | Mesmas quatro abas e navegação touch | Igual | Coberto por Playwright |
| Navegação por teclado | Setas, `Home` e `End` percorrem as abas com foco móvel | Mesmo comportamento e atributos ARIA | Igual | Coberto por browser test |
| Qualidade dos sensores | Cobertura, leitura crítica/suspeita, repetição, saltos, desatualização e estado operacional | Cálculo em C# e sinalização em cards, gráficos e tabelas | Igual | Comparado ao vivo em 03/06/2026 |
| Ferramentas de tabela | Contagem, inversão temporal e CSV da tabela visível | Mesmos controles, textos e ordenação | Igual | Comparado ao vivo e por browser test |
| Ciclo solar do header | Usa o dia real, independentemente da data histórica selecionada | `Solar.CurrentDay` separado de `Solar.Day` | Igual | Regressão unitária e ao vivo |
| Insights ambientais | Ação, feedback e estados de localização ficam no card de chuva | Mesma estrutura, texto e responsividade | Igual | Comparação visual desktop/mobile |
| Exportação resiliente | Progresso acessível, timeout e tentativa novamente | Mesmo ciclo para PDF/JSON | Igual | Contrato estrutural e downloads Playwright |
| Concorrência do modo público | Resultado antigo não substitui a consulta mais recente | Sequência de busca, bloqueio de controles e `aria-busy` | Igual | Coberto por browser test |
| Cache público | Restaura consulta recente sem persistir coordenadas | `sessionStorage` sanitizado, expiração, reload real e anúncio acessível | Igual | Coberto por browser test e validação ao vivo |
| Atualidade pública | Piso dos minutos, texto curto e sufixo após 20 minutos | `Math.floor`, `há N min` e `desatualizado` | Igual | Coberto por browser test em `03/06/2026` |
| Zoom dos gráficos | Todos os gráficos internos e públicos podem ser ampliados | Cinco gráficos públicos registrados e gráficos internos preservados | Igual | Coberto por browser test e validação ao vivo |
| Acessibilidade do zoom | Foco em Fechar, contenção de Tab, Escape, retorno de foco e scroll preservado | Mesmo ciclo, inclusive classe e eventos touch | Igual | Teste crítico repetido três vezes |
| Cálculos internos | Estatísticas, AQI, solar, aquário e heatmaps | Implementados e cobertos por testes | Igual | Comparar novos campos quando surgirem |
| Arquitetura de dados | Firebase direto no browser | Repository/cache/API ASP.NET Core | Arquitetura aceita | Preservar resultado observável e proteger endpoints |
| Chat/IA | Firebase AI Logic no cliente | Cálculo local e IA opcional no backend | Arquitetura aceita | Manter respostas e restrições equivalentes |

## Critérios para avançar ao segundo ciclo

1. Os endpoints públicos devem manter os testes de serviço e navegador aprovados.
2. O dashboard interno deve manter toda a suíte anterior aprovada.
3. A interface pública não pode disparar `/api/dashboard` ou `/api/chat` antes da autorização.
4. A troca de estado público/interno não pode duplicar eventos, gráficos ou requisições.
5. A regressão interna deve continuar usando `03/06/2026`.

## Validação de 16/08/2026

- Build: aprovado, sem avisos e sem erros.
- Suíte C#/.NET: `158/158` testes aprovados.
- Paridade ao vivo: aprovada com Projeto Web e C#/.NET executados simultaneamente.
- Data interna aplicada nos dois projetos: `03/06/2026`.
- Viewports: `1440x900` e `390x844`.
- Estados JSON observáveis: idênticos para abas, cards, qualidade, gráficos, tabelas, heatmaps, solar e insights.
- Evidências: `outputs/comparacao-comportamental-03-06-2026-nova/`.
- Evidências pós-correção: `outputs/auditoria-paralelo-20260816/pos-correcao/`.

## Validação de 08/09/2026

- Build C#: aprovado, sem avisos e sem erros.
- Suíte C#: `178/178` aprovada; o teste ao vivo precisa de `RUN_LIVE_PARITY=1` e foi executado separadamente.
- Suíte original: onze comandos aprovados.
- Modo público ao vivo: CEP, cidade, mensagens, cards, recomendações, cinco gráficos, zoom e restauração de sessão iguais.
- Estado interno em `03/06/2026`: 152 de 155 folhas observáveis iguais.
- Estado atual: `Parcial` para as últimas medições e o ponto de orvalho do Quarto porque o original aceita a chave Firebase inválida `53-82-2045/45-16`, enquanto o C# seleciona a última leitura válida.
- Relatório e evidências: `docs/AUDITORIA_PARIDADE_COMPORTAMENTAL_2026-09-08.md` e `outputs/auditoria-paridade-20260908/`.

## Revisao visual de 22/09/2026

- O frontend C# incorporou a revisao responsiva vigente do projeto Web original, sem alterar endpoints, modelos JSON ou regras do backend.
- Semantica equivalente: landmark principal e titulo perceptivel no modo publico; titulo acessivel no painel interno.
- Chuva equivalente: seletor `Precipitacao`/`Chance` ate 640 px e composicao combinada completa acima desse limite.
- Painel interno equivalente: header direcional no celular, quatro abas e toolbar separadas em tablet, alvos de toque de 44 px e assistente fixa dentro do viewport.
- Modo publico equivalente: hero compacto apos sucesso, `Alterar local`, cards em duas colunas, AQI em largura total e faixas horizontais com `scroll-snap`.
- Refinamentos moveis equivalentes: AQI com valor contrastante, chuva a 200 px, pontos solares com toque ampliado, mapa semanal sticky opaco e sincronizacao sem marcador duplicado na origem.
- Data de regressao preservada: `03/06/2026`.
- Matriz: `1440x1000`, `960x1024`, `768x1024`, `390x844`, `360x800`, `720x360` e limite adicional `320x700`.
- Build: aprovado com zero avisos e zero erros.
- Suite automatizada apos a implementacao e os refinamentos 8.1: `189/189` testes aprovados.
- Evidencias visuais: `outputs/ui-ux-responsiva-2026-09-22/`.
- A divergencia historica de chave Firebase invalida registrada em 08/09/2026 nao foi alterada por esta revisao de UI/UX.
