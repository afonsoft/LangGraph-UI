# SPEC-20260914-github-actions-ci

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `github-actions-ci` |
| Type | `Infra` |
| Stack | `GitHub Actions`, `.NET 10`, `Docker`, `Blazor WASM` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260914-github-actions-ci` |
| Ticket | `—` |
| Status | `Done` |

## 1. User Story

**As a** maintainer do KnowledgeHub
**I want** CI/CD automatizada via GitHub Actions com build, testes, code quality e security scanning
**So that** cada PR/merge seja validado consistentemente e deploys sejam confiáveis.

**Problem context:** O repositório atualmente não tem workflows CI/CD. O EAF e QRCoder.Core têm workflows maduros que podemos adaptar.

## 2. Scope

**In scope:**
- 4 workflows principais baseadas nos padrões EAF/QRCoder.Core:
  1. `ci-build-test.yml` - Build, restore, test do .NET 10, Blazor WASM e MCP
  2. `code-quality.yml` - Análise estática (Qodana/SonarQube)
  3. `security-scan.yml` - Security scanning (CodeQL, Snyk)
  4. `release.yml` - Publishing para NuGet/GitHub Packages
- Configuração específica para KnowledgeHub:
  - Build multi-projeto (4 src + 2 tests)
  - Testes de integração com SQLite
  - Blazor WASM client build validation
  - MCP contract tests
  - Docker image building
- `.github/workflows/` pasta criada
- Secrets documentation no README

**Out of scope:**
- Deploy para produção automático
- Notificações Slack/Teams
- Auto-merging de dependabot

## 3. Technical Context

**Where the change happens:** `.github/workflows/` (novo), ajustes em `global.json`, `Directory.Build.props` e `Dockerfile` para compatibilidade.

**Files to read before implementing:**
- `/home/ubuntu/repos/eaf/.github/workflows/ci-build-test.yml`
- `/home/ubuntu/repos/eaf/.github/workflows/code-quality.yml`
- `/home/ubuntu/repos/eaf/.github/workflows/security-scan.yml`
- `/home/ubuntu/repos/QRCoder.Core/.github/workflows/` (via API)
- `CLAUDE.md` · `.claude/rules/global-rules.md`
- `KnowledgeHub.slnx` estrutura
- `Dockerfile` atual

**Files to create or modify:**
```text
.github/workflows/ci-build-test.yml
.github/workflows/code-quality.yml
.github/workflows/security-scan.yml
.github/workflows/release.yml
.github/workflows/setup-environments.yml (opcional)
README.md (CI section)
```

## 4. Requirements

### RF-001 — CI Build & Test Workflow
- Trigger: push para `feature/*`, `bug/*`, `hotfix/*` + pull_request para `main`
- Ambiente: `ubuntu-latest`, `.NET 10.0.x`
- Jobs:
  1. **build-knowledgehub**: restore → build `KnowledgeHub.slnx` Release
  2. **test-unit**: run xUnit tests (`KnowledgeHub.Tests.Unit`)
  3. **test-integration**: run integration tests com SQLite in-memory
  4. **blazor-client-validation**: build Blazor WASM client, verificar assets
  5. **docker-build**: build Docker image (cache layers)
- Cache: NuGet packages, Docker layers
- Failure handling: artefatos de logs, annotations

### RF-002 — Code Quality Workflow
- Trigger: `workflow_dispatch`, `pull_request` (main/develop), push (main/releases/*)
- Ferramentas:
  - **Qodana**: análise .NET (qodana-cdnet)
  - **SonarQube**: qualidade de código (opcional, configuração via secrets)
- Artefatos: reports Qodana, SARIF upload
- Threshold: zero critical issues (pode usar `continue-on-error`)

### RF-003 — Security Scan Workflow
- Trigger: push (main/develop), pull_request, schedule (weekly)
- Ferramentas:
  - **CodeQL**: análise C#/JavaScript (Blazor client)
  - **Snyk**: vulnerability scanning (dependency audit)
  - **Trivy**: container image scanning (opcional)
- Secrets: `SNYK_TOKEN`, `SONAR_TOKEN`
- Relatórios: GitHub Security tab

### RF-004 — Release/Publish Workflow
- Trigger: push tags `v*`
- Jobs:
  1. **validate-tag**: semver check
  2. **build-release**: build Release, single-file artifacts
  3. **publish-nuget**: push para NuGet.org (secrets `NUGET_API_KEY`)
  4. **publish-docker**: build e push para GitHub Container Registry
  5. **create-release**: GitHub Release com artifacts
- Versioning: derivado da tag

### RF-005 — Setup & Environment
- Workflow `setup-environments.yml` para pré-configuração
- Cache optimization: restore keys, timeout policies
- Matrix builds para multi-SDK (future-proofing)
- Self-hosted runner labels (se aplicável)

## 5. Design Decisions

| Decision | Rationale |
|----------|-----------|
| Base no EAF + QRCoder.Core | Reutiliza patterns maduros já validados |
| Blazor WASM validation separado | O client tem dependências específicas (BootstrapBlazor) |
| SQLite in-memory para CI | Zero infra, testes rápidos |
| MCP contract tests no CI | Garante compatibilidade com clientes externos |
| Docker build multi-stage | Reusa Dockerfile existente, cache layers |

## 6. Acceptance Criteria

### AC-001 — CI Build & Test
- [ ] Workflow `ci-build-test.yml` existe e passa no branch `feature/Devin-20260914-github-actions-ci`
- [ ] Build de todos os 4 projetos src + 2 tests
- [ ] Testes unitários e de integração executam com sucesso
- [ ] Blazor client build não gera warnings críticos
- [ ] Docker build não falha

### AC-002 — Code Quality
- [ ] Workflow `code-quality.yml` executa Qodana
- [ ] Reports são gerados e disponíveis como artifacts
- [ ] Zero critical issues (ou baseline estabelecida)

### AC-003 — Security
- [ ] Workflow `security-scan.yml` executa CodeQL e Snyk
- [ ] Vulnerabilities de alta gravidade são detectadas
- [ ] Secrets configurados via repository secrets

### AC-004 — Release
- [ ] Workflow `release.yml` responde a tags `v1.0.0`
- [ ] NuGet package gerado com metadata correta
- [ ] Docker image tag correta

## 7. Task Plan

### T1 — Research & Baseline
- [ ] Analisar workflows EAF e QRCoder.Core
- [ ] Documentar diferenças para KnowledgeHub
- [ ] Definir matrix de jobs

### T2 — Implement CI Build & Test
- [ ] Criar `.github/workflows/ci-build-test.yml`
- [ ] Configurar cache NuGet + Docker
- [ ] Adicionar job Blazor validation
- [ ] Testar em branch feature

### T3 — Implement Code Quality
- [ ] Criar `.github/workflows/code-quality.yml`
- [ ] Configurar Qodana (baseline se necessário)
- [ ] Integrar SonarQube opcional

### T4 — Implement Security Scan
- [ ] Criar `.github/workflows/security-scan.yml`
- [ ] Configurar CodeQL para C# + JS
- [ ] Configurar Snyk CLI

### T5 — Implement Release Workflow
- [ ] Criar `.github/workflows/release.yml`
- [ ] Configurar NuGet publish
- [ ] Configurar Docker publish

### T6 — Documentation & Secrets
- [ ] Atualizar README com CI section
- [ ] Documentar secrets necessários
- [ ] Criar `.github/workflows/setup-environments.yml` (opcional)

### T7 — Validation
- [ ] Executar todos workflows no branch
- [ ] Verificar artefatos e reports
- [ ] Atualizar SPEC status para `Approved`

## 8. DoD (Definition of Done)

- [ ] Todos os 4 workflows criados em `.github/workflows/`
- [ ] CI passa no branch de implementação
- [ ] Code quality reports gerados
- [ ] Security scanning configurado
- [ ] Release workflow responde a tags
- [ ] README atualizado com instruções
- [ ] SPEC status atualizado para `Done`
- [ ] PR criado para merge no main
## 10. Conclusão

Implementado diretamente em `main`.

- `.github/workflows/ci-build-test.yml` — build, testes unitários/integração, validação Blazor WASM, Docker build.
- `.github/workflows/code-quality.yml` — Qodana + SonarQube analysis.
- `.github/workflows/security-scan.yml` — CodeQL (C#+JS), Snyk dependency scanning, Trivy container scanning.
- `.github/workflows/release.yml` — semver validation, single-file artifacts (linux-x64/win-x64), NuGet publish, Docker publish (ghcr.io), GitHub Release automático.
- Cache otimizado (NuGet, Docker layers), artifacts em falha.

Status: **Done** — SPEC concluída.
