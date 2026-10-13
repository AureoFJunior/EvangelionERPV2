using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Web.FluentValidator;

namespace EvangelionERPV2.Test.Security;

public sealed class ProductVariantGroupingValidatorTests
{
    [Theory]
    [InlineData(false, null, true)]
    [InlineData(true, "Preto / M", true)]
    [InlineData(true, null, false)]
    [InlineData(false, "Preto / M", false)]
    public void Create_GroupAndLabelMustTravelTogether(bool withGroup, string? label, bool expectedValid)
    {
        var request = new CreateProductRequestDTO
        {
            Name = "Camiseta Gola V",
            UnitOfMeasure = "UNIT",
            VariantGroupId = withGroup ? Guid.NewGuid() : null,
            VariantLabel = label
        };

        Assert.Equal(expectedValid, new CreateProductRequestValidator().Validate(request).IsValid);
    }

    [Fact]
    public void Update_LabelLongerThanSixtyCharactersIsInvalid()
    {
        var request = new UpdateProductRequestDTO
        {
            Id = Guid.NewGuid(),
            Name = "Camiseta Gola V",
            UnitOfMeasure = "UNIT",
            VariantGroupId = Guid.NewGuid(),
            VariantLabel = new string('x', 61)
        };

        Assert.False(new UpdateProductRequestValidator().Validate(request).IsValid);
    }
}
