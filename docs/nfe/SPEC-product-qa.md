# Especificação de Produto & QA — Emissão Fiscal Real (NFe mod. 55 / NFCe mod. 65) — SP, RS, SC, PR

**Versão:** 1.0 (draft para revisão)
**Autor:** Product Owner / QA
**Escopo:** backend `EvangelionERPV2` + contrato de API congelado para o app Expo (repo separado)
**Decisões travadas (não re-discutir):** Unimake.DFe, certificado A1 por empresa no AWS Secrets Manager, dados fiscais em campos do Product + perfil fiscal por empresa com fallback, frontend entregue como screen spec + contrato.

---

## 1. Problema & Resultado esperado

### 1.1 Quem está bloqueado hoje

| Persona | Bloqueio hoje |
|---|---|
| Dono/Admin de PME (varejo ou distribuição) | Não consegue faturar pelo ERP. Emite nota em um segundo sistema (emissor gratuito da SEFAZ, Bling, Tiny) e depois relança o pedido no Evangelion. Retrabalho duplo e divergência de estoque. |
| Vendedor / Supervisor | Fecha o pedido e não tem nada para entregar ao cliente. Precisa pedir a nota para o financeiro por WhatsApp. |
| Contador da empresa | Não recebe XML do Evangelion. Puxa os XMLs do outro emissor. O ERP é fonte de verdade contábil incompleta. |
| Cliente final | Recebe a nota por outro canal, ou não recebe. |

### 1.2 Por que o stub é inutilizável

`EvangelionERPV2.NFe.Application/Providers/StubNFeProvider.cs` fabrica uma chave de acesso de 44 dígitos com `RandomNumberGenerator` e devolve `Status = Authorized` com protocolo `HOMOLOG-<timestamp>`. Consequências:

1. **A chave não é uma chave.** Não contém UF, AAMM, CNPJ, modelo, série, número, tpEmis nem DV módulo-11. Nenhuma consulta na SEFAZ ou no portal nacional a resolve.
2. **O XML não é um XML de NFe.** É `<nfeStub>`. Não passa em nenhum validador, não abre em nenhum leitor de contador, não gera DANFE.
3. **Diz "Autorizado" sem ter falado com ninguém.** É o pior modo de falha possível: o usuário acredita que emitiu.
4. **Está desligado em produção** (`NFeSettings.Enabled = false`), e o `IssueAsync` retorna `null` → o endpoint devolve **204 No Content**. O usuário clica em "Emitir" e não acontece absolutamente nada, sem mensagem.
5. **`CancelAsync` cancela localmente** (`CancelProtocol = "CANCEL-<timestamp>"`) sem evento de cancelamento na SEFAZ e **sem verificar prazo legal**. Se ligado com um provider real por trás, cancelaria no banco uma nota que continua válida na SEFAZ.

### 1.3 Resultado esperado (definição de sucesso do usuário)

> Um usuário autorizado abre um pedido faturado, clica **Emitir NFe**, e em menos de 30 segundos vê **Autorizada**, com chave de acesso e protocolo reais; baixa o DANFE em PDF; envia por e-mail ao cliente com DANFE + XML anexos; e o contador consegue importar aquele XML.

### 1.4 Métricas de sucesso

| Métrica | Alvo v1 | Como medir |
|---|---|---|
| Taxa de autorização na 1ª tentativa (piloto, após 2 semanas) | ≥ 97% | `Authorized / (Authorized + Rejected)` por tenant |
| p95 de tempo até estado terminal (Autorizada ou Rejeitada) | ≤ 30 s | métrica Prometheus `nfe_issue_duration_seconds` |
| Notas presas em `Processing`/`AwaitingSefaz` > 15 min | 0 por semana | alerta operacional |
| Gaps na numeração por (empresa, modelo, série) | **0** | job de conciliação semanal |
| Tempo entre autorização e e-mail entregue | ≤ 2 min p95 | fila de e-mail |
| Chamados "minha nota travou" | < 1 a cada 500 notas | suporte |
| Chaves de acesso fabricadas em produção | **0** | stub removido do container de produção |
| Empresas piloto que desligaram o emissor paralelo em 30 dias | ≥ 1 de 1 | qualitativo |

---

## 2. Personas & Permissões

### 2.1 Papéis existentes

`EnumAccessLevel`: **Employee** < **Supervisor** < **Manager** < **Admin**. Admin recebe `RbacPermissions.All` (`EvangelionERPV2.Web/Security/RbacRolePermissionMap.cs:90`). O padrão de "admin-only" no repo é **omissão do mapa de papéis** — precedente literal em `RbacPermissions.Payroll` (`EvangelionERPV2.Web/Security/RbacPermissions.cs:183-185`, com o comentário explicando o motivo LGPD). Vamos seguir o mesmo padrão.

### 2.2 Matriz de permissões (alvo v1)

| Ação | Permissão | Employee | Supervisor | Manager | Admin |
|---|---|:--:|:--:|:--:|:--:|
| Ver configurações fiscais (perfil, CNPJ, IE, CSC, ambiente) | `fiscal.settings.read` | ✗ | ✗ | ✗ | ✅ |
| Editar configurações fiscais | `fiscal.settings.update` | ✗ | ✗ | ✗ | ✅ |
| Enviar / rotacionar certificado A1 | `fiscal.certificate.upload` | ✗ | ✗ | ✗ | ✅ |
| Ver status/validade do certificado (só metadados) | `fiscal.certificate.read` | ✗ | ✗ | ✅ | ✅ |
| Editar campos fiscais do produto (NCM, CFOP, CST, origem) | `products.update` (existente) | ✗ | ✅ | ✅ | ✅ |
| Emitir NFe (mod. 55) | `nfe.issue` | ✗ | ✅ | ✅ | ✅ |
| Emitir NFCe (mod. 65) | `nfe.issue` | ✗ | ✅ | ✅ | ✅ |
| Ver a nota e o status (lista + detalhe) | `nfe.read` | ✗ | ✅ | ✅ | ✅ |
| Baixar o DANFE (PDF) | `nfe.danfe.download` | ✗ | ✅ | ✅ | ✅ |
| **Baixar o XML** | `nfe.xml.download` | ✗ | ✗ | ✅ | ✅ |
| Enviar a nota por e-mail ao cliente | `nfe.email` | ✗ | ✅ | ✅ | ✅ |
| Cancelar uma nota | `nfe.cancel` | ✗ | ✗ | ✅ | ✅ |

Permissões novas: `fiscal.settings.read`, `fiscal.settings.update`, `fiscal.certificate.upload`, `fiscal.certificate.read`, `nfe.danfe.download`, `nfe.xml.download`, `nfe.email`.

### 2.3 Justificativa de cada escolha

**Configurações fiscais = admin-only, por omissão do mapa.**
Um erro de digitação no CNPJ, na IE, no CRT (regime tributário) ou no CFOP padrão gera nota rejeitada na melhor hipótese e **nota autorizada errada** na pior — que só se corrige com cancelamento dentro do prazo, ou com denúncia espontânea e retificação de apuração depois. É a mesma classe de risco do `payroll.settings.update` (altera todo pagamento futuro): mudança de configuração com efeito irreversível sobre documentos com valor legal. Além disso o perfil fiscal contém o **CSC da NFCe**, que é um segredo de emissão — quem tem o CSC + certificado pode emitir em nome da empresa.

**Certificado A1 = admin-only.**
O A1 é a identidade digital da pessoa jurídica. Quem faz upload pode substituí-lo; quem substitui pelo errado derruba a emissão da empresa inteira. Não há motivo operacional para um Manager tocar nisso — a rotação acontece uma vez por ano.
**Leitura de metadados do certificado (titular, CNPJ, validade, dias restantes) é liberada até Manager**, porque quem opera precisa saber que "faltam 12 dias para vencer" sem precisar acordar o Admin. Nunca se expõe a senha, o PFX, nem qualquer material de chave — só CN, CNPJ mascarado e datas.

**`nfe.issue` fica no Supervisor (como hoje), Employee não emite.**
Emitir é irreversível e consome número de sequência. Manter Employee fora preserva o comportamento atual e evita que um vendedor júnior queime numeração. **Tensão conhecida e aceita para a v1:** no cenário de balcão/PDV, quem opera a NFCe *é* o Employee. Se um tenant piloto exigir isso, entra na v1.1 como grant explícito `nfe.issue` no `EmployeePermissions` (mudança de uma linha), não como exceção no código. QA deve verificar que **hoje** Employee recebe 403.

**`nfe.cancel` sobe de Supervisor para Manager.**
Hoje `NFe.Cancel` está no tier Supervisor (`RbacRolePermissionMap.cs:40`). Cancelamento é irreversível, tem janela legal curta, e um cancelamento indevido dentro do prazo destrói uma nota válida sem volta. Alinha com o tratamento dado a `Orders.Delete` e `Customers.Delete`, que já vivem no Manager. **É uma redução de privilégio em relação ao comportamento atual** — precisa estar nas release notes e no changelog do tenant.

**Baixar DANFE = Supervisor. Baixar XML = Manager. Esta é a decisão LGPD central.**

O DANFE é a representação gráfica; o cliente já recebe uma cópia dele. O **XML é outra coisa**: é o documento estruturado completo, contendo — sempre e obrigatoriamente na NFe mod. 55 — o **CPF ou CNPJ do destinatário, nome completo/razão social, endereço completo com logradouro, número, complemento, bairro, município, UF e CEP**, além de e-mail e telefone quando informados. É um arquivo *machine-readable*, exfiltrável em massa, com dado pessoal identificável de todos os clientes da empresa. Um XML vale mais para um vazamento do que mil PDFs.

Por isso o XML é tratado como o repo já trata `Employees.*` e `Payroll.*`: **sobe um tier por causa do dado pessoal**, com a mesma justificativa registrada em comentário no `RbacPermissions`. Consequências obrigatórias:
- toda emissão de `GET .../DownloadXml` gera **entrada de auditoria** (`NFeDocument` precisa entrar em `EvangelionERPV2.Shared/Auditing/AuditedEntities.cs`, hoje não está);
- `NFeDocumentDTO.XmlContent` continua com `[JsonIgnore]` (já está, `EvangelionERPV2.Shared/DTOs/NFeDocumentDTO.cs:19`) — o XML **nunca** viaja num payload JSON, só como download binário dedicado;
- o endpoint de download entra na lista de endpoints sensíveis do `RequestLoggingMiddleware` para não logar corpo.

**Enviar por e-mail = Supervisor.**
O destinatário é fixo: o e-mail cadastrado do cliente do pedido. O usuário **não escolhe o destinatário livremente** — sem campo "para". Isso transforma "enviar XML por e-mail" numa operação de baixo risco de exfiltração (só vai para o titular do dado), o que permite liberá-la um tier abaixo do download do XML. Se em algum momento surgir "enviar para outro e-mail", essa variante sobe para Manager.

---

## 3. Histórias de usuário com critérios de aceite (Gherkin)

Convenção: cenários em português (é o idioma do domínio fiscal); rótulos de UI citados em pt-BR.

---

### US-01 — Como Admin, quero configurar os dados fiscais da minha empresa para poder emitir notas

```gherkin
Funcionalidade: Configurações fiscais da empresa

  Contexto:
    Dado que estou autenticado como Admin da empresa "Acme Distribuidora"

  Cenário: Primeira visita, empresa sem perfil fiscal
    Quando eu abro Configurações da Empresa > aba "Fiscal"
    Então vejo o formulário fiscal com todos os campos vazios
    E vejo o aviso "Configuração fiscal incompleta. Complete os campos obrigatórios para habilitar a emissão."
    E o ambiente vem pré-selecionado como "Homologação"
    E o botão "Salvar" está desabilitado até que um campo seja alterado

  Cenário: Salvar um perfil fiscal completo e válido
    Dado que estou na aba "Fiscal"
    Quando preencho CNPJ "12.345.678/0001-95"
    E preencho Inscrição Estadual "110.042.490.114"
    E seleciono UF "SP"
    E preencho CEP "01310-100", logradouro, número, bairro e município "São Paulo"
    E seleciono Regime Tributário "Simples Nacional"
    E preencho Série NFe "1" e Série NFCe "1"
    E preencho CFOP padrão de venda dentro do estado "5102"
    E clico em "Salvar"
    Então recebo a confirmação "Configurações fiscais salvas."
    E o aviso de configuração incompleta desaparece
    E o ambiente permanece "Homologação"

  Cenário: CNPJ com dígito verificador inválido
    Quando preencho CNPJ "12.345.678/0001-00"
    E saio do campo
    Então vejo o erro "CNPJ inválido. Verifique os dígitos."
    E o botão "Salvar" fica desabilitado

  Cenário: Inscrição Estadual incompatível com a UF
    Dado que a UF selecionada é "RS"
    Quando preencho Inscrição Estadual "110.042.490.114"
    E saio do campo
    Então vejo o erro "Inscrição Estadual inválida para o RS (esperado: 10 dígitos)."

  Cenário: Empresa isenta de Inscrição Estadual
    Quando marco "Isento de Inscrição Estadual"
    Então o campo Inscrição Estadual fica desabilitado e vazio
    E o formulário permite salvar

  Cenário: Manager tenta acessar as configurações fiscais
    Dado que estou autenticado como Manager
    Quando abro Configurações da Empresa
    Então a aba "Fiscal" NÃO aparece na lista de abas
    E uma chamada direta a GET /api/v1/Fiscal/GetSettings retorna 403

  Cenário: Não posso mudar para Produção com o perfil incompleto
    Dado que o CNPJ está preenchido mas não há certificado enviado
    Quando tento alternar o ambiente para "Produção"
    Então o seletor não muda
    E vejo "Para emitir em Produção é necessário: certificado A1 válido enviado; ao menos 1 nota autorizada em Homologação; CSC cadastrado (para NFCe)."
```

