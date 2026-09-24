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
