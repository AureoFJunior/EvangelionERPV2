using EvangelionERPV2.AiModule.Application.Services;

namespace EvangelionERPV2.AiModule.Test.Ai
{
    /// <summary>
    /// The line between "Whisper heard nothing and said so at length" and "somebody repeated
    /// themselves". Getting it wrong in one direction leaves a question in the history that nobody
    /// asked; in the other it throws away something a person actually said, which is worse.
    /// </summary>
    public class AiTranscriptSanityTests
    {
        [Fact]
        public void TheHallucinationThisWasWrittenFor_IsCaught()
        {
            // Verbatim shape of the 667-character row found in the database on 2026-09-14, persisted
            // twice as the user's own words. What was actually said: nothing.
            var loop = string.Concat(Enumerable.Repeat("O que é que é ", 48));

            Assert.True(AiTranscriptSanity.IsRepetitionLoop(loop));
        }

        [Theory]
        [InlineData("Quanto eu tenho a receber?")]
        [InlineData("Quais produtos estão com estoque crítico hoje?")]
        [InlineData("Me mostra as contas a pagar vencidas e quanto somam no total, por favor.")]
        public void RealQuestions_AreLeftAlone(string question)
        {
            Assert.False(AiTranscriptSanity.IsRepetitionLoop(question));
        }

        [Theory]
        [InlineData("sim sim sim")]
        [InlineData("não, não, não")]
        [InlineData("oi? oi? oi?")]
        public void ShortRepetition_IsOrdinarySpeech(string said)
        {
            // A person repeating themselves three times is not a hallucination, and a guard that
            // swallowed it would be a microphone that ignores you when you insist.
            Assert.False(AiTranscriptSanity.IsRepetitionLoop(said));
        }

        [Fact]
        public void ALongSentenceThatRepeatsOneWord_IsStillASentence()
        {
            // Twenty-odd words with a word repeated for emphasis: plenty of distinct words, so it
            // stays well clear of the threshold.
            var said = "Preciso muito muito muito saber quanto eu tenho a receber dos clientes "
                       + "que estão atrasados neste mês e também no mês passado, por favor.";

            Assert.False(AiTranscriptSanity.IsRepetitionLoop(said));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void NothingAtAll_IsNotALoop(string? text)
        {
            // Empty already means "nothing was said" everywhere else; it must not become a second
            // way of saying the same thing.
            Assert.False(AiTranscriptSanity.IsRepetitionLoop(text));
        }

        [Theory]
        [InlineData("Por isso que ela está aqui. Por isso que ela está aqui.", "Por isso que ela está aqui.")]
        [InlineData("Vamos verificar o estoque. Vamos verificar o estoque.", "Vamos verificar o estoque.")]
        [InlineData("Bom dia. Bom dia. Bom dia.", "Bom dia.")]
        public void ASentenceHeardTwiceInARow_IsKeptOnce(string heard, string kept)
        {
            // Straight from the database on 2026-09-14. The doubled text reached the model as the
            // question and sat in the history as the person's own words.
            Assert.Equal(kept, AiTranscriptSanity.CollapseRepeatedSentences(heard));
        }

        [Theory]
        [InlineData("Quanto eu tenho a receber?")]
        [InlineData("Verifica o estoque. Depois verifica as contas. Verifica o estoque.")]
        [InlineData("Sim. Não. Sim.")]
        public void AnythingNotRepeatedBackToBack_IsUntouched(string said)
        {
            // Repetition with something in between is how people actually talk, and changing it
            // would be putting words in their mouth.
            Assert.Equal(said, AiTranscriptSanity.CollapseRepeatedSentences(said));
        }

        [Fact]
        public void ASentenceWithNoFullStop_IsLeftExactlyAsItWas()
        {
            Assert.Equal("quanto eu tenho a receber", AiTranscriptSanity.CollapseRepeatedSentences("quanto eu tenho a receber"));
        }

        [Fact]
        public void PunctuationDoesNotHideALoop()
        {
            // Whisper punctuates its loops: "que é. que é. que é." is the same failure.
            var loop = string.Concat(Enumerable.Repeat("que é. ", 40));

            Assert.True(AiTranscriptSanity.IsRepetitionLoop(loop));
        }
    }
}
