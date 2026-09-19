# Short-term memory — session state (overwritten each session, ≤100 lines)

- **Last verified commit on `main`**: `958fdcb` (docs: approve 5 gap-analysis
  SPECs #128–#133). Prévia: `6a35d45` (merge PR #127 — MCP v2 hybrid).
- **Baseline**: `dotnet build` 0 warnings · `dotnet test` 281 unit + 158
  integration green · `dotnet format --verify-no-changes` exit 0
- **Branch protection**: `main` protected (PR + 5 status checks,
  enforce_admins=false) — owner bypass usado 2× nesta sessão a pedido explícito
  (SPECs Draft + status Approved commits diretos)
- **Active task**: Epic #128 — 5 SPECs aprovadas do gap-analysis-20260918:
  - #129 release-publish-singlefile → PR #135 (CI re-rodando após fix NU1403 lockfile)
  - #130 install-docker-env-passthrough → PR #137
  - #131 claude-md-feature-sync → PR #138
  - #132 orchestrator-state-sync → PR (esta branch)
  - #133 compose-override-chat-example → PR #139
  - #134 bug UI (FontAwesome ausente + botões sem texto) → PR #136
- **Blockers**: release repair v0.0.2 + delete v.0.0.1 aguardam merge #135 e
  confirmação destrutiva explícita do usuário
- **Next**: aguardar CI dos 6 PRs → merges → redeploy prod :5550 →
  release repair → SPECs → Done
