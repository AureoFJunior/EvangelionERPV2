using EvangelionERPV2.AiModule.Application.Services;

namespace EvangelionERPV2.AiModule.Test.Ai
{
    /// <summary>
    /// Every case here is a figure a person would have to hear and re-ask about. Portuguese spells
    /// numbers with more rules than it looks — "cem" alone but "cento e um", "mil" never "um mil",
    /// and an "e" before the last group only sometimes — and each of those is a sentence that sounds
    /// wrong to a native speaker and right to a test that only checks the digits are gone.
    /// </summary>
    public class AiSpokenTextTests
    {
        [Theory]
        [InlineData("R$ 9.226,24", "nove mil duzentos e vinte e seis reais e vinte e quatro centavos")]
        [InlineData("R$ 1,00", "um real")]
        [InlineData("R$ 2,00", "dois reais")]
        [InlineData("R$ 0,01", "um centavo")]
        [InlineData("R$ 0,50", "cinquenta centavos")]
        [InlineData("R$ 0,00", "zero reais")]
        [InlineData("R$ 100,00", "cem reais")]
        [InlineData("R$ 101,00", "cento e um reais")]
        [InlineData("R$ 1.000,00", "mil reais")]
        [InlineData("R$ 2.005,00", "dois mil e cinco reais")]
        [InlineData("R$ 1.100,00", "mil e cem reais")]
        [InlineData("R$ 1.234,00", "mil duzentos e trinta e quatro reais")]
        [InlineData("R$ 1.000.000,00", "um milhão de reais")]
        [InlineData("R$ 2.000.000,00", "dois milhões de reais")]
        [InlineData("R$ 2.500.000,00", "dois milhões e quinhentos mil reais")]
        [InlineData("R$ 1.000.234,00", "um milhão duzentos e trinta e quatro reais")]
        [InlineData("R$ 1.500.000,50", "um milhão e quinhentos mil reais e cinquenta centavos")]
        [InlineData("R$ 15,90", "quinze reais e noventa centavos")]
        [InlineData("R$ 14,00", "catorze reais")]
        public void Money_IsSaidTheWayAPersonSaysIt(string written, string spoken)
        {
            Assert.Equal(spoken, AiSpokenText.Normalize(written));
        }

        [Fact]
        public void AShortenedCentavo_IsTenths_NotUnits()
        {
            // "R$ 5,5" is five reais fifty, the way a price is written in a hurry — not five and five.
            Assert.Equal("cinco reais e cinquenta centavos", AiSpokenText.Normalize("R$ 5,5"));
        }

        [Fact]
        public void AnAmountWithNoCents_SaysOnlyTheReais()
        {
            Assert.Equal("quarenta reais", AiSpokenText.Normalize("R$ 40"));
        }

        [Fact]
        public void TheFigureIsRewrittenInsideTheSentence_AndNothingElseIs()
        {
            Assert.Equal(
                "Você tem 4 parcelas vencidas, somando nove mil duzentos e vinte e seis reais e vinte e quatro centavos.",
                AiSpokenText.Normalize("Você tem 4 parcelas vencidas, somando R$ 9.226,24."));
        }

        [Fact]
        public void EveryFigureInASentenceIsRewritten()
        {
            Assert.Equal(
                "dez reais a pagar e vinte reais a receber",
                AiSpokenText.Normalize("R$ 10,00 a pagar e R$ 20,00 a receber"));
        }

        [Fact]
        public void ASpaceAfterTheSymbolIsOptional()
        {
            Assert.Equal("cinco reais", AiSpokenText.Normalize("R$5,00"));
        }

        [Theory]
        [InlineData("O pedido 1234 foi entregue em 14/09/2026.")]
        [InlineData("Margem de 12,5% no mês.")]
        [InlineData("Restam 3 unidades do produto X-200.")]
        public void EverythingThatIsNotMoney_IsLeftAlone(string sentence)
        {
            // An order number read as "mil duzentos e trinta e quatro" is worse than the digits, and
            // a date rewritten as money would be nonsense.
            Assert.Equal(sentence, AiSpokenText.Normalize(sentence));
        }

        [Fact]
        public void NothingToSay_IsReturnedUntouched()
        {
            Assert.Equal(string.Empty, AiSpokenText.Normalize(string.Empty));
        }
    }
}
