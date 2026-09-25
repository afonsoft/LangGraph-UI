# SPEC — pgvector: coluna halfvec opcional (pgvector ≥ 0.7)

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260925-pgvector-halfvec` |
| Data | 2026-09-25 |
| Autor | Devin |
| Stack | `pgvector`, `Npgsql`, `Pgvector` |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Done` |
| Origem | análise de vector store 2026-09-25 |

## 1. User Story

**As a** operador com corpus grande (>>100k chunks)
**I want** embeddings armazenados como `halfvec` quando o servidor suporta
**So that** a tabela e o índice HNSW usem ~metade da memória/disco e queries
fiquem mais rápidas com recall praticamente igual (float32→16 por dimensão).

## 2. Contexto

A coluna é `vector(384)` (float32 = 1.5KB/linha). pgvector 0.7+ oferece
`halfvec` (float16 = 768B/linha) com `vector_cosine_ops` — metade do tamanho do
índice, menos I/O, benchmark público: recall@10 ≥ 0.99 vs float32 na maioria dos
corpora. Segurança da mudança: precisa detectar a versão do servidor e migrar.

## 3. Requisitos Funcionais

- **RF-001** `PostgresOptions.StorageType = "vector" | "halfvec"` (default
  `vector` — opt-in). Quando `halfvec`, DDL usa `halfvec(dims)` e writes fazem
  `CAST($n AS halfvec)`.
- **RF-002** Detecção automática de versão do pgvector no init
  (`SELECT extversion FROM pg_extension WHERE extname='vector'`): pediu halfvec
  + servidor <0.7 → warning estruturado e fallback para `vector`.
- **RF-003** Migração: `ALTER TABLE kh_embeddings ALTER COLUMN embedding TYPE
  halfvec` via spec de upgrade quando storage muda — bloqueado atrás de
  `PostgresOptions:AllowStorageMigration=true` (operação reescreve a tabela).
- **RF-004** HNSW `halfvec_cosine_ops` ao criar o índice em coluna halfvec.

## 4. Requisitos Não-Funcionais

- Dimensão ≤ 2000 (limite halfvec por índice no pgvector 0.7; validar contra
  `Dimensions` e recusar cedo com mensagem clara).
- Score/report continua float — `HalfVector` do pacote `Pgvector` ≥0.32.

## 5. Fora de Escopo

- Quantização binária (`bit`) + rescoring em duas fases — futuro.

## 6. Plano de Tarefas

1. Options + version detect + DDL parametrizado.
2. Upsert/search com cast condicional.
3. Spec de migração de coluna + testes com pgvector container (CI tem serviço).

## 7. Acceptance Criteria

- [ ] `StorageType=halfvec` + pgvector≥0.7 → coluna `halfvec`, índice
  `halfvec_cosine_ops`, round-trip de busca funcional.
- [ ] pgvector<0.7 → fallback `vector` + warning.
- [ ] Dimensão >2000 + halfvec → falha de config explícita no boot.

## 8. Riscos

- Reescrita da tabela em migração — documentar janela/lock.
