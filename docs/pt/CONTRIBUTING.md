# Contribuindo para o KnowledgeHub

Obrigado pelo seu interesse em contribuir para o KnowledgeHub!

## Fluxo de Desenvolvimento

1. Faça um fork do repositório e crie sua branch a partir de `main`:
   ```bash
   git checkout -b feature/AgentLLM-YYYYMMDD-descricao-curta
   ```
2. Features seguem o fluxo SDD — SPECs aprovadas em `.specs/` são a fonte da verdade (skills `/write-specs` → `/execute-specs`).
3. Faça suas alterações aderindo às convenções existentes e padrões modernos de C#.
4. Execute os testes e gates de formatação:
   ```bash
   dotnet build KnowledgeHub.slnx
   dotnet test
   dotnet format KnowledgeHub.slnx --verify-no-changes
   ```
5. Comite suas alterações usando Conventional Commits:
   ```bash
   git commit -m "feat: adicionar nova funcionalidade incrível"
   ```
6. Envie para sua branch e abra um Pull Request contra `main` — gates de CI: Build, Testes Unitários, Testes de Integração, Validação Blazor WASM, Build Docker, Qualidade de Código, Security Scan.

## Diretrizes de Código

- Siga as melhores práticas do .NET 10 e C# 14.
- Garanta que todas as APIs públicas e componentes tenham cobertura de testes adequada.
- Nunca comite segredos, chaves de API ou arquivos `.env`.
- `.github/workflows/` é protegido — alterações requerem autorização do maintainer.
- Mantenha os campos de status das SPECs em `.specs/` sincronizados com a implementação.
- Mantenha a documentação voltada ao usuário bilíngue — `README.md`/`docs/en/` espelham `README.pt-br.md`/`docs/pt/`; atualize os dois lados no mesmo PR.
- Ao adicionar ou renomear endpoints, atualize `docs/en/API.md` + `docs/pt/API.md` e a tabela de endpoints nos dois READMEs.

## Pull Requests

- Um assunto por PR; mantenha mudanças docs-only separadas de código.
- Squash-merge é o padrão; o título do PR vira a mensagem do commit — escreva em formato Conventional Commits.
- Referencie a issue/SPEC no corpo e inclua um checklist de test plan.
