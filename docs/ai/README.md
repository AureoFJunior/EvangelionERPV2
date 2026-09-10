# Agente do ERP

A pt-BR chat agent that sits on top of the ERP's own read services. It answers questions like
"quanto vendi mês passado?" or "o cliente João está devendo alguma coisa?" by calling the same
application services the dashboards call, through a small set of named tools — it never runs SQL
or touches EF Core directly.

This is Rung 1: **read-only**. Nine tasks built the module, the entities, the tool contract and
registry, eight read tools, the hand-driven agent loop, the `ai.chat` RBAC permission, the HTTP
endpoint and SignalR progress. This task (10a) wires the container up to a host Ollama and writes
this document. **It does not run the agent against a real model** — that is Task 10b, on the
repository owner's machine, because it needs Ollama installed and a GPU.

**Status: code complete, not yet run against a real model.** Every number in the "results" sections
below is a placeholder until Task 10b fills it in.

## Why Ollama runs on the host, not in Compose

`docker-compose.yml` has no `ollama` service. Ollama runs natively on the host machine instead,
and the API container reaches it at `http://host.docker.internal:11434` (mapped to the host via
`extra_hosts: host.docker.internal:host-gateway`, since that hostname only resolves for free on
Docker Desktop, not on Linux).

Two reasons, not one:

- **The GPU is the entire point.** A local model is only worth running if it uses the GPU. Getting
  a container GPU-accelerated needs `nvidia-container-toolkit` passthrough, which buys nothing here
  — the container doesn't run the model, it just calls an HTTP API.
- Ollama already listens on `11434` on the host with no extra setup. Host-native is the simpler
  path for identical behavior.

## Configuration

`AiSettings`, bound from the `AiSettings` config section (`EvangelionERPV2.Ai.Application/Configs/AiSettings.cs`):

| Key | Default | Notes |
|---|---|---|
| `Enabled` | `false` | Off until turned on, same pattern as `OpportunityRadarSettings`. `POST /api/v1/ai/chat` answers 503 while it's off |
| `Endpoint` | `http://localhost:11434` | From inside the container this is `http://host.docker.internal:11434` |
| `Model` | `qwen3:14b` | See "Which model" below — undecided pending the ten-prompt run |
| `MaxToolIterations` | `6` | Caps how many tool-call rounds one turn may take, so a model stuck in a loop fails fast instead of timing out |
| `TimeoutSeconds` | `120` | Budget for the whole turn, tool rounds included. Enforced in `AiAgentService`; overrunning it ends the turn with a pt-BR message, not a 500. A non-positive value disables the budget (no time limit). |
| `MessageRetentionDays` | `90` | Recorded for LGPD purposes; the sweep that enforces it is not built in Rung 1 |
| `ApiKey` | `null` | Unused with Ollama. Exists so pointing this module at a hosted provider is a config change, not a config-*shape* change |

`docker-compose.yml`'s `evangelionerpv2` service maps three env vars onto these keys:
`EVA_AI_ENABLED` → `AiSettings__Enabled`, `EVA_AI_ENDPOINT` → `AiSettings__Endpoint`,
`EVA_AI_MODEL` → `AiSettings__Model`. To turn the agent on for a local run:

```bash
EVA_AI_ENABLED=true ./scripts/local-up.ps1
```

## The model client

The agent talks to the model through `Microsoft.Extensions.AI`'s `IChatClient`, implemented by
`OllamaSharp.OllamaApiClient` (`AiIoC.Configure`). **`Microsoft.Extensions.AI.Ollama` is
deprecated and must not be used** — `OllamaSharp` implements `IChatClient` directly. Nothing
outside `AiIoC` knows Ollama exists; swapping to a hosted provider (OpenAI, Anthropic, Azure
OpenAI) is a different `IChatClient` registration in that one file and nothing else.

### Tool calling and streaming don't combine on this path

