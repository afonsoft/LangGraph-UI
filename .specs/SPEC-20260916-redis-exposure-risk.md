# SPEC-20260916-redis-exposure-risk

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `redis-exposure-risk` |
| Type | `Docs` (risco de segurança documentado + recomendações de hardening) |
| Stack | `.NET 10` + Docker Compose + Redis (infra do VPS) |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260916-redis-exposure-risk` |
| Ticket | `#68` |
| Status | `Done` |

## 1. User Story

**As a** operador da plataforma
**I want** que o risco do Redis do host exposto sem autenticação esteja documentado, com o caminho de hardening recomendado e validação de config no app
**So that** o deploy saiba exatamente qual exposição existe hoje e como fechá-la sem quebrar o cache.

**Problem context:**
O Redis do host (`/www/server/redis`) escuta em `0.0.0.0:6379` com `protected-mode no` e sem `requirepass` (evidência: `redis-cli config get bind/protected-mode`, `ss -tlnp`). O Knowledge MCP Hub usa db3 via `host.docker.internal:6379` — funcional, mas qualquer processo na máquina ou na rede (se a porta estiver aberta no firewall/security group) pode ler e escrever o cache de busca/embeddings. Decisão do operador (2026-09-16): **documentar o risco agora; não alterar a infra nesta entrega**.

## 2. Scope

**In scope:**
- Seção "Redis security" no README: exposição observada, impacto, e receita de hardening (bind `127.0.0.1` + `host-gateway` já cobre o container; `requirepass` + `,password=` na connection string; firewall/security-group negando 6379 externo).
- `ConfigurationValidator`: warning de startup (não fail) quando `Cache:Provider=redis` e a connection string não tem `password=` — aponta para a doc.
- Registro da decisão "aceito por ora" na própria SPEC + `docs/` quando couber.

**Out of scope:**
- Alterar `redis.conf`, firewall ou qualquer infra do VPS.
- TLS no Redis (`rediss://`) — fora de escopo; a connection string já suporta.
- Redis como dependência obrigatória — continua opt-in.

## 3. Technical Context

**Where the change happens:** docs (README) + validação de config no startup (`src/KnowledgeHub.Server/Configuration/` ou equivalente do validator existente — `SPEC-20260914-config-validation`).

**Files to read before implementing:**
- `src/KnowledgeHub.Server/` (localizar o `ConfigurationValidator`/`ValidateCache` existente)
- `docker-compose.yml`, `.env` (não commitar), `README.md`
- `.specs/SPEC-20260916-performance-memory-cache.md` §3/§5

**Files to create or modify:**
```text
README.md                                  # seção Redis security
src/KnowledgeHub.Server/**/ConfigurationValidator*.cs  # warning redis-sem-senha
tests/KnowledgeHub.Tests.Unit/Server/ConfigurationValidatorTests.cs
```

## 4. Requirements

### RF-001: Documentação do risco
- **Description:** README deve conter seção descrevendo a configuração observada (bind `0.0.0.0`, sem auth), o impacto (leitura/envenenamento do cache db3) e três opções de hardening ordenadas por esforço: (a) firewall negando 6379 externo, (b) `bind 127.0.0.1` no host (container continua alcançando via `host-gateway`), (c) `requirepass` + `,password=` em `Cache:Redis:ConnectionString`.
- **Rules:** não expor credenciais reais; exemplos com placeholders.
- **Input → Output:** leitor novo no README → entende o risco e o fix.

### RF-002: Warning de startup
- **Description:** quando `Cache:Provider=redis` e `Cache:Redis:ConnectionString` não contém `password=`, logar warning "Redis sem autenticação — ver README § Redis security". Nunca bloquear o startup.
- **Input → Output:** config sem senha → `Warn` no log; com senha → silêncio.

**Business rules / invariants:**
- O app continua funcionando com Redis sem senha (config atual do VPS é válida).
- Nenhuma mudança de comportamento de cache.

## 6. Acceptance Criteria

- **CA-001:** `Cache:Provider=redis` + conn string sem `password=` → warning emitido no startup; com `password=` → sem warning.
- **CA-002:** README contém a seção com as 3 opções de hardening.
- **CA-003:** build + unit + format verdes.

## 7. Task Plan

| # | Tarefa | Arquivos principais |
|---|--------|---------------------|
| T1 | Seção "Redis security" no README | `README.md` |
| T2 | Warning no validator + teste | validator, `ConfigurationValidatorTests.cs` |

## 8. Organization Guardrails

- Não alterar infra do VPS nesta entrega (decisão do operador).
- Não introduzir Redis como dependência obrigatória.
- Placeholders em exemplos — nunca credenciais reais.

## 9. Definition of Done

- [ ] Seção Redis security no README.
- [ ] Warning de startup com teste cobrindo ambos os caminhos.
- [ ] Build + testes + format verdes.
