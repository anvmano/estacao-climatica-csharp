# Perfil incremental de desempenho — 23/10/2026

## Ambiente e método

- Windows 10.0.19045, .NET SDK 8.0.426 / runtime 8.0.32, processo x64, 4 CPUs, limite de memória informado pelo GC: 16.024.018.944 bytes.
- Export RTDB local de 6.620.611 bytes, lido apenas em memória: AirQuality 184 dias, Aquario 190, NascePorDoSol 793, Temperatura 411. O arquivo original não foi copiado nem versionado.
- Cenário fixo: 22/10/2026; 5 amostras sequenciais por endpoint e condição, configuração Release. Frio usa URL distinta para cada amostra; quente usa a mesma URL após aquecimento. O p95 com 5 amostras corresponde ao maior valor observado.
- O `HttpClient` recebe respostas de um manipulador local que reproduz o JSON dos caminhos e `shallow=true`. Os números abaixo medem o pipeline de serviço/repositório/fonte do C#, incluindo bytes de corpo retornados ao cliente RTDB simulado. Não incluem rede, Firebase real, autenticação HTTP, serialização ASP.NET ou navegador. CPU é tempo de processo com resolução grosseira no Windows. Heap máximo é amostrado no processo acumulado e não representa memória retida por endpoint.
- Executar novamente: `dotnet run --project tools/PerformanceProbe/PerformanceProbe.csproj --configuration Release -- <caminho-do-export.json>`. Os resultados locais estão em `outputs/perf-final-2026-10-23.jsonl` (tabelas abaixo) e `outputs/perf-post-revocation-2026-10-23.jsonl` (repetição após a checagem de revogação); ambos são ignorados pelo Git.

## Leitura fria: antes e depois

| Endpoint | GETs | Bytes RTDB | p50 ms | p95 ms | CPU média ms | Alocação média bytes | Heap máximo bytes |
|---|---:|---:|---:|---:|---:|---:|---:|
| Dashboard antes | 68 | 374.840 | 49,26 | 242,77 | 65,63 | 10.829.426 | 37.069.376 |
| Dashboard depois | 52 | 374.776 | 48,85 | 181,04 | 68,75 | 9.487.478 | 37.152.768 |
| Última Sala antes | 4 | 3.628.271 | 114,33 | 155,44 | 146,88 | 27.295.171 | 117.580.520 |
| Última Sala depois | 1 | 1.032.929 | 35,58 | 46,49 | 34,38 | 5.501.893 | 58.505.856 |
| Medições Sala antes | 4 | 3.628.271 | 105,32 | 118,82 | 109,38 | 32.719.238 | 183.238.672 |
| Medições Sala depois | 1 | 1.032.929 | 51,19 | 58,35 | 46,88 | 10.297.083 | 71.802.536 |
| Qualidade do ar antes | 68 | 374.840 | 21,17 | 26,93 | 18,75 | 5.015.562 | 219.575.232 |
| Qualidade do ar depois | 52 | 374.776 | 24,59 | 31,45 | 25,00 | 3.625.752 | 78.433.800 |
| Chat antes | 4 | 3.628.271 | 79,56 | 122,78 | 100,00 | 27.344.739 | 253.387.416 |
| Chat depois | 1 | 1.032.929 | 39,79 | 80,85 | 53,13 | 4.925.493 | 109.448.104 |
| CSV antes | 68 | 374.840 | 24,59 | 34,47 | 21,88 | 10.624.112 | 235.594.360 |
| CSV depois | 52 | 374.776 | 53,15 | 62,89 | 53,13 | 9.208.128 | 124.923.496 |

O aumento da latência de CSV e de qualidade do ar em algumas execuções, apesar da redução de GETs e alocações, indica variação de execução local. Não há evidência de ganho de latência para esses dois endpoints. Os digests SHA-256 das respostas do C# permaneceram iguais nas medições antes/depois para os seis cenários.

## Leitura quente: p50 / p95 (ms)

| Endpoint | Antes | Depois | GETs e bytes em ambas |
|---|---:|---:|---:|
| Dashboard | 36,80 / 41,80 | 44,43 / 48,40 | 0 / 0 |
| Última Sala | 0,57 / 0,60 | 0,31 / 0,60 | 0 / 0 |
| Medições Sala | 15,09 / 18,13 | 15,18 / 17,00 | 0 / 0 |
| Qualidade do ar | 9,17 / 10,69 | 12,53 / 14,06 | 0 / 0 |
| Chat | 0,88 / 3,17 | 1,73 / 3,39 | 0 / 0 |
| CSV | 15,31 / 29,80 | 34,52 / 35,40 | 0 / 0 |

