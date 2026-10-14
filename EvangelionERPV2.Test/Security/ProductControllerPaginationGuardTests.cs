using AutoMapper;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace EvangelionERPV2.Test.Security
{
    public class ProductControllerPaginationGuardTests
    {
        [Fact]
        public async Task GetProducts_WhenPaginationMissing_UsesSafeDefaults()
        {
            var enterpriseId = Guid.NewGuid();
            var callerId = Guid.NewGuid();
            var productRepository = new Mock<IRepository<Product>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var productService = new Mock<IProductService<Product>>(MockBehavior.Strict);
            var mapper = CreateMapper();
            int? capturedPageNumber = null;
            int? capturedPageSize = null;

            userRepository
                .Setup(r => r.GetByIdAsync(callerId))
                .ReturnsAsync(new User
                {
                    Id = callerId,
                    EnterpriseId = enterpriseId,
                    IsActive = true,
                    AccessLevel = 0
                });

            productRepository
                .Setup(r => r.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Func<Product, bool>>()))
                .Callback<int?, int?, Func<Product, bool>?>((pageNumber, pageSize, _) =>
                {
                    capturedPageNumber = pageNumber;
                    capturedPageSize = pageSize;
                })
                .ReturnsAsync([CreateProduct(enterpriseId)]);

            var controller = CreateController(productService.Object, productRepository.Object, userRepository.Object, mapper.Object, enterpriseId, callerId);

            var result = await controller.GetProducts();

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(1, capturedPageNumber);
            Assert.Equal(50, capturedPageSize);
        }

        [Fact]
        public async Task GetProductsByFilter_WhenPageSizeTooLarge_ClampsToMaximum()
        {
            var enterpriseId = Guid.NewGuid();
            var callerId = Guid.NewGuid();
            var productRepository = new Mock<IRepository<Product>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var productService = new Mock<IProductService<Product>>(MockBehavior.Strict);
            var mapper = CreateMapper();
            int? capturedPageNumber = null;
            int? capturedPageSize = null;

            userRepository
                .Setup(r => r.GetByIdAsync(callerId))
                .ReturnsAsync(new User
                {
                    Id = callerId,
                    EnterpriseId = enterpriseId,
                    IsActive = true,
                    AccessLevel = 0
                });

            productRepository
                .Setup(r => r.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Func<Product, bool>>()))
                .Callback<int?, int?, Func<Product, bool>?>((pageNumber, pageSize, _) =>
                {
                    capturedPageNumber = pageNumber;
                    capturedPageSize = pageSize;
                })
                .ReturnsAsync([CreateProduct(enterpriseId)]);

            var controller = CreateController(productService.Object, productRepository.Object, userRepository.Object, mapper.Object, enterpriseId, callerId);

            var result = await controller.GetProductsByFilter(
                filter: new ProductFilterRequestDTO(),
                descending: false,
                pageNumber: 2,
                pageSize: 10000,
                includePictures: false);

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(2, capturedPageNumber);
            Assert.Equal(200, capturedPageSize);
        }

        [Fact]
        public async Task GetProducts_WhenIncludingPicturesAndPageSizeTooLarge_ClampsToPictureSafeMaximum()
        {
            var enterpriseId = Guid.NewGuid();
            var callerId = Guid.NewGuid();
            var productRepository = new Mock<IRepository<Product>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var productService = new Mock<IProductService<Product>>(MockBehavior.Strict);
            var mapper = CreateMapper();
            int? capturedPageNumber = null;
            int? capturedPageSize = null;

            userRepository
                .Setup(r => r.GetByIdAsync(callerId))
                .ReturnsAsync(new User
                {
                    Id = callerId,
                    EnterpriseId = enterpriseId,
                    IsActive = true,
                    AccessLevel = 0
                });

            productRepository
                .Setup(r => r.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Func<Product, bool>>()))
                .Callback<int?, int?, Func<Product, bool>?>((pageNumber, pageSize, _) =>
                {
                    capturedPageNumber = pageNumber;
                    capturedPageSize = pageSize;
                })
                .ReturnsAsync([CreateProduct(enterpriseId)]);

            var controller = CreateController(productService.Object, productRepository.Object, userRepository.Object, mapper.Object, enterpriseId, callerId);

            var result = await controller.GetProducts(pageNumber: 3, pageSize: 10000, includePictures: true);

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(3, capturedPageNumber);
            Assert.Equal(50, capturedPageSize);
        }

        [Fact]
        public void GetProducts_RequiresProductsReadPolicy()
        {
            ControllerPolicyTestHelper.AssertActionPolicy<ProductController>(
                nameof(ProductController.GetProducts),
                "rbac:" + RbacPermissions.Products.Read);
        }

        [Fact]
        public async Task AddProduct_UsesEnterpriseFromAuthenticatedUserRecord()
        {
            var enterpriseFromUser = Guid.NewGuid();
            var enterpriseFromClaim = Guid.NewGuid();
            var callerId = Guid.NewGuid();
            var productRepository = new Mock<IRepository<Product>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var productService = new Mock<IProductService<Product>>(MockBehavior.Strict);
            var mapper = CreateMapper();
            ProductPicture? capturedPayload = null;

            userRepository
                .Setup(r => r.GetByIdAsync(callerId))
                .ReturnsAsync(new User
                {
                    Id = callerId,
                    EnterpriseId = enterpriseFromUser,
                    IsActive = true
                });

            productService
                .Setup(s => s.CreateAsync(It.IsAny<ProductPicture>()))
                .Callback<ProductPicture>(payload => capturedPayload = payload)
                .ReturnsAsync((ProductPicture payload) =>
                {
                    payload.Product.Id = Guid.NewGuid();
                    return payload.Product;
                });

            var controller = CreateController(productService.Object, productRepository.Object, userRepository.Object, mapper.Object, enterpriseFromClaim, callerId);

            var result = await controller.AddProduct(new CreateProductRequestDTO
            {
                Name = "New product",
                Description = "Description",
                DefaultValue = 10,
                StorageQuantity = 2,
                UnitOfMeasure = "unit",
                IsExternal = false,
                IsService = false,
                File = "base64-placeholder"
            });

            Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(capturedPayload);
            Assert.NotNull(capturedPayload!.Product);
            Assert.Equal(enterpriseFromUser, capturedPayload.Product.EnterpriseId);
        }

        [Fact]
        public async Task AddProduct_WhenUserRecordCannotBeResolved_FallsBackToEnterpriseClaim()
        {
            var enterpriseFromClaim = Guid.NewGuid();
            var callerId = Guid.NewGuid();
            var productRepository = new Mock<IRepository<Product>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var productService = new Mock<IProductService<Product>>(MockBehavior.Strict);
            var mapper = CreateMapper();
            ProductPicture? capturedPayload = null;

            userRepository
                .Setup(r => r.GetByIdAsync(callerId))
                .ThrowsAsync(new NotFoundDatabaseException());

            productService
                .Setup(s => s.CreateAsync(It.IsAny<ProductPicture>()))
                .Callback<ProductPicture>(payload => capturedPayload = payload)
                .ReturnsAsync((ProductPicture payload) =>
                {
                    payload.Product.Id = Guid.NewGuid();
                    return payload.Product;
                });

            var controller = CreateController(productService.Object, productRepository.Object, userRepository.Object, mapper.Object, enterpriseFromClaim, callerId);

            var result = await controller.AddProduct(new CreateProductRequestDTO
            {
                Name = "New product",
                Description = "Description",
                DefaultValue = 10,
                StorageQuantity = 2,
                UnitOfMeasure = "unit",
                IsExternal = false,
                IsService = false,
                File = "base64-placeholder"
            });

            Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(capturedPayload);
            Assert.NotNull(capturedPayload!.Product);
            Assert.Equal(enterpriseFromClaim, capturedPayload.Product.EnterpriseId);
        }

        [Fact]
        public async Task AddProduct_WithPictureAdressInPayload_IgnoresLegacyPictureAddressAndCreatesProduct()
        {
            var enterpriseFromUser = Guid.NewGuid();
            var enterpriseFromClaim = Guid.NewGuid();
            var callerId = Guid.NewGuid();
            var productRepository = new Mock<IRepository<Product>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var productService = new Mock<IProductService<Product>>(MockBehavior.Strict);
            var mapper = CreateMapper();
            ProductPicture? capturedPayload = null;

            userRepository
                .Setup(r => r.GetByIdAsync(callerId))
                .ReturnsAsync(new User
                {
                    Id = callerId,
                    EnterpriseId = enterpriseFromUser,
                    IsActive = true
                });

            productService
                .Setup(s => s.CreateAsync(It.IsAny<ProductPicture>()))
                .Callback<ProductPicture>(payload => capturedPayload = payload)
                .ReturnsAsync((ProductPicture payload) =>
                {
                    payload.Product.Id = Guid.NewGuid();
                    return payload.Product;
                });

            var controller = CreateController(productService.Object, productRepository.Object, userRepository.Object, mapper.Object, enterpriseFromClaim, callerId);

            var result = await controller.AddProduct(new CreateProductRequestDTO
            {
                File = "base64-placeholder",
                Product = new LegacyCreateProductRequestDTO
                {
                    Name = "Legacy product",
                    Description = "Legacy description",
                    DefaultValue = 25,
                    StorageQuantity = 4,
                    UnitOfMeasure = "kg",
                    IsExternal = true,
                    IsService = false,
                    PictureAdress = "legacy.jpg"
                }
            });

            Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(capturedPayload);
            Assert.NotNull(capturedPayload!.Product);
            Assert.Equal(string.Empty, capturedPayload.Product.PictureAdress);
            Assert.Equal("base64-placeholder", capturedPayload.File);
            Assert.Equal(enterpriseFromUser, capturedPayload.Product.EnterpriseId);
            productService.Verify(s => s.CreateAsync(It.IsAny<ProductPicture>()), Times.Once);
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

        private static ProductController CreateController(
            IProductService<Product> productService,
            IRepository<Product> productRepository,
            IRepository<User> userRepository,
            IMapper mapper,
            Guid enterpriseId,
            Guid callerId)
        {
            var controller = new ProductController(productService, productRepository, userRepository, mapper);
            var claims = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.GroupSid, enterpriseId.ToString())
                , new Claim(ClaimTypes.Sid, callerId.ToString())
            ], "TestAuth"));

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = claims
                }
            };

            return controller;
        }

        private static Product CreateProduct(Guid enterpriseId)
        {
            return new Product
            {
                Id = Guid.NewGuid(),
                Name = "Sample product",
                PictureAdress = string.Empty,
                EnterpriseId = enterpriseId,
                IsActive = true
            };
        }
    }
}
