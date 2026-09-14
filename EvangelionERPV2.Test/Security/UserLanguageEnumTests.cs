using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.FluentValidator;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The language integers are a contract with the frontend, which maps the same values in
    /// utils/language.ts. Chinese shipped there before this enum knew about it, so every attempt to
    /// save it came back 400 from <see cref="UserController.UpdateLanguage"/>.
    /// </summary>
    public class UserLanguageEnumTests
    {
        [Theory]
        [InlineData(0, EnumLanguage.English)]
        [InlineData(1, EnumLanguage.Portuguese)]
        [InlineData(2, EnumLanguage.Spanish)]
        [InlineData(3, EnumLanguage.Japanese)]
        [InlineData(4, EnumLanguage.Chinese)]
        public void TheStoredIntegerMeansTheSameLanguageOnBothSides(int value, EnumLanguage expected)
        {
            // Pinned, not derived: these are written to User.Language, so reordering the enum would
            // silently change the language of every existing row.
            Assert.Equal(expected, (EnumLanguage)value);
            Assert.True(Enum.IsDefined(typeof(EnumLanguage), value));
        }

        [Fact]
        public void TheValidatorAcceptsChineseAndStillRejectsAnUnknownLanguage()
        {
            var validator = new UpdateLanguageRequestValidator();

            Assert.True(validator.Validate(new UserController.UpdateLanguageRequest { Language = 4 }).IsValid);
            Assert.False(validator.Validate(new UserController.UpdateLanguageRequest { Language = 5 }).IsValid);
            Assert.False(validator.Validate(new UserController.UpdateLanguageRequest { Language = -1 }).IsValid);
        }
    }
}
