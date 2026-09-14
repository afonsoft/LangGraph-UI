# SPEC — Obsidian sobre WebDAV (pasta mapeada no servidor)

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260914-obsidian-webdav` |
| Data | 2026-09-14 |
| Autor | Devin |
| Stack | `.NET 10`, `EF Core SQLite`, `Docker`, `WebDAV/davfs2` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260914-obsidian-webdav` |
| Ticket | `[A DEFINIR]` |
| Status | `Approved` |

## 1. User Story

**As a** usuário do KnowledgeHub
**I want** registrar um vault Obsidian cujo conteúdo vive num servidor WebDAV remoto, exposto ao KnowledgeHub como uma pasta mapeada no servidor (mount)
**So that** os vários arquivos Markdown dessa pasta sejam indexados e consultáveis como fonte de conhecimento, sem copiar manualmente os arquivos.

## 2. Contexto e decisão de arquitetura

O conector `ObsidianVault` **já indexa qualquer pasta local**: `IngestionService.ResolveVaultRoot` lê `configuration.path`, `SyncAsync` faz o scan completo de `*.md`, `SyncFileAsync` faz sync incremental por arquivo, e `VaultWatcherService` mantém um `FileSystemWatcher` por vault ativo.

Portanto existem duas fronteiras possíveis:

| Abordagem | Descrição | Avaliação |
|---|---|---|
| **A — Mount externo (escolhida)** | O host/contêiner monta o WebDAV como pasta local (`davfs2`, `rclone mount`, ou volume Docker). KnowledgeHub consome como `ObsidianVault` normal. | **Zero código de conector.** Reaproveita watcher, autosync, dedup por hash, tools `read_document`/`write_note`. Complexidade sai do app para a infra. |
| B — Cliente WebDAV in-app | Novo `SourceType.WebDavVault` com PROPFIND/GET periódicos, credenciais e loop de sync próprio. | Reimplementa tudo que já existe (scan, dedup, sync) + gerencia secrets no banco. Só se justificaria se o host não pudesse montar. |

**Decisão:** abordagem **A**. Esta SPEC cobre a integração mount→conector e os ajustes necessários para mounts remotos funcionarem bem (a principal diferença vs. disco local é que `FileSystemWatcher` **não recebe eventos de mudanças feitas do lado remoto** em mounts FUSE/davfs2 — ver RF-003).

## 3. Requisitos Funcionais

### RF-001 — Registro da fonte
- Uma fonte `ObsidianVault` cujo `configuration.path` aponta para a pasta montada deve passar pelo fluxo normal de criação (REST `POST /api/sources` e UI).
- A UI deve aceitar o path sem validação de existência bloqueante no cliente (a validação real é no servidor).

### RF-002 — Validação de disponibilidade do mount
- No `SyncAsync`/`SyncFileAsync`, quando `path` não existir ou `Directory.Exists` falhar, a sincronização deve falhar com mensagem clara (`vault path '<path>' not found — mount unavailable?`) registrada em `LastSyncStatus`/`LastError` da fonte — sem crash do host.
- `VaultWatcherService` já ignora roots inexistentes (`Directory.Exists` no refresh); quando o mount ficar disponível, o watcher deve ser criado no próximo ciclo de refresh (≤10 s) — comportamento existente, coberto por teste.

### RF-003 — Sync incremental sem eventos de filesystem
- Mounts WebDAV (davfs2/FUSE/rclone) **não propagam inotify** para alterações feitas remotamente — apenas escritas locais através do mount geram eventos.
- Portanto, para fontes cujo path está num mount remoto, o sync incremental deve depender de **polling**: `AutoSyncEnabled = true` com `SyncIntervalMinutes` (default 30, já existente) é o mecanismo de atualização.
- Deve ser possível configurar `syncIntervalMinutes` na criação/edição da fonte (campo já existe no modelo — verificar exposição na UI; se ausente, adicionar ao `SourceEditDialog`).

### RF-004 — Escaneamento e indexação
- Scan recursivo de `*.md` sob o root do mount, excluindo diretórios ocultos (`.obsidian/`, `.trash/` etc.) — comportamento existente do `IngestionService`.
- Dedup por URI + content hash: re-sync sem mudança não deve re-embeddar conteúdo idêntico (comportamento existente, coberto por teste).
- Arquivos removidos do mount devem ser removidos do índice no próximo full sync (comportamento existente de reconciliação — verificar e testar).

