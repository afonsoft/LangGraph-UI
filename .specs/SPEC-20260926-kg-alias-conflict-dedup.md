# SPEC — Bug: KgAliases UNIQUE violation derruba sync inteiro

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260926-kg-alias-conflict-dedup` |
| Data | 2026-09-26 |
| Autor | Devin |
| Stack | `EF Core`, `SqliteKnowledgeGraphStore` |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Done` |
| Origem | incidente 2026-09-25 — autosync Obsidian, 465 docs falhos |

## 1. User Story

**As a** usuário com fonte `graph:true`
**I want** que uma entidade com múltiplos tipos não derrube o sync
**So that** o job não fique com 465 documentos falhos por uma constraint
violada numa linha de alias.

## 2. Contexto — root cause (logs do container)

`DbUpdateException` → `SQLite Error 19: UNIQUE constraint failed:
KgAliases.AliasNormalized, KgAliases.KgNodeId` — 1908x num autosync.

`SqliteKnowledgeGraphStore.ResolveNodeAsync` cria um nó novo quando o par
`(normalized, type)` não existe e então insere "conflict" aliases para TODOS os
siblings com o mesmo normalized — **sem verificar existência** (DB ou pendente).
Quando a entidade ganha um 3º tipo (recorrente: relações resolvem com
`type=null`), as aliases `(normalized, siblingId)` já existem → constraint.

Efeito dominó: o `DbContext` é o MESMO do pipeline de docs (um `scope` por sync)
— as entradas `Added` inválidas ficam no tracker, o catch do
`ExtractGraphAsync` engole a exceção, e todo `SaveChanges` posterior re-falha →
todos os documentos seguintes falham.

## 3. Requisitos Funcionais

- **RF-001** Antes de `db.KgAliases.Add`, verificar existência no banco E nos
  entries `Added` pendentes do `ChangeTracker` (helper `AliasExistsOrPending`).
- **RF-002** Aplicar ao loop de conflict-siblings e ao branch de merge.
- **RF-003** `ExtractGraphAsync`: scope/DbContext dedicado para o graph store
  (resolve `IServiceScopeFactory`, cria scope interno) — falha de grafo jamais
  contamina o contexto dos documentos.
- **RF-004** Defesa em profundidade: no catch de `ExtractGraphAsync`, detach de
  entries `Added` de `KgNode`/`KgAlias`/`KgEdge` pendentes no contexto chamador
  (caso algum caminho futuro compartilhe o contexto).
- **RF-005** Warning por doc usa `ex.GetBaseException().Message` — a causa real
  (`UNIQUE constraint...`) em vez do envelope `DbUpdateException`.

## 4. Requisitos Não-Funcionais

- Overhead de checagem ≤1 query por resolução de nó novo (já existe a query de
  siblings — reaproveitar).

## 5. Fora de Escopo

- Merge de tipos/heurística de resolução de entidade — algoritmo inalterado.

## 6. Plano de Tarefas

1. `AliasExistsOrPendingAsync` no store + dedup nos dois branches.
2. Scope dedicado no `ExtractGraphAsync` + detach defensivo.
3. `GetBaseException` nas mensagens de doc-failure.
4. Testes: entidade com 3 tipos não viola; falha de grafo não contamina docs
   seguintes; warning carrega mensagem interna.

## 7. Acceptance Criteria

- [ ] Sync com entidade multi-tipo → zero `DbUpdateException`, docs processam.
- [ ] Graph exception num doc N → doc N+1 ainda salva normalmente.
- [ ] Warnings exibem a SqliteException real.

## 8. Riscos

- Scope extra por doc: 1 DbContext a mais por documento — custo trivial vs LLM
  calls do extractor.
