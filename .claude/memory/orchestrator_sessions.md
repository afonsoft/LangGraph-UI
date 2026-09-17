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
