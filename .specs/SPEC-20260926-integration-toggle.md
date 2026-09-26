# SPEC — Integration enable/disable toggle

| Campo | Valor |
|---|---|
| ID | `SPEC-20260926-integration-toggle` |
| Tipo | `Feature` |
| Stack | `.NET 10`, `EF Core`, `Blazor WASM`, `MCP` |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Done` — PR pendente |
| Origem | pedido do usuário — "Settings → Integrações: ativar/desativar integração; ao desabilitar remover das tools do MCP ou retornar informação de desabilitado" |

## 1. User Story

Como operador, quero ligar/desligar integrações upstream (DeepWiki, Firecrawl,
Tavily, Context7) pela UI sem remover a key nem reiniciar — uma integração
desligada não expõe tools no catálogo MCP.

## 2. Requisitos funcionais

- **RF-001**: `PUT /api/settings/integrations/{provider}/enabled {enabled}` — 404 provider desconhecido, 400 sem body.
- **RF-002**: `GET /api/settings/integrations` retorna `enabled` por item.
- **RF-003**: provider desabilitado → `GetToolsAsync` retorna `[]` → tools somem de `tools/list` e `/api/tools` (mesmo `DynamicToolCatalog`); `tools/call` em tool removida cai no erro padrão de tool desconhecida.
- **RF-004**: flag persiste em `IntegrationStates` (provider único; ausência = habilitado) — independente de key (DeepWiki funciona sem key).
- **RF-005**: toggle reseta sessão upstream + bump no `IToolCatalogChangeNotifier` → sessões vivas recebem `tools/list_changed`.
- **RF-006**: card da integração em Settings mostra `Switch` + badge "desativada".

## 3. Implementação

- Entity `IntegrationState` + migrations em **ambos** providers (sqlite + postgres).
- `IIntegrationStateService` (singleton, cache in-memory invalidado no write).
- Gate nos 4 upstream `IToolProvider`s após o check de `Options.Enabled`.
- UI: `Switch` no header do card + toast de confirmação.

## 4. Critérios de aceite

- [x] Toggle desliga → `enabled:false`, tools `firecrawl_*` ausentes de `/api/tools`; religar restaura — `SettingsApiTests.Toggle_DisableProvider_RemovesItsTools`
- [x] Provider desconhecido → 404 — `Toggle_UnknownProvider_Returns404`
- [x] Build client + server limpos; migrations sqlite+postgres geradas
