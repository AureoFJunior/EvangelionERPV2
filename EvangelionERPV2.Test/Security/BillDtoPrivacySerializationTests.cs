using System.Text.Json;
using EvangelionERPV2.Shared.DTOs;

namespace EvangelionERPV2.Test.Security
{
    public class BillDtoPrivacySerializationTests
    {
        [Fact]
        public void BillDto_DoesNotSerializeHtmlContent()
        {
            var dto = new BillDTO
            {
                Id = Guid.NewGuid(),
                HtmlContent = "<html><body>secret</body></html>",
                DigitableLine = "123",
                BarCode = "456"
            };

            var json = JsonSerializer.Serialize(dto);

            Assert.DoesNotContain("htmlContent", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("digitableLine", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("barCode", json, StringComparison.OrdinalIgnoreCase);
        }
    }
}