---

### US-02 — Como Admin, quero enviar e rotacionar o certificado A1

```gherkin
Funcionalidade: Certificado digital A1

  Cenário: Enviar um certificado A1 válido
    Dado que estou como Admin na aba "Fiscal" > cartão "Certificado Digital"
    Quando seleciono o arquivo "acme.pfx" (1,2 MB)
    E informo a senha do certificado
    E clico em "Enviar certificado"
    Então vejo "Certificado enviado com sucesso."
    E o cartão passa a exibir Titular, CNPJ mascarado ("12.***.***/0001-95"), Emissor, "Válido até 14/03/2027" e o selo verde "Válido"
    E a senha NÃO é exibida em nenhum momento após o envio
    E nenhum campo do cartão permite baixar o arquivo do certificado

  Cenário: Senha do certificado incorreta
    Quando envio um .pfx com a senha errada
    Então vejo "Senha do certificado incorreta. Verifique e tente novamente."
    E nada é gravado no cofre de segredos
    E o certificado anterior, se existir, permanece ativo

  Cenário: Arquivo não é um PFX
    Quando seleciono "contrato.pdf"
    Então o botão "Enviar certificado" permanece desabilitado
    E vejo "Selecione um arquivo .pfx ou .p12."

  Cenário: Certificado já vencido
    Quando envio um .pfx cuja validade terminou em 01/01/2025 e hoje é 29/07/2026
    Então vejo "Este certificado venceu em 01/01/2025 e não pode ser usado."
    E o certificado não é armazenado

  Cenário: CNPJ do certificado diferente do CNPJ da empresa
    Dado que o CNPJ da empresa é "12.345.678/0001-95"
    Quando envio um certificado cujo titular é "98.765.432/0001-10"
    Então vejo "O CNPJ do certificado (98.***.***/0001-10) não corresponde ao CNPJ da empresa. A SEFAZ rejeitará as notas."
    E o certificado não é armazenado

  Cenário: Rotação — substituir um certificado que ainda é válido
    Dado que existe um certificado ativo válido até 10/08/2026
    Quando envio um novo certificado válido até 10/08/2027
    Então vejo o diálogo "Substituir o certificado atual? O certificado anterior deixará de ser usado imediatamente."
    E ao confirmar, o cartão passa a mostrar a nova validade
    E notas emitidas antes continuam íntegras e consultáveis

  Cenário: Supervisor não vê o cartão de certificado
    Dado que estou como Supervisor
    Então não existe aba "Fiscal" e nenhum cartão de certificado é renderizado
```

---

### US-03 — Como usuário, quero ser avisado antes do certificado vencer

```gherkin
Funcionalidade: Aviso de vencimento do certificado

  Cenário: Selo verde
    Dado que faltam 45 dias para o vencimento
    Então o cartão exibe o selo verde "Válido — vence em 14/03/2027"
    E nenhum banner global é exibido

  Cenário: Selo âmbar a 30 dias
    Dado que faltam 30 dias ou menos para o vencimento
    Então o cartão exibe o selo âmbar "Vence em 28 dias"
    E um banner aparece no topo do app para Admin e Manager:
      "O certificado digital vence em 28 dias. Renove para não interromper a emissão de notas."

  Cenário: Selo vermelho a 7 dias
    Dado que faltam 7 dias ou menos
    Então o selo fica vermelho e o banner não pode ser dispensado por Admin

  Cenário: Certificado vencido bloqueia a emissão
    Dado que o certificado venceu ontem
    Quando abro um pedido e a seção Fiscal
    Então os botões "Emitir NFe" e "Emitir NFCe" estão desabilitados
    E a dica do botão diz "Certificado digital vencido. Peça ao administrador para enviar um novo."
    E uma tentativa direta de POST /NFe/Issue retorna 409 com code "CERTIFICATE_EXPIRED"
```

---

### US-04 — Como Supervisor, quero emitir a NFe de um pedido

```gherkin
Funcionalidade: Emissão de NFe (modelo 55)

  Contexto:
    Dado que a empresa tem perfil fiscal completo e certificado válido
    E que estou autenticado como Supervisor

  Cenário: Emissão autorizada
    Dado um pedido com cliente identificado (CPF/CNPJ e endereço completos) e 3 itens com NCM e CFOP definidos
    Quando abro o pedido e clico em "Emitir NFe"
    E confirmo o diálogo "Emitir NFe? Esta ação é irreversível — a nota será transmitida à SEFAZ e só poderá ser desfeita por cancelamento, dentro do prazo legal."
    Então o botão vira "Emitindo…" e fica desabilitado
    E em até 30 segundos o status exibido é "Autorizada"
    E vejo a chave de acesso formatada em 11 blocos de 4 dígitos
    E vejo o número, a série, o protocolo de autorização e a data/hora de autorização
    E as ações "Baixar DANFE", "Enviar por e-mail" e "Cancelar" ficam disponíveis

  Cenário: Cliente do pedido sem CPF/CNPJ
    Dado um pedido cujo cliente não possui documento cadastrado
    Então o botão "Emitir NFe" está desabilitado
    E a dica diz "O cliente precisa de CPF ou CNPJ cadastrado para emissão de NFe."
    E existe o atalho "Editar cliente"

  Cenário: Produto do pedido sem NCM
    Dado um pedido cujo item "Cabo HDMI 2m" não tem NCM
    Quando clico em "Emitir NFe"
    Então a emissão não é enviada à SEFAZ
    E vejo "Não foi possível emitir: 1 produto sem NCM — Cabo HDMI 2m."
    E existe o atalho "Corrigir produto"

  Cenário: Segunda tentativa de emissão do mesmo modelo no mesmo pedido
    Dado que o pedido já possui uma NFe Autorizada
    Então o botão "Emitir NFe" não é exibido
    E em seu lugar aparece o cartão da nota existente
    E um POST /NFe/Issue repetido retorna 409 com code "ALREADY_ISSUED" e o corpo da nota existente

  Cenário: Dois usuários clicam em "Emitir NFe" ao mesmo tempo
    Quando dois usuários emitem o mesmo pedido no mesmo segundo
    Então exatamente uma nota é criada
    E ambos veem a mesma chave de acesso
    E nenhum número de série é consumido duas vezes

  Cenário: Emissão em Homologação
    Dado que o ambiente da empresa é "Homologação"
    Quando emito uma NFe
    Então a nota é autorizada com o destinatário nomeado exatamente
      "NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL"
    E a tela exibe a tarja "HOMOLOGAÇÃO — SEM VALOR FISCAL" acima do cartão da nota
    E o DANFE gerado carrega a mesma tarja em marca d'água
```

---

### US-05 — Como Supervisor, quero emitir uma NFCe

```gherkin
Funcionalidade: Emissão de NFCe (modelo 65)

  Cenário: NFCe de venda de balcão sem identificação do consumidor
    Dado um pedido de R$ 180,00 sem cliente vinculado
    E que o CSC e o ID do CSC estão cadastrados para a UF da empresa
    Quando clico em "Emitir NFCe" e confirmo
    Então a nota é autorizada
    E o DANFE NFCe exibe "CONSUMIDOR NÃO IDENTIFICADO"
    E o DANFE NFCe exibe o QR Code e a URL de consulta da UF

  Cenário: NFCe acima do limite de identificação obrigatória
    Dado um pedido de R$ 12.400,00 sem cliente vinculado
    Quando clico em "Emitir NFCe"
    Então a emissão é bloqueada antes de ir à SEFAZ
    E vejo "Acima de R$ 10.000,00 a NFCe exige identificação do consumidor (CPF ou CNPJ)."
    E é oferecido "Informar CPF/CNPJ" ou "Emitir NFe no lugar"

  Cenário: CSC não cadastrado
    Dado que o CSC da empresa está vazio
    Então o botão "Emitir NFCe" está desabilitado
    E a dica diz "CSC não configurado. Peça ao administrador para cadastrar o CSC nas configurações fiscais."

  Cenário: NFCe com item de serviço
    Dado um pedido cujo item é um produto marcado como serviço (IsService = true)
    Então o botão "Emitir NFCe" está desabilitado
    E a dica diz "NFCe não pode conter serviços. Serviços exigem NFS-e municipal, fora do escopo do sistema."

  Cenário: NFCe para entrega em domicílio
    Dado um pedido com entrega em endereço do cliente
    Quando emito NFCe
    Então a identificação do consumidor com CPF/CNPJ e endereço é obrigatória
    E se ausente, vejo "Entrega em domicílio exige identificação e endereço do consumidor."
```

---

### US-06 — Como usuário, quero ver a nota e o seu status

```gherkin
Funcionalidade: Consulta da nota fiscal

  Cenário: Ver o cartão fiscal no pedido
    Dado um pedido com uma NFe Autorizada
    Quando abro o pedido
    Então a seção "Fiscal" exibe: modelo (NFe 55), número/série, status "Autorizada", data de autorização e chave de acesso formatada
    E "Ver detalhes" abre a gaveta com protocolo, ambiente e histórico de eventos

  Cenário: Pedido ainda sem nota
    Dado um pedido faturado e sem nota
    Quando abro o pedido
    Então a seção "Fiscal" exibe o estado vazio "Nenhuma nota emitida para este pedido."
    E exibe os botões "Emitir NFe" e "Emitir NFCe" conforme minha permissão

  Cenário: Nota rejeitada mostra o motivo em linguagem clara
    Dado uma nota rejeitada com cStat 229
    Então o status exibido é "Rejeitada"
    E vejo o texto "Inscrição Estadual do emitente inválida. Confira a IE nas configurações fiscais."
    E vejo, em texto secundário e menor, "SEFAZ 229 — Rejeição: IE do emitente inválida"
    E vejo o botão "Tentar novamente"

  Cenário: Consulta de nota de outra empresa
    Dado que estou na empresa A
    Quando consulto uma chave de acesso pertencente à empresa B
    Então recebo 404 e nenhum dado da nota

  Cenário: Lista de notas com filtros
    Quando abro "Notas Fiscais"
    Então vejo a lista paginada ordenada por data de emissão decrescente
    E posso filtrar por período, status, modelo (NFe/NFCe) e cliente
    E posso buscar por número ou por chave de acesso completa
```

---

### US-07 — Como usuário, quero baixar o DANFE em PDF

```gherkin
Funcionalidade: Download do DANFE

  Cenário: Baixar o DANFE de uma nota autorizada
    Dado uma NFe Autorizada
    Quando clico em "Baixar DANFE"
    Então recebo um PDF nomeado "NFe-000000123-serie1.pdf"
    E o PDF contém chave de acesso, código de barras da chave, protocolo, emitente, destinatário e itens

  Cenário: DANFE de nota cancelada
    Dado uma NFe Cancelada
    Quando baixo o DANFE
    Então o PDF traz a tarja "CANCELADA" e o protocolo de cancelamento

  Cenário: DANFE de nota rejeitada
    Dado uma nota Rejeitada
    Então a ação "Baixar DANFE" não é exibida
    E uma chamada direta retorna 409 com code "NOT_AUTHORIZED"

  Cenário: DANFE NFCe é resumido e tem QR Code
    Dado uma NFCe Autorizada
    Quando baixo o DANFE
    Então recebo o DANFE NFCe em formato cupom, com QR Code e URL de consulta da UF

  Cenário: Supervisor baixa DANFE mas não XML
    Dado que estou como Supervisor
    Então vejo "Baixar DANFE"
    E NÃO vejo "Baixar XML"
    E GET /NFe/DownloadXml retorna 403
```

---

### US-08 — Como usuário, quero enviar a nota por e-mail ao cliente

```gherkin
Funcionalidade: Envio da nota por e-mail

  Cenário: Envio bem-sucedido
    Dado uma NFe Autorizada de um pedido cujo cliente tem e-mail "cliente@exemplo.com.br"
    Quando clico em "Enviar por e-mail" e confirmo o diálogo que mostra o destinatário
    Então vejo "Nota enviada para cliente@exemplo.com.br."
    E o e-mail é enfileirado com dois anexos: o DANFE em PDF e o XML autorizado
    E o assunto é "Nota Fiscal 000000123 - Acme Distribuidora"
    E a gaveta da nota passa a exibir "Enviada em 29/07/2026 14:32"

  Cenário: Cliente sem e-mail
    Dado um pedido cujo cliente não tem e-mail
    Então "Enviar por e-mail" está desabilitado
    E a dica diz "O cliente não tem e-mail cadastrado."
    E existe o atalho "Editar cliente"

  Cenário: NFCe sem consumidor identificado
    Dado uma NFCe Autorizada sem cliente vinculado
    Então "Enviar por e-mail" não é exibido

  Cenário: Reenvio
    Dado uma nota já enviada
    Quando clico em "Reenviar"
    Então o envio é repetido e a data de último envio é atualizada
    E o histórico registra os dois envios

  Cenário: Envio de nota não autorizada
    Dado uma nota Rejeitada ou em processamento
    Então "Enviar por e-mail" não é exibido
    E uma chamada direta retorna 409 com code "NOT_AUTHORIZED"

  Cenário: Falha no provedor de e-mail
    Dado que o servidor SMTP está indisponível
    Quando envio a nota
    Então vejo "Não foi possível enviar agora. Tentaremos novamente automaticamente."
    E a nota permanece Autorizada e íntegra
```

---

### US-09 — Como Manager, quero cancelar uma nota dentro do prazo legal

