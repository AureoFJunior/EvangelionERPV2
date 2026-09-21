using AutoMapper;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.FluentValidator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The fiscal columns have existed on Product and ProductDTO for a while, but no request body
    /// carried them, so nothing could ever write one through the API. These cover the write path
    /// end to end: validator, controller mapping, and the legacy nested payload shape.
    /// </summary>
    public class ProductFiscalRequestTests
    {
        [Fact]
        public async Task AddProduct_CarriesFiscalFieldsOntoTheEntity()
        {
            var enterpriseId = Guid.NewGuid();
            var callerId = Guid.NewGuid();
            ProductPicture? captured = null;

            var productService = new Mock<IProductService<Product>>();
            productService
                .Setup(s => s.ApplyStockMovementAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(),
                    It.IsAny<EnumStockMovementType>(), It.IsAny<Guid?>(), It.IsAny<bool>()))
                .ReturnsAsync((Guid productId, Guid _, decimal delta, EnumStockMovementType type, Guid? _, bool _) =>
                    new StockMovement { ProductId = productId, Quantity = delta, BalanceAfter = delta, Type = (int)type });

            productService
                .Setup(s => s.CreateAsync(It.IsAny<ProductPicture>()))
                .Callback<ProductPicture>(payload => captured = payload)
                .ReturnsAsync((ProductPicture payload) => payload.Product);

            var controller = CreateController(productService.Object, enterpriseId, callerId);

            var result = await controller.AddProduct(new CreateProductRequestDTO
            {
                Name = "Fiscal product",
                Description = "Description",
                DefaultValue = 10,
                StorageQuantity = 2,
                UnitOfMeasure = "unit",
                Ncm = "12345678",
                Cest = "1234567",
                Cean = "7891234567890",
                CfopIntra = "5102",
                CfopInter = "6102",
                Origem = 0,
                UnidadeTributavel = "un",
                Cst = "00",
                Csosn = "102",
                AliquotaIcms = 0.18m,
                CstPis = "01",
                AliquotaPis = 0.0165m,
                CstCofins = "01",
                AliquotaCofins = 0.076m,
                CodigoBeneficioFiscal = "SP123456"
            });

            Assert.IsType<OkObjectResult>(result);
            var product = Assert.IsType<ProductPicture>(captured).Product;

            Assert.Equal("12345678", product.Ncm);
            Assert.Equal("1234567", product.Cest);
            Assert.Equal("7891234567890", product.Cean);
            Assert.Equal("5102", product.CfopIntra);
            Assert.Equal("6102", product.CfopInter);
            Assert.Equal((byte)0, product.Origem);
            Assert.Equal("UN", product.UnidadeTributavel);
            Assert.Equal("00", product.Cst);
            Assert.Equal("102", product.Csosn);
            Assert.Equal(0.18m, product.AliquotaIcms);
            Assert.Equal("01", product.CstPis);
            Assert.Equal(0.0165m, product.AliquotaPis);
            Assert.Equal("01", product.CstCofins);
            Assert.Equal(0.076m, product.AliquotaCofins);
            Assert.Equal("SP123456", product.CodigoBeneficioFiscal);
        }

        [Fact]
        public async Task AddProduct_WithLegacyNestedPayload_StillCarriesFiscalFields()
        {
            var enterpriseId = Guid.NewGuid();
            var callerId = Guid.NewGuid();
            ProductPicture? captured = null;

            var productService = new Mock<IProductService<Product>>();
            productService
                .Setup(s => s.ApplyStockMovementAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(),
                    It.IsAny<EnumStockMovementType>(), It.IsAny<Guid?>(), It.IsAny<bool>()))
                .ReturnsAsync((Guid productId, Guid _, decimal delta, EnumStockMovementType type, Guid? _, bool _) =>
                    new StockMovement { ProductId = productId, Quantity = delta, BalanceAfter = delta, Type = (int)type });

            productService
                .Setup(s => s.CreateAsync(It.IsAny<ProductPicture>()))
                .Callback<ProductPicture>(payload => captured = payload)
                .ReturnsAsync((ProductPicture payload) => payload.Product);

            var controller = CreateController(productService.Object, enterpriseId, callerId);

            var result = await controller.AddProduct(new CreateProductRequestDTO
            {
                Ncm = "12345678",
                Product = new LegacyCreateProductRequestDTO
                {
                    Name = "Legacy product",
                    Description = "Legacy",
                    DefaultValue = 5,
                    StorageQuantity = 1,
                    UnitOfMeasure = "kg"
                }
            });

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal("12345678", Assert.IsType<ProductPicture>(captured).Product.Ncm);
        }

        [Fact]
        public void CreateProductRequestValidator_AcceptsAnAbsentFiscalBlock()
        {
            var result = new CreateProductRequestValidator().Validate(new CreateProductRequestDTO
            {
                Name = "No fiscal data",
                DefaultValue = 1,
                StorageQuantity = 1,
                UnitOfMeasure = "unit"
            });

            Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
        }

        [Theory]
        [InlineData("Ncm", "1234")]
        [InlineData("Cest", "12")]
        [InlineData("CfopIntra", "51020")]
        [InlineData("CfopInter", "610")]
        public void CreateProductRequestValidator_RejectsMalformedFiscalCodes(string field, string value)
        {
            var request = new CreateProductRequestDTO
            {
                Name = "Product",
                DefaultValue = 1,
                StorageQuantity = 1,
                UnitOfMeasure = "unit"
            };

            typeof(CreateProductRequestDTO).GetProperty(field)!.SetValue(request, value);

            Assert.False(new CreateProductRequestValidator().Validate(request).IsValid);
        }

        [Fact]
        public void UpdateProductRequestValidator_RejectsARateSentAsAPercentage()
        {
            var result = new UpdateProductRequestValidator().Validate(new UpdateProductRequestDTO
            {
                Id = Guid.NewGuid(),
                Name = "Product",
                DefaultValue = 1,
                StorageQuantity = 1,
                UnitOfMeasure = "unit",
                AliquotaIcms = 18m
            });

            Assert.False(result.IsValid);
        }

        [Fact]
        public void UpdateProductRequestValidator_RejectsAnOriginOutsideZeroToEight()
        {
            var result = new UpdateProductRequestValidator().Validate(new UpdateProductRequestDTO
            {
                Id = Guid.NewGuid(),
                Name = "Product",
                DefaultValue = 1,
                StorageQuantity = 1,
                UnitOfMeasure = "unit",
                Origem = 9
            });

            Assert.False(result.IsValid);
        }

        private static ProductController CreateController(
            IProductService<Product> productService,
            Guid enterpriseId,
            Guid callerId)
        {
            var userRepository = new Mock<IRepository<User>>();
            userRepository
                .Setup(r => r.GetByIdAsync(callerId))
                .ReturnsAsync(new User { Id = callerId, EnterpriseId = enterpriseId, IsActive = true });

            var mapper = new Mock<IMapper>();
            mapper.Setup(m => m.Map<ProductDTO>(It.IsAny<Product>())).Returns(new ProductDTO());

            var controller = new ProductController(
                productService,
                new Mock<IRepository<Product>>().Object,
                userRepository.Object,
                mapper.Object,
                Mock.Of<IAuthorizationService>(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<string>()) == Task.FromResult(AuthorizationResult.Failed())));

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.GroupSid, enterpriseId.ToString()),
                        new Claim(ClaimTypes.Sid, callerId.ToString())
                    ], "TestAuth"))
                }
            };

            return controller;
        }
    }
}
