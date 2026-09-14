# SPEC — Conectores WebPage e DocumentFile (RAG multi-formato)

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260914-webpage-docfile-connectors` |
| Data | 2026-09-14 |
| Autor | Devin |
| Stack | `.NET 10`, `HttpClient`, `HtmlAgilityPack` ou `SmartReader`-like |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260914-webpage-docfile-connectors` |
| Status | `Draft` |

## 1. User Story

**As a** usuário
**I want** registrar páginas web e arquivos de documento (PDF/DOCX/TXT/MD soltos) como fontes
**So that** o RAG cubra as fontes clássicas dos artigos (repositórios de documentos, páginas, FAQs) — hoje só ObsidianVault ingere de fato; `WebPage`, `DocumentFile`, `RestApi` e `SqlDatabase` existem no enum mas `SyncAsync` os rejeita.

## 2. Contexto

`KnowledgeSourceService` já valida config por tipo (`url`, `filePath`, ...), mas `IngestionService` só implementa o caminho vault. Preencher os dois conectores de maior valor:

- **WebPage**: fetch URL → extrai conteúdo principal (boilerplate removal) → markdown-ish → chunk → embed. Política de recrawl via `AutoSync`/`SyncIntervalMinutes` existente.
- **DocumentFile**: pasta ou arquivo local → extrai texto por extensão → chunk → embed. PDF via `UglyToad.PdfPig` (lib .NET pura, madura); DOCX via `DocumentFormat.OpenXml`; MD/TXT direto. `FileSystemWatcher` reusa o padrão do vault.

`RestApi`/`SqlDatabase` continuam fora de escopo (design de credenciais/query próprio — SPEC futura).

## 3. Requisitos Funcionais

### RF-001 — WebPage
- `configuration`: `url` (obrigatória), `crawlDepth` (0 = só a página; 1 = +links mesma origem; máx 2), `maxPages` (default 20, máx 100), `includeSelector`/`excludeSelector` CSS opcionais.
- Fetch com `HttpClient` (timeout 30 s, `User-Agent: KnowledgeHub/<ver>`), respeita `robots.txt` (verificação simples do path), só `text/html` e mesmo-origin.
- Extração: remove `nav`/`header`/`footer`/`script`/`style`, usa `<article>`/`<main>` quando presente; fallback body. HTML→texto/markdown estruturado (headings preservados).
- DocumentUri = URL; dedup por content hash (já existente); re-sync detecta mudança de conteúdo e re-embeda só o que mudou.

### RF-002 — DocumentFile
- `configuration`: `path` (arquivo ou pasta; pasta = scan recursivo), `glob` (default `**/*`), exclui ocultos.
- Extensões: `.md`/`.txt` direto; `.pdf` via PdfPig (texto por página); `.docx` via OpenXML (parágrafos). Extensão não suportada → skipped com warning no resultado do sync.
- Pasta ganha watcher incremental (reusa `VaultWatcherService` generalizado ou `FileSystemWatcher` próprio) + AutoSync.

### RF-003 — Pipeline compartilhado
- Ambos alimentam o caminho existente: `KnowledgeDocument` (uri + hash + raw) → `MarkdownChunker` → `IEmbeddingProvider` → `IVectorStore` — mesma dedup/reconciliação do vault.
- `SyncAsync` roteia por `SourceType`; cada conector é `ISourceConnector` (interface nova: `Task<IEnumerable<RawDocument>> FetchAsync(config, ct)`), registrado por tipo — prepara o terreno para RestApi/SqlDatabase.
- Tools dinâmicas `query_{slug}` funcionam automaticamente (já são por fonte).
- UI `SourceEditDialog`: campos por tipo já dirigidos por schema — incluir os novos campos.

### RF-004 — Limites e segurança
- WebPage: sem credenciais em v1 (páginas públicas); `AllowPrivateHosts=false` — recusa URLs para localhost/169.254/10./172.16./192.168 (SSRF guard), exceto opt-in `allowPrivateHosts:true` documentado.
- DocumentFile: path deve existir e ser legível pelo uid do container; fora de `/data`/`/vaults` exige montagem (documentar).
- `maxFileSizeMB` (default 20) — arquivos maiores skipped com warning.

## 4. Requisitos Não-Funcionais

- Falha em 1 página/arquivo não aborta o sync — erro por item no `SyncResult` (existente).
- Crawl com politeness: 1 request por vez, delay 250 ms entre páginas.
- Novas deps mínimas e estáveis: `UglyToad.PdfPig`, `DocumentFormat.OpenXml`, um extrator HTML (avaliar `SmartReader` vs `HtmlAgilityPack` + limpeza própria — preferir o menor).

## 5. Fora de escopo

- RestApi/SqlDatabase connectors.
- JS-rendered pages (precisaria headless browser — fora).
- Autenticação em WebPage (basic/bearer) — fase 2.

## 6. Plano de tarefas

| # | Tarefa |
|---|---|
| T1 | `ISourceConnector` + roteamento no `IngestionService` + refactor do caminho vault para o contrato |
| T2 | `WebPageConnector` (fetch + robots + extração + crawl bounded) |
| T3 | `DocumentFileConnector` (md/txt/pdf/docx + watcher) |
| T4 | UI fields + README; contract tests dos tools dinâmicos |
| T5 | Testes: fixture HTML→chunk→search; PDF/DOCX fixture; SSRF guard; reconciliação |

## 7. Critérios de aceite

- [ ] Fonte WebPage de página pública indexa e aparece em `search_knowledge`.
- [ ] Pasta DocumentFile com `.md`+`.pdf` indexa ambos; editar arquivo dispara resync incremental.
- [ ] URL privada (127.0.0.1) recusada salvo opt-in explícito.
- [ ] Arquivo não suportado não falha o sync — skipped com aviso.
- [ ] `query_{slug}` cobre as novas fontes sem código extra.
- [ ] Suite verde.

## 8. Riscos

| Risco | Mitigação |
|---|---|
| Extração HTML ruidosa | selector include/exclude + `<article>` preferência; resultado inspecionável via `read_document`/chunks |
| PDF sem camada de texto (scan) | documentado — retorna skipped "no extractable text" |
| Crawl infinito | `maxPages`+`crawlDepth`+mesma origem hard bounds |
