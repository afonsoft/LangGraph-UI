# SPEC — Contextualização de query com histórico da conversa

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260924-conversational-query-context` |
| Data | 2026-09-24 |
| Autor | Devin |
| Stack | `.NET 10`, `LlmQueryRewriter`, `AgentService`, `ConversationThread` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260924-conversational-query` |
| Status | `Done` |
| Ticket | — |
| Origem | tabnews (query rewriting contextual); todos os guias de produção (perguntas de follow-up são o caso real dominante em chat) |

## 1. User Story

**As a** usuário numa thread de chat fazendo pergunta de follow-up ("e quanto ao segundo?", "como configuro isso?", "e em produção?")
**I want** que a busca interna receba a pergunta reescrita de forma autocontida — pronomes e referências resolvidos contra o histórico
**So that** follow-ups recuperem os documentos certos em vez de buscar "isso"/"segundo" literalmente e voltar vazio.

## 2. Contexto

`LlmQueryRewriter` (SPEC-20260923-retrieval-quality) melhora a query isolada — expande acrônimos, normaliza termos — mas **não recebe o histórico**. No `AgentService`, threads mantêm contexto para a geração, porém as tool calls internas (`search_knowledge`) usam a query como o modelo a emitiu; quando o próprio LLM resolve o pronome no tool-call ele já ajuda, mas quando passa a pergunta crua, o retrieval quebra.

Padrão canônico (LangChain `createHistoryAwareRetriever`, todo guia de chat-RAG): antes de recuperar, reescrever a pergunta como standalone usando as últimas N mensagens da thread. O histórico já está carregado em `Preparation` (`BuildContextWindow`) — basta passar uma janela curta ao rewriter quando a tool é invocada dentro de uma thread.

## 3. Requisitos Funcionais

### RF-001 — Rewriter contextual
- `IQueryRewriter` ganha overload `RewriteAsync(query, conversationContext, ct)` — assinatura nova preserva a atual (default: ignora contexto).
- `LlmQueryRewriter`: prompt contextual — "dada a conversa e a última pergunta, escreva a pergunta autocontida equivalente; não responda". Input: últimas `Agent:QueryContext:HistoryMessages` mensagens (default 4, só conteúdo textual truncado a ~200 chars/msg).
- Se `conversationContext` vazio → comportamento atual.

### RF-002 — Integração no agent loop
- `CatalogToolAIFunction`/`AgentService`: ao invocar `search_knowledge`/`ask_knowledge` dentro de uma thread, passa o contexto recente da conversa ao rewriter.
- Tool result registra `rewrittenQuery` (transparência/debug — já exposto? verificar `ScoreBreakdown`/response payload; se não, adicionar campo opcional).

### RF-003 — Cache e custo
- Cache da reescrita contextual por hash(query + context) — mesma pergunta na mesma janela não repete LLM.
- `Agent:QueryContext:Enabled` (default true quando chat configurado).

## 4. Requisitos Não-Funcionais

- +1 chamada LLM curta por tool-call de busca em thread (cacheável); skip quando a pergunta já é autocontida? — heurística barata opcional: se query não contém pronomes/referências curtas, skip (configurável `SkipHeuristic`, default off — LLM decide melhor).
- Thread `null` (chamadas diretas às tools) → caminho atual intacto.

## 5. Fora de escopo

- Compressão/sumarização adicional do histórico (já existe em threads).
- Memória cross-thread.

## 6. Plano de tarefas

| # | Tarefa |
|---|---|
| T1 | Overload contextual no `IQueryRewriter` + prompt |
| T2 | Passagem do contexto no caminho das tools dentro de thread |
| T3 | `rewrittenQuery` no resultado + cache |
| T4 | Testes: follow-up com pronome gera query autocontida; sem thread → inalterado; cache hit; prompt não deixa o rewriter responder a pergunta |

## 7. Critérios de aceite

- [ ] **Given** thread sobre "PostgreSQL" e follow-up "e como faço backup dele?" **when** o agente busca **then** a query enviada ao retrieval menciona backup de PostgreSQL explicitamente.
- [ ] Tool call fora de thread comporta-se como hoje.
- [ ] Mesma pergunta na mesma janela não repete a chamada LLM.
- [ ] Suite verde; contract tests dos tools atualizados se payload mudar.

## 8. Riscos

- Rewriter pode "responder" em vez de reescrever → prompt rígido + validação (descarta output com markup de resposta/pergunta direta longa demais; fallback à query original).
- Contexto muito longo encarece → janela curta + truncamento por msg.
