# SPEC — Diversidade de retrieval: MMR, quota por documento e score floor

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260924-mmr-diversity` |
| Data | 2026-09-24 |
| Autor | Devin |
| Stack | `.NET 10`, `SearchService`, sqlite-vec/pgvector, FTS5 |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260924-mmr-diversity` |
| Status | `Draft` |
| Ticket | — |
| Origem | Análise comparativa RAG (tabnews "RAG na Prática": estratégia MMR; pedrolealdino: chunks do mesmo doc dominando topK) |

## 1. User Story

**As a** usuário/agente consultando a base
**I want** que o topK final não seja dominado por N chunks quase idênticos do mesmo documento e que resultados abaixo de um limiar de relevância sejam descartados
**So that** o contexto enviado ao LLM cobre ângulos diferentes da pergunta em vez de repetir a mesma passagem — e respostas não se baseiam em chunks marginalmente relevantes.

## 2. Contexto

`SearchService` hoje: fusão RRF vetorial+lexical → reranker opcional → topK flat. Problemas observados na literatura e reproduzíveis aqui:

- Documentos longos geram chunks adjacentes quase duplicados; todos ranqueiam juntos e o topK vira "5 versões do mesmo parágrafo" (perda de cobertura — o recall@K real cai mesmo com score alto).
- Não existe corte por relevância mínima: se a query não tem nada na base, o topK volta cheio de lixo e o LLM recebe contexto enganoso (ver SPEC corretiva — este spec é o sinal que ela consome).
- RRF puro não mede redundância entre candidatos.

Técnica: **MMR (Maximal Marginal Relevance)** — `score(i) = λ·rel(i) − (1−λ)·max sim(i, já selecionados)`, aplicado sobre os candidatos pós-RRF usando os embeddings já armazenados (reuso dos vetores do sqlite-vec/pgvector — zero chamadas extras ao provider). Complementos baratos: **quota por documento** e **score floor** no score fundido.

## 3. Requisitos Funcionais

### RF-001 — MMR pós-fusão
- Após RRF (e antes/depois do reranker conforme `Search:Rerank:Enabled`), aplicar MMR com λ configurável (`Search:Diversity:Lambda`, default `0.7`) sobre a janela de candidatos (`topK×4`).
- Similaridade entre candidatos calculada via cosseno sobre os embeddings já persistidos (carregar vetores dos candidatos; não re-embed).
- `Search:Diversity:Enabled` (default `false` — opt-in, comportamento atual preservado).

### RF-002 — Quota por documento
- `Search:Diversity:MaxPerDocument` (default `0` = sem quota): limita quantos chunks do mesmo `DocumentId` entram no resultado final; excedentes dão lugar ao próximo candidato.
- Combinação com MMR: quota aplica primeiro (hard rule), MMR desempata o restante.

### RF-003 — Score floor
- `Search:MinScore` (default `0` = desligado): descarta candidatos com score fundido < limiar após fusão. Resultado pode voltar com menos que topK — intencional (sinal de confiança para a camada de geração/abstention).
- `SearchResultItem` já carrega `ScoreBreakdown`; o floor usa `Fused` (ou score único em modo não-híbrido).

### RF-004 — Telemetria
- Tags no `Activity` atual: `search.diversity.removed`, `search.floor.removed`, `search.final.count`.
- Métricas OTel existentes (SPEC-20260923-observability-metrics) ganham counter `search.candidates.dropped` com tag `reason=quota|mmr|floor`.

## 4. Requisitos Não-Funcionais

- Zero chamadas extras ao embedding provider — reuso dos vetores persistidos; se vetor indisponível para um candidato, MMR degrada para RRF puro naquele item.
- Overhead alvo: < +10% latência sobre o pipeline atual em 10k chunks (similaridade MMR é O(candidatos²) sobre ≤ 4·topK itens).
- Comportamento default inalterado (feature flag off).

## 5. Fora de escopo

- Cross-encoder reranker dedicado (já existe `IReranker` LLM opt-in).
- Clustering de tópicos / dedup semântico na ingestão.
- Diversidade temporal (preferir docs recentes) — candidate a spec separada de recência.

## 6. Plano de tarefas

| # | Tarefa |
|---|---|
| T1 | `MmrSelector` puro (lista de candidatos + vetores + λ → lista reordenada/filtrada) — unit-testável |
| T2 | Carregar vetores dos candidatos no `SearchService` (vector store expõe fetch-by-id) |
| T3 | Aplicar quota por `DocumentId` + score floor + MMR no pipeline; flags de config |
| T4 | Telemetria + `EvalRunner` mede antes/depois (dataset existente) |
| T5 | Testes: chunks adjacentes duplicados não dominam; quota respeitada; floor filtra; disabled = byte-idêntico ao atual |

## 7. Critérios de aceite

- [ ] **Given** 10 chunks candidatos sendo 6 do mesmo documento e `MaxPerDocument=2` **when** search **then** no máximo 2 chunks daquele doc no resultado.
- [ ] **Given** candidatos com scores fundidos [0.9, 0.4, 0.1] e `MinScore=0.3` **when** search **then** retorna 2 itens.
- [ ] **Given** dois candidatos com similaridade >0.95 **when** MMR ativo **then** o segundo é penalizado abaixo de um candidato menos similar com relevância comparável.
- [ ] **Given** `Diversity:Enabled=false` **when** search **then** resultado idêntico ao comportamento atual.
- [ ] Suite verde; eval harness reporta recall@K não-regressivo no dataset existente.

## 8. Riscos

- MMR com λ baixo pode descartar o único chunk que contém a resposta → mitigado por λ default alto (0.7) e por eval antes/depois.
- Vetores não carregáveis (store sem fetch-by-id) → degradar para quota+floor apenas.
