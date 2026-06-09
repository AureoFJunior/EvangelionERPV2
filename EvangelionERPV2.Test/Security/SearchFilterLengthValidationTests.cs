using EvangelionERPV2.Shared.Utils;

namespace EvangelionERPV2.Test.Security
{
    public class SearchFilterLengthValidationTests
    {
        [Fact]
        public void EnsureSearchFilterLength_TrimsWhitespaceAndReturnsValue()
        {
            var value = SharedFunctions.EnsureSearchFilterLength("  alpha  ", 10, "name");

            Assert.Equal("alpha", value);
        }

        [Fact]
        public void EnsureSearchFilterLength_WhenValueExceedsLimit_ThrowsArgumentException()
        {
            var value = new string('a', 151);

            var exception = Assert.Throws<ArgumentException>(() =>
                SharedFunctions.EnsureSearchFilterLength(value, 150, "name"));

            Assert.Contains("150 characters or fewer", exception.Message, StringComparison.Ordinal);
            Assert.Equal("name", exception.ParamName);
        }
    }
}
