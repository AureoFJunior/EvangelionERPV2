using AutoMapper;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Controllers;
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
            var productRepository = new Mock<IRepository<Product>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var productService = new Mock<IProductService<Product>>(MockBehavior.Strict);
            var mapper = CreateMapper();
            int? capturedPageNumber = null;
            int? capturedPageSize = null;

            productRepository
                .Setup(r => r.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Func<Product, bool>>()))
                .Callback<int?, int?, Func<Product, bool>?>((pageNumber, pageSize, _) =>
                {
                    capturedPageNumber = pageNumber;
                    capturedPageSize = pageSize;
                })
                .ReturnsAsync([CreateProduct(enterpriseId)]);

            var controller = CreateController(productService.Object, productRepository.Object, userRepository.Object, mapper.Object, enterpriseId);

            var result = await controller.GetProducts();

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(1, capturedPageNumber);
            Assert.Equal(50, capturedPageSize);
        }

        [Fact]
        public async Task GetProductsByFilter_WhenPageSizeTooLarge_ClampsToMaximum()
        {
            var enterpriseId = Guid.NewGuid();
            var productRepository = new Mock<IRepository<Product>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var productService = new Mock<IProductService<Product>>(MockBehavior.Strict);
            var mapper = CreateMapper();
            int? capturedPageNumber = null;
            int? capturedPageSize = null;

            productRepository
                .Setup(r => r.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Func<Product, bool>>()))
                .Callback<int?, int?, Func<Product, bool>?>((pageNumber, pageSize, _) =>
                {
                    capturedPageNumber = pageNumber;
                    capturedPageSize = pageSize;
                })
                .ReturnsAsync([CreateProduct(enterpriseId)]);

            var controller = CreateController(productService.Object, productRepository.Object, userRepository.Object, mapper.Object, enterpriseId);

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
            var productRepository = new Mock<IRepository<Product>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var productService = new Mock<IProductService<Product>>(MockBehavior.Strict);
            var mapper = CreateMapper();
            int? capturedPageNumber = null;
            int? capturedPageSize = null;

            productRepository
                .Setup(r => r.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Func<Product, bool>>()))
                .Callback<int?, int?, Func<Product, bool>?>((pageNumber, pageSize, _) =>
                {
                    capturedPageNumber = pageNumber;
                    capturedPageSize = pageSize;
                })
                .ReturnsAsync([CreateProduct(enterpriseId)]);

            var controller = CreateController(productService.Object, productRepository.Object, userRepository.Object, mapper.Object, enterpriseId);

            var result = await controller.GetProducts(pageNumber: 3, pageSize: 10000, includePictures: true);

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(3, capturedPageNumber);
            Assert.Equal(50, capturedPageSize);
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
            Guid enterpriseId)
        {
            var controller = new ProductController(productService, productRepository, userRepository, mapper);
            var claims = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.GroupSid, enterpriseId.ToString())
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