```gherkin
Funcionalidade: Cancelamento de nota fiscal

  Cenário: Cancelamento de NFe dentro de 24 horas
    Dado uma NFe Autorizada há 3 horas
    E que estou autenticado como Manager
    Quando clico em "Cancelar nota"
    Então vejo o diálogo "Cancelar a NFe 000000123? Esta ação é irreversível. O número da nota não poderá ser reutilizado."
    E preciso informar a justificativa (mínimo 15, máximo 255 caracteres)
    Quando informo "Cliente desistiu da compra antes da entrega" e confirmo
    Então o status vai para "Cancelamento solicitado" e depois para "Cancelada"
    E vejo o protocolo de cancelamento e a data/hora
    E o pedido volta a exibir o estado apropriado

  Cenário: Justificativa curta demais
    Quando informo "erro" como justificativa
    Então o botão de confirmar fica desabilitado
    E vejo "A justificativa deve ter no mínimo 15 caracteres (exigência da SEFAZ)."

  Cenário: Fora do prazo — bloqueio no cliente
    Dado uma NFe Autorizada há 30 horas
    Então o botão "Cancelar nota" está desabilitado
    E a dica diz "Prazo de cancelamento encerrado (24 horas após a autorização). Use devolução ou nota de entrada."

  Cenário: Fora do prazo — bloqueio no servidor
    Quando envio POST /NFe/Cancel para uma nota autorizada há 30 horas
    Então recebo 409 com code "CANCEL_WINDOW_EXPIRED"
    E NENHUM evento é transmitido à SEFAZ
    E o status da nota permanece "Autorizada"

  Cenário: NFCe fora da janela de 30 minutos
    Dado uma NFCe Autorizada há 45 minutos
    Então "Cancelar nota" está desabilitado
    E a dica diz "Prazo de cancelamento da NFCe encerrado (30 minutos após a autorização)."

  Cenário: SEFAZ rejeita o cancelamento
    Dado uma NFe Autorizada há 20 horas
    Quando solicito o cancelamento e a SEFAZ responde cStat 501
    Então o status volta para "Autorizada"
    E vejo "A SEFAZ recusou o cancelamento: prazo excedido. A nota continua válida."
    E a nota NÃO fica marcada como cancelada no sistema

  Cenário: Nota já cancelada
    Quando cancelo uma nota já Cancelada
    Então recebo 409 com code "ALREADY_CANCELLED" e o corpo da nota
    E nenhum novo evento é transmitido

  Cenário: Supervisor não pode cancelar
    Dado que estou como Supervisor
    Então o botão "Cancelar nota" não é exibido
    E POST /NFe/Cancel retorna 403
```

---

### US-10 — Como usuário, quero entender por que a SEFAZ recusou

```gherkin
Funcionalidade: Motivo de rejeição legível

  Cenário: Rejeição mapeada
    Dado que a SEFAZ retorna cStat 778 (NCM inexistente)
    Então vejo o título "Rejeitada"
    E a mensagem "NCM inválido em um ou mais itens. Confira o NCM dos produtos do pedido."
    E o botão de ação "Corrigir produtos"
    E o rodapé técnico "SEFAZ 778 — Informado NCM inexistente"

  Cenário: Rejeição não mapeada
    Dado que a SEFAZ retorna um código fora do catálogo do app
    Então vejo "A SEFAZ recusou a nota."
    E o texto original devolvido pela SEFAZ, na íntegra
    E o código numérico
    E o botão "Copiar detalhes" para o suporte

  Cenário: SEFAZ indisponível
    Dado que a SEFAZ da UF está fora do ar (cStat 108/109) ou não responde
    Então o status é "Aguardando SEFAZ"
    E vejo "A SEFAZ está indisponível no momento. Vamos tentar novamente automaticamente — não emita de novo."
    E o botão "Emitir" permanece desabilitado enquanto o status for "Aguardando SEFAZ"

  Cenário: Timeout na transmissão
    Dado que a transmissão excede o tempo limite sem resposta
    Então o status é "Aguardando SEFAZ"
    E o sistema consulta a SEFAZ pela chave antes de qualquer nova tentativa
    E o usuário NUNCA vê um botão que permita reemitir enquanto o desfecho é desconhecido

  Cenário: Nota denegada
    Dado que a SEFAZ retorna cStat 302 (irregularidade fiscal do destinatário)
    Então o status é "Denegada"
    E vejo "A SEFAZ denegou a nota por irregularidade fiscal do destinatário. A numeração foi consumida e não pode ser reutilizada. Regularize a situação do cliente."
    E as ações "Cancelar" e "Tentar novamente" NÃO são exibidas
```

---

## 4. Regras de negócio que o QA precisa fazer valer

### RN-01 — Janela de cancelamento

| Documento | UF | Prazo de cancelamento | Situação da verificação |
|---|---|---|---|
| NFe (55) | SP, RS, SC, PR | **24 horas** contadas da autorização | Regra nacional, Ajuste SINIEF 07/2005 (redação vigente) e Manual de Orientação do Contribuinte. Confiável. |
| NFe (55) — cancelamento extemporâneo | SP, RS, SC, PR | Alguns estados admitem cancelamento após 24 h e até um limite (comumente citado como 480 h / 20 dias) mediante penalidade e/ou processo administrativo | **UNVERIFIED por UF.** Não implementar. |
| NFCe (65) | SP, RS, SC, PR | **30 minutos** contados da autorização | Prazo nacional de referência para NFCe. **UNVERIFIED por UF** — RS e PR já praticaram prazos distintos em versões anteriores da legislação. |

**Ação obrigatória antes do desenvolvimento:** o prazo deve ser **configurável por UF e por modelo** no perfil fiscal (não uma constante no código), com os defaults acima, e a lista precisa ser confirmada contra o RICMS vigente de SP, RS, SC e PR pelo contador do tenant piloto. Registrar a fonte (artigo e data de consulta) na documentação do módulo.

**Regra de QA:** o prazo é validado **duas vezes** — no cliente (desabilita o botão) e no servidor (`409 CANCEL_WINDOW_EXPIRED`), e o servidor **não transmite nada** à SEFAZ quando o prazo já passou. Um cliente adulterado não pode provocar uma tentativa fora do prazo. O relógio de referência é a **data/hora de autorização devolvida pela SEFAZ**, não o `CreatedAt` local nem o relógio do dispositivo.

**Por que importa:** cancelar fora do prazo não é possível. Depois da janela, a operação se desfaz por **devolução** (nota de entrada emitida pelo próprio emitente, ou nota de devolução emitida pelo cliente), não por cancelamento. Um sistema que finge cancelar cria uma divergência entre o livro fiscal e a base da SEFAZ que só aparece na apuração — meses depois.

### RN-02 — Uma nota autorizada por pedido por modelo

O índice único `(OrderId, Type)` em `EvangelionERPV2.Shared/Entities/NFeDocument.cs:21` já garante no máximo um registro por par. A regra de produto é mais forte: **um pedido pode ter no máximo uma NFe autorizada e uma NFCe autorizada, e emitir os dois modelos para o mesmo pedido é um erro operacional**, porque significa faturar a mesma venda duas vezes.

- Se o pedido já tem NFe Autorizada, "Emitir NFCe" fica indisponível, e vice-versa. Uma tentativa direta retorna `409 CONFLICTING_MODEL`.
- Uma nota **Rejeitada** não bloqueia: é permitido corrigir e reemitir sobre o mesmo registro (comportamento atual do `ResetErroredDocumentForRetry`). Uma nota **Cancelada** também não bloqueia — o pedido pode ser refaturado com **novo número**.
- Uma nota **Denegada bloqueia definitivamente** aquele par (pedido, modelo). Não há reemissão, não há cancelamento; a numeração foi consumida.

### RN-03 — Imutabilidade da nota autorizada

Uma vez autorizada, **nenhum campo fiscal do `NFeDocument` pode ser alterado**: chave, número, série, ambiente, protocolo, data de autorização, valor total e XML são imutáveis. As únicas transições permitidas são para `Cancelada` (que **adiciona** protocolo e justificativa de cancelamento, sem alterar os campos originais).

O XML autorizado é o documento legal, com assinatura digital. Alterar um byte invalida a assinatura. O QA deve confirmar que não existe nenhum caminho — nem admin, nem migração, nem "recalcular totais" — que reescreva o XML de uma nota autorizada.

**Consequência de retenção:** o XML autorizado deve ser guardado por **5 anos** (prazo decadencial do art. 173 do CTN; a prática de mercado adota 5 anos a contar do exercício seguinte). Isso conflita com o direito de eliminação do titular sob a LGPD — e **a LGPD prevalece a favor da guarda** (art. 16, I: conservação para cumprimento de obrigação legal). Um pedido de exclusão de cliente **não pode apagar** os XMLs já emitidos. O sistema hoje só faz soft delete (`IsActive = false`), o que é compatível; QA deve confirmar que desativar um cliente **não** remove nem torna irrecuperáveis suas notas.

### RN-04 — Numeração sem lacunas e monotônica

Para cada tripla **(empresa, modelo, série)**, o número da nota é um inteiro que cresce de 1 em 1, sem repetição e sem buraco.

- Um número só é consumido quando a nota é efetivamente **transmitida**. Falha antes da transmissão (validação local, certificado, produto sem NCM) **não** consome número.
- Um número **é** consumido por: Autorizada, Rejeitada, Denegada. Rejeição queima o número — e isso é normal e esperado.
- Buracos legítimos (números queimados por rejeição e não reaproveitados) exigem **inutilização de numeração** perante a SEFAZ para regularizar o livro. Inutilização está **fora do escopo v1** (seção 10), portanto a v1 deve **minimizar rejeições com validação local agressiva antes de transmitir**, e o relatório semanal de lacunas é a rede de segurança operacional.
- **Nunca reaproveitar um número.** Reemitir após rejeição usa um número novo.
- QA: teste de concorrência com 20 emissões simultâneas na mesma empresa/série → 20 números distintos e contíguos, zero duplicados.

### RN-05 — Homologação carrega a legenda obrigatória

Em ambiente de homologação (`tpAmb = 2`), o Manual de Orientação do Contribuinte exige que a **razão social do destinatário** seja exatamente:

```
NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL
```

(sem acentos, em caixa alta — a string exata deve ser conferida contra a versão vigente do MOC/NT no momento da implementação; para NFCe a NT usa a variante com "NFC-E"). O DANFE gerado em homologação carrega marca d'água equivalente, e a UI exibe a tarja em toda tela que mostre a nota.

**Regra de QA inegociável:** é impossível confundir uma nota de homologação com uma de produção em qualquer superfície — tela, PDF, e-mail, nome do arquivo. O e-mail de uma nota de homologação leva "[TESTE — SEM VALOR FISCAL]" no assunto. Idealmente, **o envio de e-mail para o cliente em homologação é bloqueado por padrão**, com override explícito só para o e-mail do próprio Admin.

### RN-06 — NFCe exige CSC e QR Code; consumidor é opcional abaixo do limite

- A NFCe só é emitível se a empresa tiver **CSC (Código de Segurança do Contribuinte)** e **ID do CSC (idToken)** cadastrados para a UF. Sem eles, o QR Code não pode ser assinado e a nota é rejeitada. O CSC é obtido no portal da SEFAZ da UF, e é **diferente por ambiente** (homologação e produção têm CSCs distintos). O perfil fiscal guarda os dois pares.
- Identificação do consumidor é **opcional** abaixo do limite legal. Acima dele é **obrigatória**. Valor de referência: **R$ 10.000,00**. **UNVERIFIED por UF** — confirmar em SP, RS, SC e PR; o limite deve ser um campo do perfil fiscal por UF, não uma constante.
- Identificação também é obrigatória, independentemente do valor, quando há **entrega em domicílio** (o endereço do destinatário passa a ser exigido).
- NFCe é para **consumidor final, operação presencial, dentro do estado**. Venda para outra UF, para contribuinte, ou com serviço no meio, é NFe. QA deve verificar o bloqueio nos três casos.
- O DANFE NFCe **precisa** carregar o QR Code e a URL de consulta da UF. Um DANFE NFCe sem QR Code é inválido para o consumidor.

### RN-07 — Pedido com nota autorizada não pode ser editado

Depois que uma nota é autorizada, o pedido que a originou congela nos campos que compõem a nota: itens, quantidades, valores unitários, valor total, cliente. O XML já foi assinado com aqueles valores; se o pedido mudar, o ERP e a SEFAZ passam a discordar sobre o que foi vendido.

- `PUT /Order/UpdateOrder` em pedido com nota Autorizada ou Denegada → `409 ORDER_LOCKED_BY_FISCAL_DOCUMENT`, com a mensagem "Este pedido tem nota fiscal autorizada e não pode ser alterado. Cancele a nota (dentro do prazo) para editar."
- Campos **não** fiscais permanecem editáveis: `PaymentScheduledDate`, `Payday`, observações internas.
- Se a nota for cancelada dentro do prazo, o pedido **destrava** e volta a ser editável.
- Uma nota Rejeitada **não** trava o pedido — pelo contrário, editar o pedido é o caminho normal de correção.
- QA: verificar que o bloqueio vive **no serviço de pedido**, não só na tela, e cobre também os caminhos por fila (`Worker.Order`, `InsertQueuedOrder`).

### RN-08 — O que acontece com a nota quando o pedido é estornado

O estorno de pedido já existe (`POST /Order/RefundOrder`, `RbacPermissions.Orders.Refund`). Interação obrigatória com a nota:

