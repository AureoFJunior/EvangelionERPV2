using EvangelionERPV2.AiModule.Application.Configs;
using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Serilog;
using System.Diagnostics;
using System.Text.Json;

namespace EvangelionERPV2.AiModule.Application.Services
{
    public sealed class AiAgentService : IAiAgentService
    {
        private const int MaxHistoryMessages = 20;

        private readonly IChatClient _chatClient;
        private readonly IAiToolRegistry _registry;
        private readonly AiSettings _settings;
        private readonly IRepository<AiMessage> _messages;
        private readonly IAiProgressNotifier _notifier;
        private readonly IAiPendingActionService _pendingActions;

        public AiAgentService(
            IChatClient chatClient,
            IAiToolRegistry registry,
            IOptions<AiSettings> settings,
            IRepository<AiMessage> messages,
            IAiProgressNotifier notifier,
            IAiPendingActionService pendingActions)
        {
            _chatClient = chatClient;
            _registry = registry;
            _settings = settings.Value;
            _messages = messages;
            _notifier = notifier;
            _pendingActions = pendingActions;
        }

        public async Task<AiTurnResult> RunTurnAsync(AiTurnRequest request, CancellationToken cancellation = default)
        {
            try
            {
                return await RunTurnCoreAsync(request, cancellation);
            }
            finally
            {
                await NotifyAsync(() => _notifier.DoneAsync(request.Context.UserId, request.ConversationId));
            }
        }

        private async Task<AiTurnResult> RunTurnCoreAsync(AiTurnRequest request, CancellationToken cancellation)
        {
            var stopwatch = Stopwatch.StartNew();
            var tools = _registry.Build(request.GrantedPermissions, request.Context);

            var messages = await LoadHistoryAsync(request.ConversationId);
            messages.Insert(0, new ChatMessage(ChatRole.System, AiSystemPrompt.Build(DateTime.UtcNow, request.Spoken)));
            messages.Add(new ChatMessage(ChatRole.User, request.UserMessage));

            await PersistAsync(request.ConversationId, AiMessageRole.User, request.UserMessage);

            var options = new ChatOptions
            {
                Tools = tools.Cast<AITool>().ToList(),
                ToolMode = ChatToolMode.Auto,
            };
            var toolsCalled = new List<string>();
            var proposals = new List<AiPendingActionCard>();
            var promptTokens = 0;
            var completionTokens = 0;

            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            if (_settings.TimeoutSeconds > 0)
                budget.CancelAfter(TimeSpan.FromSeconds(_settings.TimeoutSeconds));

            async Task<AiTurnResult> FinishAsync(string text)
            {
                stopwatch.Stop();
                await PersistAsync(request.ConversationId, AiMessageRole.Assistant, text,
                    toolsCalled, promptTokens, completionTokens, stopwatch.ElapsedMilliseconds);

                foreach (var chunk in AiTextChunker.Split(text))
                    await NotifyAsync(() => _notifier.ChunkAsync(request.Context.UserId, chunk));

                return new AiTurnResult(text, toolsCalled, promptTokens, completionTokens, stopwatch.ElapsedMilliseconds, proposals);
            }

            try
            {
                for (var iteration = 0; iteration < _settings.MaxToolIterations; iteration++)
                {
                    var response = await _chatClient.GetResponseAsync(messages, options, budget.Token);

                    promptTokens += (int)(response.Usage?.InputTokenCount ?? 0);
                    completionTokens += (int)(response.Usage?.OutputTokenCount ?? 0);

                    var calls = response.Messages
                        .SelectMany(message => message.Contents)
                        .OfType<FunctionCallContent>()
                        .ToList();

                    messages.AddMessages(response);

                    if (calls.Count == 0)
                        return await FinishAsync(response.Text ?? string.Empty);

                    var results = new List<AIContent>();
                    foreach (var call in calls)
                    {
                        await NotifyAsync(() => _notifier.StatusAsync(request.Context.UserId, DescribeTool(call.Name)));
                        toolsCalled.Add(call.Name);

                        var tool = _registry.Find(call.Name);
                        if (tool?.RequiresConfirmation == true)
                        {
                            var arguments = call.Arguments is null
                                ? new Dictionary<string, object?>()
                                : new Dictionary<string, object?>(call.Arguments);

                            // Arguments are checked and, where possible, repaired before a card
                            // exists: a name becomes the id it meant, and what cannot be repaired
                            // comes back to the model in this same turn instead of reaching a human
                            // as a card that was always going to fail.
                            var prepared = await tool.PrepareAsync(arguments, request.Context, budget.Token);
                            if (prepared.Error is not null)
                            {
                                Log.Logger.Information(
                                    "AI write proposal rejected before the card. Tool={Tool}", call.Name);

                                results.Add(new FunctionResultContent(call.CallId,
                                    JsonSerializer.Serialize(new { erro = prepared.Error })));
                                continue;
                            }

                            // The model never runs a write. It proposes one, and the human holding
                            // the confirmation card is what stands between ERP free text and a
                            // mutation. See the design doc, "Every write is confirmed by a human".
                            var card = await _pendingActions.ProposeAsync(
                                request.ConversationId,
                                tool,
                                prepared.Arguments,
                                budget.Token);

                            proposals.Add(card);
                            results.Add(new FunctionResultContent(call.CallId, ProposedJson(card)));
                            continue;
                        }

                        results.Add(new FunctionResultContent(call.CallId, await ExecuteAsync(tools, call, budget.Token)));
                    }

                    messages.Add(new ChatMessage(ChatRole.Tool, results));
                }
            }
            catch (OperationCanceledException) when (budget.IsCancellationRequested && !cancellation.IsCancellationRequested)
            {
                Log.Logger.Warning("AI turn exceeded its budget. TimeoutSeconds={TimeoutSeconds}", _settings.TimeoutSeconds);
                return await FinishAsync("A consulta demorou demais e foi interrompida. Tente perguntar de forma mais específica.");
            }

            return await FinishAsync("Não consegui concluir essa consulta. Tente perguntar de forma mais específica.");
        }

