using EvangelionERPV2.AiModule.Application.Services;

namespace EvangelionERPV2.AiModule.Test.Ai
{
    /// <summary>
    /// A tool that hands back four hundred rows does not produce a better answer, it produces a
    /// model that has forgotten the question. Every tool result is capped, and the cap is stated in
    /// the payload so the model can say "mostrando 20 de 137" instead of implying it saw them all.
    /// </summary>
    public class AiJsonTests
    {
        [Fact]
        public void ForModel_KeepsEverythingUnderTheCap()
        {
            var json = AiJson.ForModel(new[] { 1, 2, 3 }, totalFound: 3, cap: 20);

            Assert.Contains("\"total\":3", json);
            Assert.Contains("\"mostrando\":3", json);
            Assert.Contains("\"truncado\":false", json);
        }

        [Fact]
        public void ForModel_TruncatesAndSaysSo()
        {
            var rows = Enumerable.Range(1, 50).ToArray();

            var json = AiJson.ForModel(rows, totalFound: 137, cap: 20);

            Assert.Contains("\"total\":137", json);
            Assert.Contains("\"mostrando\":20", json);
            Assert.Contains("\"truncado\":true", json);
        }

        [Fact]
        public void ForModel_OnNothingFound_IsStillValidJson()
        {
            var json = AiJson.ForModel(Array.Empty<int>(), totalFound: 0, cap: 20);

            Assert.Contains("\"total\":0", json);
            Assert.Contains("\"truncado\":false", json);
        }
    }
}
