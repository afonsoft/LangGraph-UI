# SPEC-20260916-deploy-env-docs

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `deploy-env-docs` |
| Type | `Docs` |
| Stack | `Markdown` + Docker Compose env |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260916-deploy-env-docs` |
| Ticket | `#70` |
| Status | `Approved` |

## 1. User Story

**As a** operador fazendo deploy do KnowledgeHub
**I want** uma referência completa das variáveis de ambiente do `docker-compose.yml` e um `.env.example` commitado
**So that** eu configure Firecrawl/Tavily/Redis/auth sem ler o compose nem adivinhar nomes.

**Problem context:**
`docker-compose.yml` referencia `KNOWLEDGEHUB_PORT`, `FIRECRAWL_*`, `TAVILY_*`, `CACHE_PROVIDER`, `REDIS_CONNECTIONSTRING` (compose:35-46) e a app consome `Chat__*`, `Embeddings__*`, `DeepWiki__*`, `Database__*` etc. README não documenta nenhuma delas e `.env.example` não existe (`ls .env*` → só `.env` gitignored).

## 2. Scope

**In scope:**
- `.env.example` commitado com todas as vars do compose, comentadas e com placeholders seguros.
- Seção "Configuração (.env)" no README com tabela: var → propósito → default → obrigatória?
- Cross-ref com a seção Redis security (SPEC-20260916-redis-exposure-risk) se ambas existirem.

**Out of scope:**
- Documentar endpoints da API ou UI.
- Secrets reais — apenas placeholders.
- Mudanças de código.

## 3. Technical Context

**Where the change happens:** documentação de deploy.

**Files to read before implementing:**
- `docker-compose.yml` (vars referenciadas)
- `src/KnowledgeHub.Server/appsettings.json` (seções de config)
- `README.md` (estrutura atual)
- `.gitignore` (confirmar que `.env` continua ignorado e `.env.example` não)

## 4. Requirements

### RF-001: `.env.example`
- **Description:** arquivo na raiz com todas as vars do compose + as mais comuns do appsettings, cada uma com comentário curto e placeholder — nunca valor real.
- **Input → Output:** `cp .env.example .env` → compose sobe com defaults sensatos.

### RF-002: Tabela no README
- **Description:** tabela cobrindo 100% das vars usadas no `docker-compose.yml` mais `Chat:Provider`, `Embeddings:Provider`, `DeepWiki:*`, `Database:Path`, admin seed (se existir).
- **Rules:** cada linha cita o default real do compose/appsettings — sem drift.

## 6. Acceptance Criteria

- **CA-001:** diff entre vars referenciadas no compose e vars documentadas = vazio (verificável por script/grep).
- **CA-002:** `.env.example` commitado; `.env` continua gitignored.
- **CA-003:** README renderiza a tabela corretamente.

## 7. Task Plan

| # | Tarefa | Arquivos |
|---|--------|----------|
| T1 | Extrair lista completa de vars (compose + appsettings) | — |
| T2 | `.env.example` | `.env.example` |
| T3 | Seção/tabela no README + cross-ref Redis | `README.md` |

## 8. Organization Guardrails

- Nunca commitar `.env` real — só o template.
- Defaults no exemplo devem ser os defaults reais do compose.

## 9. Definition of Done

- [ ] `.env.example` commitado e completo.
- [ ] Tabela README sem drift com o compose.
- [ ] `.env` segue fora do git.
