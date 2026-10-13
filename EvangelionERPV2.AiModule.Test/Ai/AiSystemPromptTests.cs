using EvangelionERPV2.AiModule.Application.Services;

namespace EvangelionERPV2.AiModule.Test.Ai
{
    public class AiSystemPromptTests
    {
        private static readonly DateTime Today = new(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void ATypedTurn_GetsNoSpokenRules()
        {
            var prompt = AiSystemPrompt.Build(Today);

            Assert.DoesNotContain("falada em voz alta", prompt);
            Assert.DoesNotContain("Nada de tabelas", prompt);
        }

        [Fact]
        public void ASpokenTurn_IsToldToWriteForEars()
        {
            var prompt = AiSystemPrompt.Build(Today, spoken: true);

            Assert.Contains("falada em voz alta", prompt);
            Assert.Contains("Nada de tabelas", prompt);
            Assert.Contains("por extenso", prompt);
        }

        [Fact]
        public void ASpokenTurn_KeepsEveryRuleATypedTurnHas()
        {
            var typed = AiSystemPrompt.Build(Today);
            var spoken = AiSystemPrompt.Build(Today, spoken: true);

            Assert.StartsWith(typed, spoken);
            Assert.Contains("Nunca invente números", spoken);
        }

        [Fact]
        public void TheDateIsStillStamped()
        {
            Assert.Contains("2026-09-11", AiSystemPrompt.Build(Today, spoken: true));
        }
    }
}
