# SPEC-20260915 — Boot chain cache revalidation

**Status:** Approved
**Data:** 2026-09-15
**Autor:** Devin
**Relacionada:** SPEC-20260915-wasm-boot-proxy-hardening, SPEC-20260915-table-record-item-factory

## Problema

Fix do `CreateItemCallback` (PR #56) foi deployado e o wasm em produção contém a correção
(`CreatePlaceholder` presente em `KnowledgeHub.Client.1w73073qi5.wasm`), porém o usuário
continua vendo o erro `ApiKeyDto create instance failed`.

Causa raiz: a cadeia de boot mutável é cacheável por tempo demais, e os assets
fingerprinted são `immutable` (1 ano). Um elo mutável cacheado aponta para o manifest
antigo → wasm fingerprinted antigo é servido de cache (browser / Cloudflare / SWG
corporativo) → bundle velho executa indefinidamente.

Cadeia de boot (headers observados em produção):

| Recurso | Cache atual |
|---------|-------------|
| `/` + SPA fallback (`index.html`) | sem `Cache-Control` → cache heurístico |
| `/js/boot.js` | `max-age=14400` |
| `/_framework/blazor.webassembly.js` | `max-age=14400` |
| `/_framework/dotnet.js` (importado pelo bwa.js) | `max-age=14400` |
| `/_framework/dotnet.boot.js` (manifest de boot) | `max-age=14400` |
| `*.{fingerprint}.*` | `max-age=31536000, immutable` (correto) |

## Requisitos

### RF-001 — no-cache na cadeia mutável de boot

Respostas para os seguintes recursos devem carregar `Cache-Control: no-cache`
(revalidação obrigatória; ETag preserva o fast-path 304):

- `index.html` — todo path sem extensão que cai no `MapFallbackToFile` (SPA shell),
  exceto prefixes de API/infra (`/api/*`, `/hubs/*`, `/health/*`, `/framework-assets/*`).
- `/js/boot.js`
- `/_framework/blazor.webassembly.js`
- `/_framework/dotnet.js`
- `/_framework/dotnet.boot.js`

### RF-002 — não regredir o resto

- Assets fingerprinted continuam `immutable`.
- `/framework-assets/*` mantém seus headers (immutable + Content-Encoding).
- APIs, health checks, SignalR e demais estáticos (`_content`, css, lib, favicon)
  não recebem o header novo.

## Design

Middleware pequeno registrado antes de `MapStaticAssets()`, que usa
`Response.OnStarting` para sobrescrever `Cache-Control` quando o path casa o
conjunto mutável de boot. `OnStarting` garante que o header final vence o valor
default do `MapStaticAssets`/`MapFallbackToFile` sem depender de ordem de escrita.

## Fora de escopo

- Purga de cache Cloudflare (operacional).
- Service worker / offline.
- Fingerprint token `#[.{fingerprint}]` em index.html — não cobre `dotnet.js`
  importado internamente de forma não-fingerprinted; no-cache cobre a cadeia inteira.

## DoD

- [ ] `GET /` e `GET /api-keys` → `Cache-Control: no-cache`
- [ ] `GET /js/boot.js`, `/_framework/blazor.webassembly.js`, `/_framework/dotnet.js` → `no-cache`
- [ ] Fingerprinted wasm continua `immutable`
- [ ] Testes de integração cobrindo os headers
- [ ] Build + format + suíte verdes; PR mergeado; redeploy; smoke em produção
