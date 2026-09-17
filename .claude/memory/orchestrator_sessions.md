# Orchestrator Sessions

Append-only session log. Most recent first. Read during Phase 0 to recover
context — see `.agents/skills/orchestrator/SKILL.md` Phase 8.

---

## Session — 2026-09-17 14:00

**Scope**: Epic #90 (gap-analysis-20260917) — execução dos 5 SPECs aprovados
(#91–#95) + fechamento da sessão anterior (PR #87 format gate).

**Decisions**:
- Skills migradas para layout canônico skills.sh: `.agents/skills/` real
  (gitignored) + `.claude/skills/*` symlinks + `skills-lock.json` pins.
- `main` protegida via GitHub: PR obrigatório + 5 status checks (Build, Unit,
  Integration, Blazor, Docker); `enforce_admins=false` = escape hatch do owner.
- Harness-files: usuário escolheu opção (a) — harness `.claude/` completo
  provisionado (settings.json, rules/, agents/, CONTEXT/RULES/MEMORY/TOOLS/
  WORKFLOWS/README, memory protocol).

**Delivered**:
- PRs mergeadas: #87 (`5c1f79b` format gate + SPEC sync), #88 (`cd662d0`
  skills update), #89 (`7f3c261` SPECs + report), #96 (`00f1c18` README sync),
  #97 (branch-protection doc). PR #98 (harness) aberta.
- Issues: #91, #92, #93, #95 fechadas; #94 fecha com merge da #98.
- Branches: 13 locais + 16 remotas mergeadas deletadas; 0 residuais merged.
- SPECs 20260917 ×5 marcados `Done` com evidência.

**Remaining**:
- Merge da PR #98 + fechar #94 e Epic #90.
- Backlog pending_approval: ONNX embeddings, sqlite-vec, McpProxy SourceType,
  tools passthrough, `install.sh --systemd` (host mutation), SSE E2E prod
  (credencial).
- 2 branches unmerged mantidas: `feature/Devin-20260915-rename-mcp-monitor`,
  `feature/Devin-20260916-specs-mobile-apikey-settings`.

**Lessons**:
- `gh pr merge --auto` exige auto-merge habilitado no repo — não está; mesclar
  manualmente após checks.
- `enforce_admins=false` → push direto do owner NÃO é bloqueado; verificar o
  gate via API, não via test push.
- `dotnet format` não aceita `--nologo`.
- Todo push reinicia CI — batchar mudanças num commit antes do merge.

## 2026-09-17 — Epic #106 feature SPECs

- Executadas 4/6 SPECs aprovadas via execute-specs: #110→PR#113, #109→PR#114, #107→PR#115, #108→PR#116. Todas mergeadas; issues fechadas com evidência.
- main @ 906a11a — 271 unit + 153 integration verdes, format gate 0.
- Re-deploy: `install.sh --docker` rebuildou imagem; container recriado via `docker compose up -d` (install.sh não propaga .env/volumes — registrado como limitação conhecida). Healthy em :5550.
- Bloqueados: #111 (host systemd+sudo), #112 (aft_* prod).
- Incidente: commit inicial da #108 incluiu models/*.onnx (90MB) porque a branch veio de main sem o .gitignore do ONNX — revertido via amend + force-push antes do merge.
