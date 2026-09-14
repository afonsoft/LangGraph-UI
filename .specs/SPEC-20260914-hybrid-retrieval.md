# SPEC — Retrieval híbrido: FTS5 lexical + vetorial com fusão RRF e filtros

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260914-hybrid-retrieval` |
| Data | 2026-09-14 |
| Autor | Devin |
| Stack | `.NET 10`, `EF Core SQLite (FTS5)`, vector store existente |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260914-hybrid-retrieval` |
| Status | `Done` |
| Ticket | `#34` |

## 1. User Story

**As a** usuário consultando a base de conhecimento
**I want** buscas que combinem similaridade semântica com correspondência lexical (termos exatos, nomes próprios, códigos)
**So that** queries com identificadores, siglas e termos literais — onde embedding puro é fraco — retornem os documentos certos.

## 2. Contexto

`SearchService` hoje: embed query → `IVectorStore.SearchAsync` topK → hidrata chunks. Limitações conhecidas (literatura RAG — AWS/Alura):

- Vetorial falha em termos exatos: IDs, siglas, nomes próprios, literais de código.
- Sem filtro por fonte/metadados exposto nas tools (`sourceId` existe internamente, não chega ao usuário).
- Sem rerank: o topK vetorial vai direto ao consumidor.

Solução pragmática sem dependência nova: **SQLite FTS5** (`chunks_fts` virtual table, `porter`/`unicode61` tokenizer) + **fusão Reciprocal Rank Fusion (RRF)** entre ranking vetorial e lexical. PostgresVectorStore mantém comportamento vetorial + `ILIKE`/`tsvector` opcional — mas o alvo primário é SQLite (deployment atual).

## 3. Requisitos Funcionais

### RF-001 — Índice FTS5
- Migration: `CREATE VIRTUAL TABLE chunks_fts USING fts5(text, content='Chunks', content_rowid=...)` ou tabela FTS independente com `ChunkId` (binary GUID como TEXT), populada por backfill e mantida por triggers OU pelo caminho de escrita do `IngestionService`/`write_knowledge` (escolher no design — preferir writes explícitos a triggers para manter EF testável).
- Config `Search:Lexical:Enabled` (default true em SQLite).

### RF-002 — Fusão RRF
- `SearchService` executa ambos os rankings em paralelo: vetorial (topK×N candidatos, N=4) e lexical `MATCH` (mesma janela).
- `score = Σ 1/(k + rank)` com `k=60`; resultado único ordenado por RRF, campos `vectorRank`/`ftsRank` preservados no item para debug.
- `SearchResultItem` ganha `ScoreBreakdown` opcional (`Vector`, `Lexical`, `Fused`).

### RF-003 — Filtros nas tools
- `search_knowledge` e `ask_knowledge` ganham args opcionais: `source` (slug), `mode` (`hybrid`|`semantic`|`lexical`, default `hybrid`).
- Schemas pinados em `McpContractTests` atualizados.

### RF-004 — Manutenção do índice
- Inserts/updates/deletes de chunks sincronizam o FTS no mesmo `SaveChanges` lógico (via `IngestionService` e `write_knowledge`).
- `POST /api/sources/{id}/sync` e migração inicial fazem backfill idempotente.

## 4. Requisitos Não-Funcionais

- Fallback: se FTS não existir (DB antigo sem migration aplicada — impossível após `Migrate()`, mas defensivo) → modo semantic puro + warning.
- Query lexical deve sanitizar termos (escape de `"`, `*`, operadores FTS) — erro de sintaxe FTS nunca vaza como 500.
- Overhead alvo: busca híbrida < +30% da latência vetorial pura em DB de 10k chunks.

## 5. Fora de escopo

- Reranker cross-encoder/LLM (pode ser SPEC futura — o RRF já captura a maior parte do ganho).
- Postgres `tsvector` (PostgresVectorStore fica em modo semantic; documentado).
- Highlight/snippet `fts5 snippet()` — nice-to-have, não bloqueante.

## 6. Plano de tarefas

| # | Tarefa |
|---|---|
| T1 | Migration + entidade/`DbSet` do FTS + backfill |
| T2 | `LexicalSearch` service (MATCH sanitizado, ranking) |
| T3 | `SearchService`: fusão RRF + `mode` + filtro `source` |
| T4 | Args nas tools + contract tests + DTO `ScoreBreakdown` |
| T5 | Testes: termo exato achado por lexical que vetorial perde; mode=semantic idêntico ao atual; sync mantém FTS consistente |

## 7. Critérios de aceite

- [ ] Query por literal presente num doc ranqueia #1 em modo híbrido mesmo com similaridade vetorial baixa.
- [ ] `mode=semantic` preserva resultados atuais.
- [ ] `source` filtra corretamente nos dois rankings.
- [ ] Insert/update/delete de chunk reflete no FTS (teste de integração).
- [ ] FTS syntax error (ex.: `"` não balanceada) → resultado lexical vazio, não exceção.
- [ ] Suite verde; contract tests atualizados.

## 8. Riscos

| Risco | Mitigação |
|---|---|
| FTS desincroniza dos chunks | sync via caminho de escrita único + teste de reconciliação |
| RRF premia lexical em queries puramente semânticas | janela de candidatos + `mode` escapável; medir com caso de teste real |
| Tokenizer inadequado p/ pt-BR | `unicode61` com `remove_diacritics 2` |
