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
    /// <summary>
    /// Drives the tool-call loop by hand rather than through UseFunctionInvocation(). Rung 2 has to
    /// intercept a call before it executes, hold it as a pending action and wait for a human; a
    /// loop we own turns that into a branch instead of a rewrite.
    /// </summary>
    public sealed class AiAgentService : IAiAgentService
    {
        private const int MaxHistoryMessages = 20;

        private readonly IChatClient _chatClient;
        private readonly IAiToolRegistry _registry;
        private readonly AiSettings _settings;
        private readonly IRepository<AiMessage> _messages;
        private readonly IAiProgressNotifier _notifier;

        public AiAgentService(
            IChatClient chatClient,
            IAiToolRegistry registry,
            IOptions<AiSettings> settings,
            IRepository<AiMessage> messages,
            IAiProgressNotifier notifier)
        {
            _chatClient = chatClient;
            _registry = registry;
            _settings = settings.Value;
            _messages = messages;
            _notifier = notifier;
        }

        /// <remarks>
        /// Precondition: the caller has already resolved the conversation and confirmed it belongs
        /// to the calling user and tenant. This method does not re-check that. The controller does
        /// it once, against a DbContext whose global filter already scopes the read to the tenant;
        /// repeating it here would be a second source of truth for the same rule.
        /// </remarks>
        public async Task<AiTurnResult> RunTurnAsync(AiTurnRequest request, CancellationToken cancellation = default)
        {
            var stopwatch = Stopwatch.StartNew();
            var tools = _registry.Build(request.GrantedPermissions, request.Context);

            var messages = await LoadHistoryAsync(request.ConversationId);
            messages.Insert(0, new ChatMessage(ChatRole.System, AiSystemPrompt.Build(DateTime.UtcNow)));
            messages.Add(new ChatMessage(ChatRole.User, request.UserMessage));

            await PersistAsync(request.ConversationId, AiMessageRole.User, request.UserMessage);

            // Cast, not ToList: ChatOptions.Tools is IList<AITool> and the registry hands back
            // AIFunction. The compiler will not widen the element type for you.
            var options = new ChatOptions
            {
                Tools = tools.Cast<AITool>().ToList(),
                ToolMode = ChatToolMode.Auto,
            };
            var toolsCalled = new List<string>();
            var promptTokens = 0;
            var completionTokens = 0;

            for (var iteration = 0; iteration < _settings.MaxToolIterations; iteration++)
            {
                var response = await _chatClient.GetResponseAsync(messages, options, cancellation);

                promptTokens += (int)(response.Usage?.InputTokenCount ?? 0);
                completionTokens += (int)(response.Usage?.OutputTokenCount ?? 0);

                var calls = response.Messages
                    .SelectMany(message => message.Contents)
                    .OfType<FunctionCallContent>()
                    .ToList();

                messages.AddMessages(response);

                if (calls.Count == 0)
                {
                    stopwatch.Stop();
                    var text = response.Text ?? string.Empty;
                    await PersistAsync(request.ConversationId, AiMessageRole.Assistant, text,
                        toolsCalled, promptTokens, completionTokens, stopwatch.ElapsedMilliseconds);

                    foreach (var chunk in AiTextChunker.Split(text))
                        await NotifyAsync(() => _notifier.ChunkAsync(request.Context.UserId, chunk));

                    await NotifyAsync(() => _notifier.DoneAsync(request.Context.UserId, request.ConversationId));

                    return new AiTurnResult(text, toolsCalled, promptTokens, completionTokens, stopwatch.ElapsedMilliseconds);
                }

                var results = new List<AIContent>();
                foreach (var call in calls)
                {
                    await NotifyAsync(() => _notifier.StatusAsync(request.Context.UserId, DescribeTool(call.Name)));
                    toolsCalled.Add(call.Name);
                    results.Add(new FunctionResultContent(call.CallId, await ExecuteAsync(tools, call, cancellation)));
                }

                messages.Add(new ChatMessage(ChatRole.Tool, results));
            }

            stopwatch.Stop();

            // The cap was reached. Say so rather than returning an empty string: a model stuck in a
            // loop has already failed, and the user is owed an answer that admits it.
            const string exhausted = "Não consegui concluir essa consulta. Tente perguntar de forma mais específica.";
            await PersistAsync(request.ConversationId, AiMessageRole.Assistant, exhausted,
                toolsCalled, promptTokens, completionTokens, stopwatch.ElapsedMilliseconds);
            return new AiTurnResult(exhausted, toolsCalled, promptTokens, completionTokens, stopwatch.ElapsedMilliseconds);
        }

        /// <summary>Progress is decoration. A hub that is down must not cost the user their answer.</summary>
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
            _ => "Consultando...",
        };

        /// <summary>
        /// A failing tool comes back as a tool result, not an exception. Bad arguments and transient
        /// faults are both things the model can react to on the next iteration; an exception ends
        /// the turn and tells the user nothing.
        /// </summary>
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
                // ErrorType only, never the message: a tool result may quote customer data.
                Log.Logger.Warning("AI tool call failed. Tool={Tool} ErrorType={ErrorType}", call.Name, ex.GetType().Name);
                return "{\"erro\":\"a consulta falhou\"}";
            }
        }

        private async Task<List<ChatMessage>> LoadHistoryAsync(Guid conversationId)
        {
            // AiMessage has no dedicated repository, so this is the generic RepositoryBase, which
            // returns an empty list for no rows. OrEmptyAsync is defensive: every entity-specific
            // repository in this codebase signals "nothing matched" by throwing instead, so an
            // AiMessageRepository added later would follow that pattern and break the first turn.
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
        // ponytail: chunks a finished answer rather than streaming from the model, because tool
        // calling and streaming cannot be combined on this path. Upgrade path if true token
        // latency is ever wanted: once the loop sees a response with no tool calls, re-issue that
        // final turn through GetStreamingResponseAsync with ChatOptions.Tools cleared — one extra
        // model call, which today is not worth it.
        public static IEnumerable<string> Split(string text, int size = 80)
        {
            if (string.IsNullOrEmpty(text))
                yield break;

            for (var offset = 0; offset < text.Length; offset += size)
                yield return text.Substring(offset, Math.Min(size, text.Length - offset));
        }
    }
}
