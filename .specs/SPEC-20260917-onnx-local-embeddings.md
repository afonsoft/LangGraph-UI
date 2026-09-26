# SPEC-20260917-onnx-local-embeddings

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `onnx-local-embeddings` |
| Type | `Feature` |
| Stack | `.NET 10 / C# 14` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260917-onnx-local-embeddings` |
| Ticket | #107 |
| Status | `Done — PR #115 (6b0ef8a)` |

## 1. User Story

**As a** operador self-hosted do Knowledge MCP Hub
**I want** um provedor de embeddings local via ONNX Runtime (all-MiniLM-L6-v2)
**So that** a busca vetorial tenha semântica real sem depender de Ollama/OpenAI ou rede.

**Problem context:**
`Embeddings:Provider` hoje aceita `deterministic` (term-overlap stub, sem semântica), `ollama` e `openai` (ambos exigem serviço externo). O backlog #1 (SPEC-20260913-ingestion-obsidian §Out of scope) deferiu o provider ONNX por adicionar ~90 MB de payload nativo ao binário single-file. A interface `IEmbeddingProvider` já está pronta para o quarto provider.

## 2. Scope

**In scope:**
- `OnnxEmbeddingProvider : IEmbeddingProvider` usando `Microsoft.ML.OnnxRuntime` + tokenizer do all-MiniLM-L6-v2 (384 dims).
- `Embeddings:Provider = "onnx"` no `EmbeddingProviderFactory` + validação de config (modelo/tokenizer path, `Embeddings:Dimensions=384`).
- Download/carregamento do modelo: pasta de dados (`KnowledgeHub:DataDir` ou `models/` ao lado do executável), download on-demand no primeiro uso OU bundle opcional — decidir na implementação.
- Testes unitários (shape, determinismo, dimension guard) + integração marcada `[Trait]` para pular sem o modelo.

**Out of scope:**
- GPU/DirectML/CUDA — CPU only.
- Troca de modelo arbitrária (fixo all-MiniLM-L6-v2 nesta iteração).
- Migração de embeddings existentes — o `EmbeddingDimensionGuard`/model-match já rejeita vetores de outro provider; re-sync de sources é o caminho.

## 3. Technical Context

**Where the change happens:** `src/KnowledgeHub.Server/Embeddings/`.

**Files to read before implementing:**
- `Embeddings/IEmbeddingProvider.cs`, `EmbeddingProviderFactory.cs`, `EmbeddingOptions.cs`, `DeterministicEmbeddingProvider.cs` (contrato + factory)
- `EmbeddingCompatibilityCheck.cs` (guard de dimensão)
- `SPEC-20260913-ingestion-obsidian.md` §scope/backlog

**Files to create/modify:**
- `Embeddings/OnnxEmbeddingProvider.cs` (novo), `EmbeddingProviderFactory.cs` (case "onnx"), `EmbeddingOptions.cs` (ModelPath?), `appsettings.json` doc + README/env table, csproj (pacotes `Microsoft.ML.OnnxRuntime`, tokenizer).

**Risks:** +90 MB no publish single-file (mitigação: documentar; opcionalmente pacote separado); tokenizer compat (usar `Microsoft.ML.Tokenizers` ou BertTokenizers); primeira chamada lenta (lazy init + warmup opcional).

## 4. Requirements

### RF-001: Provider ONNX
- **Description:** `Embeddings:Provider=onnx` produz embeddings 384-d do all-MiniLM-L6-v2 localmente, CPU.
- **Rules:** implementa `IEmbeddingProvider` (mesma assinatura dos demais); normalização L2 igual aos outros providers; `CancellationToken` respeitado.
- **Input → Output:** texto → `float[384]` determinístico.

### RF-002: Config e validação
- **Description:** `EmbeddingOptions` aceita `onnx`; `Provider` desconhecido continua caindo em `deterministic`; caminho do modelo configurável (`Embeddings:ModelPath`, default `models/all-MiniLM-L6-v2/`).
- **Input → Output:** `Provider=onnx` sem modelo → erro claro no startup validate (`IValidateOptions`/ConfigurationValidator).

### RF-003: Dimensão e compatibilidade
- **Description:** ONNX fixo em 384; `EmbeddingDimensionGuard` e `EmbeddingModel` matching funcionam como nos outros providers; mismatch conta e pula (regra já existente).
- **Input → Output:** vetor 384 onnx não ranqueia com vetores de outro model.

## 6. Acceptance Criteria

- **CA-001:** Given `Provider=onnx` + modelo presente, when ingest/query, then embeddings 384-d e busca semântica funcional offline.
- **CA-002:** Given `Provider=onnx` sem modelo, when startup, then erro de validação claro (não crash silencioso).
- **CA-003:** `dotnet build` 0 warnings, `dotnet test` verde (testes onnx gated por presença do modelo).
- **CA-004:** README documenta provider onnx + impacto no tamanho do binário.

## 7. Task Plan

| # | Tarefa | Validação |
|---|--------|-----------|
| T1 | Pacotes + `OnnxEmbeddingProvider` | unit: shape/determinismo |
| T2 | Factory + options + validation | startup com/sem modelo |
| T3 | Testes unit + integração gated | `dotnet test` verde |
| T4 | Docs (README config table, compose env) | grep confirma |

## 8. Organization Guardrails

- Branch `feature/Devin-20260917-onnx-local-embeddings`; nada em `main`.
- Modelo/binários ONNX NÃO entram no git (download/bundle externo).
- `packages.lock.json` regenerado via `dotnet restore` após novos pacotes.

## 9. Definition of Done

- [ ] Provider onnx funcional e testado.
- [ ] Validação de config clara.
- [ ] Documentação atualizada.
- [ ] PR mergeado via gate normal.