## Etapas e paridade

1. Verificação de token: a instância agora é compartilhada entre requisições. Simulação de cinco requisições sequenciais: ciclo anterior (`transient`) fez 5 chamadas `accounts:lookup`; ciclo atual (`singleton`) fez 1. O cache termina em 30 segundos ou na expiração JWT, o que vier primeiro. Quando `accounts:lookup` retorna `validSince`, o verificador rejeita tokens cujo `auth_time` é anterior a esse limite, conforme a [documentação do Identity Platform](https://docs.cloud.google.com/identity-platform/docs/use-rest-api). Revogações podem levar até 30 segundos para surtir efeito em um token já armazenado no cache do processo.
2. Chat e consultas de última medição, medições por ambiente, qualidade do ar por intervalo e comparação pedem só os ramos históricos usados. O histórico completo desses ramos continua disponível para consultas e chat; o dashboard e as exportações continuam montados com seus dados necessários.
3. A resposta `shallow=true` filtra dias inexistentes. O dia de hoje é sempre consultado mesmo que ainda não apareça na lista em cache, preservando a virada do dia. No cenário medido, o dashboard caiu de 68 para 52 GETs; os bytes quase não mudaram porque as respostas eliminadas eram `null`.
4. `JsonElement` é imutável; a fonte parou de clonar novamente o valor ao devolver o resultado e passou a descartar o `JsonDocument` de parsing. A alocação fria da última medição caiu de cerca de 8.018.382 bytes após a etapa 2 para 5.501.893 bytes no resultado final. Essa etapa não alterou GETs ou os digests.

O export registra a última amostra da Sala em 22/10/2026 às 22:00; sua idade observada na medição tabulada foi 3,31 horas e na repetição posterior 10,27 horas. O indicador inicial de idade do probe considerava apenas a data à meia-noite e foi corrigido; não há baseline confiável de idade anterior. O TTL das leituras permaneceu em 30 segundos, inferior ao intervalo esperado de 60 minutos dos sensores documentado no painel original. Na repetição posterior, os GETs e bytes mantiveram os valores da tabela. O digest do dashboard mudou com a hora da execução, pois a resposta contém valores dependentes do relógio; os demais cinco digests permaneceram iguais.

Os testes de C# cobrem data selecionada, transição de mês, dia atual ausente no `shallow`, sensor sem dados, última leitura, chat solar e climático, gráficos, JSON, CSV e PDF. Os testes de navegador do projeto C# usam dados simulados. O teste `LiveOriginalParityTests` depende dos dois painéis conectados e não foi executado contra o painel original nesta sessão. Não houve diferença nas respostas medidas do C#, mas a comparação visual e operacional completa com o original ainda não foi demonstrada.

Há uma diferença funcional preexistente confirmada pelo código: o original assina quatro caminhos Firebase com `listenToPath` e recebe atualizações; o frontend C# chama `carregarDashboard()` na inicialização e na troca de data, sem assinatura ou atualização periódica dos sensores. Assim, a página C# aberta pode continuar exibindo uma leitura antiga mesmo depois do TTL do backend. A mudança de atualização em tempo real ficou fora destas otimizações de backend, pois precisa de um desenho e medição próprios de tráfego e atualização. Não há medição de latência de rede real, concorrência de GETs ou carga do navegador.

Validação: `dotnet test EstacaoClimatica.sln --configuration Release --no-restore` passou com 229 testes antes da última checagem de revogação; após ela, 176 testes sem navegador passaram. Na suíte completa final, 229 de 230 passaram e o teste de navegador `ModoInternoCelularPaisagem_DeveSepararAbasEToolbarSemPerderInteracoes` falhou uma vez; isolado, passou na repetição. As alterações desta rodada não tocaram no frontend. `git diff --check` terminou sem erros de whitespace.

Validação para publicação em 23/10/2026: os 230 testes passaram tanto no projeto local quanto na cópia sanitizada. Os 10 testes do verificador de token também passaram após a substituição dos e-mails por exemplos. O PerformanceProbe compilou em Release e executou os seis cenários e a comparação de autenticação com uma fixture sintética, sem acessar o Firebase real.
