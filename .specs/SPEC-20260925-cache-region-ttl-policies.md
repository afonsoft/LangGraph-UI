# SPEC — Políticas de TTL por região de cache

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260925-cache-region-ttl-policies` |
| Data | 2026-09-25 |
| Autor | Devin |
| Stack | `IDistributedCache`, `CacheOptions` |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Draft` |
| Origem | análise de cache 2026-09-25 |

## 1. User Story

**As a** operador afinando memória/Redis
**I want** TTLs por região de cache (search, answers, tools, embeddings, embeddings-doc)
**So that** embeddings (imutáveis por conteúdo) vivam dias e respostas de busca
vivam minutos, sem um TTL único global.

## 2. Contexto

`CacheKeys` já prefixa regiões (`search:`, `answer:`, `tool:`, `embed:`…) e
`TelemetryTags.RegionFor` deriva a região — mas o TTL é decidido ad-hoc em cada
call site. Resultado: embeddings e resultados de busca compartilham a mesma
política, o que desperdiça (reembedding igual custa chamada de provider) ou
envelhece (resposta sobrevive a sync se TTL longo — mitigado hoje por
`IndexVersionToken` apenas onde a key o embute).

## 3. Requisitos Funcionais

- **RF-001** `CacheOptions.Ttl` mapa por região com defaults:
  `search` 5min, `answer` 5min, `tool` 60min (floor existente), `embed` 7d,
  `embed-doc` 7d, `graph` 30min, `default` 30min. Config `Cache:Ttl:{region}=`.
- **RF-002** Helper central `CacheKeys.Region(key)` + `TtlFor(key)` usado por
  `SafeCache.Set*` quando o caller não passa TTL explícito.
- **RF-003** Embedding cache por hash de conteúdo: key `embed:{model}:{sha256(text)}`
  — dedup natural entre fontes/reindex; sem `indexVersion` na key (conteúdo é
  imutável — texto igual ⇒ vetor igual).
- **RF-004** Métrica `cache_evictions`/`cache_sets` por região (já existe hit/miss;
  adicionar set com tamanho).

## 4. Requisitos Não-Funcionais

- `ToolCacheService` mantém o floor de 60min (spec anterior) — config só aumenta.
- Chaves existentes seguem funcionais (TTL afeta só novas escritas).

## 5. Fora de Escopo

- L1/L2 → spec híbrida. Invalidação pub/sub → spec pub/sub.

## 6. Plano de Tarefas

1. `CacheOptions` + binding do mapa `Ttl`.
2. `EmbedTextAsync` call sites → content-hash key sem indexVersion.
3. `SafeCache.Set*` com TTL default por região.
4. Testes: resolução de TTL por prefixo, embedding dedup por hash.

## 7. Acceptance Criteria

- [ ] Mesma string embedada por duas fontes → 1 chamada ao provider.
- [ ] `Cache:Ttl:search` respeitado na leitura/escrita.
- [ ] Métricas de set por região visíveis.

## 8. Riscos

- Embedding cache por content-hash sem indexVersion: mudança de modelo já muda
  a key (`{model}` no prefixo) — seguro. Texto PII em hash é seguro (SHA-256
  one-way); o valor cacheado é o vetor float[] — sem conteúdo.
