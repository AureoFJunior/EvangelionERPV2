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
            Assert.False(AiTranscriptSanity.IsRepetitionLoop(said));
        }

        [Fact]
        public void ALongSentenceThatRepeatsOneWord_IsStillASentence()
        {
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
            Assert.False(AiTranscriptSanity.IsRepetitionLoop(text));
        }

        [Theory]
        [InlineData("Por isso que ela está aqui. Por isso que ela está aqui.", "Por isso que ela está aqui.")]
        [InlineData("Vamos verificar o estoque. Vamos verificar o estoque.", "Vamos verificar o estoque.")]
        [InlineData("Bom dia. Bom dia. Bom dia.", "Bom dia.")]
        public void ASentenceHeardTwiceInARow_IsKeptOnce(string heard, string kept)
        {
            Assert.Equal(kept, AiTranscriptSanity.CollapseRepeatedSentences(heard));
        }

        [Theory]
        [InlineData("Quanto eu tenho a receber?")]
        [InlineData("Verifica o estoque. Depois verifica as contas. Verifica o estoque.")]
        [InlineData("Sim. Não. Sim.")]
        public void AnythingNotRepeatedBackToBack_IsUntouched(string said)
        {
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
            var loop = string.Concat(Enumerable.Repeat("que é. ", 40));

            Assert.True(AiTranscriptSanity.IsRepetitionLoop(loop));
        }
    }
}
