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
        private readonly IRepository<AiConversation> _conversations;
        private readonly IRepository<AiMessage> _messages;

        public AiAgentService(
            IChatClient chatClient,
            IAiToolRegistry registry,
            IOptions<AiSettings> settings,
            IRepository<AiConversation> conversations,
            IRepository<AiMessage> messages)
        {
            _chatClient = chatClient;
            _registry = registry;
            _settings = settings.Value;
            _conversations = conversations;
            _messages = messages;
        }

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
                    return new AiTurnResult(text, toolsCalled, promptTokens, completionTokens, stopwatch.ElapsedMilliseconds);
                }

                var results = new List<AIContent>();
                foreach (var call in calls)
                {
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
            // OrEmptyAsync because a conversation on its first turn has no rows, and this
            // repository layer says that by throwing rather than returning an empty list.
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
}
