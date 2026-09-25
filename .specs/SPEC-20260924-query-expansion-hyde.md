# SPEC — Expansão de query: multi-query + HyDE

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260924-query-expansion-hyde` |
| Data | 2026-09-24 |
| Autor | Devin |
| Stack | `.NET 10`, `IQueryRewriter`/`LlmQueryRewriter`, `SearchService`, RRF |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260924-query-expansion-hyde` |
| Status | `Done` |
| Ticket | — |
| Origem | tabnews "RAG na Prática" (técnicas avançadas: HyDE, multi-query); literatura Gao et al. 2023 |

## 1. User Story

**As a** usuário com pergunta ambígua, curta ou mal formulada ("como configura aquilo?")
**I want** que o sistema gere reformulações/variantes da query e um documento hipotético (HyDE) para ampliar recall
**So that** a busca encontre o documento certo mesmo quando a pergunta original não usa o vocabulário do corpus.

## 2. Contexto

`LlmQueryRewriter` já reescreve a query em uma única versão melhorada. Dois upgrades conhecidos:

- **Multi-query (RAG-Fusion):** gerar 2-4 reformulações (sinônimos, decomposição, variações de especificidade), executar busca por cada uma e fundir rankings via RRF — cobre vocabulários que uma única formulação perde.
- **HyDE (Hypothetical Document Embeddings):** LLM escreve uma resposta/documento hipotético para a pergunta; embedamos o documento hipotético (que está no "espaço de resposta") em vez da query — historicamente melhora recall em queries curtas/ambíguas. O doc hipotético NUNCA entra no contexto nem é citado — é só veículo de retrieval.

Ambos são opt-in e plugam no ponto onde `IQueryRewriter` já é chamado, antes dos rankings vetorial/lexical.

## 3. Requisitos Funcionais

### RF-001 — Multi-query
- `Search:QueryExpansion:Mode` = `off|multi|hyde|both` (default `off`).
- `MultiQueryExpander` (novo, ao lado de `IQueryRewriter`): 1 chamada LLM retorna N reformulações (`Search:QueryExpansion:Count`, default 3, max 5). Prompt exige JSON array; parse fail → fallback para query original.
- Cada variante executa pipeline híbrido completo (paralelo, `Task.WhenAll`, respeitando rate limit); rankings fundidos via `RrfFuser` existente sobre N listas.

### RF-002 — HyDE
- `HydeGenerator`: 1 chamada LLM ("escreva um trecho de documento que responderia a pergunta") → embedding do texto hipotético substitui o embedding da query no braço vetorial; braço lexical continua com a query original (HyDE é técnica vetorial — doc hipotético no FTS poluiria).
- Modo `both`: multi-query no lexical + HyDE no vetorial, fundidos.

### RF-003 — Custos e cache
- Expansões e docs hipotéticos cacheados em `IDistributedCache` (key = hash da query normalizada + modelo; TTL `Search:QueryExpansion:Ttl`, default 1h).
- LLM indisponível/timeout → fallback silencioso para query original (nunca falha a busca).
- Arg `expand` em `search_knowledge` para override por chamada (`off|multi|hyde|both|default`).

### RF-004 — Telemetria
- Tags: `search.expansion.mode`, `search.expansion.variants`, `search.expansion.cached`.
- `ScoreBreakdown` ganha `ExpandedFrom` (qual variante produziu o hit) para debug/eval.

## 4. Requisitos Não-Funcionais

- Custo: +1 chamada LLM (cacheável) por query expandida; +N-1 buscas internas (paralelas — latência ≈ a mais lenta, não a soma).
- Sem chat configurado (offline/ONNX-only) → expansion desliga automaticamente.
- P95 alvo: +400ms max sobre busca simples quando LLM local.

## 5. Fora de escopo

- Step-back prompting, decomposição de multi-hop (candidata a futura spec de agentic retrieval).
- Fine-tuning de expansão.

## 6. Plano de tarefas

| # | Tarefa |
|---|---|
| T1 | `IQueryExpander` + `MultiQueryExpander` + `HydeGenerator` (prompts, parse resiliente, cache) |
| T2 | `SearchService`: N pipelines paralelos + fusão RRF multi-lista + `ExpandedFrom` |
| T3 | Args/config + contract tests |
| T4 | Telemetria + eval antes/depois (Recall@K é a métrica alvo) |
| T5 | Testes: variantes fundidas; HyDE só no braço vetorial; fallback sem LLM; cache hit não chama LLM |

## 7. Critérios de aceite

- [ ] Query com vocabulário divergente do doc recupera o doc em modo `multi` que falha em `off` (caso no eval dataset).
- [ ] HyDE nunca aparece como resultado nem citation.
- [ ] Sem `IChatClient` → comportamento idêntico a `off`, sem exceção.
- [ ] Segunda execução da mesma query não repete chamada LLM (cache).
- [ ] Suite verde; contract tests atualizados.

## 8. Riscos

- Variantes ruins podem puxar ruído para o ranking fundido → cap de variantes + eval obrigatório antes de ligar em produção.
- Custo de LLM em alto volume → cache + opt-in por tool arg.
