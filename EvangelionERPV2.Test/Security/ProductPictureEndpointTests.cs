using AutoMapper;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
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
    /// Review spec P7: product lists stop carrying base64 and load each photo from
    /// <c>Product/Picture/{id}</c>, which must stay inside the caller's tenant and be cacheable.
    /// </summary>
    public class ProductPictureEndpointTests
    {
        private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

        private sealed class Harness
        {
            public Mock<IProductService<Product>> ProductService { get; } = new();
            public ProductController Controller { get; }

            public Harness(Guid callerEnterprise, params Product[] stored)
            {
                var products = new Mock<IRepository<Product>>();
                foreach (var product in stored)
                    products.Setup(r => r.GetByIdAsync(product.Id)).ReturnsAsync(product);

                ProductService.Setup(s => s.GetThumbnailBase64Async(It.IsAny<string?>())).ReturnsAsync((Convert.ToBase64String(Png), "h"));
                ProductService.Setup(s => s.GetPictureBase64Async(It.IsAny<string?>())).ReturnsAsync(Convert.ToBase64String(Png));

                Controller = new ProductController(
                    ProductService.Object, products.Object, Mock.Of<IRepository<User>>(), Mock.Of<IMapper>(),
                    Mock.Of<IAuthorizationService>());
                Controller.ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.GroupSid, callerEnterprise.ToString())], "TestAuth"))
                    }
                };
            }
        }

        private static Product Photographed(Guid enterpriseId, string key = "products/abc.png") =>
            new() { Id = Guid.NewGuid(), EnterpriseId = enterpriseId, IsActive = true, PictureAdress = key };

        [Fact]
        public async Task TheThumbnail_IsServedAsAnImage_WithAPrivateCacheAndAnETag()
        {
            var tenant = Guid.NewGuid();
            var product = Photographed(tenant);
            var harness = new Harness(tenant, product);

            var file = Assert.IsType<FileContentResult>(await harness.Controller.Picture(product.Id));

            Assert.Equal("image/png", file.ContentType);
            Assert.Equal(Png, file.FileContents);
            Assert.Equal("private, max-age=300", harness.Controller.Response.Headers.CacheControl.ToString());
            Assert.StartsWith("\"", harness.Controller.Response.Headers.ETag.ToString());
            harness.ProductService.Verify(s => s.GetThumbnailBase64Async("products/abc.png"), Times.Once);
            harness.ProductService.Verify(s => s.GetPictureBase64Async(It.IsAny<string?>()), Times.Never);
        }

        [Fact]
        public async Task ACallerWhoAlreadyHasIt_Gets304_AndNothingIsReadFromStorage()
        {
            var tenant = Guid.NewGuid();
            var product = Photographed(tenant);
            var first = new Harness(tenant, product);
            await first.Controller.Picture(product.Id);
            var etag = first.Controller.Response.Headers.ETag.ToString();

            var again = new Harness(tenant, product);
            again.Controller.Request.Headers.IfNoneMatch = etag;
            var result = await again.Controller.Picture(product.Id);

            Assert.Equal(StatusCodes.Status304NotModified, Assert.IsType<StatusCodeResult>(result).StatusCode);
            again.ProductService.Verify(s => s.GetThumbnailBase64Async(It.IsAny<string?>()), Times.Never);
        }

        [Fact]
        public async Task ANewPhoto_GetsANewETag_AndFullAndThumbDiffer()
        {
            var tenant = Guid.NewGuid();
            var before = Photographed(tenant, "products/v1.png");
            var after = Photographed(tenant, "products/v2.png");
            after.Id = before.Id;

            var one = new Harness(tenant, before);
            await one.Controller.Picture(before.Id);
            var two = new Harness(tenant, after);
            await two.Controller.Picture(after.Id);
            var full = new Harness(tenant, after);
            await full.Controller.Picture(after.Id, "full");

            Assert.NotEqual(one.Controller.Response.Headers.ETag.ToString(), two.Controller.Response.Headers.ETag.ToString());
            Assert.NotEqual(two.Controller.Response.Headers.ETag.ToString(), full.Controller.Response.Headers.ETag.ToString());
            full.ProductService.Verify(s => s.GetPictureBase64Async("products/v2.png"), Times.Once);
        }

        [Fact]
        public async Task AnotherTenantsProduct_OrOneWithoutAPhoto_IsNotFound()
        {
            var tenant = Guid.NewGuid();
            var foreign = Photographed(Guid.NewGuid());
            var bare = Photographed(tenant, "");
            var harness = new Harness(tenant, foreign, bare);

            Assert.IsType<NotFoundResult>(await harness.Controller.Picture(foreign.Id));
            Assert.IsType<NotFoundResult>(await harness.Controller.Picture(bare.Id));
            Assert.IsType<NotFoundResult>(await harness.Controller.Picture(Guid.NewGuid()));
            Assert.IsType<BadRequestObjectResult>(await harness.Controller.Picture(bare.Id, "huge"));
        }

        [Fact]
        public async Task ASingleProduct_SaysWhetherItsFamilyHasAPhoto_SoAnEditDoesNotDropTheCover()
        {
            var tenant = Guid.NewGuid();
            var group = Guid.NewGuid();
            var product = new Product { Id = Guid.NewGuid(), EnterpriseId = tenant, IsActive = true, VariantGroupId = group, Name = "Caneca" };
            var products = new Mock<IRepository<Product>>();
            products.Setup(r => r.GetByIdAsync(product.Id)).ReturnsAsync(product);
            var service = new Mock<IProductService<Product>>();
            service.Setup(s => s.FamiliesWithPictureAsync(tenant, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(group)))).ReturnsAsync([group]);
            var authorization = new Mock<IAuthorizationService>();
            authorization.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<string>()))
                .ReturnsAsync(AuthorizationResult.Failed());
            var controller = new ProductController(service.Object, products.Object, Mock.Of<IRepository<User>>(),
                EvangelionERPV2.Shared.Configs.MapperConfig.RegisterMaps().CreateMapper(), authorization.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.GroupSid, tenant.ToString())], "TestAuth"))
                    }
                }
            };

            var dto = Assert.IsType<EvangelionERPV2.Shared.DTOs.ProductDTO>(Assert.IsType<OkObjectResult>(await controller.GetProduct(product.Id)).Value);

            Assert.True(dto.FamilyHasPicture);
        }
    }
}
