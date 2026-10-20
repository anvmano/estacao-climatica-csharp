# Auditoria de paridade comportamental com o projeto original

Data: 08/09/2026.

Fonte da verdade: `D:\Documentos\03_Projetos\estacao climatica web\atualizacao claude`  
Projeto auditado: `D:\Documentos\03_Projetos\Estacao climatica C#`

## Conclusão

O projeto C# está funcionalmente em paralelo com o original na maior parte dos fluxos, mas a paridade ao vivo não é total no estado atual do Firebase. Foram comparadas 155 folhas do estado observável do dashboard interno; 152 ficaram iguais e três divergiram. As três diferenças têm uma única causa: o Firebase contém a chave inválida `53-82-2045/45-16` no ramo do Quarto.

O original ordena a data e a hora como texto e apresenta essa leitura como a mais recente. O C# interpreta datas reais, deixa a chave inválida fora da seleção e apresenta a última leitura válida. Isso altera dois cards do resumo global e, por consequência, o insight de ponto de orvalho do Quarto.

| Estado ao vivo | Original | C#/.NET |
| --- | --- | --- |
| Temp. Quarto | `27.50°C · 53/82/2045 · 45:16` | `26.30°C · 08/09/2026 · 17:00` |
| Umidade Quarto | `44.70% · 53/82/2045 · 45:16` | `76.20% · 08/09/2026 · 17:00` |
| Ponto de orvalho | `Risco baixo · 14.4°C` | `Atenção · 21.8°C` |

Essa diferença já existe no dado externo e não foi causada pela adequação de nomes. Nenhum código de produção foi alterado nesta auditoria.

## Abrangência

A matriz detalhada contém 52 famílias de comportamento em `outputs/auditoria-paridade-20260908/matriz-comportamentos.csv`. Foram revisados:

- abertura pública, CEP, cidade, geolocalização, concorrência e cache;
- cards externos, previsões, AQI, ciclo solar, recomendações e atualidade;
- login, allowlist, logout e proteção das APIs;
- data selecionada, abas, gráficos, estatísticas, tabelas, heatmaps e qualidade;
- indicadores do header, seções colapsáveis, zoom e responsividade;
- exportações JSON, CSV e PDF;
- assistente, memória curta, períodos, métricas, AQI, solar e heatmaps;
- acesso ao Firebase, cache/retry e endpoints de consulta exclusivos do backend C#.

### Resultado da matriz

- `Igual`: confirmado diretamente por comparação ao vivo ou de estado;
- `Igual em teste`: confirmado nas suítes determinísticas dos dois projetos;
- `Arquitetura equivalente`: implementação diferente com resultado ou contrato pretendido equivalente;
- `Parcial`: dois comportamentos afetados pela leitura inválida do Quarto;
- `Não validado ao vivo`: login Google com conta real e geração por IA real.

## Comparação interna ao vivo

Os dois projetos foram executados simultaneamente em `127.0.0.1`, com a data `03/06/2026`, viewport desktop `1440x900` e viewport móvel `390x844`. O teste coletou e comparou:

- data, aba ativa e nomes das quatro abas;
- quantidades e textos dos cards;
- estatísticas dos ambientes;
- ferramentas, cabeçalhos e linhas das tabelas;
- badges de qualidade;
- estado astronômico e ação dos insights;
- rotação, labels, eixos, unidades e datasets dos gráficos;
- células dos três tipos de heatmap;
- ciclo solar, duração do dia e textos dos insights;
- ausência de overflow e visibilidade dos controles no celular;
- erros de console e de página.

O estado móvel ficou integralmente igual: aba Sala ativa, controles visíveis e ausência de overflow horizontal nos dois projetos. No desktop, somente as três folhas descritas na conclusão divergiram.

A primeira tentativa ao vivo parou antes da carga porque o token sintético do navegador foi enviado ao verificador Firebase real. O servidor foi reiniciado apenas para auditoria com `InternalAccess__Enforce=false`, sem alterar arquivos de configuração ou código, e a segunda execução chegou à comparação completa.

## Modo público ao vivo

O navegador validou os projetos lado a lado contra as APIs atuais.

### CEP

- `01001-000`: local, horário, cards, AQI, recomendações, ciclo solar e textos ficaram iguais;
- CEP `123`: mesma mensagem `Informe um CEP com 8 dígitos.`, mesmo `aria-invalid=true` e mesmo estado visual;
- os dois renderizaram cinco gráficos e cinco botões `Ampliar gráfico`.

### Cidade

A pesquisa `Gramado` retornou, na mesma ordem:

1. `Gramado - Rio Grande do Sul`;
2. `Gramado (Sobradinho) - Rio Grande do Sul`;
3. `Gramado (Marau) - Rio Grande do Sul`;
4. `Gramado (Redenção da Serra) - São Paulo`;
5. `Gramado (Seara) - Santa Catarina`.

Após escolher a primeira opção, todo o texto renderizado no resultado foi idêntico, incluindo cards, recomendações, AQI, Lua, estações e duração solar.

### Zoom e cache

Nos dois projetos, o zoom público abriu um diálogo `plot-zoom-overlay`, colocou o foco em `Fechar gráfico ampliado`, fechou com `Escape` e devolveu o foco ao acionador. Após recarregar, ambos restauraram a consulta, os cinco botões de zoom e anunciaram `Última consulta desta sessão restaurada.`.