        private static async Task NotifyAsync(Func<Task> notify)
        {
            try
            {
                await notify();
            }
            catch (Exception ex)
            {
                Log.Logger.Warning("AI progress notification failed. ErrorType={ErrorType}", ex.GetType().Name);
            }
        }

        private static string DescribeTool(string toolName) => toolName switch
        {
            "buscar_produtos" => "Consultando produtos...",
            "buscar_clientes" => "Consultando clientes...",
            "consultar_pedidos" => "Consultando pedidos...",
            "resumo_de_vendas" => "Somando as vendas...",
            "painel_do_dia" => "Montando o panorama do dia...",
            "contas_a_receber" => "Consultando o financeiro do cliente...",
            "previsao_de_caixa" => "Projetando o caixa...",
            "estoque_baixo" => "Verificando o estoque...",
            "produtos_mais_vendidos" => "Rankeando os produtos...",
            "melhores_clientes" => "Rankeando os clientes...",
            "clientes_inadimplentes" => "Verificando quem está devendo...",
            "contas_a_pagar" => "Consultando as contas a pagar...",
            "criar_cliente" => "Preparando o cadastro do cliente...",
            "atualizar_cliente" => "Preparando a alteração do cliente...",
            "atualizar_produto" => "Preparando a alteração do produto...",
            "criar_pedido" => "Preparando o pedido...",
            "compras_pendentes" => "Verificando as compras que não chegaram...",
            "ajustar_estoque" => "Preparando o ajuste de estoque...",
            "receber_compra" => "Preparando a entrada da mercadoria...",
            _ => "Consultando...",
        };

        /// <summary>
        /// What the model reads back after proposing a write. It says the action is waiting rather
        /// than done, so the answer it writes does not claim something that has not happened.
        /// </summary>
        private static string ProposedJson(AiPendingActionCard card) =>
            JsonSerializer.Serialize(new
            {
                status = "aguardando_confirmacao",
                acaoId = card.Id,
                resumo = card.Summary,
                instrucao = "Diga que a ação está aguardando a confirmação da pessoa na tela. Não afirme que já foi feita.",
            });

        private static async Task<string> ExecuteAsync(
            IReadOnlyList<AIFunction> tools, FunctionCallContent call, CancellationToken cancellation)
        {
            var function = tools.FirstOrDefault(x => x.Name == call.Name);
            if (function is null)
                return "{\"erro\":\"ferramenta desconhecida\"}";

            try
            {
                var arguments = new AIFunctionArguments(call.Arguments);
                var value = await function.InvokeAsync(arguments, cancellation);
                return value?.ToString() ?? "{}";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Logger.Warning("AI tool call failed. Tool={Tool} ErrorType={ErrorType}", call.Name, ex.GetType().Name);
                return "{\"erro\":\"a consulta falhou\"}";
            }
        }

        private async Task<List<ChatMessage>> LoadHistoryAsync(Guid conversationId)
        {
            var stored = (await AiQuery.OrEmptyAsync(() => _messages.GetAllAsync(x => x.AiConversationId == conversationId)))
                .OrderByDescending(x => x.CreatedAt)
                .Take(MaxHistoryMessages)
                .OrderBy(x => x.CreatedAt)
                .ToList();

            return stored
                .Select(x => new ChatMessage(
                    x.Role == AiMessageRole.User ? ChatRole.User : ChatRole.Assistant,
                    x.Content))
                .ToList();
        }

        private async Task PersistAsync(
            Guid conversationId,
            string role,
            string content,
            IReadOnlyList<string>? toolsCalled = null,
            int promptTokens = 0,
            int completionTokens = 0,
            long latencyMs = 0)
        {
            await _messages.CreateAsync(new AiMessage
            {
                Id = Guid.NewGuid(),
                AiConversationId = conversationId,
                Role = role,
                Content = content,
                ToolCallsJson = JsonSerializer.Serialize(toolsCalled ?? Array.Empty<string>()),
                Model = _settings.Model,
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                LatencyMs = latencyMs,
            });
            await _messages.CommitAsync();
        }
    }

    public static class AiTextChunker
    {
        // ponytail: answers are chunked after completion because tool calling and streaming cannot
        // combine on this path. Upgrade: re-issue the final turn via GetStreamingResponseAsync once no tool calls remain.
        public static IEnumerable<string> Split(string text, int size = 80)
        {
            if (string.IsNullOrEmpty(text))
                yield break;

            for (var offset = 0; offset < text.Length; offset += size)
                yield return text.Substring(offset, Math.Min(size, text.Length - offset));
        }
    }
}
