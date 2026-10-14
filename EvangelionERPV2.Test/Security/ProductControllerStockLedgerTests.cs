using System.Linq.Expressions;
using AutoMapper;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.ProductModule.Domain.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The product edit form used to assign StorageQuantity straight onto the entity, which moved
    /// stock without writing the StockMovement that explains it — the one thing the ledger exists
    /// to prevent. These cover the routing that replaced it.
    /// </summary>
    public class ProductControllerStockLedgerTests
    {
        [Fact]
        public async Task UpdateProduct_WhenTheCountChanges_PassesTheRequestedCountToTheAtomicUpdate()
        {
            var enterpriseId = Guid.NewGuid();
            var stored = CreateProduct(enterpriseId, storageQuantity: 12m);

            var harness = new Harness(enterpriseId, stored);

            var result = await harness.Controller.UpdateProduct(BuildRequest(stored, storageQuantity: 20m));

            Assert.IsType<OkObjectResult>(result);

            Assert.Equal(20m, harness.SavedProduct!.StorageQuantity);
        }

        [Fact]
        public async Task UpdateProduct_WhenTheCountIsUnchanged_WritesNoMovement()
        {
            var enterpriseId = Guid.NewGuid();
            var stored = CreateProduct(enterpriseId, storageQuantity: 12m);

            var harness = new Harness(enterpriseId, stored);

            var result = await harness.Controller.UpdateProduct(BuildRequest(stored, storageQuantity: 12m));

            Assert.IsType<OkObjectResult>(result);

            harness.ProductService.Verify(x => x.ApplyStockMovementAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(),
                It.IsAny<EnumStockMovementType>(), It.IsAny<Guid?>(), It.IsAny<bool>()), Times.Never);

            Assert.Equal(12m, harness.SavedProduct!.StorageQuantity);
        }

        [Fact]
        public async Task DeleteProduct_LoadsTheProductFromTheFreshRepositoryPath()
        {
            var enterpriseId = Guid.NewGuid();
            var stored = CreateProduct(enterpriseId, storageQuantity: 12m);
            var harness = new Harness(enterpriseId, stored);
            harness.ProductService.Setup(x => x.Delete(stored.Id)).Returns(stored);

            var result = await harness.Controller.DeleteProduct(stored.Id);

            Assert.IsType<OkObjectResult>(result);
            harness.FreshProducts.Verify(x => x.GetFreshByIdAsync(stored.Id), Times.Once);
        }

        [Fact]
        public async Task UploadPicture_LoadsTheProductFromTheFreshRepositoryPath()
        {
            var enterpriseId = Guid.NewGuid();
            var stored = CreateProduct(enterpriseId, storageQuantity: 12m);
            var harness = new Harness(enterpriseId, stored);
            harness.ProductService.Setup(x => x.UpdatePictureAsync(It.IsAny<ProductPicture>()))
                .ReturnsAsync((ProductPicture picture) => picture.Product);

            var result = await harness.Controller.UploadPicture(new UploadProductPictureRequestDTO
            {
                ProductId = stored.Id,
                File = "image-data"
            });

            Assert.IsType<OkObjectResult>(result);
            harness.FreshProducts.Verify(x => x.GetFreshByIdAsync(stored.Id), Times.Once);
        }

        private sealed class Harness
        {
            public Mock<IProductService<Product>> ProductService { get; } = new(MockBehavior.Strict);
            public Mock<IProductRepository<Product>> FreshProducts { get; private set; } = null!;
            public ProductController Controller { get; }
            public Product? SavedProduct { get; private set; }

            public Harness(Guid enterpriseId, Product stored)
            {
                var callerId = Guid.NewGuid();
                var productRepository = new Mock<IRepository<Product>>(MockBehavior.Strict);
                var products = productRepository.As<IProductRepository<Product>>();
                FreshProducts = products;
                var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);

                products.Setup(r => r.GetFreshByIdAsync(stored.Id)).ReturnsAsync(stored);

                userRepository.Setup(r => r.GetByIdAsync(callerId)).ReturnsAsync(new User
                {
                    Id = callerId,
                    EnterpriseId = enterpriseId,
                    IsActive = true,
                    AccessLevel = 0
                });

                ProductService
                    .Setup(x => x.ApplyStockMovementAsync(
                        It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(),
                        It.IsAny<EnumStockMovementType>(), It.IsAny<Guid?>(), It.IsAny<bool>()))
                    .ReturnsAsync((Guid productId, Guid tenantId, decimal delta,
                                   EnumStockMovementType type, Guid? referenceId, bool allowNegative)
                        => new StockMovement
                        {
                            Id = Guid.NewGuid(),
                            ProductId = productId,
                            EnterpriseId = tenantId,
                            Quantity = delta,
                            BalanceAfter = stored.StorageQuantity + delta,
                            Type = (int)type,
                            ReferenceId = referenceId,
                            IsActive = true
                        });

                ProductService
                    .Setup(x => x.UpdateAsync(It.IsAny<Product>()))
                    .ReturnsAsync((Product product) =>
                    {
                        SavedProduct = product;
                        return product;
                    });

                Controller = new ProductController(
                    ProductService.Object,
                    productRepository.Object,
                    userRepository.Object,
                    CreateMapper().Object,
                    Mock.Of<IAuthorizationService>(x =>
                        x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<string>())
                            == Task.FromResult(AuthorizationResult.Failed())));

                Controller.ControllerContext = new ControllerContext
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
            }
        }

        private static UpdateProductRequestDTO BuildRequest(Product stored, decimal storageQuantity)
        {
            return new UpdateProductRequestDTO
            {
                Id = stored.Id,
                Name = stored.Name,
                Description = stored.Description,
                DefaultValue = stored.DefaultValue,
                StorageQuantity = storageQuantity,
                UnitOfMeasure = stored.UnitOfMeasure,
                IsActive = true
            };
        }

        private static Product CreateProduct(Guid enterpriseId, decimal storageQuantity)
        {
            return new Product
            {
                Id = Guid.NewGuid(),
                Name = "Sample product",
                Description = "Sample product",
                UnitOfMeasure = nameof(EnumUnitOfMeasure.Unit),
                DefaultValue = 10m,
                StorageQuantity = storageQuantity,
                PictureAdress = string.Empty,
                EnterpriseId = enterpriseId,
                IsActive = true
            };
        }

        private static Mock<IMapper> CreateMapper()
        {
            var mapper = new Mock<IMapper>(MockBehavior.Strict);
            mapper.Setup(m => m.Map<ProductDTO>(It.IsAny<Product>()))
                .Returns<Product>(product => new ProductDTO
                {
                    Id = product.Id,
                    Name = product.Name,
                    PictureAdress = product.PictureAdress ?? string.Empty
                });

            return mapper;
        }
    }
}
