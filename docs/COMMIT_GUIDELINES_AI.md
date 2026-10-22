# Diretrizes para Criação de Mensagens de Commit

> Este arquivo reúne as diretrizes para mensagens de commit deste repositório.

## Objetivo

Ao concluir alterações no projeto, analise os arquivos modificados, o comportamento anterior e o comportamento resultante para criar uma mensagem de commit clara, objetiva e compatível com o padrão **Conventional Commits**.

A mensagem deve descrever o que foi alterado e, quando relevante, o motivo ou impacto da alteração.

---

## Formato obrigatório

```text
<tipo>(<escopo>): <resumo curto>

<descrição opcional>
```

Exemplo:

```text
perf(relatorio): carregar bibliotecas do PDF apenas sob demanda

Evita carregar html2canvas e jsPDF na abertura inicial da página.
```

---

## Tipos permitidos

| Tipo       | Quando usar                                                                 |
| ---------- | --------------------------------------------------------------------------- |
| `feat`     | Nova funcionalidade disponível ao usuário ou sistema                        |
| `fix`      | Correção de comportamento incorreto, erro ou bug                            |
| `refactor` | Reorganização interna sem alterar comportamento funcional                   |
| `perf`     | Melhoria de desempenho, carregamento, memória ou processamento              |
| `docs`     | Alteração somente em documentação                                           |
| `test`     | Criação, ajuste ou correção de testes                                       |
| `style`    | Alteração visual, CSS, formatação ou lint sem alteração de regra de negócio |
| `build`    | Build, bundler, dependências, scripts ou configuração de empacotamento      |
| `ci`       | Pipeline de integração, deploy ou automação de repositório                  |
| `chore`    | Manutenção técnica sem impacto funcional direto                             |

---

## Definição do escopo

O escopo deve representar o módulo principal afetado, em letras minúsculas e sem espaços.

Exemplos de escopo:

```text
dashboard
firebase
relatorio
pdf
graficos
sensores
aquario
sala
quarto
chat
ia
exportacao
dependencias
documentacao
arquitetura
testes
```

Se a alteração atingir vários módulos e não houver um módulo principal claro, omita o escopo:

```text
refactor: reorganizar carregamento de módulos da aplicação
```

---

## Regras para o resumo

A primeira linha deve:

* Começar com verbo no infinitivo.
* Ser escrita em português.
* Ter preferência por até 72 caracteres.
* Explicar a alteração principal.
* Não terminar com ponto final.
* Não usar termos vagos como `ajustes`, `alterações`, `melhorias`, `update` ou `correções`.
* Não listar todos os arquivos alterados.
* Não incluir detalhes técnicos secundários que pertençam ao corpo.

### Exemplos corretos

```text
feat(chat): adicionar consulta de histórico por horário
fix(firebase): corrigir leitura de registros por data
perf(relatorio): adiar carregamento de dependências do PDF
refactor(sensores): separar cálculo e leitura de pH
docs(arquitetura): atualizar mapa de dependências
test(aquario): cobrir cálculo de média de temperatura
```

### Exemplos incorretos

```text
ajustes no projeto
correções gerais
melhorias
update arquivos
alterações no firebase e no pdf
```

---

## Corpo da mensagem

Inclua corpo somente quando houver contexto útil.

O corpo deve:

* Explicar o motivo, impacto ou comportamento alterado.
* Usar frases curtas.
* Informar efeitos relevantes para carregamento, desempenho, compatibilidade ou dados.
* Informar validações executadas quando disponíveis.
* Não repetir o resumo.

Exemplo:

```text
perf(exportacao): carregar bibliotecas do PDF somente ao exportar

html2canvas e jsPDF deixam de ser carregados na abertura inicial.
Reduz o custo inicial para usuários que não utilizam exportação.

Validações:
- dotnet build EstacaoClimatica.sln
- dotnet test EstacaoClimatica.sln
- Smoke test no Chrome headless
```

---

## Alterações que devem gerar commits separados

Sugira separar em commits diferentes quando as alterações não forem dependentes entre si.

Exemplos:

* Nova funcionalidade e atualização de documentação não relacionada.
* Refactor amplo e correção de bug independente.
* Alteração visual e mudança de regra de negócio.
* Atualização de dependência e implementação de funcionalidade.
* Testes de um módulo sem relação com a alteração principal.

Não sugira separar quando documentação, testes ou ajustes de configuração forem necessários para concluir a mesma funcionalidade.

---

## Alterações com quebra de compatibilidade

Quando uma alteração exigir ajuste em consumidores existentes, use `!` após o tipo ou escopo:

```text
feat(api)!: alterar formato de retorno dos dados de sensores
```

Inclua no corpo:

```text
BREAKING CHANGE: o retorno deixa de usar propriedades planas e passa a agrupar dados por sensor.
```

---

## Processo obrigatório de análise

Antes de gerar a mensagem:

1. Analise os arquivos modificados.
2. Identifique a intenção principal da alteração.
3. Diferencie funcionalidade, correção, refactor, performance, documentação e configuração.
4. Identifique o módulo mais afetado para definir o escopo.
5. Verifique se houve mudança de comportamento observável.
6. Verifique se há impacto em desempenho, carregamento, compatibilidade, segurança ou persistência de dados.
7. Identifique testes, validações ou smoke tests executados.
8. Avalie se as alterações devem ser divididas em mais de um commit.
9. Gere a mensagem final em bloco de código, pronta para copiar.

---

## Formato da resposta esperado da IA

Sempre responda nesta estrutura:

````markdown
## Tipo identificado

`<tipo>`

## Escopo identificado

`<escopo ou "sem escopo">`

## Justificativa

- Motivo da classificação.
- Principal comportamento alterado.
- Impacto relevante, se houver.

## Mensagem de commit

```text
<tipo>(<escopo>): <resumo>

<descrição opcional>
````

## Separação recomendada

* `Não necessária`, quando as alterações fizerem parte da mesma entrega.

Ou:

* Commit 1: `<mensagem>`
* Commit 2: `<mensagem>`

````

---

## Exemplos por cenário

### Nova funcionalidade

```text
feat(aquario): adicionar gráfico de turbidez diária

Exibe a variação de turbidez ao longo do dia na aba Aquário.
````

### Correção

```text
fix(chat): corrigir consulta de temperatura por hora

Normaliza a data antes de buscar os registros no Firebase.
```

### Performance

```text
perf(dashboard): adiar montagem de heatmaps climáticos

Cria o DOM dos heatmaps apenas quando a seção é expandida.
```

### Refactor

```text
refactor(pdf): separar geração de gráficos da montagem do relatório

Mantém o resultado visual e reduz acoplamento entre os módulos.
```

### Dependências e build

```text
build(vite): configurar divisão de dependências por carregamento dinâmico
```

### Documentação

```text
docs(arquitetura): registrar fluxo de carregamento sob demanda
```
