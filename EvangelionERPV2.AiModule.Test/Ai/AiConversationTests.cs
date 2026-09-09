using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.AiModule.Test.Ai
{
    /// <summary>
    /// A conversation carries EnterpriseId directly so the AppDbContext global query filter picks
    /// it up by convention; the messages reach the tenant through their parent, the same shape
    /// OpportunitySignal and Bill already use. These tests pin the defaults that make the token
    /// columns meaningful — a row that silently defaults its counts to something other than zero
    /// would quietly corrupt every cost figure derived from them.
    /// </summary>
    public class AiConversationTests
    {
        [Fact]
        public void NewConversation_StartsWithNoMessages()
        {
            var conversation = new AiConversation();

            Assert.NotNull(conversation.Messages);
            Assert.Empty(conversation.Messages!);
        }

        [Fact]
        public void NewMessage_HasZeroedCounters()
        {
            var message = new AiMessage();

            Assert.Equal(0, message.PromptTokens);
            Assert.Equal(0, message.CompletionTokens);
            Assert.Equal(0, message.LatencyMs);
        }

        [Fact]
        public void Roles_AreTheFourTheLoopEmits()
        {
            Assert.Equal("system", AiMessageRole.System);
            Assert.Equal("user", AiMessageRole.User);
            Assert.Equal("assistant", AiMessageRole.Assistant);
            Assert.Equal("tool", AiMessageRole.Tool);
        }
    }
}
