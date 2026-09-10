using EvangelionERPV2.AiModule.Application.Services;

namespace EvangelionERPV2.AiModule.Test.Ai
{
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