| Situação da nota no momento do estorno | Comportamento v1 |
|---|---|
| Sem nota | Estorno normal, sem efeito fiscal. |
| Nota Rejeitada / Erro | Estorno normal; a nota permanece no histórico como rejeitada. |
| Nota Autorizada, **dentro** da janela de cancelamento | O diálogo de estorno oferece **"Estornar e cancelar a nota fiscal"** como opção marcada por padrão. Se o cancelamento falhar na SEFAZ, o estorno **também não é aplicado** — os dois andam juntos, e o usuário vê o erro. |
| Nota Autorizada, **fora** da janela | O estorno é permitido, mas a tela exibe, e exige o reconhecimento explícito, do aviso: **"A nota fiscal 000000123 permanece válida na SEFAZ. Prazo de cancelamento encerrado. Emita uma nota de devolução ou solicite a devolução ao cliente — o sistema não faz isso automaticamente."** O pedido é marcado com a pendência fiscal "Estornado com nota fiscal ativa", visível na lista de notas como filtro. |
| Nota Cancelada | Estorno normal. |
| Nota Denegada | Estorno normal; a numeração continua consumida. |

A nota de devolução (emissão de entrada com CFOP de devolução) **está fora do escopo v1** e é executada manualmente pelo contador. A pendência fiscal existe precisamente para que essa lista manual seja visível e não se perca.

### RN-09 — Precedência de dados fiscais (Produto → Perfil da empresa)

Cada item da nota resolve seus dados fiscais nesta ordem: **campo do Produto → default do perfil fiscal da empresa → erro de validação**. Nunca há um valor "chutado" pelo sistema. Se nem o produto nem o perfil definem o CFOP, a emissão falha **localmente**, antes de consumir número, com a lista dos produtos afetados.

### RN-10 — Validação local antes de transmitir

Toda emissão passa por uma verificação local que impede a transmissão e **não consome número** quando faltam pré-requisitos: perfil incompleto, certificado ausente/vencido/CNPJ divergente, CSC ausente (NFCe), cliente sem documento (NFe), cliente sem endereço completo (NFe), produto sem NCM/CFOP/origem/unidade, valor total zerado ou negativo, pedido sem itens, pedido já com nota do outro modelo. Cada falha aponta o objeto exato e oferece um atalho para corrigi-lo.

---

## 5. Máquina de estados do documento fiscal

### 5.1 Estados e rótulos visíveis ao usuário

O enum atual (`NFeStatus`: Pending, Authorized, Cancelled, Error) é insuficiente — ele não distingue "a SEFAZ recusou" de "o nosso código explodiu", nem representa "não sabemos o desfecho". Estados alvo:

| Estado (interno) | Rótulo pt-BR | Rótulo en | Cor | Terminal? |
|---|---|---|---|---|
| `Draft` | Rascunho | Draft | cinza | não (não persistido em v1) |
| `Processing` | Em processamento | Processing | azul | não |
| `AwaitingSefaz` | Aguardando SEFAZ | Awaiting SEFAZ | âmbar | não |
| `Authorized` | Autorizada | Authorized | verde | não (pode ser cancelada) |
| `Rejected` | Rejeitada | Rejected | vermelho | não (pode ser reemitida) |
| `Denied` | Denegada | Denied | vermelho escuro | **sim** |
| `CancellationRequested` | Cancelamento solicitado | Cancellation requested | azul | não |
| `Cancelled` | Cancelada | Cancelled | cinza escuro | **sim** |
| `Error` | Erro no sistema | System error | vermelho | não (pode ser reemitida) |

`Error` significa exclusivamente falha nossa (rede, disco, bug) antes de haver certeza de transmissão. Falha **da SEFAZ com resposta** é `Rejected`. Falha **sem resposta** é `AwaitingSefaz` — nunca `Error`, porque `Error` sugere ao usuário que ele pode tentar de novo, e tentar de novo depois de um timeout é como se emite nota duplicada.

### 5.2 Tabela estado × evento → próximo estado

| Estado atual | Evento | Próximo estado | Efeito colateral |
|---|---|---|---|
| — | `IssueRequested` (validação local passou) | `Processing` | número consumido e reservado |
| — | `IssueRequested` (validação local falhou) | *(nenhum registro)* | 400 com a lista de problemas; nenhum número consumido |
| `Processing` | `SefazAuthorized` (cStat 100) | `Authorized` | grava chave, protocolo, XML autorizado, data SEFAZ; trava o pedido |
| `Processing` | `SefazRejected` (cStat ≠ 100, não denegação) | `Rejected` | grava cStat + xMotivo; número queimado; pedido continua editável |
| `Processing` | `SefazDenied` (cStat 110 / 301 / 302) | `Denied` | número queimado permanentemente; sem reemissão, sem cancelamento |
| `Processing` | `SefazUnavailable` (cStat 108 / 109) | `AwaitingSefaz` | agenda reconsulta com backoff |
| `Processing` | `Timeout` / sem resposta | `AwaitingSefaz` | **nunca** retransmite às cegas; agenda consulta pela chave |
| `Processing` | `InternalFailure` (exceção antes da transmissão) | `Error` | número liberado se comprovadamente não transmitido |
| `AwaitingSefaz` | `ConsultResolvedAuthorized` (100) | `Authorized` | idem autorização |
| `AwaitingSefaz` | `ConsultResolvedRejected` | `Rejected` | idem rejeição |
| `AwaitingSefaz` | `ConsultResolvedDenied` | `Denied` | idem denegação |
| `AwaitingSefaz` | `ConsultResolvedNotFound` (cStat 217 — chave não consta) | `Rejected` | seguro retransmitir; motivo "não chegou à SEFAZ" |
| `AwaitingSefaz` | `ConsultStillUnavailable` | `AwaitingSefaz` | novo backoff; após N tentativas, alerta operacional |
| `AwaitingSefaz` | `MaxRetriesExceeded` | `AwaitingSefaz` | **não** muda de estado; abre item de runbook |
| `Rejected` | `IssueRequested` (nova tentativa, validação local passou) | `Processing` | **novo** número; nunca reaproveita o queimado |
| `Error` | `IssueRequested` (nova tentativa) | `Processing` | idem |
| `Authorized` | `CancelRequested` (dentro do prazo) | `CancellationRequested` | transmite evento 110111 |
| `Authorized` | `CancelRequested` (fora do prazo) | `Authorized` | 409 `CANCEL_WINDOW_EXPIRED`; **nada é transmitido** |
| `CancellationRequested` | `SefazCancelHomologated` (cStat 135 / 155) | `Cancelled` | grava protocolo de cancelamento; destrava o pedido |
| `CancellationRequested` | `SefazCancelRejected` (ex.: 501, 420) | `Authorized` | mensagem de recusa; nota **permanece válida** |
| `CancellationRequested` | `Timeout` / sem resposta | `CancellationRequested` | reconsulta o evento; nunca marca cancelada sem protocolo |
| `Cancelled` | `CancelRequested` | `Cancelled` | 409 `ALREADY_CANCELLED`, idempotente |
| `Cancelled` | `IssueRequested` | `Processing` | permitido — refaturamento com **novo** número |
| `Denied` | qualquer | `Denied` | 409 `DENIED_TERMINAL` |
| qualquer | `EmailRequested` (não `Authorized` nem `Cancelled`) | inalterado | 409 `NOT_AUTHORIZED` |

### 5.3 Invariantes que o QA deve verificar

1. Nunca existe transição para `Cancelled` sem protocolo de cancelamento da SEFAZ. (O `CancelAsync` atual viola isso — `EvangelionERPV2.NFe.Application/Services/NFeService.cs`, `CancelProtocol = "CANCEL-<timestamp>"`.)
2. Nunca existe transição para `Authorized` sem protocolo e sem XML assinado.
3. `AwaitingSefaz` só sai por **consulta**, jamais por retransmissão.
4. `Denied` não tem nenhuma transição de saída.
5. Nenhum estado não-terminal fica > 15 minutos sem gerar alerta.

---

## 6. Especificação de telas — app Expo

Convenções assumidas do outro repo: módulos em `features/*`, HTTP central em `services/erpService.ts`, `Sidebar`, i18n pt-BR/en, Jest + RNTL + Cypress.

### 6.0 Máscaras e validações reutilizáveis

| Campo | Máscara | Validação |
|---|---|---|
| CNPJ | `99.999.999/9999-99` | 14 dígitos + DV módulo 11; rejeita todos-iguais |
| CPF | `999.999.999-99` | 11 dígitos + DV módulo 11; rejeita todos-iguais |
| CEP | `99999-999` | 8 dígitos; autopreenche endereço via consulta (opcional, degrada para manual) |
| IE — SP | `999.999.999.999` | 12 dígitos, DV específico de SP |
| IE — RS | `999/9999999` | 10 dígitos, DV específico de RS |
| IE — SC | `999.999.999` | 9 dígitos, DV específico de SC |
| IE — PR | `99999999-99` | 10 dígitos, DV específico de PR |
| IE (isento) | — | checkbox "Isento"; desabilita e limpa o campo |
| NCM | `99999999` | **exatamente 8 dígitos numéricos**; sem pontos |
| CEST | `9999999` | exatamente 7 dígitos; opcional, obrigatório quando o NCM é de ST |
| CFOP | `9999` | **exatamente 4 dígitos**; 1º dígito ∈ {1,2,3} entrada, {5,6,7} saída — em venda só aceita 5/6/7 |
| CSC | texto | 32–40 caracteres alfanuméricos; exibido mascarado após salvar |
| ID do CSC | `999999` | 1–6 dígitos |
| Série | `999` | 1–3 dígitos, 1–889 |
| Chave de acesso | `9999 9999 9999 …` (11 blocos de 4) | 44 dígitos; DV módulo 11 |
| Justificativa de cancelamento | texto | 15–255 caracteres |

Máscaras são de **apresentação**; a API sempre recebe e devolve **somente dígitos**.

---

### 6.1 Configurações da Empresa → aba **Fiscal**

**Gate:** a aba **não é renderizada** se o usuário não tiver `fiscal.settings.read`. Não é `disabled` — é ausente. Um Manager não deve nem saber que existe uma aba fiscal, porque saber que existe convida ao pedido "me dá acesso". O deep link `/enterprise/settings/fiscal` para não-admin redireciona para a aba padrão sem mensagem de erro (não vazar a existência do recurso).

**Seções:**

**A. Identificação fiscal**
| Campo | Tipo | Obrigatório | Regra |
|---|---|---|---|
| CNPJ | texto mascarado | sim | DV válido; imutável após a 1ª nota autorizada em produção (exibe cadeado + tooltip) |
| Inscrição Estadual | texto mascarado por UF | sim, salvo isento | DV por UF |
| Isento de IE | checkbox | — | desabilita e limpa a IE |
| Inscrição Municipal | texto | não | livre |
| Razão Social | texto | sim | 2–60 caracteres |
| Nome Fantasia | texto | não | ≤ 60 |
| Regime Tributário (CRT) | select | sim | Simples Nacional / Simples com excesso de sublimite / Regime Normal / MEI |
| CNAE principal | `9999-9/99` | não | 7 dígitos |

**B. Endereço fiscal** — substitui o campo `Adress` livre atual (`EvangelionERPV2.Shared/Entities/Enterprise.cs:24`), que **não serve** para nota fiscal: a NFe exige logradouro, número, complemento, bairro, código IBGE do município, nome do município, UF e CEP como campos separados.

| Campo | Obrigatório | Regra |
|---|---|---|
| CEP | sim | 8 dígitos |
| Logradouro | sim | 2–60 |
| Número | sim | 1–60; aceita "S/N" |
| Complemento | não | ≤ 60 |
| Bairro | sim | 2–60 |
| Município | sim | select alimentado pela UF; grava código IBGE |
| UF | sim | select limitado a **SP, RS, SC, PR** na v1, com o aviso "Outras UFs ainda não suportadas" |

**C. Séries e numeração**
| Campo | Obrigatório | Regra |
|---|---|---|
| Série NFe | sim | 1–889 |
| Próximo número NFe | sim | ≥ 1; editável só enquanto não houver nota autorizada naquela série; depois somente leitura com "Ajustar numeração" que abre confirmação de risco |
| Série NFCe | sim se NFCe habilitada | idem |
| Próximo número NFCe | sim se NFCe habilitada | idem |

**D. Padrões fiscais (fallback)**
| Campo | Obrigatório | Regra |
|---|---|---|
| CFOP padrão — venda dentro do estado | sim | 4 dígitos, começa com 5 |
| CFOP padrão — venda fora do estado | sim | 4 dígitos, começa com 6 |
| Origem da mercadoria padrão | sim | select 0–8 |
| CSOSN padrão (Simples) ou CST ICMS padrão (Normal) | sim | select conforme o CRT |
| Unidade tributável padrão | sim | select alinhado ao `EnumUnitOfMeasure` existente |

**E. NFCe**
| Campo | Obrigatório | Regra |
|---|---|---|
| Habilitar NFCe | switch | — |
| CSC — Homologação | sim se NFCe | mascarado após salvar (`••••••••1a2b`) |
| ID do CSC — Homologação | sim se NFCe | 1–6 dígitos |
| CSC — Produção | sim para ir a produção | idem |
| ID do CSC — Produção | idem | idem |
| Limite de identificação obrigatória | sim | moeda; default R$ 10.000,00; texto de apoio "Confirme o valor vigente na sua UF com o contador" |

**F. Ambiente**
Radio **Homologação / Produção**. Produção é bloqueada por checklist (ver 9.3), com a lista de itens pendentes visível e cada item linkado à seção que o resolve.

