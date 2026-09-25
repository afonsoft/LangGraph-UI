# SPEC — Seletor de chunking por fonte no SourceEditDialog

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260925-semantic-chunking-source-ui` |
| Data | 2026-09-25 |
| Autor | Devin |
| Stack | `Blazor WASM`, `BootstrapBlazor`, `SourceEditDialog.razor` |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Done` |
| Ticket | GAP-requirements-semantic-chunking-source-ui |
| Origem | gap-analysis 2026-09-25 — aceite não cumprido de SPEC-20260924-semantic-chunking RF-003 |

## 1. User Story

**As a** operador editando uma fonte de conhecimento
**I want** escolher a estratégia de chunking (auto/semantic) na UI de edição
**So that** não precise editar `ConfigurationJson` cru para ativar chunking semântico numa fonte de prosa.

## 2. Contexto

SPEC-20260924-semantic-chunking RF-003 exigia: "`ConnectorConfig`/`Source` ganham `Chunking` override por fonte (`null` = auto atual). UI de edição de fonte ganha o seletor." A parte de backend existe — `ChunkerSelector.StrategyFor(source.ConfigurationJson)` lê `{"chunking":"semantic"}` — mas `SourceEditDialog.razor` não expõe o campo: a única forma de ativar é escrever JSON à mão. `BuildConfiguration()` (:279) já preserva chaves desconhecidas, então um valor setado manualmente sobrevive ao save — falta só o seletor.

## 3. Requisitos Funcionais

### RF-001 — Seletor no dialog
- Campo "Estratégia de chunking" com opções `Auto` (default, key ausente), `Semantic` (`"chunking":"semantic"`).
- Tooltip/descrição: semantic embedd sentenças para cortar por mudança de assunto (custo: ~1 embedding/sentença na ingestão; só prose/markdown).
- `SourceEditModel` ganha `Chunking` (string?); `FromDto` lê `configuration.chunking`; `BuildConfiguration` escreve/remove a chave.

### RF-002 — Visibilidade
- Campo visível apenas para tipos cujo conteúdo é texto (DocumentFile, ObsidianVault, WebPage, Notion) — oculto para `McpProxy`.

## 4. Requisitos Não-Funcionais

- Zero mudança de backend; `StrategyFor` já tolera ausência/null.
- Não quebrar `BuildConfiguration` para chaves desconhecidas (preservação existente mantida).

## 5. Fora de escopo

- Percentis/minTokens por fonte (ficam globais em `Ingestion:Semantic:*`).
- Validação de modelo de embedding antes de ativar semantic.

## 6. Plano de tarefas

| # | Tarefa |
|---|---|
| T1 | `SourceEditModel.Chunking` + FromDto/BuildConfiguration |
| T2 | Select no dialog (somente tipos de texto) + hint de custo |
| T3 | Build + teste manual: salvar semantic → sync usa breakpoints |

## 7. Critérios de aceite

- [ ] Selecionar "Semantic" numa fonte persiste `{"chunking":"semantic"}` e o próximo sync usa `SemanticTextChunker`.
- [ ] "Auto" remove a chave; fontes existentes abrem com "Auto".
- [ ] Fonte `McpProxy` não exibe o campo.

## 8. Riscos

- Usuário ativa semantic numa fonte gigante sem perceber custo → hint no label + cap `MaxSentencesPerDoc` já existente protege.
