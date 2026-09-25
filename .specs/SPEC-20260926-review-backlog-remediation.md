# SPEC-20260926 — devin-review backlog remediation

| Campo | Valor |
|---|---|
| SPEC | SPEC-20260926-review-backlog-remediation |
| Autor | Devin |
| Stack | `KnowledgeHub.Server` (AgentService, Chat clients, IngestionService/Queue, VaultWatcher, Caching, VectorStores, EmbeddingProviderResolver, AsymmetricEmbeddingProvider, OnnxEmbeddingProvider, SettingsEndpoints, StreamingEndpoints, PostgresVectorStore, ToolSlugger, HealthChecks, Ingestion/Chunking) + `KnowledgeHub.Client` (Playground) + `install.sh`, `restore.sh`, `backup.sh` |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Approved` |
| Ticket | devin-ai-integration review backlog — 184 comentários inline em 35 PRs (#9–#219); cada comentário triado contra main@cd8d213 |
| Origem | `/tmp/devin-all.txt` — dedup vs `.claude/memory/devin-review-triage-20260925.md` (cobriu #184–#206); esta SPEC cobre o restante + era antiga #9–#43 |

## 1. User Story

O devin-ai-integration deixou 184 comentários em PRs fechados. A rodada E22 já endereçou os ~90 findings dos PRs #184–#206. Esta SPEC cobre: (a) os 29 comentários **novos** postados nos PRs E22 (#207, #215–#219) — bugs reais introduzidos/não cobertos pela própria remediação; (b) os 55 comentários da era antiga (#9–#43) nunca triados em massa. Cada item abaixo foi verificado contra main@cd8d213.

## 2. Triage completo

### Resolvido em main (sem ação)
- #9: vetores órfãos pgvector (cascade delete existe); secrets `***` reenviados (server mantém stored quando vazio); embeddings inválidos sem reindex (chunker-version reindex).
- #11: env vars propagadas (docker_deploy passa `Embeddings__*`/`VectorStore__*`/`DeepWiki__*`); systemd `WorkingDirectory=$PREFIX`.
- #9 DocumentFile `path` (server lê `path` + tolera `filePath`).
- #16/#43: wipe por falha HTTP (FailedUris/Truncated — E22); backup.sh basename (hash do path — E22).
- #27: skill `gap-analysis` existe.
- Todos os ~90 findings de #184–#206 (E22, SPECs Done).

### Out of scope
- #26 CI duplicada em merge (`push` + `pull_request:closed`): `.github/workflows/` é protegido — documentar, não corrigir aqui.

## 3. Requirements (evidência AS-IS verificada)

### Cluster 1 — Agente/chat
| RF | Bug | Evidência |
|---|---|---|
| RF-101 | Tool results chegam **vazios** ao provider | `OpenAiChatClient.MapMessages` serializa `m.Text` que ignora `FunctionResultContent.Result` (:64); idem `OllamaChatClient` (:59). Conteúdo da tool nunca chega ao modelo. |
| RF-102 | Resume concorrente executa aprovação 2× | `ResumeAsync` lê `ResumedAt null` → seta → `SaveChanges` (:185-191). Duas requests leem null antes do Save de qualquer uma. |
| RF-103 | Resume perde `ThreadId` + turno não persistido | `ResumeAsync` retorna `RunLoopAsync` direto — `PersistTurnAsync`/ThreadId attach (:160-164) só existem no caminho `RunAsync`. |
| RF-104 | Aprovação descarta tools irmãs | `foreach (call in calls)` → `SuspendAsync` armazena só `PendingCall` (:475); demais calls da mesma resposta nunca executam. |

### Cluster 2 — Ingestão/integridade
| RF | Bug | Evidência |
|---|---|---|
| RF-201 | Reindex deixa doc sem chunks | `ExecuteDeleteAsync` chunks antigos (:409) é imediato — falha no chunker/scan/save deixa doc com 0 chunks (detach não restaura). Mesmo padrão no vault path (:190). |
| RF-202 | SecurityEvent órfão de doc descartado | `DetachPartialAsync` desanexa doc+chunks mas não `SecurityEvent` (`SourceId`/`DocumentId` casados) → próximo SaveChanges persiste evento de doc não persistido. |
| RF-203 | Notion DB-query falha apaga linhas | Falha em `FetchDatabaseRowsAsync` → linhas não vistas nem em `FailedUris` nem `Truncated` → reconciliação deleta. Marcar `Truncated` no ctx. |
| RF-204 | Notion FetchItemAsync omite propriedades | Row fetch renderiza `includeProperties:false` — conteúdo pesquisável some na reindexação on-demand. |
| RF-205 | Drive público: edição mesmo tamanho não atualiza | `TryGetPublicFileMetaAsync` devolve só `Size` → `etag=size` → fingerprint estável → loop incremental pula. Probe-only meta deve gerar fingerprint vazio. |
| RF-206 | Cancel entre persist e TryWrite deixa job `queued` | Falha de `failed`-marking usa request-ct — um cancel entre os dois SaveChanges deixa `queued` eterno (dedup deadlock). Usar `CancellationToken.None` no repair. |
| RF-207 | `maxTokens<1` → loop infinito no chunker | `i += maxChars - overlapChars` com `maxChars=0` → incremento 0. Guardar `maxTokens>=1` (defense-in-depth mesmo com validação upstream). |
| RF-208 | VaultWatcher: flush preso 10s + watcher preso ao path antigo | `Task.Delay(RefreshInterval)` bloqueia o loop inteiro entre refresh+flush; `_watches[id]` nunca re-verifica root — edit de path mantém watcher no dir antigo. |
| RF-209 | `chunks` órfãos pós-falha (scanner events coberto em RF-202) | — |

### Cluster 3 — Cache/pubsub
| RF | Bug | Evidência |
|---|---|---|
| RF-301 | Remote clear ignorado sem L1 | `OnReceived` early-return quando `_cache is not L1L2Cache` → `ClearLocalTrackedAsync` nunca roda com `L1Enabled=false`. Mover o clear pra fora do gate de tipo. |
| RF-302 | Resubscribe duplicado no reconnect | `ConnectionFailed` zera `_subscribed` → `ConnectionRestored` chama `TrySubscribe` que adiciona NOVO delegate (SE.Redis já restaura o anterior) → N handlers por clear. Remover o reset — só re-subscribe quando o primeiro falhou. |
| RF-303 | Keys novas apagadas durante remote clear | `ClearLocalTrackedAsync` itera `_trackedKeys` enquanto `TrackKey` insere + `Clear()` final mata tracking de keys criadas após o evento. Snapshot do keyset no evento; remover só essas. |
| RF-304 | SCAN ignora cancel entre páginas | `ct.ThrowIfCancellationRequested` só corre por chave entregue — uma página travada ignora o cancel. Deadline elapsed-based além do ct por key. |

### Cluster 4 — SQLite concorrência (completar RF-001 da search-SPEC)
| RF | Bug | Evidência |
|---|---|---|
| RF-401 | Provider `sqlite` (default) segue concorrente no DbContext | `SqliteVectorStore.SearchAsync` usa `db` scoped — `Task.WhenAll` na expansão falha com EF-concurrent-operation. Serializar via `SqliteConnectionLease.GateFor` na conexão EF (dedicated clone não basta — é o DbContext inteiro). |
| RF-402 | Write conn nunca carrega `vec0` | `EnsureInitializedOnAsync` retorna cedo quando `_initialized` — mas `LoadVector()` é por conexão; a conn de escrita falha após primeira busca dedicada. `LoadVector()` incondicional por conexão nova; `_initialized` só guarda schema/backfill. |

### Cluster 5 — pgvector
| RF | Bug | Evidência |
|---|---|---|
| RF-501 | `relaxed_order` devolve fora de ordem | CTE materializada + `ORDER BY embedding <=>` externo nos candidatos do scan iterativo. |
| RF-502 | DROP INDEX + ALTER fora de tx | Falha no ALTER deixa tabela sem índice permanentemente. Envolver DROP+ALTER na mesma transação; recriar via threshold após commit. |
| RF-503 | Testes: só constante + sem assert de ops class | Estender: assert `SET LOCAL hnsw.iterative_scan` no CommandText; assert índice recriado com `halfvec_cosine_ops`; fresh-db test em tabela/db isolada. |

### Cluster 6 — Embeddings/settings
| RF | Bug | Evidência |
|---|---|---|
| RF-601 | Drain com cap descarta provider em uso | `Task.WhenAny(drain, Delay(30s))` → dispose com leases ativas (batch ONNX >30s crasha). Timeout → warning + continuar drenando (nunca dispose com refs>0). |
| RF-602 | `SwapDrainSeconds` não configurável | `DisposeGrace` hardcoded 30s — SPEC pedia `Embeddings:SwapDrainSeconds`. Config + warn de excedente. |
| RF-603 | ONNX inválido → 500, não 400 | `Load` lança não-`EmbeddingProviderException` para arquivo corrompido → filtro do PUT não captura. Capturar genérica no probe → 400. |
| RF-604 | ONNX output dim dinâmica (-1) | `dims[^1]` pode ser -1/0 → `_dimensions` inválido. Rejeitar modelo com dim<=0 na Load/PUT. |
| RF-605 | Decorator assimétrico ignora `input_type` | `EmbedQueryAsync`/`EmbedDocumentAsync` chamam `_inner.EmbedAsync` — inner role-aware (que aplica `input_type` no gateway) é bypassado. Delegar aos métodos role-aware do inner. |
| RF-606 | `settings-changed` não invalida réplicas | `InvalidationSubscriber` ignora o tópico → `EmbeddingSettingsService.Invalidate()` no subscriber (snapshot re-lê a store local + loga "changed elsewhere"). |
| RF-607 | Readiness saudável com provider caído | `EmbeddingHealthCheck` lê props estáticas — endpoint inacessível continua ready. Probe `EmbedAsync("health")` com cache 30s (evita martelo). |

### Cluster 7 — Scripts/ops
| RF | Bug | Evidência |
|---|---|---|
| RF-701 | `.env` loader aceita aspas/CR/comentários | `read -r key val` cru → `PORT="5000"`/`5000\r`/`5000 #x` quebra docker run. Sanitizar val: strip CR, aspas envolventes, comentário trailing. |
| RF-702 | `check_data_dir` só avisa | uid 1654 não escreve em dir 755 do operador → container falha. Tentar `sudo -n chown`/`chmod` quando possível; manter warn se não. |
| RF-703 | restore.sh ignora falha ao parar serviço | `systemctl stop \|\| true` → restore sobrescreve DB em uso → corrupção. Se o serviço está rodando e o stop falha → abortar. |
| RF-704 | ToolSlugger: colisão de sufixo | `foo` + `foo_2` natural + outro `foo` → `foo_2` colide. `while (used.ContainsKey(slug))` no sufixo. |
| RF-705 | Stream abstain sem `done` | `abstain` termina o SSE; Playground só renderiza `done` → resposta vazia. Emitir `done` com AskResponse de abstenção (além/depois de `abstain`). |
| RF-706 | Meta stream sem `retries:int` | SPEC pede int; `meta` tem só `retried:bool`. Emitir contagem real de tentativas. |

## 4. Não-requirements
- `.github/workflows/` (protegido).
- Replica-convergência real de settings (RF-606 é o escopo honesto: invalidate+hint).
- Orphan-vector reconciler periódico pgvector — downgrade pra follow-up se a janela estourar.

## 5. Acceptance
- [ ] Todos os RFs implementados com teste unitário por RF (integration onde fizer sentido).
- [ ] `dotnet test` unit+integration verdes; `dotnet format --verify` limpo; 0 warnings.
- [ ] Comentário de revisão respondido implicitamente por evidência de código (verificação self-review do diff).
- [ ] Uma única branch + PR com a tabela RF→fix no corpo.
