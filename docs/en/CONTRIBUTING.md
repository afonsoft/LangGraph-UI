# Contributing to KnowledgeHub

Thank you for your interest in contributing to KnowledgeHub!

## Development Workflow

1. Fork the repository and create your branch from `main`:
   ```bash
   git checkout -b feature/AgentLLM-YYYYMMDD-short-description
   ```
2. Make your changes adhering to existing project conventions and C# modern standards.
3. Run tests and formatting gates:
   ```bash
   dotnet test
   dotnet format KnowledgeHub.slnx --verify-no-changes
   ```
4. Commit your changes using Conventional Commits:
   ```bash
   git commit -m "feat: add amazing new feature"
   ```
5. Push to your branch and open a Pull Request against `main`.

## Coding Guidelines

- Follow .NET 10 and C# 12 best practices.
- Ensure all public APIs and components have appropriate test coverage.
- Never commit secrets, API keys, or `.env` files.
