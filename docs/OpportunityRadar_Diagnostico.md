# Diagnóstico Inicial: Radar de Oportunidades Ocultas

## Arquitetura atual (objetiva)
- Backend: monólito modular em .NET 10 com projetos por contexto (`*.Application`, `*.Domain`, `*.Infra`) e API em `EvangelionERPV2.Web`.
- Persistência: EF Core 10 + SQL Server via `AppDbContext` em `EvangelionERPV2.Shared`.
- Integrações: RabbitMQ (workers de pedido/email), Redis, AWS Secrets Manager, SignalR, métricas Prometheus (`/metrics`).
- API: versionamento por rota (`api/v{version}/{controller}/{action}`), autenticação JWT, rate limit e middlewares de log/exceção.
- Frontend: Expo/React Native Web com módulos em `features/*`, serviço HTTP central em `services/erpService.ts`, E2E com Cypress.

## Entidades e capacidades já existentes relevantes
- `Customer`, `Order`, `OrderedProduct`, `Product`, `PayableBill`, `PayableBillProduct`, `Bill`, `Enterprise`, `User`.
- Estoque disponível em `Product.StorageQuantity`.
- Custos/compras parcialmente rastreáveis via `PayableBill` + `PayableBillProduct` + `ProductsReceivedAt`.
- Não há entidade explícita de `Supplier`, `SalesChannel` ou `Region`; esses sinais precisarão ser inferidos no MVP.

## Jobs/filas/agendadores existentes
- `EvangelionERPV2.Worker.Order` e `EvangelionERPV2.Worker.Email` (BackgroundService + RabbitMQ).
- Na API principal (`EvangelionERPV2.Web`) não há scheduler batch dedicado para analytics; necessário adicionar worker interno para recomputação do radar.

## APIs/telas reaproveitáveis
- APIs para dados base: pedidos, itens, produtos, clientes, payable bills.
- Tela frontend com navegação modular já pronta (`Sidebar`, `App.tsx`) e padrão de cards/listagens reutilizável.
- Serviço `ErpService` já centraliza chamadas de API e paginação/filtros.

## Autenticação/autorização atual
- JWT com claims de usuário/empresa (`Sid`, `uid`, `GroupSid`).
- Controllers protegidos com `[Authorize]`.
- Controle fino por perfil ainda é limitado no backend; no frontend já existe distinção visual por papéis gerenciais.

## Stack de testes atual
- Backend: xUnit + Moq em projetos `*.Test`.
- Frontend: Jest + React Native Testing Library + Cypress E2E.
- Não existe suíte dedicada de integração para módulo analítico; será adicionada.

## O que reaproveitar
- Modelo de modularização e DI existentes.
- `Order`, `OrderedProduct`, `Product`, `Customer`, `PayableBill` como fontes de sinais do radar.
- Infra observável existente (Serilog + Prometheus + middleware de request logging).
- Padrão de APIs REST já utilizado e componentes de UI/lista do frontend.

## O que adaptar
- `AppDbContext`/migrations para entidades de oportunidades.
- `MapperConfig` para DTOs do radar.
- Autorização para ações de execução (aceitar/implementar) por nível de acesso gerencial.
- `ErpService`, `Sidebar`, `App.tsx` e i18n para o novo módulo frontend.
- Instrumentação de métricas específicas do radar (geradas, aceitas, implementadas, uplift).

## O que criar do zero
- Domínio do radar: `Opportunity`, `OpportunitySignal`, `OpportunityRecommendation`, `OpportunityFeedback`, `OpportunityRunLog`.
- Engine de detecção (5 detectores + scoring + priorização).
- Endpoints dedicados:
  - `GET /opportunities`
  - `GET /opportunities/:id`
  - `POST /opportunities/:id/feedback`
  - `POST /opportunities/recompute`
  - `GET /opportunities/summary`
- Worker batch interno (execução agendada) e execução manual controlada.
- Tela “Radar de Oportunidades” com explicabilidade e feedback.
- Documentação técnica/funcional do módulo + plano de rollout com feature flag.

## Riscos técnicos e mitigação
- Ausência de dados explícitos de fornecedor/canal/região:
  - Mitigação: inferências explícitas com hipótese registrada em cada recomendação e score de confiança penalizado.
- Custo de processamento em lote:
  - Mitigação: janela histórica configurável, paginação e métricas de tempo por run.
- Idempotência e duplicidade de oportunidades:
  - Mitigação: fingerprint por oportunidade + atualização/upsert por run.
- Adoção sem regressão:
  - Mitigação: feature flag + execução manual primeiro + ativação gradual por ambiente.
- Segurança de ações críticas:
  - Mitigação: validação de perfil no backend para aceitar/implementar recomendações e trilha de auditoria.