### RF-005 — Leitura e escrita pelo vault
- `read_document` e `query_{slug}` funcionam sobre o mount transparentemente.
- `write_note` em mount read-only falha com erro claro; a SPEC **recomenda montar read-only** (`ro`) quando o WebDAV for fonte somente-leitura, e documenta que `write_note` exige mount `rw`.

### RF-006 — Configuração no container
- `docker-compose.yml` deve documentar (comentário ou seção no README) o volume extra:
  ```yaml
  volumes:
    - ./data:/data
    - /srv/webdav/obsidian:/vaults/obsidian:ro   # mount davfs2 no host
  ```
- Alternativa sem mount no host: volume Docker com driver `rclone`/plugin — fora do escopo de implementação, documentado como opção.
- O caminho **dentro do container** é o que vai em `configuration.path` (ex.: `/vaults/obsidian`), não o path do host.

### RF-007 — Documentação operacional do mount no host
- README ganha seção "Obsidian via WebDAV" com receita `davfs2`:
  - `/etc/davfs2/davfs2.conf`, entrada em `/etc/fstab` ou `mount -t davfs2 <url> /srv/webdav/obsidian`
  - secrets em `/etc/davfs2/secrets` (nunca no repo, nunca em `configuration.json` da fonte)
  - remount em `/etc/fstab` com `_netdev` + systemd `davfs2` para sobreviver reboot
  - cache do davfs2 (`cache_size`, `file_refresh`) e impacto na latência de sync

## 4. Requisitos Não-Funcionais

- **RNF-001**: mount indisponível nunca derruba o host nem bloqueia outras fontes — falha é por-fonte, registrada no status.
- **RNF-002**: timeouts de leitura em mount lento não podem travar o watcher loop — sync roda com `CancellationToken` e erros são capturados por arquivo (existente).
- **RNF-003**: zero novas dependências NuGet — nenhum cliente WebDAV é adicionado ao app.
- **RNF-004**: credenciais WebDAV ficam fora do banco e do repo — apenas no `davfs2/secrets` ou equivalente do host.

## 5. Fora de escopo

- Cliente WebDAV dentro do app (PROPFIND/GET direto) — abordagem B, só se o mount externo se provar inviável.
- Mount automático pelo container (requer `CAP_SYS_ADMIN`/FUSE no container — risco de segurança; o mount é responsabilidade do host).
- Sync bidirecional Obsidian↔KnowledgeHub além do que `write_note` já faz.

## 6. Plano de tarefas

| # | Tarefa | Arquivos |
|---|---|---|
| T1 | Mensagem de erro clara para mount ausente em `SyncAsync`/`SyncFileAsync` + status na fonte | `IngestionService.cs` |
| T2 | Expor `syncIntervalMinutes`/`autoSync` na `SourceEditDialog` se ainda não exposto | `SourceEditDialog.razor`, `KnowledgeSourceDtos.cs` |
| T3 | Testes: sync com path inexistente (status de erro), reconciliação de deletados, dedup por hash | `tests/KnowledgeHub.Tests.Integration/` |
| T4 | README: seção WebDAV (receita davfs2 + volume compose + ro/rw) | `README.md`, `docker-compose.yml` (comentário) |
| T5 | Validação real: montar davfs2 local (ou pasta simulando mount), registrar fonte, indexar, editar remoto, verificar autosync | manual |

## 7. Critérios de aceite

- [ ] Fonte `ObsidianVault` apontando para pasta montada indexa todos os `*.md` (full sync).
- [ ] Edição remota refletida no índice no próximo ciclo de `SyncIntervalMinutes` (polling), sem depender de FSW.
- [ ] Mount derrubado → sync falha com status de erro claro na fonte; app e demais fontes seguem saudáveis; mount restaurado → watcher/sync retomam sozinhos.
- [ ] Arquivo deletado no remoto some do índice após full sync.
- [ ] Re-sync idempotente (hash) — sem re-embedding de conteúdo inalterado.
- [ ] README documenta mount, volume, credenciais e modo ro/rw.
- [ ] Suite completa verde, zero warnings.

## 8. Riscos

| Risco | Mitigação |
|---|---|
| FSW silencioso em mount remoto (falsa sensação de sync incremental) | SPEC torna polling obrigatório; README alerta; UI sugere `autoSync` |
| Mount trava leituras (rede lenta/queda) | timeouts por arquivo já existentes; RNF-002 |
| Credencial WebDAV vaza para `configuration.json`/banco | credenciais vivem só no `davfs2/secrets` do host — RNF-004 |
| UID do container (1654) sem permissão de leitura no mount | README orienta permissões/grupo; mount `ro` com `uid=`/`gid=` no fstab |
