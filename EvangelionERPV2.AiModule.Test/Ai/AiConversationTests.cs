using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.AiModule.Test.Ai
{
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