**Estados da tela:**
- *Loading:* skeleton de 6 linhas de campo; nenhum botão ativo.
- *Vazio (sem perfil):* formulário com defaults, banner âmbar "Configuração fiscal incompleta".
- *Erro de carregamento:* "Não foi possível carregar as configurações fiscais." + "Tentar novamente".
- *Salvando:* botão "Salvando…", formulário read-only.
- *Erro de validação do servidor:* erros mapeados campo a campo; banner com o resumo no topo.

**Botão "Salvar" desabilitado quando:** nada mudou; há erro de validação em qualquer campo; salvamento em andamento.

**Diálogos de confirmação:**
- Trocar de Homologação para Produção → *"Ativar emissão em Produção? As próximas notas terão valor fiscal e não poderão ser desfeitas fora do prazo legal de cancelamento."* [Cancelar] [Ativar produção]
- "Ajustar numeração" → *"Alterar o próximo número pode gerar lacunas ou duplicidades na sua numeração fiscal. Faça isso apenas com orientação do seu contador."* Exige digitar o novo número duas vezes.

---

### 6.2 Cartão **Certificado Digital** (dentro da aba Fiscal)

**Sem certificado:** ícone, "Nenhum certificado enviado", botão primário "Enviar certificado A1".

**Com certificado:** Titular (CN), CNPJ mascarado, Emissor, "Válido de … até …", selo de validade, botão secundário "Substituir certificado". **Nunca** há botão de download, e a senha nunca reaparece.

**Selo de validade:**
| Condição | Selo | Banner global |
|---|---|---|
| > 30 dias | verde "Válido — vence em DD/MM/AAAA" | nenhum |
| 8–30 dias | âmbar "Vence em N dias" | banner dispensável para Admin/Manager |
| 1–7 dias | vermelho "Vence em N dias" | banner **não** dispensável para Admin |
| vencido | vermelho "Vencido em DD/MM/AAAA" | banner não dispensável; emissão bloqueada |

**Modal de upload:** seletor de arquivo (`.pfx`, `.p12`, ≤ 5 MB) + campo de senha (`secureTextEntry`, sem autofill, sem "mostrar senha" persistente). Botão desabilitado até haver arquivo **e** senha. Barra de progresso durante o envio. Ao substituir um certificado válido, o diálogo de confirmação de substituição precede o envio.

**Erros de upload (cópia pt-BR):**
| Situação | Texto |
|---|---|
| Senha errada | "Senha do certificado incorreta. Verifique e tente novamente." |
| Arquivo inválido | "Arquivo inválido. Envie um certificado A1 nos formatos .pfx ou .p12." |
| Certificado vencido | "Este certificado venceu em DD/MM/AAAA e não pode ser usado." |
| CNPJ divergente | "O CNPJ do certificado (99.***.***/9999-99) não corresponde ao CNPJ da empresa. A SEFAZ rejeitará as notas." |
| Arquivo grande demais | "Arquivo muito grande. O limite é 5 MB." |
| Falha no cofre | "Não foi possível armazenar o certificado com segurança. Tente novamente em alguns instantes." |

---

### 6.3 Detalhe do Pedido → seção **Fiscal**

Posicionada abaixo dos itens. Renderizada só com `nfe.read`.

**Estado vazio:** "Nenhuma nota emitida para este pedido." + [Emitir NFe] [Emitir NFCe].

**Estado com nota:** cartão compacto — badge do modelo (NFe 55 / NFCe 65), chip de status colorido, número/série, data de autorização, chave de acesso truncada (`3525 0712 … 4471`) com botão de copiar a chave completa, e "Ver detalhes".

**Condições de desabilitação dos botões de emissão** (cada uma com tooltip própria — nunca um botão cinza mudo):

| Condição | Tooltip |
|---|---|
| Sem `nfe.issue` | botão **não renderizado** |
| Emissão desativada para a empresa | "A emissão de notas ainda não está ativada para esta empresa." |
| Perfil fiscal incompleto | "Configuração fiscal incompleta. Fale com o administrador." |
| Certificado ausente/vencido | "Certificado digital vencido ou não configurado." |
| Já existe nota do mesmo modelo (não rejeitada) | botão substituído pelo cartão da nota |
| Já existe nota do outro modelo autorizada | "Este pedido já foi faturado com NFCe." (ou NFe) |
| Pedido sem itens | "Adicione itens ao pedido antes de emitir." |
| Total ≤ 0 | "O valor total do pedido precisa ser maior que zero." |
| NFe: cliente sem CPF/CNPJ | "O cliente precisa de CPF ou CNPJ cadastrado." + atalho "Editar cliente" |
| NFe: cliente sem endereço completo | "O endereço do cliente está incompleto." + atalho |
| NFCe: sem CSC | "CSC não configurado nas configurações fiscais." |
| NFCe: valor acima do limite sem consumidor | "Acima de R$ 10.000,00 a NFCe exige CPF ou CNPJ do consumidor." |
| NFCe: pedido contém serviço | "NFCe não pode conter serviços." |
| Produto sem NCM/CFOP/origem | "N produto(s) sem dados fiscais." + atalho "Ver produtos" |
| Nota em `Processing`/`AwaitingSefaz` | "Emissão em andamento." + spinner |
| Pedido estornado | "Pedido estornado. Não é possível emitir nota." |

**Diálogo de confirmação de emissão (irreversível):**
> **Emitir NFe?**
> A nota será transmitida à SEFAZ e passará a ter valor fiscal. **Esta ação é irreversível** — depois de autorizada, só pode ser desfeita por cancelamento, dentro do prazo legal de 24 horas.
> Pedido #1234 · Cliente: Maria Silva · Total: R$ 1.240,00
> [Cancelar] [**Emitir NFe**]

Para NFCe, o prazo citado é 30 minutos. Em homologação, o diálogo é precedido pela tarja "HOMOLOGAÇÃO — SEM VALOR FISCAL" e o texto de irreversibilidade é substituído por "Ambiente de teste — a nota não terá valor fiscal."

---

### 6.4 Gaveta de detalhe da nota

Abre por cima da tela atual, largura 480 px em web, tela cheia em mobile.

**Cabeçalho:** modelo + número/série + chip de status. Tarja de homologação quando aplicável.

**Corpo:**
| Bloco | Conteúdo |
|---|---|
| Status | rótulo, data/hora do último evento, e — se rejeitada/denegada — a mensagem amigável + o texto original da SEFAZ em fonte menor |
| Identificação | chave de acesso em 11 blocos com copiar; número; série; modelo; ambiente; protocolo de autorização; data de autorização (fuso do usuário, com o UTC no tooltip) |
| Valores | valor total; quantidade de itens |
| Destinatário | nome; documento **mascarado** (`123.***.***-01`) com botão "Mostrar" que exige `nfe.xml.download` e **gera entrada de auditoria** |
| Cancelamento | quando cancelada: justificativa, protocolo, data/hora |
| Histórico | linha do tempo dos eventos: emitida, autorizada, e-mail enviado, cancelamento solicitado, cancelada — com autor e horário |

**Ações (barra inferior fixa):**
| Ação | Visível quando | Habilitada quando |
|---|---|---|
| Baixar DANFE | `nfe.danfe.download` | status ∈ {Autorizada, Cancelada} |
| Baixar XML | `nfe.xml.download` | status ∈ {Autorizada, Cancelada} |
| Enviar por e-mail | `nfe.email` | Autorizada **e** cliente com e-mail |
| Reenviar e-mail | `nfe.email` e já enviada | idem |
| Atualizar status | `nfe.read` | status ∈ {Processing, AwaitingSefaz, CancellationRequested} |
| Tentar novamente | `nfe.issue` | status ∈ {Rejeitada, Erro} |
| Cancelar nota | `nfe.cancel` | Autorizada **e** dentro do prazo (contador regressivo visível) |

**Contador de prazo:** enquanto Autorizada e dentro da janela, a barra de ações exibe "Cancelamento disponível por mais 21h 14min". Ao zerar, o botão desabilita sozinho sem recarregar a tela.

**Diálogo de cancelamento (irreversível):**
> **Cancelar a NFe 000000123?**
> **Esta ação é irreversível.** O número da nota será consumido e não poderá ser reutilizado. O cancelamento será registrado na SEFAZ.
> Justificativa (obrigatória, mínimo 15 caracteres) — [textarea, contador de caracteres]
> [Voltar] [**Cancelar nota**] *(destrutivo, vermelho, desabilitado até a justificativa ser válida)*

**Diálogo de envio de e-mail:**
> **Enviar a nota por e-mail?**
> Destinatário: maria@exemplo.com.br
> Anexos: DANFE (PDF) e XML da nota.
> [Cancelar] [Enviar]

O campo de destinatário é **somente leitura**. Sem "adicionar destinatário" na v1.

**Estados:** loading (skeleton); erro de carregamento com "Tentar novamente"; ação em andamento (botão com spinner, demais desabilitados); *offline* → "Sem conexão. As ações fiscais precisam de internet."

---

### 6.5 Lista **Notas Fiscais** (novo item de Sidebar)

Visível com `nfe.read`.

**Filtros:** período (padrão: mês corrente), status (multi-seleção), modelo (NFe/NFCe/todos), cliente (busca), ambiente (Homologação/Produção — só aparece se houver notas dos dois), e a chip especial **"Pendências fiscais"** (pedidos estornados com nota ativa, notas presas > 15 min).

**Busca:** número da nota ou chave de acesso completa (44 dígitos, aceita colada com espaços).

**Colunas (web) / linhas (mobile):** data de emissão · modelo · número/série · cliente · valor · status · ações rápidas (Ver, DANFE).

**Estados:**
- *Loading:* 10 linhas skeleton.
- *Vazio (sem filtro):* "Nenhuma nota fiscal emitida ainda." + link "Como emitir sua primeira nota".
- *Vazio (com filtro):* "Nenhuma nota encontrada com esses filtros." + "Limpar filtros".
- *Erro:* "Não foi possível carregar as notas." + "Tentar novamente".

**Exportar:** "Exportar XMLs do período" (ZIP) — exige `nfe.xml.download`, gera auditoria, limitado a 1 exportação por minuto por usuário, teto de 1.000 notas. É o caminho de exfiltração em massa mais óbvio; precisa de limite e rastro.

---

### 6.6 Cadastro de Produto → seção **Fiscal**

Dentro do formulário de produto existente, seção recolhível "Dados fiscais", visível com `products.update`.

| Campo | Obrigatório para emitir | Regra |
|---|---|---|
| NCM | sim (produto) | 8 dígitos |
| CEST | condicional | 7 dígitos |
| CFOP dentro do estado | não (cai no default) | 4 dígitos |
| CFOP fora do estado | não (cai no default) | 4 dígitos |
| Origem da mercadoria | sim | select 0–8 |
| CST/CSOSN ICMS | não (default) | select por CRT |
| CST PIS / CST COFINS | não (default) | select |
| Unidade tributável | sim | select |
| Alíquota ICMS (%) | não | 0–100, 2 casas |

Indicador visível na lista de produtos: chip âmbar **"Sem dados fiscais"** nos produtos incompletos, com filtro dedicado. Sem isso, o usuário só descobre o problema no momento de emitir.

---

### 6.7 Cópia pt-BR para as principais famílias de rejeição da SEFAZ

Cada card mostra: **título** (o rótulo do status), **mensagem amigável**, **ação sugerida** e, em texto secundário, `SEFAZ <cStat> — <xMotivo original>`. Os códigos numéricos abaixo são os de uso corrente; **devem ser reconferidos contra a versão vigente do MOC/NT na implementação**, e o mapeamento precisa ter fallback para código desconhecido.

| # | Família (cStat típico) | Mensagem ao usuário (pt-BR) | Ação |
|---|---|---|---|
| 1 | **204 / 539** — Duplicidade de NF-e | "Esta nota já foi enviada à SEFAZ. Estamos buscando o resultado da emissão anterior — não emita novamente." | *(automática: consulta pela chave)* |
| 2 | **217** — NF-e não consta na base da SEFAZ | "A nota não chegou à SEFAZ. Pode tentar emitir novamente com segurança." | Tentar novamente |
| 3 | **108 / 109** — Serviço paralisado | "A SEFAZ está fora do ar no momento. Vamos tentar automaticamente assim que voltar — não é necessário fazer nada." | *(automática)* |
| 4 | **110 / 301 / 302** — Uso denegado | "A SEFAZ denegou a nota por irregularidade fiscal do emitente ou do destinatário. A numeração foi consumida e a nota não pode ser cancelada nem reemitida. Regularize a situação e emita uma nova nota." | Ver cliente |
| 5 | **229** — IE do emitente inválida | "A Inscrição Estadual da empresa está inválida ou não habilitada para emissão. Confira nas configurações fiscais." | Configurações fiscais *(só Admin vê o atalho)* |
| 6 | **280 / 281 / 282 / 283** — Certificado inválido ou fora da validade | "O certificado digital está vencido ou inválido. Envie um certificado novo para voltar a emitir." | Falar com o administrador / Enviar certificado |
| 7 | **297 / 225** — Assinatura ou schema do XML inválido | "Houve um problema técnico ao montar a nota. Nossa equipe foi notificada. Tente novamente em alguns minutos." | Tentar novamente + Copiar detalhes |
| 8 | **778** — NCM inexistente | "O NCM de um ou mais produtos não existe na tabela oficial. Corrija o NCM dos produtos do pedido." | Ver produtos |
| 9 | **CFOP inválido / incompatível com a operação** *(família de rejeição de CFOP)* | "O CFOP usado não é válido para esta operação (venda dentro/fora do estado). Confira o CFOP do produto ou o padrão nas configurações." | Ver produtos |
| 10 | **CNPJ/CPF do destinatário inválido** *(família de rejeição de destinatário)* | "O CPF ou CNPJ do cliente está inválido. Corrija o cadastro do cliente e emita novamente." | Editar cliente |
| 11 | **228** — Data de emissão muito atrasada | "A data de emissão está fora do prazo aceito pela SEFAZ. Emita a nota novamente agora." | Tentar novamente |
| 12 | **656** — Consumo indevido | "Fizemos consultas demais à SEFAZ em pouco tempo. Aguarde alguns minutos antes de tentar de novo." | *(bloqueia botão por 5 min)* |
| 13 | **501 / 420** — Cancelamento fora do prazo / nota com evento impeditivo | "A SEFAZ recusou o cancelamento: o prazo já passou. A nota continua válida — trate por devolução." | Entender devolução |
| 14 | **Desconhecido** | "A SEFAZ recusou a nota." + texto original integral + código | Copiar detalhes / Falar com o suporte |

