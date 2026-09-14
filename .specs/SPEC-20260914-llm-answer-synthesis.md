# SPEC — LLM Provider e síntese de resposta server-side (perna "Generation" do RAG)

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260914-llm-answer-synthesis` |
| Data | 2026-09-14 |
| Autor | Devin |
| Stack | `.NET 10`, `Microsoft.Extensions.AI` (IChatClient), `Ollama`, `OpenAI` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260914-llm-answer-synthesis` |
| Status | `Approved` |

## 1. User Story

**As a** consumidor do KnowledgeHub (UI, REST ou MCP)
**I want** que `ask_knowledge` sintetize uma resposta final com citações, gerada por um LLM configurável no servidor
**So that** o RAG seja completo (Retrieve → Augment → **Generate**) dentro do produto, e não dependa do LLM do cliente MCP para redigir a resposta.

## 2. Contexto

Hoje `ask_knowledge` retorna apenas o *contexto recuperado* formatado — a geração fica a cargo do cliente MCP (Cursor/Claude Desktop). Isso funciona para agentes externos, mas:

- O Playground e futuras UIs não têm LLM próprio para sintetizar.
- A cadeia RAG fica incompleta como produto standalone (comparar com AWS Bedrock Knowledge Bases, que gera a resposta com citações).

Abstração: `Microsoft.Extensions.AI.IChatClient` — interface padrão .NET para chat models (Ollama, OpenAI, Azure, Anthropic via pacotes `*.Ollama`, `*.OpenAI`...). Segue o mesmo padrão já usado em `IEmbeddingProvider`/`EmbeddingProviderFactory`.

## 3. Requisitos Funcionais

### RF-001 — `IChatProvider`/`IChatClient` configurável
- Nova seção `Chat:` em config: `Provider` (`none`|`ollama`|`openai`), `Model`, `Endpoint`/`BaseUrl`, `ApiKey` (env only), `Temperature`, `MaxTokens`.
- `Provider=none` (default) → comportamento atual preservado (contexto cru), zero breaking change.
- Factory `ChatClientFactory` espelhando `EmbeddingProviderFactory`.

### RF-002 — Síntese com citações
- `ask_knowledge` ganha args opcionais: `generate` (bool, default true quando provider configurado), `source` (filtro opcional por slug).
- Com provider ativo: prompt template = system ("responda apenas com base no contexto; cite [n]") + contexto rankeado + pergunta → resposta textual + bloco `structuredContent` com `answer` + `citations[]` (`{index, source, title, uri, score}`).
- Sem contexto recuperado: resposta declara explicitamente que a base não teve matches (anti-alucinação — requisito dos artigos AWS/Alura).
- `generate=false` mantém o formato atual de contexto puro.

### RF-003 — REST
- `POST /api/ask` (ou extensão de `/api/search`) retornando `{answer, citations[], latencyMs, model}`.
- O Playground exibe answer + citações clicáveis quando a tool for `ask_knowledge`.

### RF-004 — Compatibilidade MCP
- `inputSchema` de `ask_knowledge` muda (novos campos opcionais) — atualizar o pin em `McpContractTests`.
- Nenhum tool existente removido ou renomeado.

## 4. Requisitos Não-Funcionais

- Timeout de chamada LLM configurável (default 120 s); falha do provider → `isError` com mensagem clara, nunca 500 sem corpo.
- Sem streaming nesta SPEC (ver SPEC de streaming separada).
- `ApiKey` nunca em `configuration.json` de fonte nem em logs — env var/`--environment` apenas.
- Sem lock de provider: trocar `Chat:Provider` exige apenas restart.

## 5. Fora de escopo

- Loop agêntico multi-step (SPEC `agent-chat-loop`).
- Streaming de tokens (SPEC `streaming-answers`).
- Rerank/hybrid search (SPEC `hybrid-retrieval`).

## 6. Plano de tarefas

| # | Tarefa |
|---|---|
| T1 | `ChatOptions` + `ChatClientFactory` + registro condicional de `IChatClient` |
| T2 | `AnswerService`: monta prompt, chama LLM, extrai/valida citações |
| T3 | `ask_knowledge`: novos args + structuredContent; update contract test |
| T4 | `POST /api/ask` + DTOs em Shared |
| T5 | Playground: render answer + citações; README + `.env` exemplo |

## 7. Critérios de aceite

- [ ] `Chat:Provider=none` → comportamento idêntico ao atual (teste de contrato).
- [ ] Com Ollama configurado, `ask_knowledge` retorna resposta sintetizada com ≥1 citação quando há contexto.
- [ ] Base vazia → resposta declara ausência de matches (não inventa).
- [ ] Citações carregam `source`/`uri` reais do hit correspondente.
- [ ] Falha/timeout do LLM → `isError:true`, mensagem clara.
- [ ] Suite verde; `McpContractTests` atualizado e passando.

## 8. Riscos

| Risco | Mitigação |
|---|---|
| Alucinação de citações pelo LLM | prompt fixo + validação: índices citados fora do range são removidos/marcados |
| Provider ausente mas `generate=true` | fallback para contexto puro + aviso no resultado |
| Latência LLM longa em MCP | timeout configurável; `isError` em vez de travar sessão |