`AiAgentService.RunTurnAsync` calls `_chatClient.GetResponseAsync` (not the streaming overload)
because tool calling and token streaming cannot be combined through this client. Once a turn's
final answer comes back with no more tool calls, `AiTextChunker.Split` cuts it into fixed-size
pieces and the agent sends those over SignalR as `AiChunk` events. **This is not token streaming.**
The status events (`AiStatus`, e.g. "Consultando produtos...") are real, sent as each tool call
happens — but the answer text itself is only chunked after the model has finished generating it,
not streamed as it generates. Anyone building a UI against `/aiHub` should not expect the chunk
cadence to reflect model latency the way a true token stream would.

## The eight tools

Every tool is behind `ai.chat` first (Supervisor tier and above — see `RbacRolePermissionMap.cs`),
then behind its own `RequiredPermission`. `AiToolRegistry.Build` filters the tool list down to
what the calling user's granted permissions cover *before* the model ever sees a tool list — a
tool the caller may not use is not merely refused if invoked, it is never named to the model as
existing.

| Tool (pt-BR) | Required permission | Tier floor | What it does |
|---|---|---|---|
| `buscar_produtos` | `products.read` | Supervisor | Product search |
| `buscar_clientes` | `customers.read` | Supervisor | Customer search |
| `consultar_pedidos` | `orders.read` | Employee (Supervisor via `ai.chat`) | Order lookup |
| `resumo_de_vendas` | `sales.performance.read` | **Manager** | Monthly sales summary by seller, with goal and commission |
| `painel_do_dia` | `dashboard.read` | **Manager** | The daily panel: receivables, payables due, critical stock, fiscal pending, unreconciled bank lines |
| `contas_a_receber` | `bills.read` | Supervisor | A customer's receivables |
| `previsao_de_caixa` | `cashflow.forecast.read` | Supervisor | Day-by-day cash projection |
| `estoque_baixo` | `products.read` | Supervisor | Low-stock products |

Tier floors are read directly from `EvangelionERPV2.Web/Security/RbacRolePermissionMap.cs`, current
as of this task.

### Worked example: what a Supervisor can't ask

`resumo_de_vendas` and `painel_do_dia` require `sales.performance.read` and `dashboard.read`,
both granted starting at **Manager**, not Supervisor. A Supervisor — the lowest tier that can even
reach the agent, since `ai.chat` itself starts at Supervisor — genuinely cannot use those two
tools. Asking "como estamos hoje?" (prompt 4 below) as a Supervisor must not produce `painel_do_dia`
numbers; the agent has to answer from whatever tools it *does* have, or say it cannot see that
picture.

The remaining six tools (`buscar_produtos`, `buscar_clientes`, `consultar_pedidos`,
`contas_a_receber`, `previsao_de_caixa`, `estoque_baixo`) are all granted at Supervisor tier or
below, so a Supervisor can reach them. **`previsao_de_caixa` is visible to a Supervisor** —
`cashflow.forecast.read` is granted starting at Supervisor tier in the current
`RbacRolePermissionMap`, not Manager-and-above. Anyone testing isolation against the ten prompts
below should aim the "must refuse" case (Step 5 of the task brief) at prompt 4
("como estamos hoje?") or a sales-summary question, not at prompt 7 (cash-flow) — a Supervisor
asking about cash flow should get a real number, not a refusal.

## Cost ledger

`AiMessage` carries `Model`, `PromptTokens`, `CompletionTokens` and `LatencyMs` per turn — the only
record of what a turn cost. Whether `OllamaSharp`'s `ChatResponse.Usage` actually populates
`PromptTokenCount`/`OutputTokenCount` for a local model has **not been verified yet**; that is
Task 10b, Step 6 of the task brief (`SELECT TOP 20 ... FROM AiMessage`). If assistant rows come
back with `PromptTokens = 0` and `LatencyMs = 0`, the provider isn't populating `Usage` and that
needs to be noted here rather than left as a silently-useless column — **not yet confirmed either
way**.

## Logging