**Regras de escrita da cópia:** nunca expor jargão de XML (tag, namespace, cStat) na mensagem principal; sempre dizer o que fazer; nunca sugerir "tentar novamente" quando repetir causa duplicidade; nunca esconder o texto original da SEFAZ — o contador do cliente vai pedir por ele.

---

## 7. Contrato de API (congelado para o app Expo)

Convenção existente: `api/v{version}/{controller}/{action}`, JWT global, `enterpriseId` derivado da claim `GroupSid` — **nunca** enviado pelo cliente.

### 7.1 Configuração fiscal

| Verbo | Rota | Policy | Request | Response 200 |
|---|---|---|---|---|
| GET | `/api/v1/Fiscal/GetSettings` | `rbac:fiscal.settings.read` | — | `FiscalProfileDTO` |
| PUT | `/api/v1/Fiscal/UpdateSettings` | `rbac:fiscal.settings.update` | `FiscalProfileDTO` | `FiscalProfileDTO` |
| GET | `/api/v1/Fiscal/GetCertificateStatus` | `rbac:fiscal.certificate.read` | — | `CertificateStatusDTO` |
| POST | `/api/v1/Fiscal/UploadCertificate` | `rbac:fiscal.certificate.upload` | `multipart/form-data`: `file` (.pfx/.p12, ≤5 MB) + `password` | `CertificateStatusDTO` |
| GET | `/api/v1/Fiscal/GetReadiness` | `rbac:nfe.read` | — | `FiscalReadinessDTO` |

`GET /Fiscal/GetSettings` **sempre devolve 200**, com um objeto default quando não há linha — mesmo padrão de `PayrollService.GetSettingsAsync` (`EvangelionERPV2.Bills.Application/Services/PayrollService.cs:36`). O flag `isComplete` diz ao cliente se falta configuração.

```jsonc
// FiscalProfileDTO
{
  "cnpj": "12345678000195",              // só dígitos
  "stateRegistration": "110042490114",
  "stateRegistrationExempt": false,
  "municipalRegistration": "",
  "legalName": "Acme Distribuidora Ltda",
  "tradeName": "Acme",
  "taxRegime": "SimplesNacional",        // SimplesNacional | SimplesExcesso | RegimeNormal | MEI
  "cnae": "4649408",
  "address": { "zipCode":"01310100","street":"Av. Paulista","number":"1000",
               "complement":"Sala 12","district":"Bela Vista",
               "cityIbgeCode":"3550308","cityName":"São Paulo","state":"SP" },
  "nfeSeries": 1, "nfeNextNumber": 1,
  "nfceEnabled": true, "nfceSeries": 1, "nfceNextNumber": 1,
  "defaultCfopInState": "5102", "defaultCfopOutOfState": "6102",
  "defaultOrigin": "0", "defaultIcmsCode": "102", "defaultUnit": "Unit",
  "cscHomologationId": "000001", "cscHomologationMasked": "••••••1a2b",
  "cscProductionId": "", "cscProductionMasked": "",
  "nfceIdentificationThreshold": 10000.00,
  "nfeCancellationWindowMinutes": 1440,
  "nfceCancellationWindowMinutes": 30,
  "environment": "Homologation",         // Homologation | Production
  "issuanceEnabled": false,
  "isComplete": false,
  "missingFields": ["cnpj","certificate"]
}
```
No `PUT`, o CSC é enviado em claro; nas leituras **só volta mascarado**. Certificado e senha **nunca** aparecem em nenhum DTO.

```jsonc
// CertificateStatusDTO
{ "present": true, "subjectName": "ACME DISTRIBUIDORA LTDA:12345678000195",
  "cnpjMasked": "12.***.***/0001-95", "issuer": "AC Certisign RFB G5",
  "validFrom": "2026-03-14T00:00:00Z", "validTo": "2027-03-14T23:59:59Z",
  "daysUntilExpiry": 228, "state": "Valid" }   // Valid | ExpiringSoon | Expired | Missing | CnpjMismatch

// FiscalReadinessDTO  — o que o app usa para habilitar/desabilitar botões
{ "canIssueNfe": true, "canIssueNfce": false,
  "environment": "Homologation",
  "blockers": [ { "code":"CSC_MISSING", "scope":"NFCe",
                  "message":"CSC não configurado nas configurações fiscais." } ] }
```

### 7.2 Documentos fiscais

| Verbo | Rota | Policy | Request | Response |
|---|---|---|---|---|
| GET | `/api/v1/NFe/GetByOrder/{orderId}?type=` | `rbac:nfe.read` | — | `NFeDocumentDTO[]` (200) |
| GET | `/api/v1/NFe/List` | `rbac:nfe.read` | query: `pageNumber,pageSize,from,to,status[],type,customerId,search,environment` | `PaginatedResultDTO<NFeDocumentDTO>` (200) |
| GET | `/api/v1/NFe/Consult/{accessKey}` | `rbac:nfe.read` | — | `NFeDocumentDTO` (200) |
| POST | `/api/v1/NFe/Issue/{orderId}?type=` | `rbac:nfe.issue` | `{ "idempotencyKey": "uuid" }` | `NFeDocumentDTO` (200/202) |
| POST | `/api/v1/NFe/Refresh/{accessKey}` | `rbac:nfe.read` | — | `NFeDocumentDTO` (200) |
| POST | `/api/v1/NFe/Cancel/{accessKey}` | `rbac:nfe.cancel` | `{ "reason": "…15–255…" }` | `NFeDocumentDTO` (200) |
| GET | `/api/v1/NFe/DownloadDanfe/{accessKey}` | `rbac:nfe.danfe.download` | — | `application/pdf` (200) |
| GET | `/api/v1/NFe/DownloadXml/{accessKey}` | `rbac:nfe.xml.download` | — | `application/xml` (200) |
| POST | `/api/v1/NFe/SendByEmail/{accessKey}` | `rbac:nfe.email` | — | `{ "queued": true, "recipient": "m***@exemplo.com.br" }` (202) |

```jsonc
// NFeDocumentDTO (resposta) — XmlContent NUNCA presente
{
  "id": "…", "orderId": "…",
  "type": "NFe",                       // NFe | NFCe   (string, não int — ver 7.4)
  "status": "Authorized",              // ver 5.1
  "statusLabel": { "pt-BR": "Autorizada", "en": "Authorized" },
  "accessKey": "35250712345678000195550010000001231000000017",
  "number": "000000123", "series": "1",
  "environment": "Homologation",
  "protocol": "135250000012345",
  "issuedAt": "2026-07-29T14:31:02Z",
  "authorizedAt": "2026-07-29T14:31:07Z",
  "totalValue": 1240.00,
  "sefaz": { "code": "100", "message": "Autorizado o uso da NF-e" },
  "userMessage": null,
  "cancellation": { "reason": null, "protocol": null, "at": null,
                    "allowedUntil": "2026-07-30T14:31:07Z", "canCancel": true },
  "email": { "lastSentAt": null, "recipientMasked": "m***@exemplo.com.br", "canSend": true },
  "createdAt": "…", "updatedAt": "…"
}
```

### 7.3 Códigos de status e significado

| Código | Significado neste contrato |
|---|---|
| 200 | Sucesso com corpo |
| 202 | Aceito; ainda em processamento (emissão assíncrona, e-mail enfileirado). O corpo traz `status: "Processing"`; o cliente faz polling em `Refresh` |
| 400 | Payload inválido ou pré-requisito local não atendido. **Corpo com lista de problemas** (ver 7.5) |
| 401 | Sem token / token inválido / sem claim de empresa |
| 403 | Autenticado mas sem a permissão RBAC |
| 404 | Pedido, nota ou chave não existe **nesta empresa** (deliberadamente indistinguível de "existe em outra empresa") |
| 409 | Conflito de estado — sempre com `code` legível (lista em 7.5) |
| 422 | A SEFAZ recusou (rejeição/denegação). Corpo com `sefaz.code`, `sefaz.message` e `userMessage` |
| 429 | Rate limit local, ou repasse de consumo indevido da SEFAZ |
| 503 | Emissão desabilitada para o tenant, ou SEFAZ indisponível. Header `Retry-After` |

### 7.4 ⚠️ Problemas do contrato atual que vão confundir o cliente — e a correção proposta

**(a) 204 significa três coisas diferentes e incompatíveis.** Em `EvangelionERPV2.Web/Controllers/NFeController.cs`, `Issue` devolve **204** quando: o pedido não existe (`order == null`), quando o `NotFoundDatabaseException` é capturado, **e** quando `IssueAsync` retorna `null` porque `NFeSettings.Enabled == false`. `GetByOrder` devolve 204 tanto para "pedido não existe" quanto para "não há nota". O app não consegue distinguir *"o pedido sumiu"*, *"emissão desligada"* e *"ainda não tem nota"* — três situações com três telas completamente diferentes. Hoje, com a emissão desligada em produção, o efeito é: **o botão não faz nada e não diz nada.**

> **Correção obrigatória, e é o item de maior impacto de UX desta especificação:**
> - `GetByOrder`: pedido inexistente → **404**; pedido sem nota → **200 com array vazio** (não 204). Coleção vazia é um resultado legítimo, não uma ausência de conteúdo.
> - `Issue` com emissão desligada → **503** com `code: "ISSUANCE_DISABLED"` e a mensagem "A emissão de notas ainda não está ativada para esta empresa."
> - `Issue`/`Consult`/`Cancel` com pedido/nota inexistente → **404** com `code: "NOT_FOUND"`.
> - **204 desaparece completamente** do `NFeController`. Nenhum endpoint deste contrato retorna 204.
>
> É uma quebra de contrato em relação ao comportamento hoje publicado. Como o consumidor é um único app interno e o endpoint está efetivamente inerte em produção (flag desligado, resposta vazia), **a quebra é aceita em `v1`** em vez de criar uma `v2` para consertar um erro. Deve ser coordenada com o release do app e registrada no changelog.

**(b) `type` e `status` viajam como inteiros do enum.** `NFeDocumentType`/`NFeStatus` serializam como `1`/`2`. Reordenar o enum quebra o app silenciosamente. **Correção: serializar como string** (`"NFe"`, `"Authorized"`) neste controller.

**(c) `Consult` recebe a chave de acesso na URL.** O `RequestLoggingMiddleware` já redige o segmento (`SanitizeEndpointPath`, linhas 208-209) — bom. Mas a chave ainda transita em URL, e URL vaza em proxy, histórico e APM de terceiros. **Correção mínima v1:** manter a redação existente e **estendê-la** para `/NFe/DownloadXml/`, `/NFe/DownloadDanfe/`, `/NFe/SendByEmail/` e `/NFe/Refresh/`, que são rotas novas e hoje não estão na lista. Migrar a chave para o corpo fica para v2.

**(d) `GetByOrder` devolve um objeto, mas um pedido pode ter duas notas** (NFe e NFCe — o índice único é por `(OrderId, Type)`). O contrato atual força o cliente a fazer duas chamadas. **Correção: devolver array.**

**(e) Emissão é síncrona e pode levar > 30 s.** Um app mobile em rede ruim vai estourar timeout e o usuário vai clicar de novo. **Correção: `Issue` aceita `idempotencyKey`** e pode responder **202** com `status: "Processing"`; o app faz polling em `Refresh`. Sem isso, a proteção contra duplo clique fica dependendo de um `SemaphoreSlim` estático em memória (`NFeService.IssuanceLocks`), que **não funciona com mais de uma instância da API** — hoje há um lock por processo, e a defesa real é o índice único no banco. QA deve testar isso com 2 réplicas.

**(f) `Issue` devolve 200 mesmo quando a SEFAZ rejeita.** O documento existe e foi persistido com status de rejeição, então "200 com status Rejected" é defensável; mas o app precisa de um sinal claro. **Correção: 422** quando o desfecho é rejeição/denegação, com o mesmo corpo `NFeDocumentDTO`. Reserva-se 200 para autorização e 202 para em andamento.

### 7.5 Corpo de erro padronizado

```jsonc
{
  "code": "CANCEL_WINDOW_EXPIRED",
  "message": "Prazo de cancelamento encerrado (24 horas após a autorização).",
  "details": [ { "field":"products[2].ncm", "code":"NCM_MISSING",
                 "message":"NCM não informado", "ref":"Cabo HDMI 2m" } ],
  "sefaz": { "code":"501", "message":"Rejeicao: Prazo de cancelamento superior ao previsto" }
}
```

