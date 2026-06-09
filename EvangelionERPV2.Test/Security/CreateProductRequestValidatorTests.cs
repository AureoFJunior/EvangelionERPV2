using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.FluentValidator;
using System.Reflection;

namespace EvangelionERPV2.Test.Security
{
    public class CreateProductRequestValidatorTests
    {
        [Fact]
        public void Validate_WhenFileIsMissing_IsValid()
        {
            var validator = new CreateProductRequestValidator();
            var request = new CreateProductRequestDTO
            {
                Name = "Produto",
                Description = "Descricao",
                DefaultValue = 10,
                StorageQuantity = 5,
                UnitOfMeasure = "KG",
                IsExternal = false,
                IsService = false,
                File = null
            };

            var result = validator.Validate(request);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_WhenFileIsProvidedAndInvalid_ReturnsValidationError()
        {
            var validator = new CreateProductRequestValidator();
            var request = new CreateProductRequestDTO
            {
                Name = "Produto",
                Description = "Descricao",
                DefaultValue = 10,
                StorageQuantity = 5,
                UnitOfMeasure = "KG",
                IsExternal = false,
                IsService = false,
                File = "invalid-base64"
            };

            var result = validator.Validate(request);

            Assert.False(result.IsValid);
            Assert.Contains(
                result.Errors,
                error => error.ErrorMessage.Contains("valid base64 payload", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Validate_WhenPictureAdressIsProvided_IsValid()
        {
            var validator = new CreateProductRequestValidator();
            var request = new CreateProductRequestDTO
            {
                Name = "Produto",
                Description = "Descricao",
                DefaultValue = 10,
                StorageQuantity = 5,
                UnitOfMeasure = "KG",
                IsExternal = false,
                IsService = false,
                PictureAdress = "products/legacy.jpg"
            };

            var result = validator.Validate(request);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void NormalizeCreateProductRequest_WhenRootNameAndLegacyValuesAreMixed_PreservesLegacyNumericAndBoolValues()
        {
            var request = new CreateProductRequestDTO
            {
                Name = "Root name",
                Product = new LegacyCreateProductRequestDTO
                {
                    Name = "Legacy name",
                    Description = "Legacy description",
                    DefaultValue = 12.5,
                    StorageQuantity = 7,
                    UnitOfMeasure = "UN",
                    IsExternal = true,
                    IsService = true
                }
            };

            var normalized = InvokeNormalizeCreateProductRequest(request);

            Assert.Equal("Root name", normalized.Name);
            Assert.Equal("Legacy description", normalized.Description);
            Assert.Equal(12.5, normalized.DefaultValue.GetValueOrDefault());
            Assert.Equal(7, normalized.StorageQuantity.GetValueOrDefault());
            Assert.Equal("UN", normalized.UnitOfMeasure);
            Assert.True(normalized.IsExternal.GetValueOrDefault());
            Assert.True(normalized.IsService.GetValueOrDefault());
        }

        private static CreateProductRequestDTO InvokeNormalizeCreateProductRequest(CreateProductRequestDTO request)
        {
            var method = typeof(ProductController).GetMethod(
                "NormalizeCreateProductRequest",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            return Assert.IsType<CreateProductRequestDTO>(method!.Invoke(null, [request]));
        }
    }
}
