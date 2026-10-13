using System.Linq.Expressions;
using System.Security.Claims;
using AutoMapper;
using EvangelionERPV2.CustomerModule.Application.Interface;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// Every repository mock in this suite runs filter predicates through
    /// <c>predicate.Compile()</c> against LINQ-to-Objects, so an expression that compiles in C#
    /// but has no SQL Server translation (e.g. <c>string.Contains(value, StringComparison)</c>)
    /// passes green in unit tests and throws <see cref="InvalidOperationException"/> in
    /// production. These tests capture the predicate the controller actually builds and push it
    /// through the real SQL Server query pipeline via <c>ToQueryString()</c>, which compiles the
    /// query without opening a connection.
    /// </summary>
    public class ControllerFilterPredicateSqlTranslationTests
    {
        [Fact]
        public async Task GetCustomersByFilter_PredicateTranslatesToSqlServer()
        {
            var enterpriseId = Guid.NewGuid();
            var repository = new Mock<IRepository<Customer>>(MockBehavior.Strict);
            Expression<Func<Customer, bool>>? capturedPredicate = null;

            repository
                .Setup(r => r.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Expression<Func<Customer, bool>>>()))
                .Callback<int?, int?, Expression<Func<Customer, bool>>?>((_, _, predicate) => capturedPredicate = predicate)
                .ReturnsAsync([]);

            var controller = new CustomerController(
                new Mock<ICustomerService<Customer>>(MockBehavior.Strict).Object,
                repository.Object,
                new Mock<IRepository<User>>(MockBehavior.Strict).Object,
                new Mock<ICustomerSummaryService>().Object,
                new Mock<ICustomerLocationService>().Object,
                Mock.Of<IAuthorizationService>(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<string>()) == Task.FromResult(AuthorizationResult.Failed())),
                new Mock<IMapper>(MockBehavior.Strict).Object);
            AttachEnterpriseClaim(controller, enterpriseId);

            await controller.GetCustomersByFilter(
                filter: new CustomerFilterRequestDTO
                {
                    Name = "acme",
                    Email = "buyer@example.com",
                    Document = "12345678909",
                    PhoneNumber = "11999998888",
                    IsActive = true
                },
                descending: false);

            Assert.NotNull(capturedPredicate);

            using var context = CreateSqlServerContext();
            var sql = context.Set<Customer>().Where(capturedPredicate!).ToQueryString();

            Assert.Contains("LIKE", sql, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task GetProductsByFilter_PredicateTranslatesToSqlServer()
        {
            var enterpriseId = Guid.NewGuid();
            var repository = new Mock<IRepository<Product>>(MockBehavior.Strict);
            Expression<Func<Product, bool>>? capturedPredicate = null;

            repository
                .Setup(r => r.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Expression<Func<Product, bool>>>()))
                .Callback<int?, int?, Expression<Func<Product, bool>>?>((_, _, predicate) => capturedPredicate = predicate)
                .ReturnsAsync([]);

            var controller = new ProductController(
                new Mock<IProductService<Product>>(MockBehavior.Strict).Object,
                repository.Object,
                new Mock<IRepository<User>>(MockBehavior.Strict).Object,
                new Mock<IMapper>(MockBehavior.Strict).Object,
                Mock.Of<IAuthorizationService>(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<string>()) == Task.FromResult(AuthorizationResult.Failed())));
            AttachEnterpriseClaim(controller, enterpriseId);

            await controller.GetProductsByFilter(
                filter: new ProductFilterRequestDTO { Name = "widget", IsActive = true, VariantGroupIds = [Guid.NewGuid()] },
                descending: false);

            Assert.NotNull(capturedPredicate);

            using var context = CreateSqlServerContext();
            var sql = context.Set<Product>().Where(capturedPredicate!).ToQueryString();

            Assert.Contains("LIKE", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[VariantGroupId]", sql, StringComparison.OrdinalIgnoreCase);
        }

        private static AppDbContext CreateSqlServerContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer("Server=translation-check-only;Database=none;Encrypt=false")
                .Options;

            return new AppDbContext(options);
        }

        private static void AttachEnterpriseClaim(ControllerBase controller, Guid enterpriseId)
        {
            var claims = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.GroupSid, enterpriseId.ToString())
            ], "TestAuth"));

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = claims }
            };
        }
    }
}
