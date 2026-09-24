# Contribuindo para o KnowledgeHub

Obrigado pelo seu interesse em contribuir para o KnowledgeHub!

## Fluxo de Desenvolvimento

1. Faça um fork do repositório e crie sua branch a partir de `main`:
   ```bash
   git checkout -b feature/AgentLLM-YYYYMMDD-descricao-curta
   ```
2. Faça suas alterações aderindo às convenções existentes e padrões modernos de C#.
3. Execute os testes e gates de formatação:
   ```bash
   dotnet test
   dotnet format KnowledgeHub.slnx --verify-no-changes
   ```
4. Comite suas alterações usando Conventional Commits:
   ```bash
   git commit -m "feat: adicionar nova funcionalidade incrível"
   ```
5. Envie para sua branch e abra um Pull Request contra `main`.

## Diretrizes de Código

- Siga as melhores práticas do .NET 10 e C# 12.
- Garanta que todas as APIs públicas e componentes tenham cobertura de testes adequada.
- Nunca comite segredos, chaves de API ou arquivos `.env`.
