# Short-term memory — session state (overwritten each session, ≤100 lines)

- **Last verified commit on `main`**: `f282c43` (merge PR #125 — skills lock).
- **Baseline**: `dotnet build` 0 warnings · `dotnet test` 281 unit + 158
  integration green · `dotnet format --verify-no-changes` exit 0
- **Branch protection**: `main` protected (PR + 5 status checks,
  enforce_admins=false) — owner bypass usado a pedido explícito p/ docs SPECs
- **Deploy**: container `knowledgehub` healthy em `:5550`, imagem rebuildada de
  main (FontAwesome fix + MCP híbrido + env passthrough)
- **Active task**: nenhuma — fila zerada
- **Done hoje**: MCP v2 hybrid (PR #127), Epic #128 5/5 SPECs (PRs #135,
  #137–#142), bug UI #134 (PR #136), release v0.0.2 reparada (2 archives +
  GHCR), v.0.0.1 deletada, skills lock (PR #125)
- **Blockers**: none
- **Next**: aguardar nova direção do usuário (novo gap-analysis ou feature)