O sucesso com geolocalização foi validado com coordenadas simuladas pelas suítes. A auditoria não concedeu acesso à localização real do computador.

## Estrutura e ações

A análise estática encontrou os mesmos 27 controles identificados, as mesmas quatro abas e as mesmas três regiões `aria-live`. O original declara onze canvases climáticos diretamente no HTML; o C# cria esses canvases quando recebe a definição dos gráficos do backend. Depois da carga, o teste ao vivo encontrou os mesmos IDs, séries e eixos.

O original usa os eventos customizados `climate-collapsible-expanded` e `public-solar-events-updated` para comunicação entre módulos. O C# chama os módulos correspondentes diretamente. Essa é uma diferença arquitetural, pois o estado observável das seções e do indicador solar ficou igual.

## Autenticação e APIs

As suítes confirmam que:

- o painel privado só inicializa depois de um usuário autorizado;
- usuário fora da allowlist permanece no modo público;
- logout limpa dashboard, gráficos e chat;
- endpoints internos retornam `401` sem token;
- saúde e clima público permanecem acessíveis sem token;
- o verificador normaliza e-mails, rejeita token inválido e reutiliza cache;
- os endpoints de ambientes, medições, séries, resumo, AQI, solar, alertas, comparação e exportação validam filtros e contratos.

O login Google real não foi executado, pois exigiria uma conta e uma sessão interativa. A implementação, a lista de e-mails e os estados autorizados/não autorizados foram revisados e testados.

## Assistente

O original passou sua regressão de assistente. No C#, 37 cenários verificam valor por horário, última medição, continuidade, ambientes exclusivos, AQI, faixas, janelas móveis, comparações, heatmaps, solar, números por extenso, limites e atalhos da Estação. A interface real também testa abertura, foco, envio ao endpoint e renderização da resposta.

A arquitetura é diferente: o original usa Firebase AI Logic no cliente; o C# calcula no backend e usa Gemini opcionalmente para classificação/redação. Com IA desativada, as regras locais estão cobertas. Uma resposta gerada ao vivo não foi comparada porque não há chave configurada e a IA está desativada no ambiente.

## Exportação

Foram aprovados downloads JSON e PDF pelo botão real, contrato por aba, metadados, resumo, tabela, dados processados, gráfico solar fora da tela, progresso, timeout e nova tentativa. O original também validou um PDF com quatro páginas e sete blocos visuais. A exportação CSV da tabela visível manteve contagem, ordenação, BOM UTF-8 e separador `;`.

## Comandos e resultados

Projeto C#:

```powershell
dotnet build EstacaoClimatica.sln
dotnet test EstacaoClimatica.sln
dotnet test EstacaoClimatica.sln --no-build --filter "Category=LiveParity"
```

- build: aprovado, zero avisos e zero erros;
- suíte comum: 178/178 aprovada;
- paridade ao vivo habilitada: falhou por três diferenças em 155 folhas comparadas.

O teste `LiveParity` retorna imediatamente quando `RUN_LIVE_PARITY` não é `1`; portanto, sua presença entre os 178 testes comuns não significa que a comparação ao vivo passou. A execução separada com a variável habilitada é a evidência correta.

Projeto original:

```powershell
npm run validate
npm run lint
npm run test:assistant
npm run test:report
npm run test:pdf-artifact
npm run test:export
npm run test:accessibility
npm run test:data-quality
npm run test:public
npm run test:axe
npm run test:tables
```

Todos os onze comandos foram aprovados. `validate` conferiu 88 arquivos JavaScript; o artefato PDF teve quatro páginas e sete blocos.

## Evidências

- `outputs/auditoria-paridade-20260908/interno/estado-original.json`
- `outputs/auditoria-paridade-20260908/interno/estado-dotnet.json`
- `outputs/auditoria-paridade-20260908/interno/estado-mobile-original.json`
- `outputs/auditoria-paridade-20260908/interno/estado-mobile-dotnet.json`
- `outputs/auditoria-paridade-20260908/interno/original-desktop.png`
- `outputs/auditoria-paridade-20260908/interno/dotnet-desktop.png`
- `outputs/auditoria-paridade-20260908/interno/original-mobile-sala.png`
- `outputs/auditoria-paridade-20260908/interno/dotnet-mobile-sala.png`
- `outputs/auditoria-paridade-20260908/diferencas-estado-interno.json`
- `outputs/auditoria-paridade-20260908/auditoria-estrutura.json`
- `outputs/auditoria-paridade-20260908/firebase-temperatura.json`
- `outputs/auditoria-paridade-20260908/testes/paridade-ao-vivo-segunda-execucao.trx`
- `outputs/auditoria-paridade-20260908/validacao-csharp.log`
- `outputs/auditoria-paridade-20260908/validacao-original.log`

## Decisão necessária antes de alterar comportamento

Há duas formas coerentes de tratar a divergência:

1. reproduzir no C# a ordenação textual permissiva do original, fazendo o clone também exibir datas e horas inválidas como leituras recentes;
2. manter a validação do C# e corrigir o original para rejeitar chaves impossíveis, além de remover ou corrigir o registro inválido no Firebase.

A segunda opção preserva a integridade climática e evita que um dado corrompido afete cards e recomendações. Nenhuma das opções foi aplicada nesta auditoria porque ambas mudam comportamento e a solicitação foi analisar e verificar a paridade.
