using AutoMapper;
using EvangelionERPV2.EnterpriseModule.Application.Interface;
using EvangelionERPV2.EnterpriseModule.Domain.Interface;
using EvangelionERPV2.Shared.Auditing;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.Security;
using EvangelionERPV2.Web.Security.Iam;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Claims;

namespace EvangelionERPV2.Test.Security.Iam
{
    // SPEC-iam-zero-trust IAM-4: new enterprises start in zero trust, and their admin can bootstrap access.
    public class IamNewEnterpriseTests : IDisposable
    {
        private readonly IamTestDatabase _database = new();
        private readonly MovableTime _time = new(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));
        private readonly Enterprise _enterprise = new() { Id = Guid.NewGuid(), Name = "Nova", IsActive = true, IamMode = EnumIamMode.Grants };

        public void Dispose() => _database.Dispose();

        private static MemoryDistributedCache NewCache() => new(Options.Create(new MemoryDistributedCacheOptions()));

        private User NewUser(string name, EnumAccessLevel level, bool mfa = true) => new()
        {
            Id = Guid.NewGuid(), FirstName = name, LastName = "Silva", UserName = name.ToLowerInvariant(), Email = $"{name}@nova.com", Password = "x",
            AccessLevel = (short)level, EnterpriseId = _enterprise.Id, IsActive = true, MfaEnabled = mfa
        };

        private static ClaimsPrincipal Token(User user) =>
            new(new ClaimsIdentity([new Claim(ClaimTypes.Sid, user.Id.ToString()), new Claim(ClaimTypes.GroupSid, user.EnterpriseId.ToString()!)], "test"));

        private async Task<IReadOnlySet<string>> Holds(User user) =>
            await new PermissionEvaluator(_database.CreateContext(), NewCache(), _time).HoldsAsync(Token(user), user);

        [Fact]
        public async Task WithNoAccessManagerYet_TheAdminCanManageAccess_AndNobodyElse()
        {
            var admin = NewUser("Ana", EnumAccessLevel.Admin);
            var manager = NewUser("Bia", EnumAccessLevel.Manager);
            await _database.SeedAsync(_enterprise, admin, manager);

            Assert.Contains(RbacPermissions.Iam.Manage, await Holds(admin));
            Assert.DoesNotContain(RbacPermissions.Orders.Read, await Holds(admin));
            Assert.DoesNotContain(RbacPermissions.Iam.Manage, await Holds(manager));
        }

        [Fact]
        public async Task OnceSomeoneIsGrantedIamManage_TheAdminRoleStopsCarryingIt()
        {
            var admin = NewUser("Ana", EnumAccessLevel.Admin);
            var owner = NewUser("Bia", EnumAccessLevel.Admin);
            await _database.SeedAsync(_enterprise, admin, owner);
            var db = _database.CreateContext();
            var iam = new IamService(db, new AuditTrailWriter(db), NewCache(), new Repository<Enterprise>(db), _time);

            await iam.ApplyAsync(admin, owner.Id, [new GrantChange(RbacPermissions.Iam.Manage, null, GrantState.Allow, null, "dona da empresa")]);

            Assert.DoesNotContain(RbacPermissions.Iam.Manage, await Holds(admin));
            using var read = _database.CreateContext();
            Assert.Contains(RbacPermissions.Iam.Manage, await Holds(read.User.Single(u => u.Id == owner.Id)));
        }

        [Theory]
        [InlineData(null, EnumIamMode.Grants)]
        [InlineData("true", EnumIamMode.Grants)]
        [InlineData("false", EnumIamMode.Role)]
        public async Task ANewEnterprise_StartsInTheConfiguredMode(string? setting, EnumIamMode expected)
        {
            Enterprise? created = null;
            var service = new Mock<IEnterpriseService<Enterprise>>();
            service.Setup(s => s.CreateAsync(It.IsAny<Enterprise>()))
                .Callback<Enterprise>(e => created = e)
                .ReturnsAsync((Enterprise e) => e);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(setting == null ? [] : new Dictionary<string, string?> { ["Iam:NewEnterprisesStartInGrantsMode"] = setting })
                .Build();
            var controller = new EnterpriseController(service.Object, new Mock<IRepository<Enterprise>>().Object, new Mock<IEnterpriseRepository<Enterprise>>().Object,
                new Mock<IMapper>().Object, new Mock<IAuthorizationService>().Object, configuration)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.GroupSid, Guid.NewGuid().ToString())], "test")) }
                }
            };

            await controller.AddEnterprise(new CreateEnterpriseRequestDTO { Name = "Nova", Email = "a@nova.com", PhoneNumber = "11", Adress = "Rua" });

            Assert.Equal(expected, created!.IamMode);
        }
    }
}
