# SPEC-20260924-hosted-services-and-serilog-logging

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `hosted-services-and-serilog-logging` |
| Type | `Refactor / Observability / Architecture` |
| Stack | `.NET 10 (ASP.NET Core + BackgroundService + Serilog.AspNetCore)` |
| Repository | `LangGraph-UI` |
| Branch | `feature/Antigravity-20260924-hosted-services-and-serilog-logging` |
| Ticket | [#182](https://github.com/afonsoft/LangGraph-UI/issues/182) |
| Status | `Done` |

## 1. User Story

**As a** engenheiro de software e operador do KnowledgeHub
**I want** background jobs estruturados como HostedServices dedicados com logs ricos e o sistema de logging aprimorado com Serilog (estruturado, sinks para Console e Arquivo com rotação e enriquecimento de contexto)
**So that** as operações em segundo plano (sincronizações automáticas de fontes de nuvem, limpeza de dados órfãos, recálculo de métricas) sejam confiáveis, observáveis e rastreáveis sem sobrecarregar o host, com logs detalhados e fáceis de depurar em produção.

**Problem context:**
Atualmente:
1. Os background jobs estão concentrados e acoplados dentro de `VaultWatcherService` (que mistura o monitoramento por FileSystemWatcher de vaults locais com o loop de auto-sync de todas as demais fontes). Não há isolamento entre jobs de tipos diferentes, dificultando auditoria, logs independentes e controle de ciclo de vida.
2. O sistema de logs utiliza o logging padrão do .NET sem formatação avançada ou sinks estruturados para arquivo em disco com rotação automática. Em ambientes produtivos ou containers, a falta de Serilog limita o enriquecimento automático de propriedades (como CorrelationId, SourceId, JobName, TraceId), dificultando correlações em falhas de background jobs.

## 2. Scope

**In scope:**
- **Modernização do Sistema de Log com Serilog:**
  - Instalação dos pacotes: `Serilog.AspNetCore`, `Serilog.Sinks.Console`, `Serilog.Sinks.File`.
  - Configuração via `builder.Host.UseSerilog(...)` e seção `"Serilog"` no `appsettings.json`.
  - Enriquecimento automático de logs: `FromLogContext()`, `WithMachineName()`, `WithProcessId()`, `WithThreadId()`, `WithCorrelationId()`.
  - Sink Console estruturado (colorido no terminal dev / JSON compatível com OpenTelemetry/Datadog em produção).
  - Sink Rolling File: gravação diária em `logs/knowledgehub-.log` com limite de retenção (ex.: 14 dias) e tamanho máximo por arquivo (ex.: 50MB).
- **Arquitetura de HostedServices para Background Jobs:**
  - `ScheduledSyncBackgroundService`:
    - Responsável exclusivo por agendar e executar o auto-sync periódico de fontes remotas (`AwsS3`, `AzureFiles`, `OciStorage`, `GoogleDrive`, `WebPage`, `Notion`).
    - Controle de concorrência com semáforos por fonte.
    - Logs detalhados em cada ciclo: início do job, duração em ms, documentos baixados, chunks gerados, avisos e tratamento de erros sem interrupção do loop.
  - `MaintenanceBackgroundService`:
    - Tarefas periódicas de manutenção: purga de diretórios de staging órfãos com mais de 24h sem fonte ativa, reconciliação de integridade e telemetria.
  - `VaultWatcherService`:
    - Refatorado para focar estritamente na observação reativa de sistemas de arquivos locais (`ObsidianVault` e `DocumentFile` local).
- **Logs Detalhados nos Background Jobs:**
  - `LogInformation` estruturado com tokens semânticos: `{JobName}`, `{SourceId}`, `{SourceName}`, `{DurationMs}`, `{Status}`, `{DocsProcessed}`.
  - Tratamento de exceções com `LogError(ex, "Job {JobName} falhou para source {SourceId}")`.

**Out of scope:**
- Adição de fila externa como RabbitMQ ou Kafka (mantém a arquitetura standalone leve e confiável sem dependências pesadas obrigatórias).

## 3. Technical Context

**Where the change happens:**
- `src/KnowledgeHub.Server/KnowledgeHub.Server.csproj` — adição dos pacotes Serilog.
- `src/KnowledgeHub.Server/Program.cs` — bootstrap do Serilog e registro dos HostedServices.
- `src/KnowledgeHub.Server/BackgroundServices/` — novos `ScheduledSyncBackgroundService.cs`, `MaintenanceBackgroundService.cs` e refatoração do `VaultWatcherService.cs`.
- `src/KnowledgeHub.Server/appsettings.json` — configuração dos níveis de log e sinks do Serilog.
- `tests/KnowledgeHub.Tests.Unit/` — testes de execução dos HostedServices e validação de configuração do Serilog.

**Files to create or modify:**
```text
src/KnowledgeHub.Server/KnowledgeHub.Server.csproj                        (modify)
src/KnowledgeHub.Server/Program.cs                                       (modify)
src/KnowledgeHub.Server/appsettings.json                                 (modify)
src/KnowledgeHub.Server/BackgroundServices/ScheduledSyncBackgroundService.cs (create)
src/KnowledgeHub.Server/BackgroundServices/MaintenanceBackgroundService.cs   (create)
src/KnowledgeHub.Server/BackgroundServices/VaultWatcherService.cs            (modify)
tests/KnowledgeHub.Tests.Unit/Server/BackgroundServices/ScheduledSyncBackgroundServiceTests.cs (create)
```

## 4. Requirements

### RF-001: Integração do Serilog no Host
- **Description:** O host da aplicação deve ser configurado com `Serilog.AspNetCore`.
- **Rules:**
  - Leitura da seção `"Serilog"` no `appsettings.json`.
  - Console sink habilitado por padrão.
  - Rolling File sink gravando em `logs/knowledgehub-.log`, rotacionando diariamente com retenção de 14 dias.
  - Suporte a enriquecimento contextual (`LogContext.PushProperty(...)`).

### RF-002: HostedService para Auto-Sync Periódico (`ScheduledSyncBackgroundService`)
- **Description:** BackgroundService dedicado que consulta as fontes ativas com `AutoSyncEnabled = true` a cada intervalo configurável.
- **Rules:**
  - Executa o sync via `IIngestionService.SyncAsync(source.Id)`.
  - Registra log com início, duração, resultado e status sem bloquear outras fontes em caso de falha.
  - Graceful cancellation com suporte a `stoppingToken`.

### RF-003: HostedService de Manutenção (`MaintenanceBackgroundService`)
- **Description:** BackgroundService executado em intervalos espaçados (ex.: a cada 6 horas) para expurgo de arquivos de staging temporários sem fonte ativa.
- **Rules:** Não interfere nas operações ativas de ingestão.

### RF-004: Enriquecimento Estruturado nos Logs dos Jobs
- **Description:** Cada execução de background job deve abrir um escopo de log (`using (logger.BeginScope(...))`) contendo `JobName`, `JobId` e `SourceId`.

## 5. API Contract

Não expõe APIs públicas; os jobs operam em background e os logs são emitidos para o Console e para arquivos em `logs/`.

Configuração sugerida em `appsettings.json`:
```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Microsoft.Hosting.Lifetime": "Information",
        "KnowledgeHub": "Debug"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj} {Properties:j}{NewLine}{Exception}"
        }
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/knowledgehub-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 14,
          "fileSizeLimitBytes": 52428800,
          "rollOnFileSizeLimit": true,
          "outputTemplate": "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}"
        }
      }
    ],
    "Enrich": [ "FromLogContext", "WithMachineName", "WithProcessId", "WithThreadId" ]
  }
}
```

## 6. Acceptance Criteria

- [ ] **Given** a inicialização da aplicação **when** o host sobre **then** o Serilog assume a captura dos logs e gera o arquivo `logs/knowledgehub-YYYYMMDD.log`.
- [ ] **Given** uma fonte de nuvem configurada para auto-sync **when** o período de intervalo expira **then** o `ScheduledSyncBackgroundService` executa o sync e grava logs estruturados com tempo de execução e contagem de itens.
- [ ] **Given** uma falha de rede durante o sync automático de uma fonte **when** o job captura a exceção **then** ele grava log de erro com detalhes sem encerrar o HostedService nem afetar as outras fontes.
- [ ] **Given** o desligamento da aplicação (shutdown gracioso) **when** o sinal de cancelamento é emitido **then** os HostedServices concluem as tarefas em andamento dentro do timeout configurado.

## 7. Task Plan

- [ ] **T1:** Instalar pacotes NuGet do Serilog em `KnowledgeHub.Server.csproj`.
- [ ] **T2:** Configurar bootstrap do Serilog em `Program.cs` e `appsettings.json`.
- [ ] **T3:** Implementar `ScheduledSyncBackgroundService.cs` com logs estruturados e escopo de telemetria.
- [ ] **T4:** Implementar `MaintenanceBackgroundService.cs` para limpeza e rotinas de manutenção.
- [ ] **T5:** Refatorar `VaultWatcherService.cs` removendo o auto-sync duplicado e focando em eventos de arquivo.
- [ ] **T6:** Criar testes unitários para os background services.

## 8. Organization Guardrails

- **Branches:** nunca comitar em `main` ou `develop`.
- **Performance:** garantir que a rotação e gravação de logs em arquivo seja assíncrona para não onerar o pipeline de requisições.
- **Resiliência:** loops de background jobs devem possuir `try/catch` de topo de forma que nenhuma exceção não tratada encerre o processo do host.

## 9. Definition of Done

- [ ] Requisitos RF-001 a RF-004 implementados.
- [ ] Serilog gravando logs no Console e em arquivo com rotação.
- [ ] HostedServices isolados e funcionando com cobertura de testes unitários.
- [ ] Solution compilando e testes passando.