Catálogo de `code` (congelado): `ISSUANCE_DISABLED`, `FISCAL_PROFILE_INCOMPLETE`, `CERTIFICATE_MISSING`, `CERTIFICATE_EXPIRED`, `CERTIFICATE_CNPJ_MISMATCH`, `CSC_MISSING`, `ALREADY_ISSUED`, `CONFLICTING_MODEL`, `ISSUE_IN_PROGRESS`, `ORDER_NOT_FOUND`, `ORDER_EMPTY`, `ORDER_REFUNDED`, `ORDER_LOCKED_BY_FISCAL_DOCUMENT`, `CUSTOMER_DOCUMENT_MISSING`, `CUSTOMER_ADDRESS_INCOMPLETE`, `PRODUCT_FISCAL_DATA_MISSING`, `NFCE_THRESHOLD_REQUIRES_CUSTOMER`, `NFCE_SERVICE_NOT_ALLOWED`, `NOT_AUTHORIZED`, `ALREADY_CANCELLED`, `DENIED_TERMINAL`, `CANCEL_WINDOW_EXPIRED`, `CANCEL_REJECTED_BY_SEFAZ`, `SEFAZ_UNAVAILABLE`, `SEFAZ_REJECTED`, `EMAIL_RECIPIENT_MISSING`, `RATE_LIMITED`, `NOT_FOUND`.

O app **nunca** decide comportamento com base em `message` — só em `code`. `message` é para exibir; a tradução para en é responsabilidade do i18n do app, usando `code` como chave.

---

## 8. Plano de testes

### 8.1 Divisão

**Unitário** (xUnit + Moq, `EvangelionERPV2.NFeModule.Test` — projeto novo, seguindo o padrão dos demais `*Module.Test`) — sem rede, sem SEFAZ, sem banco real:
1. Validação de CNPJ, CPF, IE (SP/RS/SC/PR), NCM, CFOP, CEP — válidos, inválidos, todos-iguais, tamanho errado, com e sem máscara.
2. Cálculo do DV da chave de acesso (módulo 11) e composição da chave a partir de UF + AAMM + CNPJ + modelo + série + número + tpEmis + cNF.
3. Resolução fiscal Produto → perfil → erro (cada campo, cada nível).
4. Máquina de estados: **toda** célula da tabela 5.2, inclusive as transições proibidas (que devem lançar/rejeitar).
5. Cálculo da janela de cancelamento: 23h59, 24h00, 24h01 (NFe); 29min, 30min, 31min (NFCe); horário de verão e fuso; relógio de referência = data SEFAZ.
6. Mapeamento cStat → mensagem amigável, incluindo o fallback para código desconhecido.
7. Regra do limite de identificação da NFCe: abaixo, exatamente no limite, acima.
8. Legenda de homologação: string exata no destinatário quando `tpAmb=2`, ausente quando `tpAmb=1`.
9. Alocação de numeração: sequencial, sem gap, sem reuso; liberação em falha pré-transmissão; retenção em rejeição.
10. Validação local completa: cada blocker da tabela 6.3 produz o `code` e o `ref` corretos, e **nenhum** consome numeração.
11. `NFeDocumentDTO` nunca serializa `XmlContent` (teste de serialização, não de reflexão).
12. Bloqueio de edição de pedido com nota autorizada (RN-07) em **todos** os caminhos de escrita de pedido.

**Integração** (banco real via container, SEFAZ falsa) — a SEFAZ é substituída por um duplo que responde envelopes gravados:
1. Emissão completa feliz: NFe e NFCe, persistência, DTO, auditoria.
2. Concorrência: 20 requisições simultâneas de emissão no mesmo pedido → 1 nota, 19 conflitos, 0 números duplicados. Repetir com **2 instâncias da API** para provar que a defesa não depende do lock em memória.
3. Idempotência: mesma `idempotencyKey` 3 vezes → 1 nota, 3 respostas iguais.
4. Timeout: duplo não responde → `AwaitingSefaz`; consulta subsequente resolve para Authorized; **verificar que não houve retransmissão**.
5. Duplicidade (204/539): duplo responde duplicidade → sistema consulta pela chave e converge para Authorized sem criar segundo registro.
6. Cancelamento: dentro do prazo → Cancelled com protocolo; fora do prazo → 409 e **nenhuma** chamada ao duplo (asserção sobre o duplo, não sobre a resposta).
7. Cancelamento recusado pela SEFAZ → volta a Authorized, não fica Cancelled.
8. Isolamento multi-tenant: empresa A não lê, não baixa, não cancela e não envia por e-mail nada da empresa B — 404 em todos os casos, para todos os 9 endpoints.
9. RBAC: matriz completa da seção 2.2 × 9 endpoints × 4 papéis = 36 asserções de 200/403.
10. Download: DANFE é PDF válido e abre; XML é XML válido e valida contra o schema da NFe; nomes de arquivo corretos.
11. E-mail: mensagem enfileirada com 2 anexos, destinatário = e-mail do cliente do pedido, assunto correto, tarja de homologação quando aplicável.
12. Numeração: 100 emissões sequenciais → 1..100 sem lacuna; intercalar 10 rejeições → numeração continua monotônica sem reuso.
13. Estorno: cada linha da tabela RN-08.

**QA manual contra a homologação da SEFAZ** — ver 8.2. Não automatizável na v1 e não deve entrar em CI: a homologação da SEFAZ cai, muda e tem rate limit.

### 8.2 Casos de homologação por UF × modelo

Autorizadores por UF **precisam ser confirmados no portal nacional (Serviços por UF) na semana da implementação** — a lista muda. Expectativa: SP, PR e RS com autorizador próprio; SC atendido pela SVRS. Cada UF tem CSC próprio e endpoints próprios.

Para **cada** uma das 8 combinações (SP, RS, SC, PR) × (NFe 55, NFCe 65):

| # | Caso | Resultado esperado |
|---|---|---|
| H-01 | Status do serviço da UF | Serviço em operação |
| H-02 | Venda simples, 1 item, consumidor final, dentro do estado | Autorizada (100) |
| H-03 | Venda com 3 itens, unidades diferentes, desconto | Autorizada; totais batem centavo a centavo |
| H-04 | Venda com item de valor fracionário (0,004 Kg — o repo já usa `decimal(18,4)` para quantidade) | Autorizada; sem truncamento |
| H-05 | *(NFe)* Venda interestadual para contribuinte | Autorizada com CFOP 6xxx |
| H-06 | *(NFe)* Venda interestadual para não-contribuinte | Autorizada; DIFAL tratado ou explicitamente fora de escopo |
| H-07 | *(NFCe)* Sem identificação do consumidor | Autorizada; DANFE com QR Code válido e "CONSUMIDOR NÃO IDENTIFICADO" |
| H-08 | *(NFCe)* Com CPF do consumidor | Autorizada; CPF no XML e no DANFE |
| H-09 | *(NFCe)* QR Code escaneado no portal da UF | Portal exibe a nota corretamente |
| H-10 | Legenda de homologação no destinatário | String exata presente; DANFE com marca d'água |
| H-11 | Consulta da nota pela chave, após autorização | Retorna autorizada, com o protocolo correto |
| H-12 | Cancelamento dentro do prazo | Evento homologado (135); protocolo gravado |
| H-13 | Cancelamento fora do prazo | Recusado (501); nota permanece válida |
| H-14 | DANFE gerado é aceito por um leitor de terceiros | PDF válido; chave legível em código de barras |
| H-15 | XML importado no validador do contador | Importa sem erro |
| H-16 | Numeração após 10 emissões consecutivas | 1..10, sem lacuna |

Total: **8 × ~16 = ~120 execuções manuais**, com evidência (print da tela + XML + protocolo) arquivada por combinação. Isso é uma semana de QA dedicada; planejar como tal.

### 8.3 Testes negativos (executar em pelo menos 2 UFs, SP e uma da SVRS)

| # | Cenário | Esperado |
|---|---|---|
| N-01 | Certificado vencido | Emissão bloqueada localmente; nunca chega à SEFAZ; `409 CERTIFICATE_EXPIRED` |
| N-02 | Certificado de outro CNPJ | Bloqueado no upload; se forçado, rejeição da SEFAZ tratada e legível |
| N-03 | CNPJ da empresa errado (dígito trocado) | Rejeitado no formulário; se salvo por API, rejeição da SEFAZ legível |
| N-04 | IE inválida | Rejeição 229 com mensagem amigável e atalho para configurações |
| N-05 | NCM inexistente (`00000000`) | Rejeição 778 com mensagem apontando o produto |
| N-06 | CFOP incompatível (5102 em venda interestadual) | Rejeição legível |
| N-07 | Produto sem NCM | **400 local**, nenhuma transmissão, **nenhum número consumido** |
| N-08 | Cliente sem CPF/CNPJ (NFe) | 400 local com atalho |
| N-09 | SEFAZ offline (bloquear rede) | `AwaitingSefaz`; retry automático; botão de emitir bloqueado |
| N-10 | Timeout de rede no meio da transmissão | `AwaitingSefaz`; **consulta**, nunca retransmissão |
| N-11 | Emissão duplicada (2 abas, 2 cliques) | 1 nota; a segunda recebe 409 com a nota existente |
| N-12 | Cancelar após a janela | 409, nenhuma chamada à SEFAZ, nota permanece válida |
| N-13 | Cancelar nota já cancelada | 409 idempotente |
| N-14 | Cancelar nota denegada | 409 `DENIED_TERMINAL` |
| N-15 | E-mail de nota não autorizada | 409 `NOT_AUTHORIZED` |
| N-16 | E-mail com cliente sem endereço de e-mail | 409 `EMAIL_RECIPIENT_MISSING`, botão já desabilitado na UI |
| N-17 | Editar pedido com nota autorizada | 409 `ORDER_LOCKED_BY_FISCAL_DOCUMENT` |
| N-18 | Pedido estornado, tentar emitir | 409 `ORDER_REFUNDED` |
| N-19 | Valor total zero | 400 local |
| N-20 | Rajada de 50 consultas em 1 minuto | Rate limit local antes de disparar consumo indevido (656) na SEFAZ |

### 8.4 Testes LGPD (obrigatórios, bloqueiam o release)

| # | Verificação | Como testar |
|---|---|---|
| L-01 | XML nunca aparece em resposta JSON | Varrer o corpo de **todos** os endpoints do controller procurando `<NFe`, `<infNFe`, `<dest` |
| L-02 | Corpo de requisição/resposta dos endpoints fiscais não é logado | Emitir com log em Debug; grep no arquivo de log por CPF, CNPJ do cliente, chave, CSC, senha do certificado |
| L-03 | Chave de acesso redigida no caminho logado | Confirmar que `/NFe/DownloadXml/…`, `/DownloadDanfe/…`, `/SendByEmail/…`, `/Refresh/…` foram **adicionados** à redação — hoje só `Consult` e `Cancel` estão (`RequestLoggingMiddleware.cs:208-209`) |
| L-04 | CSC e senha do certificado nunca em log, nunca em resposta, nunca em mensagem de erro | grep global; provocar erro com senha errada e inspecionar |
| L-05 | Download de XML gera entrada de auditoria | Exige adicionar `NFeDocument` a `AuditedEntities` (`EvangelionERPV2.Shared/Auditing/AuditedEntities.cs`) — hoje **não está** |
| L-06 | Emissão e cancelamento geram auditoria com autor, horário, IP | Consultar a trilha via `Audit` |
| L-07 | Alteração de configuração fiscal gera auditoria | Exige adicionar `Enterprise`/`FiscalProfile` ao allowlist — hoje **não estão** |
| L-08 | Não-admin não lê configuração fiscal | Supervisor e Manager → 403 em `GetSettings` |
| L-09 | Supervisor não baixa XML | 403 em `DownloadXml`, 200 em `DownloadDanfe` |
| L-10 | Documento do destinatário mascarado por padrão na UI; "Mostrar" exige permissão e audita | Teste de UI + de API |
| L-11 | Isolamento entre tenants em todos os endpoints | 404, nunca 403 (403 revelaria a existência do recurso) |
| L-12 | Certificado não é recuperável pela API | Nenhum endpoint devolve o PFX ou a senha, em nenhuma forma |
| L-13 | Exportação em massa de XML é limitada e auditada | Rate limit + teto + entrada de auditoria por exportação |
| L-14 | Desativar cliente (soft delete) não apaga nem esconde as notas dele | Guarda de 5 anos preservada (RN-03) |
| L-15 | E-mail de homologação não vai para o cliente real | Bloqueado por padrão em `tpAmb=2` |

### 8.5 Definição de pronto por fase

**Fase 1 — Modelo fiscal e configuração**
Perfil fiscal e campos fiscais de produto persistem e validam; certificado sobe, é lido do Secrets Manager e a validade é exibida; aba Fiscal navegável e admin-gated; **nenhuma emissão real ainda**.
DoD: unitários de validação verdes; L-08, L-12 verdes; RBAC dos 5 endpoints de configuração verde; cobertura ≥ 80% no módulo novo.

**Fase 2 — Emissão de NFe em homologação**
NFe autorizada em pelo menos uma UF; máquina de estados completa; DANFE em PDF; validação local com blockers.
DoD: H-01..H-04, H-10..H-12 verdes em SP; todos os unitários de estado verdes; N-07..N-11 verdes; nenhum 204 restante no `NFeController`; contrato publicado para o time do app.

**Fase 3 — NFCe, cancelamento e e-mail**
NFCe com CSC e QR Code; cancelamento com prazo real; e-mail com DANFE + XML.
DoD: H-07..H-09, H-12, H-13 verdes; N-12..N-16 verdes; L-01..L-07 verdes; anexos de e-mail funcionando com `EmailStructure` estendido.

**Fase 4 — Cobertura das 4 UFs**
Matriz completa de 8 combinações executada e com evidência arquivada.
DoD: todos os ~120 casos de 8.2 com evidência; todos os negativos em SP + 1 SVRS; todos os LGPD verdes; runbook escrito; alertas configurados.

