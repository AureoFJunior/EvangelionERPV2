# 11 — Roadmap de implementação

## Fase 0 — Evidência e segurança imediata

1. Rotacionar o segredo local e executar secret scan.
2. Concluído localmente (2026-10-20): `dotnet list EvangelionERPV2.sln package --vulnerable --include-transitive` não encontrou pacotes vulneráveis nas fontes atuais; `dotnet build EvangelionERPV2.sln --no-restore --verbosity quiet` terminou com 0 erros e 368 warnings; `dotnet test EvangelionERPV2.sln --no-restore --no-build --verbosity quiet` passou em 3.408 testes, sem falhas ou skips. O scan reflete as fontes NuGet atuais e o teste não usou broker/banco reais. Tratar warnings fora de correções comprovadas em itens separados; revalidar após mudanças de dependências.
3. Triar/atualizar dependências sem alterar arquitetura.

## Fase 1 — Contrato de autorização

1. Implementados testes HTTP no endpoint MVC real `OrderController.GetOrders` e negociação real do `OrderHub`, com middleware tenant e handler RBAC reais: JWT ausente/inválido e mismatch de tenant → 401; role insuficiente → 403; role autorizada → controller/hub.
2. ERP-003 está concluído localmente: uma rota representativa de cada uma das 29 famílias MVC protegidas retorna 401 sem JWT e 403 para role sem permissões; sete endpoints dedicados também cobrem mismatch de tenant e acesso autorizado. OrderHub e AiHub completam negociação e handshake WebSocket/SignalR no TestServer. `ProductionJwtConfigurationTests` carrega o appsettings Production e testa o setup real, incluindo assinatura, issuer/audience e resolução simulada da referência Secrets Manager. Revisão operacional ainda deve confirmar o segredo real e WebSocket após deploy.
3. Token SelfAPI fica restrito às cinco actions worker explicitamente marcadas e não recebe permissões RBAC de role/grants; teste HTTP confirma a barreira de rota comum.
3. Redaction do `access_token` no logger da aplicação verificada com teste; proxy e Application Insights seguem pendentes operacionalmente.

## Fase 2 — Contrato cliente

1. Obter checkout frontend/Cypress e registrar commit/base.
2. Gerar matriz de chamadas, DTOs, erros, paginação e selectors.
3. Implementar somente regressões comprovadas e journeys P1.

## Fase 3 — Confiabilidade

1. Testar filas, redelivery, concorrência e transições de Order/PDV/NFe.
2. Introduzir idempotência somente onde o teste demonstrar duplicidade.
3. Medir queries antes de alterar índices/cache.

## Definition of done

- Evidência reproduzível, mudança mínima e revisão de segurança.
- Unit/integration/E2E apropriados executados e resultados registrados.
- Sem novos warnings vulneráveis conhecidos.
- Contrato e documentação atualizados sem apagar histórico necessário.

## Revisão Ponytail concluída

Foram rejeitados novos brokers, frameworks, bibliotecas de estado, camadas de autorização e refactors amplos. O backlog mantém somente correções localizadas, testes que fecham riscos reais e decisões que exigem evidência adicional.
