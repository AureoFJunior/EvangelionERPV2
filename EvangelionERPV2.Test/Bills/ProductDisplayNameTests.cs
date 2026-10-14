using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.Test.Bills
{
    /// <summary>Every screen, report, till and invoice names a variant by its family and its option.</summary>
    public class ProductDisplayNameTests
    {
        [Theory]
        [InlineData("Monster", "Mango Loco", "Monster — Mango Loco")]
        [InlineData("Caneca", null, "Caneca")]
        [InlineData("Caneca", " ", "Caneca")]
        public void ReadsNameThenVariant(string name, string? variant, string expected) =>
            Assert.Equal(expected, new Product { Name = name, VariantLabel = variant }.DisplayName);
    }
}
