# SPEC-20260914-playground-tool-form

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `playground-tool-form` |
| Type | `Bugfix` |
| Stack | `.NET 10`, `Blazor WASM`, `JSON Schema` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260914-playground-tool-form` |
| Ticket | `—` |
| Status | `Done` |

## 1. User Story

**As a** platform user testing tools in the Playground
**I want** the generated form to accept natural input for every `inputSchema` shape — `anyOf` unions, string arrays, enums, nullable fields — and to offer ready-made examples per tool
**So that** I can invoke tools like DeepWiki `ask_question` by typing `langchain-ai/langgraph`, without hand-writing JSON or reading the schema.

**Problem context:**
`BuildFields()` maps `repoName` (`anyOf: [string, string[]]`) to `Type = "anyOf"` → JSON textarea; `RunToolAsync()` has no `"anyOf"` case → falls into `JsonDocument.Parse("langchain-ai/langgraph")` → toast **"Valor inválido em 'repoName' / esperado anyOf"**. Only `"owner/repo"` (JSON-quoted) or `["a/b"]` works today — undiscoverable. The same UX gap hits `array` fields (`tags`, `tools` — raw JSON required), `enum` (`mode` — free text, no hint of valid values) and there are no examples anywhere: the user must guess valid arguments per tool.

## 2. Scope

**In scope:**
- `ToolArgumentBuilder` in `KnowledgeHub.Shared` — pure, testable: parses `inputSchema` into field descriptors and coerces raw text input into `JsonElement` per resolved kind.
- Playground form inputs: `enum` → `Select`; `array items:string` and `anyOf ⊆ {string, string[]}` → single text input, comma-separated; `anyOf` `T | null` → optional simple input of `T`; other unions/objects → JSON textarea with coercion + member-type validation.
- `examples` keyword added to the `inputSchema` of every internal tool (`KnowledgeToolsProvider`, `SourceQueryToolsProvider`, `ObsidianToolsProvider`, `DeepWikiToolsProvider`) — root-level array of complete-argument objects; property-level `examples` become input placeholders.
- Playground "Preencher exemplo" button: fills the form from `examples[0]` (repeat click cycles when >1); schemas without `examples` get a generated minimal skeleton from `required` + types.
- Friendly validation errors listing accepted forms (e.g. `string | string[]`).
- Unit tests for `ToolArgumentBuilder`.

**Out of scope:**
- JSON Schema validation beyond `type` / `items` / `maxItems` (`pattern`, `format`, `minLength`, `minimum`...) — the server already validates and returns `isError`.
- Changes to the MCP protocol surface, REST endpoints, or `ToolDescriptorDto` shape — `examples` is a standard JSON Schema annotation carried inside the existing `InputSchema`.
- Server-side argument validation changes (`DeepWikiToolsProvider.ValidateRepoName` already accepts `string | string[]`).
- OneOf/allOf/$ref resolution beyond the `anyOf` cases above.
- SignalR `/hubs/mcp` transport failure (separate bug, parked).

## 3. Technical Context

**Where the change happens:**
- `src/KnowledgeHub.Shared/` — new `Tooling/ToolArgumentBuilder.cs` + `ToolField` record. Shared (not Client) so unit tests can reach it — `KnowledgeHub.Tests.Unit` does not reference the WASM project.
- `src/KnowledgeHub.Client/Pages/Playground.razor` — `BuildFields()`/`RunToolAsync()` rewired to the builder; `FieldModel` replaced by `ToolField` + `TextValue`; new inputs (`Select` for enum, text for string-lists) and the example button.
- Tool schemas — `examples` added in the four providers (static `JsonObject`/`JsonNode` literals).

**Files to read before implementing:**
- `src/KnowledgeHub.Client/Pages/Playground.razor` — current field build + arg assembly
- `src/KnowledgeHub.Server/Mcp/ToolProviders/{KnowledgeToolsProvider,SourceQueryToolsProvider,ObsidianToolsProvider}.cs`
- `src/KnowledgeHub.Server/Mcp/Upstream/DeepWikiToolsProvider.cs` — `anyOf` schema + server-side `ValidateRepoName`
- `tests/KnowledgeHub.Tests.Unit/` — xUnit conventions (`ToolSluggerTests`, `DeepWikiToolValidationTests`)
- `src/KnowledgeHub.Shared/Contracts/ToolDtos.cs` — confirms no DTO change needed

**Files to create or modify:**
```text
src/KnowledgeHub.Shared/Tooling/ToolArgumentBuilder.cs          (new)
src/KnowledgeHub.Client/Pages/Playground.razor                  (modify)
src/KnowledgeHub.Server/Mcp/ToolProviders/KnowledgeToolsProvider.cs   (examples)
src/KnowledgeHub.Server/Mcp/ToolProviders/SourceQueryToolsProvider.cs (examples)
src/KnowledgeHub.Server/Mcp/ToolProviders/ObsidianToolsProvider.cs    (examples)
src/KnowledgeHub.Server/Mcp/Upstream/DeepWikiToolsProvider.cs         (examples)
tests/KnowledgeHub.Tests.Unit/Shared/ToolArgumentBuilderTests.cs      (new)
tests/KnowledgeHub.Tests.Integration/McpContractTests.cs              (PinnedSchemas sync)
```

## 4. Requirements

### RF-001: `ToolField` — resolved field descriptor
- **Description:** `ToolArgumentBuilder.ParseFields(JsonElement inputSchema)` returns `IReadOnlyList<ToolField>` in `properties` order. Each `ToolField` carries `Name`, `Kind` (`String | Integer | Number | Boolean | StringList | StringOrStringList | Enum | Json`), `TypeLabel` (display, e.g. `string | string[]`), `Description`, `Required`, `EnumValues`, `MaxItems`, `Placeholder`, `Simple` (text input vs textarea).
- **Input → Output:** `inputSchema` (`type:object`, `properties`, `required[]`) → ordered field list.

### RF-002: `anyOf` resolution
- **Rules:** `anyOf` with exactly one non-`{"type":"null"}` member → kind of that member, `Required=false` (nullable). `anyOf` whose members ⊆ `{type:string, type:array items:string}` → `StringOrStringList`, `TypeLabel` = `string | string[]`, `MaxItems` from the array member. Any other `anyOf` → `Json` kind with `TypeLabel` = member types joined by ` | `.
- **Input → Output:** property schema → resolved `ToolField`.

### RF-003: `anyOf` / argument coercion
- **Description:** `TryBuildArgument(ToolField, string? raw, out JsonElement value, out string? error)`:
  - `StringOrStringList`: `a/b` → `"a/b"`; `a/b, c/d` → `["a/b","c/d"]` (trim, drop empty segments); count > `MaxItems` → error.
  - `StringList` (`array items:string`): `a, b` → `["a","b"]`; single `a` → `["a"]`.
  - `Json`/other unions: `JsonDocument.Parse` first; on failure, if a union member is `string` → treat raw text as string; else error. Parsed value's `ValueKind` must match ≥1 member type.
  - Scalars: `integer|number` → `double.Parse`/`long` (invariant); `boolean` → `true/false`; `string` → raw.
  - Empty raw on non-required → omit the argument (never emit `null` unless the schema's only option is `null`).
- **Input → Output:** `("langchain-ai/langgraph")` → `"langchain-ai/langgraph"`; `("a/b, c/d")` → `["a/b","c/d"]`; `("!!", union without string)` → `error`.

### RF-004: `enum` and `array` fields
- **Rules:** `type:string` + `enum[]` → `Enum` kind, rendered `Select` (BootstrapBlazor) with the literal values; `array` with `items.type` in `{string}` → `StringList`; `array`/`object` otherwise → `Json` textarea (current behavior).

### RF-005: Labels, placeholders, errors
- **Rules:** label shows `TypeLabel` (never the literal `"anyOf"`); `Placeholder` = property `examples[0]` → `default` → type hint. Validation failure toast: `Valor inválido em '{name}'` + `esperado {TypeLabel}` (+ example hint when the field has one). Required-missing check unchanged (`Campos obrigatórios`).

### RF-006: `examples` in tool schemas
- **Description:** every internal tool `inputSchema` gains `"examples": [ { ...complete args object... } ]` at the root (JSON Schema annotation — flows through `InputSchema`/`ToolDescriptorDto` unchanged, visible to MCP clients too). Property-level `"examples": [...]` where useful (e.g. `repoName`). `query_{slug}` tools share `SourceQueryToolsProvider.Schema` — one example covers all instances.

### RF-007: "Preencher exemplo" button
- **Description:** Playground shows the button on the selected tool. Click fills every field from `examples[i]` (`i` cycles on repeat click when the array has >1). Values are written back in the field's *raw text* form (arrays → comma-separated for `StringList`/`StringOrStringList`; `Json` kinds → serialized JSON). Schema without `examples` → skeleton generated from `required` + `Kind` (`""`, `0`, `false`, `[]`/member hint).

### RF-008: Tests
- **Description:** `ToolArgumentBuilderTests` (xUnit, `tests/KnowledgeHub.Tests.Unit/Shared/`) covering: field parsing for every real schema in §3; bare string → `anyOf` string; comma list → array; `maxItems` overflow → error; `T|null` empty → omitted + non-empty → typed value; enum field kind + values; non-string array → Json kind; invalid JSON in non-string union → error naming accepted types; skeleton generation; `examples` fill round-trip (example args → fields → built args equal example).

## 5. API Contract

No endpoint or DTO change. `examples` rides inside the existing `inputSchema`:

```jsonc
// GET /api/tools → tools[].inputSchema (DeepWiki ask_question)
{
  "type": "object",
  "properties": {
    "repoName": {
      "anyOf": [ {"type":"string"},
                 {"type":"array","items":{"type":"string"},"maxItems":10} ],
      "description": "GitHub repo(s) in owner/repo format (max 10)",
      "examples": ["langchain-ai/langgraph"]
    },
    "question": {"type":"string","description":"...","examples":["How does checkpointing work?"]}
  },
  "required": ["repoName","question"],
  "examples": [ {"repoName":"langchain-ai/langgraph","question":"How does checkpointing work?"} ]
}
```

## 6. Acceptance Criteria

- [x] **Given** `ask_question` selected **when** `repoName` = `langchain-ai/langgraph` (bare text) **then** the call sends `"repoName": "langchain-ai/langgraph"` — no `Valor inválido` toast. *(covered: `ToolArgumentBuilderTests` anyOf bare-string cases + live smoke POST /api/tools/ask_question → real upstream response)*
- [x] **Given** `ask_question` **when** `repoName` = `a/b, c/d` **then** the call sends `"repoName": ["a/b","c/d"]`. *(covered: `ToolArgumentBuilderTests` comma-list case)*
- [x] **Given** `ask_question` **when** `repoName` = 11 comma-separated repos **then** toast `Valor inválido em 'repoName'` citing the `maxItems` limit — no request fired. *(covered: `ToolArgumentBuilderTests` maxItems case)*
- [x] **Given** a `T | null` optional field left empty **when** executing **then** the argument is omitted from the payload. *(covered: `ToolArgumentBuilderTests` nullable-union case)*
- [x] **Given** `search_knowledge` **when** the form renders **then** `mode` is a `Select` with `hybrid | semantic | lexical`, and `tags`-style `array items:string` fields accept `a, b` as `["a","b"]`. *(covered: Choice/StringList kinds in tests + `Select` render in Playground.razor, verified by build)*
- [x] **Given** any internal tool **when** clicking "Preencher exemplo" **then** the form fields are filled with a valid example that executes successfully. *(covered: `FillFromArguments` tests + `examples` present in all provider schemas, confirmed via live GET /api/tools)*
- [x] **Given** a schema without `examples` **when** clicking "Preencher exemplo" **then** fields are filled with the generated skeleton. *(covered: `FillSkeleton` tests)*
- [x] **Given** `dotnet test` **then** `ToolArgumentBuilderTests` + full suite pass. *(118 unit + 83 integration, 0 failures)*

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| `anyOf` non-string union | `!!` | toast `esperado string | object` (member types listed) |
| Whitespace-only required field | `   ` | treated as empty → `Campos obrigatórios` |
| Comma list with empties | `a/b,, c/d ,` | `["a/b","c/d"]` (trimmed, empties dropped) |
| `enum` untouched | — | omitted if optional; `Campos obrigatórios` if required |
| Write tool + example | `write_note` → Preencher exemplo → Executar | HITL write-confirmation still gates the call |
| `query_{slug}` tools | shared schema | one `examples` entry serves all instances |

## 7. Task Plan

- [x] **T1 — Builder (red→green):** `ToolArgumentBuilder` + `ToolField` in `KnowledgeHub.Shared/Tooling/`; `ToolArgumentBuilderTests` written first (reproduction: bare string on `anyOf` fails today, must pass).
- [x] **T2 — Playground rewiring:** `BuildFields` → `ParseFields`; render by `Kind` (incl. `Select` for enum); `RunToolAsync` → `TryBuildArgument` per field; labels/placeholders/errors per RF-005; "Preencher exemplo" per RF-007.
- [x] **T3 — Schema examples:** add `examples` (root + key properties) to the four providers' schemas **and update `PinnedSchemas` in `McpContractTests.cs` in the same commit** — the file documents this exact rule: the schema diff must be reviewable in the same commit.
- [x] **T4 — Verify:** `dotnet build KnowledgeHub.slnx`, `dotnet format --verify-no-changes`, `dotnet test`; manual smoke at `/playground` incl. a real `ask_question` call. *(local smoke done: GET /api/tools serves `examples`; POST ask_question with bare `repoName` string returned a real upstream answer — pending browser check after deploy)*

**7.1 Validation:** Bugfix → reproduction test first, regression tests included; .NET → unit tests for the builder rules; existing suite must stay green.

## 8. Organization Guardrails

- **Branches:** `feature/Devin-20260914-playground-tool-form`; never commit to `main`/`master`/`develop`.
- **Workflows:** `.github/workflows/` untouched.
- **Contracts:** `inputSchema` remains a valid JSON Schema — `examples` is an annotation keyword; `McpContractTests` pins schemas exactly (canonicalized compare) — per its own documented rule, a deliberate schema change is fine **if `PinnedSchemas` is updated in the same commit** so the diff is reviewable.
- **Security:** examples must contain no real secrets/paths; write tools still gated by the existing HITL confirm + `allowWrite`.
- **Scope:** no new endpoints, no DTO shape change, no server-side validation changes.

## 9. Definition of Done

- [x] All requirements (section 4) implemented.
- [x] All acceptance criteria (section 6) covered by passing tests or verified manually.
- [x] Edge cases handled.
- [x] `dotnet build`, `dotnet format --verify-no-changes`, `dotnet test` green.
- [x] Guardrails respected; no PII/secrets in examples or logs.
- [ ] Manual smoke at `https://rag.afonsoft.dev/playground` — `ask_question` with a bare `owner/repo` executes. *(requires deploy; local smoke against `localhost:5009` passed)*

## Open Questions / Pending Ambiguity

- None — design settled with the user (smart inputs `string|string[]` + fallback coercion; `T|null` optional; member validation `type`/`items`/`maxItems`; builder in `Shared` + unit tests; `examples` in schema + skeleton fallback).
