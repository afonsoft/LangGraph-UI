# SPEC-20260916-api-key-settings

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `api-key-settings` |
| Type | `Backend + Frontend` |
| Stack | `.NET 10 / Blazor WebAssembly + BootstrapBlazor / EF Core SQLite` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260916-api-key-settings` |
| Status | `Approved` |

## 1. User Story

**As a** platform administrator
**I want** each API key to have its own configurable settings (starting with chat provider config) that inherit from the global defaults
**So that** different clients (Cursor, Claude Desktop, scripts) using different API keys can use different LLM endpoints/models without affecting each other or the global configuration.

**Problem context:** Currently, all API keys share the same global chat provider settings (`ChatSettings`). A user who wants one API key to use Ollama locally and another to use OpenAI remotely cannot do so. Every API key must be able to optionally override the global chat provider settings (endpoint, model, API key) while falling back to the global defaults when not configured.

## 2. Scope

**In scope:**
- New database entity `ApiKeyChatSettings` linking to `ApiKey` with optional overrides for endpoint, model, and API key.
- Effective-settings resolution: API-key override → global ChatSettings → environment variables.
- REST endpoints: `GET/PUT/DELETE /api/api-keys/{id}/settings/chat`.
- MCP tool: `set_api_key_chat_settings` (JSON-RPC) for programmatic configuration.
- UI: settings icon (gear) on each row in `/api-keys` table opening a modal with the same form as global Settings.
- Masked DTOs: API key hints never expose full secrets; same pattern as `ChatSettingsDto`.
- Service interface `IApiKeyChatSettingsService` with effective-options resolution and client factory.
- Chat request pipeline updated to resolve settings per API key when `Authorization: Bearer aft_*` is present.

**Out of scope:**
- Per-API-key settings for integrations (Firecrawl, DeepWiki) — can be added later following the same pattern.
- Per-API-key rate limits or quotas (separate feature).
- UI for batch-editing multiple API keys at once.

## 3. Related SPECs

- SPEC-20260914-auth-login — API key authentication and audit.
- SPEC-20260916-settings-chat-config — global chat settings (store over env).
- SPEC-20260916-mobile-layout-responsive — the settings modal must work on mobile.

## 4. Existing Code

**Server:**
- `Domain/Entities/ChatSettings.cs` — single-row global override (Id=1).
- `Domain/Entities/ApiKey.cs` — key metadata (no settings FK yet).
- `Settings/IChatSettingsService.cs` + `ChatSettingsService.cs` — global effective config resolution.
- `Api/SettingsEndpoints.cs` — global settings REST endpoints.
- `Data/KnowledgeHubDbContext.cs` — DbSets and EF configuration.

**Client:**
- `Pages/ApiKeys.razor` — table with actions "Uso" and "Revogar"; no settings action yet.
- `Pages/Settings.razor` — global chat settings form.
- `Services/SettingsApiClient.cs` — typed HttpClient for settings.

**Shared:**
- `Contracts/ChatSettingsDtos.cs` — `ChatSettingsDto`, `SaveChatSettingsRequest`.

## 5. Functional Requirements

### RF-001: Database Model

Add `ApiKeyChatSettings` entity:
```csharp
public sealed class ApiKeyChatSettings
{
    public int Id { get; set; }
    public Guid ApiKeyId { get; set; }
    public ApiKey ApiKey { get; set; } = null!;
    public string? Endpoint { get; set; }  // null = inherit from global
    public string? Model { get; set; }     // null = inherit from global
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

The API key secret (if overridden) is stored in `IntegrationSecret` with a composite slug `apikey-chat-{apiKeyId}` (reusing the existing encrypted secret store), NOT in this entity.

EF configuration:
- One-to-one between `ApiKey` and `ApiKeyChatSettings` (optional).
- Cascade delete when `ApiKey` is deleted.

### RF-002: Effective Settings Resolution

`IApiKeyChatSettingsService.GetEffectiveOptions(Guid apiKeyId)` resolves in order:
1. If `ApiKeyChatSettings` exists for the key and has non-null endpoint/model → use those.
2. Else fallback to `IChatSettingsService.GetEffectiveOptions()` (global store → env).
3. API key secret: look up `IntegrationSecret` with slug `apikey-chat-{apiKeyId}`; if absent, fallback to global chat API key (store → env).

The service must cache per-key snapshots with a TTL or invalidation mechanism, similar to `ChatSettingsService`.

### RF-003: REST Endpoints

Under `/api/api-keys/{id}/settings/chat`:

**GET** — returns effective state for the API key (same shape as `ChatSettingsDto` but with inheritance indicators).
```csharp
public sealed record ApiKeyChatSettingsDto : ChatSettingsDto
{
    /// <summary>True when this API key has its own endpoint/model override.</summary>
    public required bool HasOverride { get; init; }
    /// <summary>Which fields are overridden: endpoint, model, apiKey, or none.</summary>
    public required string[] OverrideFields { get; init; }
}
```

**PUT** — saves or updates the API key override. Body same as `SaveChatSettingsRequest` but all fields optional (null = remove override for that field).
```csharp
public sealed record SaveApiKeyChatSettingsRequest
{
    public string? Endpoint { get; init; }
    public string? Model { get; init; }
    public string? ApiKey { get; init; }  // blank = keep existing; null = remove override
}
```

**DELETE** — removes the API key override entirely (deletes `ApiKeyChatSettings` row and its secret).

Authorization: same as global settings — requires admin/auth cookie. The API key being configured does NOT need to be the same as the caller's API key (admin configures all keys).

### RF-004: MCP Tool

Expose `set_api_key_chat_settings` as an MCP tool (JSON-RPC) so external agents can configure their own key:

```json
{
  "name": "set_api_key_chat_settings",
  "description": "Override chat provider settings for the current API key. Null fields inherit from global defaults.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "endpoint": { "type": ["string", "null"], "description": "OpenAI-compatible base URL" },
      "model": { "type": ["string", "null"], "description": "Model name" },
      "apiKey": { "type": ["string", "null"], "description": "API key override" }
    }
  }
}
```

The tool resolves the caller's API key from the SSE session context (same mechanism as audit logging) and applies settings to that key only. Non-API-key sessions (cookie auth) are rejected with `Unauthorized`.

### RF-005: Chat Pipeline Integration

When a chat request arrives with `Authorization: Bearer aft_*`:
1. The existing auth middleware validates the key and populates `ClaimsPrincipal`.
2. The chat service must look up the API key ID from the principal claim.
3. Call `IApiKeyChatSettingsService.GetEffectiveOptions(apiKeyId)` instead of the global service.
4. If the API key has no override, the global settings are used transparently.

This applies to:
- `POST /api/chat` (agent chat loop)
- `POST /api/ask` (single-shot RAG)
- Any future endpoint using `IChatClient`

### RF-006: UI — Settings Icon on API Keys Page

In `Pages/ApiKeys.razor`, add a gear/settings icon button to each row's action column:
```razor
<Button Size="Size.ExtraSmall" Color="Color.Secondary" Icon="fa-solid fa-gear"
        Text="Config" OnClick="() => ShowSettings(v.Row)" />
```

Clicking opens a `Modal` (reusing the same modal pattern as create/revoke) titled "Configurar chat — {key.Name}".

The modal form mirrors the global Settings page but with:
- A checkbox/toggle "Usar configuração global" (default ON).
- When ON: fields are disabled and show the inherited global values as read-only hints.
- When OFF: fields become editable; blank API key keeps the existing override; typing a new key stores it.
- Save button: PUT `/api/api-keys/{id}/settings/chat`.
- "Remover override" button: DELETE `/api/api-keys/{id}/settings/chat` (only visible when override exists).

### RF-007: Settings Modal State Display

The modal must show:
- Inherited endpoint/model (read-only, with badge "global").
- Overridden endpoint/model (editable, with badge "esta chave").
- API key state: "herdada de global", "sobreposta para esta chave", or "não configurada".
- Last updated timestamp of the override (if any).

## 6. Non-Functional Requirements

### RNF-001: Performance
- Per-key effective options must be cached; invalidation on PUT/DELETE.
- Cache key: `apikey-chat-{apiKeyId}`.
- Global settings changes must NOT invalidate per-key caches (they inherit dynamically on next access).

### RNF-002: Security
- API key secrets stored via `IIntegrationSecretStore` (already encrypted at rest).
- Full secrets never returned in DTOs; only masked hints.
- The MCP tool `set_api_key_chat_settings` can only modify the caller's own key.
- Admin UI can modify any key.

### RNF-003: Backward Compatibility
- Existing API keys without overrides continue using global settings — zero behavior change.
- Existing `ChatSettings` table and global endpoints are untouched.
- Migration is additive only (new table `ApiKeyChatSettings`, no data migration needed).

## 7. API / Data Contracts

### DTOs (add to `ChatSettingsDtos.cs` or new file)

```csharp
public sealed record ApiKeyChatSettingsDto : ChatSettingsDto
{
    public required bool HasOverride { get; init; }
    public required string[] OverrideFields { get; init; }
}

public sealed record SaveApiKeyChatSettingsRequest
{
    public string? Endpoint { get; init; }
    public string? Model { get; init; }
    public string? ApiKey { get; init; }
}
```

### REST Endpoints

| Method | Path | Auth | Body | Response |
| --- | --- | --- | --- | --- |
| GET | `/api/api-keys/{id}/settings/chat` | Cookie | — | `ApiKeyChatSettingsDto` |
| PUT | `/api/api-keys/{id}/settings/chat` | Cookie | `SaveApiKeyChatSettingsRequest` | `ApiKeyChatSettingsDto` |
| DELETE | `/api/api-keys/{id}/settings/chat` | Cookie | — | `204 No Content` |

### MCP Tool

```json
{
  "name": "set_api_key_chat_settings",
  "description": "Override chat provider settings for the current API key",
  "inputSchema": {
    "type": "object",
    "properties": {
      "endpoint": { "type": ["string", "null"] },
      "model": { "type": ["string", "null"] },
      "apiKey": { "type": ["string", "null"] }
    }
  }
}
```

## 8. UI/UX

### API Keys Table — New Action Column

```
| Nome    | Prefix   | Criada | Último uso | Status | Ações                |
|---------|----------|--------|------------|--------|----------------------|
| cursor  | aft_abc… | ...    | ...        | ativa  | [Uso] [Config] [Rev] |
```

On mobile (< 576px), the action buttons may collapse to an icon-only or dropdown pattern (see SPEC-20260916-mobile-layout-responsive).

### Settings Modal

```
┌─────────────────────────────────────────┐
│ Configurar chat — cursor          [×]   │
├─────────────────────────────────────────┤
│ [✓] Usar configuração global            │
│                                         │
│ Endpoint (global): https://api.openai…  │
│ Model (global): gpt-4o                  │
│                                         │
│ ─── ou configurar para esta chave ───   │
│                                         │
│ Endpoint: [____________________]        │
│ Model:    [____________________]        │
│ API key:  [••••315f] [Substituir…]      │
│                                         │
│ [Salvar]  [Remover override]            │
└─────────────────────────────────────────┘
```

When "Usar configuração global" is checked, the override fields are disabled and cleared on save (nulls sent → removes override).

## 9. Security

- Secrets use existing `IIntegrationSecretStore` encryption.
- Full API key never leaves the server in responses.
- The MCP tool scope is restricted to the authenticated API key's own settings.
- Audit logging: log PUT/DELETE on per-key settings (reuse `ApiKeyUsageEvent` pattern or add a new audit table if needed — for MVP, reuse existing middleware logging).

## 10. Data Model / Persistence

### Migration

```csharp
migrationBuilder.CreateTable(
    name: "ApiKeyChatSettings",
    columns: table => new
    {
        Id = table.Column<int>(type: "INTEGER", nullable: false)
            .Annotation("Sqlite:Autoincrement", true),
        ApiKeyId = table.Column<Guid>(type: "TEXT", nullable: false),
        Endpoint = table.Column<string>(type: "TEXT", nullable: true),
        Model = table.Column<string>(type: "TEXT", nullable: true),
        UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
    },
    constraints: table =>
    {
        table.PrimaryKey("PK_ApiKeyChatSettings", x => x.Id);
        table.ForeignKey(
            name: "FK_ApiKeyChatSettings_ApiKeys_ApiKeyId",
            column: x => x.ApiKeyId,
            principalTable: "ApiKeys",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);
    });

migrationBuilder.CreateIndex(
    name: "IX_ApiKeyChatSettings_ApiKeyId",
    table: "ApiKeyChatSettings",
    column: "ApiKeyId",
    unique: true);
```

### Secret Storage

API key secret overrides use slug format: `apikey-chat-{apiKeyId:N}` (lowercase, no braces).
Example: `apikey-chat-550e8400-e29b-41d4-a716-446655440000`

## 11. Acceptance Criteria

### AC-001: Default Inheritance
```gherkin
Given an API key "cursor" with no override
When I call GET /api/api-keys/{cursor-id}/settings/chat
Then the response shows the global chat settings
And HasOverride is false
And OverrideFields is empty
```

### AC-002: Override and Effective Resolution
```gherkin
Given an API key "cursor"
When I PUT /api/api-keys/{cursor-id}/settings/chat with endpoint="http://localhost:11434" and model="llama3"
Then the override is stored
And subsequent GET returns HasOverride=true and the new endpoint/model
And the API key's chat requests use the overridden endpoint/model
```

### AC-003: Partial Override
```gherkin
Given an API key with endpoint overridden but model null
When I call GET /api/api-keys/{id}/settings/chat
Then endpoint comes from the override
And model comes from global settings
And OverrideFields contains only "endpoint"
```

### AC-004: MCP Tool Self-Service
```gherkin
Given an authenticated SSE session with API key "aft_xxx"
When I call tool set_api_key_chat_settings with { "model": "gpt-4o-mini" }
Then the API key's model override is updated
And the admin UI reflects the change on next load
```

### AC-005: UI Settings Modal
```gherkin
Given I am on /api-keys
When I click the gear icon on a key row
Then a modal opens showing the current effective settings
And I can toggle "Usar configuração global"
And when unchecked I can edit and save overrides
```

### AC-006: Delete Override
```gherkin
Given an API key with chat settings override
When I call DELETE /api/api-keys/{id}/settings/chat
Then the override row and secret are removed
And the key falls back to global settings
```

### AC-007: Backward Compatibility
```gherkin
Given existing API keys created before this feature
When the feature is deployed
Then all keys continue working with global settings
And no manual migration is required
```

## 12. Test Strategy

- **Unit:** Test `ApiKeyChatSettingsService` effective resolution logic with mocked `IChatSettingsService` and secret store.
- **Integration:** Test REST endpoints with authenticated client; verify DTO masking.
- **Integration:** Test MCP tool with SSE session context.
- **E2E:** Playwright test opening the settings modal from `/api-keys`, toggling override, saving, and verifying the effective config.

## 13. Rollout

1. Create migration (additive table + index).
2. Implement server-side: entity, service, endpoints, MCP tool registration.
3. Implement client-side: modal component, API client method, ApiKeys page update.
4. Update chat pipeline to use per-key resolution when API key auth is present.
5. Run integration tests.
6. No downtime; additive changes only.

## 14. Risks

| Risk | Mitigation |
| --- | --- |
| Cache invalidation complexity | Keep it simple: in-memory `ConcurrentDictionary` with manual invalidation on PUT/DELETE |
| Secret slug collision | Use GUID format `apikey-chat-{guid:N}`; verify uniqueness |
| Chat pipeline performance | Per-key lookup is indexed by ApiKeyId; cache mitigates repeated access |
| UI modal duplication | Extract a shared `ChatSettingsForm` component used by both `/settings` and the modal |

## 15. Open Questions

- [A DEFINIR] Should per-key settings also cover embedding provider (separate from chat), or is chat enough for MVP?
- [A DEFINIR] Should non-admin API keys be allowed to read their own settings via REST (not just MCP tool)?
- [A DEFINIR] Do we need an audit log table specifically for settings changes, or is the existing request audit sufficient?