`AiController.Chat` logs one line per turn: tool names, prompt/completion token counts and
latency — `"AI turn completed. Tools={Tools} PromptTokens={PromptTokens} ..."`. It does not log the
user's message or the model's answer; those live only in `AiMessage.Content`, reachable only
through `ai.chat`. This has been confirmed by reading `AiController.cs` and `AiAgentService.cs`;
running `docker compose logs` to confirm nothing leaked in practice (Step 7 of the task brief) has
not been done yet.

## Which model — not decided yet

Two candidates, both fitting a 16 GB RTX 4080 SUPER: `qwen3:14b` and `gpt-oss:20b`.
`AiSettings.Model` defaults to `qwen3:14b` today, but **that default has not been validated against
either candidate and is not a decision** — it's just what has to go in the config file until one
is chosen. The procedure below is how that choice gets made; the result table is intentionally
empty until it's run.

### Procedure

1. Install Ollama on the host and pull both models (not done by this task):
   ```bash
   curl -fsSL https://ollama.com/install.sh | sh
   ollama pull qwen3:14b
   ollama pull gpt-oss:20b
   ```
2. Apply the pending migration and bring the stack up with the agent on:
   ```bash
   ./scripts/local-db-update.sh
   EVA_AI_ENABLED=true ./scripts/local-up.ps1
   ```
3. With a Supervisor-or-above token, run each of the ten prompts below against
   `POST /api/v1/ai/chat` (Swagger or curl), once per model (change `AiSettings__Model` /
   `EVA_AI_MODEL` and restart between models). Record which tool the agent chose and whether the
   answer was right.
4. Prompt 6 matters most — it requires chaining `buscar_clientes` into `contas_a_receber`, which is
   where a smaller model tends to fail first. Prompt 10 is the honesty check: no tool answers it,
   and a good agent declines instead of guessing.
5. Set `AiSettings.Model` to whichever wins and record why, in one line, below.

### Prompts and results (not yet run — fill in after Task 10b)

| # | Prompt | `qwen3:14b` tool(s) called | `qwen3:14b` correct? | `gpt-oss:20b` tool(s) called | `gpt-oss:20b` correct? |
|---|---|---|---|---|---|
| 1 | quantos produtos eu tenho com coca no nome? | | | | |
| 2 | qual o preço do produto X? | | | | |
| 3 | quanto vendi mês passado? | | | | |
| 4 | como estamos hoje? | | | | |
| 5 | quais clientes chamados João? | | | | |
| 6 | o cliente [nome] está devendo alguma coisa? | | | | |
| 7 | como fica meu caixa nos próximos 60 dias? | | | | |
| 8 | o que eu preciso comprar? | | | | |
| 9 | quantos pedidos nos últimos 7 dias? | | | | |
| 10 | qual a capital da França? (must decline) | | | | |

Date run: _(not yet run)_. Model versions (`ollama list` output): _(not yet run)_.

**Decision: undecided.** `AiSettings.Model` stays at the code default (`qwen3:14b`) until the run
above happens and this line is replaced with the winner and a one-line reason.

## Isolation checks — not yet run

Two assertions no unit test can make on its own, from Step 5 of the task brief. Neither has been
exercised against a running instance yet:

- As a Supervisor, asking a question that needs `painel_do_dia` or `resumo_de_vendas` (see the
  worked example above) must produce a refusal or an answer built from other tools, never
  Manager-tier numbers.
- A user in tenant A asking about a product that exists only in tenant B, by exact name, must get
  "not found" — the tenant filter runs inside the tool's own service call, the same
  `AppDbContext` global query filter every other module uses, but that has not been observed
  against real cross-tenant data.

## What Rung 2 adds

Rung 2 adds `AiPendingAction`, four write tools, confirm/reject endpoints, an expiry sweep on
`LeasedScheduleWorker`, and an eval harvest that exports every confirmed or rejected action to
`docs/ai/evals/*.jsonl`. None of it is built yet. Its design depends on how the model actually
behaves in the ten-prompt run above — if tool choice is reliable, one confirmation card per action
is enough; if arguments come out wrong often, the card needs to be editable before confirming,
which is a different contract. That is why Rung 2 is written after this run, not before it.