**Fase 5 — Piloto em produção**
Um tenant emitindo de verdade.
DoD: checklist 9.3 completo e assinado; 20 notas reais autorizadas sem intervenção manual; zero lacuna na numeração; contador do tenant confirma que importou os XMLs; rollback testado.

---

## 9. Rollout

### 9.1 Flag por tenant

O flag atual `NFeSettings.Enabled` é **global**, via `IOptions` (`EvangelionERPV2.NFe.Application/Configs/NFeSettings.cs`). Isso não serve: precisamos de um tenant emitindo em produção enquanto os outros continuam desligados. Requisito de produto: **`issuanceEnabled` e `environment` vivem no perfil fiscal da empresa**, não em `appsettings`. O flag global vira o "disjuntor geral" (kill switch) que sobrepõe tudo.

Precedência: `global kill switch (off ⇒ todos off)` → `issuanceEnabled do tenant` → `environment do tenant` → `readiness (perfil + certificado + CSC)`.

### 9.2 Sequência

1. **Interno, homologação.** Tenant sintético de desenvolvimento, sem clientes reais. Toda a matriz 8.2.
2. **Piloto, homologação.** Um tenant real e voluntário, `environment = Homologation`, emitindo em paralelo ao emissor atual. Duas semanas. Todo XML de homologação conferido pelo contador do tenant.
3. **Piloto, produção, apenas NFe.** Mesmo tenant, produção, começando por **um pedido de valor baixo**, com o Admin do tenant presente e alguém do time acompanhando. Depois 10 notas, depois liberação normal. NFCe permanece desligada.
4. **Piloto, produção, NFe + NFCe.** Após 2 semanas sem incidentes.
5. **Onboarding gradual.** Novos tenants entram um a um; cada um refaz o checklist 9.3 por conta própria. Sem liberação em massa na v1 — cada tenant tem CNPJ, IE, certificado, CSC e regime tributário próprios, e cada onboarding pode falhar por um motivo novo.

### 9.3 Checklist para um tenant mudar para Produção

O botão "Produção" fica bloqueado até que **todos** os itens estejam verdes, com o item pendente linkado à tela que o resolve:

- [ ] CNPJ preenchido, DV válido, e conferido contra o cartão CNPJ
- [ ] Inscrição Estadual preenchida (ou marcado isento) e válida para a UF
- [ ] Endereço fiscal completo, com código IBGE do município
- [ ] Regime tributário selecionado e confirmado pelo contador
- [ ] Certificado A1 enviado, válido, com **mais de 30 dias** de validade restante, e CNPJ do certificado igual ao da empresa
- [ ] Série e próximo número definidos e conferidos contra a última nota emitida no sistema anterior *(item mais esquecido; um número repetido gera rejeição por duplicidade e um número pulado gera lacuna)*
- [ ] CFOPs padrão e CST/CSOSN padrão definidos
- [ ] Todos os produtos ativos com NCM preenchido (indicador "0 produtos sem dados fiscais")
- [ ] **Ao menos 3 notas autorizadas em homologação, com XML conferido pelo contador do tenant**
- [ ] Para NFCe: CSC de **produção** cadastrado (diferente do de homologação) e QR Code testado no portal da UF
- [ ] Prazos de cancelamento da UF conferidos com o contador e registrados no perfil
- [ ] Ao menos um usuário Admin e um Manager treinados na tela e no runbook
- [ ] Aceite formal registrado: quem aprovou, quando

### 9.4 Rollback

| Nível | Ação | Tempo | Efeito nas notas já emitidas |
|---|---|---|---|
| Tenant | `issuanceEnabled = false` no perfil fiscal | segundos, sem deploy | Nenhum. Notas autorizadas continuam válidas, visíveis, baixáveis e enviáveis por e-mail |
| Tenant | Voltar `environment` para Homologation | segundos | Nenhum. **Mas a numeração de produção não volta atrás** — o próximo retorno a produção continua de onde parou |
| Global | Kill switch geral | segundos | Nenhum |
| Código | Rollback de deploy | minutos | Nenhum |

**O que rollback nunca faz:** desfazer uma nota autorizada na SEFAZ. Depois que a SEFAZ autoriza, a nota existe no mundo. Rollback impede **novas** emissões; corrigir as existentes é cancelamento (dentro do prazo) ou devolução (fora). Isso precisa estar escrito no runbook em letras grandes, porque é a primeira coisa que alguém vai pedir às 2 da manhã.

**Regra de deploy:** nunca implantar mudança no módulo fiscal na véspera de fechamento de mês, nem na sexta.

### 9.5 Runbook de suporte

**R-01 — SEFAZ fora do ar (108/109 ou timeout)**
Confirmar no portal de status da UF. Notas ficam em "Aguardando SEFAZ" e são reconsultadas automaticamente com backoff. **Não** cancelar, **não** reemitir, **não** mexer no banco. Comunicar os tenants da UF afetada com previsão. Contingência offline/SVC não existe na v1 (seção 10) — a operação de faturamento fica parada, e isso é uma limitação conhecida e comunicada.

**R-02 — Nota presa em "Em processamento" ou "Aguardando SEFAZ" > 15 min**
1. Consultar a chave direto no portal da SEFAZ.
2. Se **existe e está autorizada** na SEFAZ → forçar a reconsulta pela ferramenta administrativa até o estado convergir. Nunca reemitir.
3. Se **não existe** (217) → seguro liberar para reemissão.
4. Se **não há chave** gravada → a transmissão não chegou a acontecer; liberar o número e reemitir.
5. Registrar no incidente qual dos quatro caminhos foi usado.

**R-03 — Certificado vencido em produção**
Emissão bloqueada para o tenant. Contatar o Admin, orientar a compra/renovação do A1 e o upload. Não há workaround. Prevenção: o alerta de 30 dias existe justamente para isso; se um tenant chegou aqui, revisar por que o alerta foi ignorado.

**R-04 — Lacuna detectada na numeração**
O relatório semanal aponta (empresa, modelo, série, faixa). Reunir os números queimados por rejeição/denegação. Inutilização de numeração **não existe na v1** — orientar o contador do tenant a inutilizar pelo emissor gratuito da SEFAZ e registrar. Priorizar inutilização para a v1.1 se aparecer em mais de um tenant.

**R-05 — Nota autorizada com dados errados**
Dentro do prazo → cancelar e reemitir corrigida. Fora do prazo → não há conserto pelo sistema; encaminhar ao contador (devolução, ou carta de correção quando o erro for corrigível por CC-e — e CC-e também está fora da v1).

**R-06 — Cliente reclama que não recebeu o e-mail**
Verificar `email.lastSentAt` na gaveta; verificar a fila de e-mail; verificar spam. Reenviar. Alternativa imediata: baixar o DANFE e enviar por outro canal.

**R-07 — "Emiti duas vezes"**
Verificar as duas chaves na SEFAZ. Se ambas autorizadas, cancelar a segunda dentro do prazo. Abrir incidente: a proteção de idempotência falhou e isso é bug, não erro do usuário.

**Alertas configurados:** documento não-terminal > 15 min; taxa de rejeição > 10% em 1 h para um tenant; certificado vencendo em 30/7/1 dias; SEFAZ indisponível > 10 min para uma UF; lacuna de numeração detectada; qualquer emissão em produção com legenda de homologação (nunca deveria acontecer — é sintoma de bug grave).

---

## 10. Fora do escopo da v1

Cada item abaixo é uma exclusão deliberada, com o motivo e o gatilho que a traria de volta.

**1. Contingência offline da NFCe.**
Permite emitir NFCe sem internet, com transmissão posterior em até 24 h. Exige gestão de contingência no dispositivo, controle de reenvio, tratamento de rejeição em lote e um DANFE de contingência distinto. É praticamente um segundo módulo. Além disso o app Expo é primariamente web e não tem armazenamento local confiável para documentos fiscais assinados. **Gatilho para incluir:** um tenant de varejo com PDV cuja internet cai com frequência. **Mitigação v1:** com a SEFAZ fora, não se vende com nota — a mesma limitação que o emissor gratuito da SEFAZ tem.

**2. Contingência SVC-AN / SVC-RS.**
Autorizadores de contingência acionados quando a SEFAZ da UF cai. Exige detecção do momento de entrar em contingência, troca de `tpEmis`, endpoint diferente por UF, e o retorno à normalidade. Só faz sentido depois que a emissão normal estiver estável nas 4 UFs. **Gatilho:** mais de um evento de indisponibilidade prolongada afetando faturamento de tenant pagante. Deve ser o **primeiro item da v1.1**.

**3. Carta de Correção Eletrônica (CC-e).**
Evento que corrige erros que não alteram valores, destinatário nem data. Prazo de 30 dias (**UNVERIFIED** — confirmar). Exige modelagem de eventos, DANFE da carta e regras do que pode ou não ser corrigido. É um alívio operacional, não um bloqueador de faturamento — sem ela ainda se emite, se cancela e se envia. **Gatilho:** volume de chamados R-05.

**4. Inutilização de numeração.**
Regulariza lacunas perante a SEFAZ. Não é necessária para emitir, só para manter o livro limpo. A v1 minimiza lacunas com validação local agressiva antes de transmitir, e o relatório semanal torna as lacunas visíveis. **Gatilho:** lacunas recorrentes no piloto. Deve entrar junto com a CC-e.

**5. Outras UFs (MG, RJ, BA, GO, …).**
Cada UF tem endpoints, CSC, regras de IE, particularidades de ICMS e prazos próprios. Quatro UFs já são ~120 casos de homologação manual. Ampliar sem antes estabilizar multiplica um custo de QA que ninguém quer pagar duas vezes. **Gatilho:** as 4 UFs estáveis por 60 dias + demanda comercial concreta. **A UI deve bloquear outras UFs explicitamente**, com mensagem clara, e não deixar o usuário configurar algo que vai falhar na SEFAZ.

**6. NFS-e (Nota Fiscal de Serviço eletrônica).**
Não é o mesmo problema com outro nome. É **municipal**: cada prefeitura tem seu próprio padrão, sua própria autenticação e seu próprio webservice; há literalmente milhares de variações. Não usa a SEFAZ, não usa a Unimake.DFe, não usa chave de acesso de 44 dígitos, não tem DANFE. O padrão nacional (NFS-e nacional) ainda está em adoção desigual. Compartilha **zero** infraestrutura com o que está sendo construído aqui. **Gatilho:** decisão de produto separada, com discovery e orçamento próprios. A v1 deve **dizer isso ao usuário** quando ele tentar emitir NFCe para um item marcado como serviço.

**7. CT-e (transporte) e MDF-e (manifesto de carga).**
Domínio de logística: exigem cadastro de transportador, veículo, motorista, rota, RNTRC. Só interessa a transportadoras. Nenhum tenant atual é transportadora. **Gatilho:** um tenant de transporte, e mesmo assim é um módulo novo.

**Também fora da v1, para não haver ambiguidade:** manifestação do destinatário (ciência/confirmação/desconhecimento), NFe complementar, NFe de ajuste, NFe de devolução automatizada, EFD/SPED Fiscal, exportação para sistemas contábeis, danfe simplificado, venda com pagamento em múltiplas formas detalhado no `tpag`, DIFAL/FCP interestadual calculado automaticamente (v1 usa o valor informado no perfil), substituição tributária calculada automaticamente, e impressão térmica direta do DANFE NFCe.

---

## Arquivos críticos para a implementação

- `/home/peanuut/CodeProjects/EvangelionERPV2/EvangelionERPV2.Web/Controllers/NFeController.cs` — contrato a ser reescrito; eliminar os três significados de 204, adicionar `List`, `Refresh`, `DownloadDanfe`, `DownloadXml`, `SendByEmail`
- `/home/peanuut/CodeProjects/EvangelionERPV2/EvangelionERPV2.Web/Security/RbacPermissions.cs` e `/home/peanuut/CodeProjects/EvangelionERPV2/EvangelionERPV2.Web/Security/RbacRolePermissionMap.cs` — permissões novas; `fiscal.*` admin-only por omissão (padrão `Payroll`); `nfe.cancel` sobe para Manager; `nfe.xml.download` no Manager
- `/home/peanuut/CodeProjects/EvangelionERPV2/EvangelionERPV2.NFe.Application/Services/NFeService.cs` — `CancelAsync` hoje cancela sem SEFAZ e sem checar prazo; máquina de estados expandida
- `/home/peanuut/CodeProjects/EvangelionERPV2/EvangelionERPV2.Shared/Entities/NFeDocument.cs` e `/home/peanuut/CodeProjects/EvangelionERPV2/EvangelionERPV2.Shared/DTOs/NFeDocumentDTO.cs` — estados novos, campos de SEFAZ, serialização de enums como string
- `/home/peanuut/CodeProjects/EvangelionERPV2/EvangelionERPV2.Shared/Entities/Enterprise.cs` — sem CNPJ, sem IE, endereço em um único campo `Adress`; precisa do perfil fiscal com endereço estruturado
- `/home/peanuut/CodeProjects/EvangelionERPV2/EvangelionERPV2.Shared/Auditing/AuditedEntities.cs` — `NFeDocument` e o perfil fiscal precisam entrar (testes L-05, L-06, L-07)
- `/home/peanuut/CodeProjects/EvangelionERPV2/EvangelionERPV2.Web/Logging/RequestLoggingMiddleware.cs` — estender a redação de chave de acesso e a lista de endpoints sensíveis para as rotas novas (teste L-03)
- `/home/peanuut/CodeProjects/EvangelionERPV2/EvangelionERPV2.Shared/Entities/EmailStructure.cs` — sem suporte a anexos; bloqueia a US-08