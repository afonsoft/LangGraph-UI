# Contributing to KnowledgeHub

Thank you for your interest in contributing to KnowledgeHub!

## Development Workflow

1. Fork the repository and create your branch from `main`:
   ```bash
   git checkout -b feature/AgentLLM-YYYYMMDD-short-description
   ```
2. Features follow the SDD flow — approved SPECs in `.specs/` are the source of truth (`/write-specs` → `/execute-specs` skills).
3. Make your changes adhering to existing project conventions and C# modern standards.
4. Run tests and formatting gates:
   ```bash
   dotnet build KnowledgeHub.slnx
   dotnet test
   dotnet format KnowledgeHub.slnx --verify-no-changes
   ```
5. Commit your changes using Conventional Commits:
   ```bash
   git commit -m "feat: add amazing new feature"
   ```
6. Push to your branch and open a Pull Request against `main` — CI gates: Build, Unit Tests, Integration Tests, Blazor WASM Validation, Docker Image Build, Code Quality, Security Scan.

## Coding Guidelines

- Follow .NET 10 and C# 14 best practices.
- Ensure all public APIs and components have appropriate test coverage.
- Never commit secrets, API keys, or `.env` files.
- `.github/workflows/` is protected — changes require maintainer authorization.
- Keep `.specs/` status fields in sync with implementation.
- Keep user-facing docs bilingual — `README.md`/`docs/en/` mirror `README.pt-br.md`/`docs/pt/`; update both sides in the same PR.
- When adding or renaming endpoints, update `docs/en/API.md` + `docs/pt/API.md` and the endpoints table in both READMEs.

## Pull Requests

- One concern per PR; keep docs-only and code changes separate.
- Squash-merge is the default; the PR title becomes the commit message — write it in Conventional Commits form.
- Link the driving issue/SPEC in the body and include a test plan checklist.
