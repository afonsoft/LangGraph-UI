# SPEC-20260916-compose-vault-mount

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `compose-vault-mount` |
| Type | `Infra` (config drift — deploy reproduzível) |
| Stack | `Docker Compose` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260916-compose-vault-mount` |
| Ticket | `GAP-operation-compose-vault-mount-drift` |
| Status | `Approved` |

## 1. User Story

**As a** operador fazendo redeploy
**I want** que o mount do vault Obsidian local seja uma camada explícita (`docker-compose.override.yml`) em vez de edição não commitada
**So that** um redeploy a partir de checkout limpo não perca silenciosamente o backend do `obsidian_write`, e o working tree fique limpo.

**Problem context:**
`docker-compose.yml` está dirty desde o deploy do cache: o mount `/home/ubuntu/.openclaw/workspace/vault:/vaults/obsidian` (rw) existe só na working tree deste VPS. O mount é intencional — habilita `obsidian_write` contra o vault OpenClaw — mas não é reproduzível nem visível no repo. Docker Compose aplica `docker-compose.override.yml` automaticamente sobre o arquivo base — é a convenção nativa para customização por-host. Decisão do operador (2026-09-16): **override file**.

## 2. Scope

**In scope:**
- Reverter o mount do `docker-compose.yml` commitado e movê-lo para `docker-compose.override.yml` (commitado — o path é específico do VPS mas serve de referência; alternativa: `.example` + override gitignored — definir em implementação conforme convenção do repo).
- Documentar no README como declarar mounts locais (override pattern).
- Redeploy com `docker compose up -d` deve continuar montando o vault (override aplicado automaticamente).

**Out of scope:**
- Outros mounts (WebDAV já está documentado como comentário).
- Mudanças na app.

## 3. Technical Context

**Files to read before implementing:**
- `docker-compose.yml` (mount atual + seção WebDAV comentada)
- `README.md` (seção Obsidian/vaults)
- `.gitignore`

**Files to create or modify:**
```text
docker-compose.yml            # remove o mount local
docker-compose.override.yml   # mount do vault (VPS)
README.md                     # nota sobre override
.gitignore                    # se override ficar local-only
```

## 4. Requirements

### RF-001: Mount via override
- **Description:** `docker-compose.yml` volta ao estado commitado (sem mount local); `docker-compose.override.yml` declara `./data` + vault mount.
- **Rules:** `docker compose config` deve mostrar o mount efetivo; `git status` limpo após o commit.
- **Input → Output:** checkout limpo + override presente → mesmo deploy de hoje.

### RF-002: Documentação
- **Description:** README explica o override para mounts por-host (vault rw, WebDAV ro) com exemplo mínimo.

## 6. Acceptance Criteria

- **CA-001:** `git status` limpo; `docker compose config` inclui `/home/ubuntu/.openclaw/workspace/vault:/vaults/obsidian`.
- **CA-002:** `docker compose up -d` recria container com o mount; `obsidian_write` funcional (smoke: escrever nota de teste via tool ou verificar mount com `docker exec ls /vaults/obsidian`).
- **CA-003:** checkout limpo sem override → sobe sem o mount, sem erro.

## 7. Task Plan

| # | Tarefa | Arquivos |
|---|--------|----------|
| T1 | Criar override + limpar compose base | `docker-compose*.yml` |
| T2 | README: seção override | `README.md` |
| T3 | Redeploy + smoke do mount | — |

## 8. Organization Guardrails

- Sem secrets em arquivos commitados.
- Override é por-host: outro ambiente pode não ter o vault — documentar isso.

## 9. Definition of Done

- [ ] Working tree limpa.
- [ ] Mount funciona via override em produção.
- [ ] README documenta o padrão.
