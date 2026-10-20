# 03 — Bugs e verificações

## Confirmados

### BUG-000 — Autorização de token de máquina é ampla por policy

- Classificação: `CONFIRMED`, `P1`/security. Ver [SEC-000](04-security-review.md#sec-000--token-selfapi-alcança-operações-globais-sem-permissão-rbac-adicional).
- Reprodução segura: usar somente token SelfAPI de ambiente de teste e chamar cada endpoint machine; não testar com segredo de produção.
- Fix mínimo: policies dedicadas por operação/escopo e testes negativos; preservar a integração existente.

### BUG-004 — Defaults/configuração de ambiente inconsistentes

- Classificação: `CONFIRMED`, `P1`/configuration. `docker-compose.yml:7-12` tem senha SA fallback; `Worker.Notification/appsettings.Production.json:15-23` aponta Redis `dev`.
- Fix mínimo: fail-fast para segredo obrigatório, corrigir referência prod e adicionar lint de configuração.

### BUG-001 — Dependências com avisos de vulnerabilidade no build

- Classificação: `CONFIRMED` como achado de supply chain; severidade operacional `P1` até triagem.
- Evidência inicial: NU1902/NU1903 para `SQLitePCLRaw.lib.e_sqlite3` e `System.Security.Cryptography.Pkcs`/`Xml`.
- Corrigido: a referência explícita a `SQLitePCLRaw.bundle_e_sqlite3` no projeto de testes que usava SQLite sem o bundle usa a versão central 3.0.3; Pkcs e Xml fixados em 10.0.13 com pinning transitivo central.
- Verificação: restore completo; nenhum dos IDs vulneráveis/versões afetadas aparece nos `project.assets.json`; `dotnet test EvangelionERPV2.sln --no-restore` passou em todos os projetos de teste. `dotnet list package --vulnerable` não concluiu por demora na consulta de auditoria NuGet.
- Fontes primárias: [SQLite advisory](https://github.com/advisories/GHSA-2m69-gcr7-jv3q), [Pkcs advisory](https://github.com/advisories/GHSA-846f-f764-76mx), [Xml advisory](https://github.com/advisories/GHSA-7hqp-7c5m-4vq6).
- Ponytail: **FIX** apenas para pacotes afetados; não trocar ORM, banco ou framework.

## Likely / runtime verification

### BUG-002 — Proteção de tenant precisa de teste HTTP de ponta a ponta

- Evidência: middleware faz consultas por IDs de claims (`ActiveTenantEnforcementMiddleware.cs:33-64`) e handlers repetem a checagem (`PermissionAuthorizationHandler.cs:19-42`); não há cliente/Cypress local para provar todos os controllers.
- Reprodução proposta: token A acessa IDs/queries da empresa B em cada família de endpoint; esperar 401/403 ou conjunto vazio conforme contrato.
- Status: controle básico agora tem teste HTTP local via `TestServer`: mismatch entre claim e empresa persistida resulta em 401; papel Employee sem `Orders.Update` resulta em 403; Supervisor autorizado recebe 200. Isto não valida todos os endpoints nem confirma BOLA.
- Próximo: estender a matriz aos controllers/hubs reais, validar JWT e executar testes contra banco/configuração de integração.

### BUG-003 — Segredo de desenvolvimento no arquivo ignorado

- Evidência: `.env.local` contém credencial local e é ignorado por `.gitignore:367`; não está rastreado (`git ls-files --error-unmatch .env.local` falhou).
- Impacto: exposição em máquina/backup local, não prova vazamento no Git.
- Ação mínima: revogar/rotacionar o valor local e garantir que exemplos usem placeholders; não imprimir o segredo em logs ou specs.
- Status: `REVIEW`. `.env.local` está ignorado e não aparece como arquivo no histórico local; seu conteúdo não foi lido, e não foi feito scan de segredos completo nem rotação.

## Não classificados como bug

Comentários `ponytail` em serviços e entidades são decisões/limites de escala, não evidência de falha. Devem virar testes/alertas quando o limite for atingido, não refatorações preventivas.

## Implementação e verificação

- Corrigido: removida referência duplicada ao projeto `User.Application` no Web.
- Corrigido: configuração Redis do `Worker.Notification` em Production agora aponta para o ambiente `prd`.
- Corrigido: Compose falha explicitamente quando `SQLSERVER_SA_PASSWORD` não é informado, sem senha padrão fraca.
- Corrigidos testes determinísticos: grants IAM usam o relógio injetado; testes de rate-limit que compartilham estado estático não executam em paralelo.
- Adicionado teste de middleware para claim de empresa diferente de `User.EnterpriseId`.
- Verificação: grants IAM/rate-limit corrigidos; teste HTTP novo prova os três resultados 401/403/200 do middleware e RBAC. `dotnet test EvangelionERPV2.sln --no-restore` passou em todos os projetos, incluindo 1.658 testes em `EvangelionERPV2.Test`.
- Dependências: SQLite bundle 3.0.3 e Pkcs/Xml 10.0.13; restore/build/testes passaram e versões vulneráveis identificadas não aparecem nos `project.assets.json`. `dotnet list package --vulnerable` ficou sem concluir por demora na consulta NuGet.
- Não foram executados endpoints reais da API com JWT, workers reais, banco/Redis/RabbitMQ, frontend ou Cypress. O token SelfAPI permanece compartilhado entre workers conforme decisão do usuário; suas permissões machine não foram segmentadas.
- ERP-007 verificado sem mudança de lógica: retries e redrives preservam o `MessageId`; a chave `(Handler, MessageId)` é única; `ProcessedMessage` é registrado dentro da transação que cria pedido, itens e atualiza estoque/saldo; redelivery vira resposta de sucesso no controller. Testes existentes cobrem índice, retenção e resposta HTTP a duplicata. Não foi executado broker/banco real.
- ERP-008 verificado sem mudança adicional de lógica: a referência duplicada do projeto Web já havia sido removida; testes cobrem que o filtro FluentValidation popula `ModelState`, que todos os DTOs de entrada têm validator (ou validação explícita), e que payload inválido mantém 400 com mensagem genérica. Testes direcionados passaram.
- ERP-000 restringido localmente: após autenticação, `SelfApiEndpointEnforcementMiddleware` nega ao token SelfAPI qualquer endpoint sem policy `machine:self-api-worker`; os cinco actions chamados pelos workers usam essa policy e a autorização RBAC por role/grants nega tokens machine. O login anônimo dos workers continua fora dessa allowlist porque não chega autenticado com token. Testes HTTP verificam máquina/usuário em endpoint comum e worker. Suíte completa passou (incluindo 1.671 testes no projeto principal); build da solução passou com 0 erros. Smoke test após deploy ainda pendente; credencial compartilhada mantém as cinco operações worker no mesmo blast radius.
- ERP-003 concluído localmente: a matriz TestServer valida uma rota real de cada família MVC protegida (29 no total), com JWT ausente (401) e JWT assinado para tenant ativo mas role sem permissões (403). Sete endpoints dedicados também verificam tenant divergente e acesso autorizado; OrderHub e AiHub completam handshake WebSocket SignalR com JWT por query. A configuração JWT foi exercitada a partir do appsettings Production e setup real, com Secrets Manager simulado; segredo real e caminho WebSocket implantado ficam para revisão operacional.
