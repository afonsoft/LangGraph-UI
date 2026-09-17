# SPEC-20260917-systemd-host-verify

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `systemd-host-verify` |
| Type | `Infra` (verificação operacional) |
| Stack | `bash / systemd` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | n/a (verificação; possível `chore/` para evidências) |
| Ticket | #111 |
| Status | `Approved` |

## 1. User Story

**As a** mantenedor
**I want** provar que `./install.sh --host --systemd` instala, habilita e sobe `knowledgehub.service` num host real
**So that** o caminho de deploy sem Docker deixe de ser INCONCLUSIVO.

**Problem context:**
Carried de gap-analysis-20260916 e 20260917: `install_systemd()` (install.sh:164-220) nunca foi exercitado em host real — requer `sudo`, escreve `/etc/systemd/system/`, habilita serviço. Tier 3: mutação de sistema, exige aprovação explícita no ato.

## 2. Scope

**In scope:**
- Executar `./install.sh --host --systemd` num host descartável (VM/container com systemd, ou o próprio VPS com rollback documentado).
- Verificar: unit instalada, `systemctl status knowledgehub` ativo, endpoint responde, restart persistence.
- Rollback: `systemctl disable --now` + remover unit + remover `/opt/knowledgehub`.

**Out of scope:**
- Mudar o script (só se a verificação revelar bug — aí vira SPEC/bugfix próprio).
- Deploy em produção.

## 3. Technical Context

**Files to read:** `install.sh` §install_systemd (linhas 164-220), template da unit, `--data-dir`/`--prefix` flags.

**Risks:** host sem systemd (containers docker padrão não têm) — escolher alvo com init real; `sudo` interativo — precisa de credencial/TTY ou NOPASSWD.

## 4. Requirements

### RF-001: Verificação end-to-end
- **Description:** install → enable → start → health → reboot-test (ou `systemctl restart`) → rollback.
- **Input → Output:** evidência de `systemctl status` ativo + `curl` 200 + log de cleanup.

## 6. Acceptance Criteria

- **CA-001:** serviço sobe e responde após install.
- **CA-002:** restart limpo (sem falha no `systemctl restart`).
- **CA-003:** host devolvido ao estado anterior (rollback verificado).
- **CA-004:** resultado registrado no memory report + backlog item atualizado.

## 7. Task Plan

| # | Tarefa | Validação |
|---|--------|-----------|
| T1 | Escolher host descartável c/ systemd | `systemctl --version` |
| T2 | `./install.sh --host --systemd` + verificações | status active + curl 200 |
| T3 | Rollback + evidências | unit removida |

## 8. Organization Guardrails

- Tier 3 — executar só com aprovação explícita e host alvo nomeado.
- Nunca no VPS de produção sem janela combinada.

## 9. Definition of Done

- [ ] systemd path provado ou bug reportado com evidência.
- [ ] Backlog item atualizado.
