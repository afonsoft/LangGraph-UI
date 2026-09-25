# SPEC — Segurança na troca de embeddings: gate de dims contra saída real, signature completa, dispose drenado, réplicas/documentação

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260926-embeddings-swap-safety` |
| Data | 2026-09-26 |
| Autor | Devin |
| Stack | `KnowledgeHub.Server` (SettingsEndpoints, EmbeddingProviderResolver, EmbeddingSettingsService, EmbeddingOptions) + `KnowledgeHub.Client` (Settings.razor) |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Approved` |
| Ticket | devin-ai-integration review — PR #205 (4 findings 🔴/🟡) + PR #184 (input_type) + #190 (role na key) |
| Origem | `.claude/memory/devin-review-triage-20260925.md` — cluster C (C1–C5) |

## 1. User Story

**As a** operador trocando provider/modelo de embeddings em runtime
**I want** que a validação cubra a saída real do modelo e que a troca não corrompa buscas em voo nem caches
**So that** uma config inválida falha no PUT (não na ingestão) e a troca é segura.

## 2. Evidências (AS-IS)

| # | Achado | Evidência |
|---|---|---|
| C1 | Gate de dims valida `body.Dimensions != store.Dimensions` mas não a **saída real do provider** — store=512 + dims=512 + `provider=onnx` passa, embora o ONNX (all-MiniLM-L6-v2) emita sempre 384 → ingestão/busca falham em runtime | `SettingsEndpoints.cs` PUT `/embeddings` (check só contra `store.Dimensions`) |
| C2 | `EmbeddingProviderResolver.Signature` omite `Asymmetric.Auto` — flip Auto→false (ou true) mantém a mesma signature → provider não rebuilda e fingerprint do cache `emb:` não muda → vetores stale reutilizados | `Signature()` inclui `Enabled/QueryPrefix/DocumentPrefix` mas não `Auto` (`EmbeddingOptions.AsymmetricOptions.Auto` existe) |
| C3 | Swap descarta o provider anterior via `Task.Run(DisposeQuietly)` **imediatamente** — inferência ONNX em voo é morta no meio | `EmbeddingProviderResolver.cs:61` |
| C4 | Réplicas não convergem provider — save escreve no SQLite **local** e publica só invalidação de cache; cada réplica mantém seu próprio `EmbeddingSettings` e segue com o provider antigo | `EmbeddingSettingsService` (bump index-version + publish, mas settings são por-instância) |
| C5 | `AsymmetricEmbeddingProvider` chama `_inner.EmbedAsync` (genérico) nas variantes role-aware — `QueryInputType`/`DocumentInputType` configurados nunca chegam ao provider interno | `AsymmetricEmbeddingProvider.cs:40-47` |
| C6 | Texto da UI diz "aplica sem reiniciar" mesmo no campo dims (que é rejeitado quando ≠ store) | `Settings.razor` |

## 3. Requisitos Funcionais

### RF-001 — Gate de dims contra a saída real do provider

- PUT `/embeddings` rejeita `Dimensions` diferente da **saída efetiva do provider alvo**, além da checagem atual contra o store.
- ONNX: saída fixa do modelo — `Dimensions` deve igualar a dimensão do modelo carregável (384 para all-MiniLM-L6-v2); resolver via metadata do provider ou constante do provider ONNX. Caso store=512 e provider=onnx(384) → 400 com mensagem clara (`provider onnx emite 384 — ajuste Embeddings:Dimensions=384 no ambiente + reindex`).
- Providers remotos: `Dimensions` permanece declarativo (sem probe de rede no PUT) — documentado.

### RF-002 — Signature cobre toda opção que altera vetor

- `Signature()` inclui `Asymmetric.Auto` (e qualquer campo futuro que mude o conteúdo do vetor — convenção registrada em comentário no método).
- Regressão: teste provando que flip `Auto` true↔false muda `Fingerprint` e dispara rebuild.

### RF-003 — Dispose drenado do provider anterior

- Troca de provider espera inferências em voo terminarem antes do dispose: refcount de calls ativas + grace period (ex.: até 30s, configurável `Embeddings:SwapDrainSeconds`) — após o grace, dispose segue mesmo com chamadas pendentes (log warning).
- `DelegatingEmbeddingProvider`/`Resolver` expõe o contador; o swap não pode vazar sessão nem matar request sem teto de espera.

### RF-004 — Réplicas: propagação honesta de settings de embeddings

- Escopo mínimo honesto nesta entrega: documentar que settings runtime-editáveis são **por instância** (multi-réplica usa env vars ou PUT em cada nó) + endpoint GET expõe `instanceScoped: true` no payload para a UI mostrar a nota.
- Além disso, o bus publica `settings-changed` junto ao `index-version` para que réplicas que compartilhem o mesmo store de settings re-leiam na hora (forward-compatible).
- Convergência total de settings entre réplicas (store compartilhado) fica fora — ver Fora de Escopo.

### RF-005 — `input_type` preservado sob o decorador assimétrico

- `AsymmetricEmbeddingProvider` encaminha para as variantes role-aware do inner (`EmbedQueryAsync`/`EmbedDocumentAsync` e batches) quando existirem — `QueryInputType`/`DocumentInputType` chegam ao provider real junto com o prefixo.

### RF-006 — Texto da UI alinhado ao comportamento

- Card de embeddings distingue: mudança de provider/modelo com mesmas dims → "aplica sem reiniciar"; mudança de dims → "requer env + restart + reindex" (mensagem já devolvida pelo PUT — UI antecipa o aviso).

## 4. RNFs

- Zero chamada de rede nova no caminho de PUT (gate ONNX é local/metadata).
- Grace period default não pode segurar swap indefinidamente (teto configurável).
- Signature muda só com campos vetor-relevantes — instável por definição, nunca com segredo em claro.

## 5. Fora de Escopo

- Store de settings compartilhado entre réplicas (Redis/DB central) — documentado como limitação; decisão de arquitetura separada se necessária.
- Probe ativo de dims em providers remotos.
- Rotação de API key com drain (já coberto por RF-003).

## 6. Plano de Tarefas

1. `Signature()` + `Auto` (+ teste de fingerprint).
2. Gate ONNX dims→saída real no PUT (constante/metadata do provider).
3. Refcount + grace no swap (`SwapDrainSeconds`).
4. `AsymmetricEmbeddingProvider` → variantes role-aware do inner.
5. GET `instanceScoped` + publish `settings-changed` + texto da UI.
6. Testes: gate onnx 512 rejeitado; Auto flip rebuilda; dispose espera call em voo; input_type chega ao inner com asym.
7. Suites + format + PR.

## 7. Critérios de Aceite

- [ ] PUT onnx com `Dimensions` ≠ saída do modelo → 400 com orientação de env+reindex.
- [ ] Flip de `Asymmetric.Auto` altera fingerprint e rebuilda o provider.
- [ ] Embed em voo durante swap completa dentro do grace (ou é drenado com warning no teto).
- [ ] Busca com `input_type` configurado envia o parâmetro mesmo com `asymmetric` ligado.
- [ ] GET embeddings expõe `instanceScoped:true`; UI exibe nota de escopo por instância e texto correto de restart.
