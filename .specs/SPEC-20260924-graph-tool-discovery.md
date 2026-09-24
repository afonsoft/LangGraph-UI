# SPEC-20260924-graph-tool-discovery — Graph tool UX + entity discovery via search

- **Status**: Done
- **Ticket**: GAP-DOCS-graph-discovery (user request 2026-09-24)
- **Depends on**: SPEC-20260923-graphrag (Done)

## Contexto

As 4 tools de grafo falham com `unknown component` quando o chamador não conhece
os nomes de entidades extraídos — os `examples` do schema usam nomes fictícios
(`payments-api`, `web-frontend`) e não há como descobrir nomes reais sem consultar
o banco. O ciclo natural deveria ser: buscar → ver componentes → atravessar o grafo.

## Requisitos

### RF-001 — Componentes por chunk no retorno de busca

`SearchResultItem` ganha `Components?: IReadOnlyList<string>` — nomes de
`KgNodes` ligados a arestas cuja `EvidenceChunkId` é o chunk retornado.
Preenchido na hidratação do `SearchService` (consulta única batch via
`EvidenceChunkId`, já indexada), gated por `IGraphSettingsService.Enabled` —
quando o grafo está off, nenhuma consulta extra roda. Cobre `search_knowledge`,
`ask_knowledge`, `query_{slug}`, REST `/api/search` e o eval harness (mesmo
caminho). Sem migration — campo calculado, não persistido.

### RF-002 — Componentes nas citações do ask

`CitationDto` ganha `Components?: IReadOnlyList<string>` propagado de
`SearchResultItem` em `ExtractCitations` — a resposta estruturada do
`ask_knowledge`/`/api/ask` carrega as entidades citadas.

### RF-003 — Superfícies de texto

`FormatHits` (usado por `search_knowledge`/`query_{slug}`) e o texto de
citações do `ask_knowledge` exibem `components: a, b` quando presentes —
agents MCP leem texto, então o campo precisa aparecer na superfície textual.

### RF-004 — Descrições e exemplos reais das graph tools

Reescrever `Description`/`examples` das 4 tools com: quando usar (vs
`search_knowledge`), o que retorna, e exemplos baseados em entidades reais do
vault (`OmniRoute`, `AgentRouter`, `OpenClaw`). Incluir a dica de descoberta:
"call `search_knowledge` first — results carry `components` you can traverse".

## Acceptance criteria

- [ ] `search_knowledge` retorna `components:` por hit quando há arestas
      evidenciadas naquele chunk; ausente quando o grafo está vazio/desligado
- [ ] `ask_knowledge` structuredContent citações carregam `components`
- [ ] Graph disabled → nenhuma query em `KgEdges` no path de busca
- [ ] Descrições das 4 tools documentam when-to-use + discovery tip
- [ ] Testes: enrichment unit test (chunk→components), disabled gate, citation
      propagation, contrato MCP pinado atualizado (descriptions/schemas mudam)

## Out of scope

- Nodes sem arestas (entidades soltas) não aparecem — sem link chunk→nó no modelo.
- `read_document`/`write_note` — não pedido.
