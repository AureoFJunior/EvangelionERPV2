using Microsoft.Extensions.Configuration;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Security;
using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.Security;
using EvangelionERPV2.Web.Security.Iam;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Json;

namespace EvangelionERPV2.Test.Security.Iam
{
    // SPEC-iam-zero-trust IAM-2: sensitive and privileged permissions need MFA and a fresh code on this token.
    public class IamStepUpTests : IDisposable
    {
        private readonly IamTestDatabase _database = new();
        private readonly MemoryDistributedCache _cache = new(Options.Create(new MemoryDistributedCacheOptions()));
        private readonly MovableTime _time = new(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));
        private readonly Enterprise _enterprise = new() { Id = Guid.NewGuid(), Name = "Loja", IsActive = true };
        private readonly User _admin;

        public IamStepUpTests()
        {
            _admin = new User
            {
                Id = Guid.NewGuid(), FirstName = "Ana", LastName = "Lima", UserName = "ana", Email = "ana@loja.com", Password = "x",
                AccessLevel = (short)EnumAccessLevel.Admin, EnterpriseId = _enterprise.Id, IsActive = true, MfaEnabled = true
            };
            _database.SeedAsync(_enterprise, _admin).GetAwaiter().GetResult();
        }

        public void Dispose() => _database.Dispose();

        private static ClaimsPrincipal Token(User user, string jti = "token-a", bool machine = false)
        {
            var claims = new List<Claim> { new(ClaimTypes.Sid, user.Id.ToString()), new(ClaimTypes.GroupSid, user.EnterpriseId.ToString()!), new("jti", jti) };
            if (machine)
                claims.Add(new Claim(MachineClaims.ClientType, MachineClaims.SelfApiValue));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        }

        private PermissionEvaluator Evaluator() => new(_database.CreateContext(), _cache, _time);

        private Task<PermissionDecision> Decide(User user, string permission, string jti = "token-a", bool machine = false) =>
            Evaluator().DecideAsync(Token(user, jti, machine), user, permission);

        private Task SteppedUp(string jti = "token-a", TimeSpan? ago = null) =>
            StepUp.RecordAsync(_cache, jti, _time.GetUtcNow() - (ago ?? TimeSpan.Zero));

        [Fact]
        public async Task AStandardPermission_NeedsNoStepUp()
        {
            Assert.Equal(PermissionDecision.Allowed, await Decide(_admin, RbacPermissions.Orders.Read));
        }

        [Fact]
        public async Task WithEnforcementOff_HeldSensitivePermissionsWorkWithoutACode()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Iam:EnforceStepUp"] = "false" }).Build();
            var evaluator = new PermissionEvaluator(_database.CreateContext(), _cache, _time, configuration);
            _admin.MfaEnabled = false;

            Assert.Equal(PermissionDecision.Allowed, await evaluator.DecideAsync(Token(_admin), _admin, RbacPermissions.Bills.Settle));
            Assert.Contains(RbacPermissions.Bills.Settle, await evaluator.EffectiveAsync(Token(_admin), _admin));
        }

        [Fact]
        public async Task ASensitivePermission_WithoutMfa_AsksForMfa()
        {
            _admin.MfaEnabled = false;

            Assert.Equal(PermissionDecision.MfaRequired, await Decide(_admin, RbacPermissions.Bills.Settle));
        }

        [Fact]
        public async Task ASensitivePermission_WorksForFifteenMinutesAfterTheCode()
        {
            Assert.Equal(PermissionDecision.StepUpRequired, await Decide(_admin, RbacPermissions.Bills.Settle));

            await SteppedUp();
            Assert.Equal(PermissionDecision.Allowed, await Decide(_admin, RbacPermissions.Bills.Settle));

            _time.Advance(TimeSpan.FromMinutes(16));
            Assert.Equal(PermissionDecision.StepUpRequired, await Decide(_admin, RbacPermissions.Bills.Settle));
        }

        [Fact]
        public async Task APrivilegedPermission_HasAFiveMinuteWindow()
        {
            await SteppedUp(ago: TimeSpan.FromMinutes(4));
            Assert.Equal(PermissionDecision.Allowed, await Decide(_admin, RbacPermissions.Payroll.Read));
            Assert.Equal(PermissionDecision.Allowed, await Decide(_admin, RbacPermissions.Bills.Settle));

            _time.Advance(TimeSpan.FromMinutes(2));
            Assert.Equal(PermissionDecision.StepUpRequired, await Decide(_admin, RbacPermissions.Payroll.Read));
            Assert.Equal(PermissionDecision.Allowed, await Decide(_admin, RbacPermissions.Bills.Settle));
        }

        [Fact]
        public async Task AProofOnOneToken_DoesNotUnlockAnother()
        {
            await SteppedUp("token-a");

            Assert.Equal(PermissionDecision.StepUpRequired, await Decide(_admin, RbacPermissions.Bills.Settle, jti: "token-b"));
        }

        [Fact]
        public async Task TheMachineClient_CannotUseRolePermissions()
        {
            _admin.MfaEnabled = false;

            Assert.Equal(PermissionDecision.Denied, await Decide(_admin, RbacPermissions.Bills.Settle, machine: true));
            Assert.Empty(await Evaluator().HoldsAsync(Token(_admin, machine: true), _admin));
        }

        [Fact]
        public async Task TheMachineClient_CannotUseStorefrontTokenScope()
        {
            var principal = Token(_admin, machine: true);
            ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(MachineClaims.TokenScope, MachineClaims.StorefrontScopeValue));

            Assert.Equal(PermissionDecision.Denied,
                await Evaluator().DecideAsync(principal, _admin, RbacPermissions.Storefront.CatalogRead));
        }

        [Fact]
        public async Task TheMachineClient_NeverReachesIam()
        {
            Assert.Equal(PermissionDecision.Denied, await Decide(_admin, RbacPermissions.Iam.Manage, machine: true));
            Assert.Equal(PermissionDecision.Denied, await Decide(_admin, RbacPermissions.Iam.Read, machine: true));
            var holds = await Evaluator().HoldsAsync(Token(_admin, machine: true), _admin);
            Assert.DoesNotContain(holds, p => p.StartsWith("iam.", StringComparison.Ordinal));
            Assert.Empty(holds);
        }

        [Fact]
        public async Task ANonHolder_IsDeniedNotAskedForACode()
        {
            var clerk = new User { Id = Guid.NewGuid(), AccessLevel = (short)EnumAccessLevel.Employee, EnterpriseId = _enterprise.Id, IsActive = true, MfaEnabled = true };

            Assert.Equal(PermissionDecision.Denied, await Decide(clerk, RbacPermissions.Bills.Settle));
        }

        [Fact]
        public async Task GrantsMode_IsGatedTheSameWay()
        {
            var clerk = new User
            {
                Id = Guid.NewGuid(), FirstName = "Caio", LastName = "Reis", UserName = "caio", Email = "caio@loja.com", Password = "x",
                AccessLevel = (short)EnumAccessLevel.Employee, EnterpriseId = _enterprise.Id, IsActive = true, MfaEnabled = true
            };
            await _database.SeedAsync(clerk);
            var db = _database.CreateContext();
            var iam = new IamService(db, new Shared.Auditing.AuditTrailWriter(db), _cache, new Repository<Enterprise>(db), _time);
            await iam.SwitchAsync(_admin, EnumIamMode.Grants);
            await iam.ApplyAsync(_admin, clerk.Id, [new GrantChange(RbacPermissions.Bills.Settle, null, GrantState.Allow, null, "fechamento")]);
            using var read = _database.CreateContext();
            var fresh = read.User.AsNoTracking().Single(u => u.Id == clerk.Id);

            Assert.Equal(PermissionDecision.StepUpRequired, await Decide(fresh, RbacPermissions.Bills.Settle, jti: "clerk"));
            await SteppedUp("clerk");
            Assert.Equal(PermissionDecision.Allowed, await Decide(fresh, RbacPermissions.Bills.Settle, jti: "clerk"));
        }

        [Fact]
        public async Task Holds_ListsWhatIsHeld_AndEffectiveOnlyWhatWorksNow()
        {
            var holds = await Evaluator().HoldsAsync(Token(_admin), _admin);
            var usable = await Evaluator().EffectiveAsync(Token(_admin), _admin);

            Assert.Contains(RbacPermissions.Bills.Settle, holds);
            Assert.DoesNotContain(RbacPermissions.Bills.Settle, usable);
            Assert.Contains(RbacPermissions.Orders.Read, usable);

            await SteppedUp();
            Assert.Contains(RbacPermissions.Bills.Settle, await Evaluator().EffectiveAsync(Token(_admin), _admin));
        }

        [Fact]
        public async Task TheResultHandler_AnswersAForbiddenTheClientCanActOn()
        {
            var http = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
            var requirement = new PermissionRequirement(RbacPermissions.Payroll.Read);
            var context = new AuthorizationHandlerContext([requirement], Token(_admin), http);
            StepUpAuthorizationResultHandler.Remember(context, PermissionDecision.StepUpRequired, RbacPermissions.Payroll.Read);

            await new StepUpAuthorizationResultHandler().HandleAsync(_ => Task.CompletedTask, http,
                new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build(), PolicyAuthorizationResult.Forbid());

            Assert.Equal(StatusCodes.Status403Forbidden, http.Response.StatusCode);
            http.Response.Body.Position = 0;
            var body = JsonDocument.Parse(http.Response.Body).RootElement;
            Assert.Equal("step_up_required", body.GetProperty("code").GetString());
            Assert.Equal(RbacPermissions.Payroll.Read, body.GetProperty("permission").GetString());
            Assert.Equal(5, body.GetProperty("windowMinutes").GetInt32());
        }

        private IamController Controller(ClaimsPrincipal principal)
        {
            var db = _database.CreateContext();
            var mfa = new UserModule.Application.Services.MfaService(
                new Repository<User>(db), new Moq.Mock<IRepository<MfaRecoveryCode>>().Object, new Moq.Mock<IRepository<RefreshToken>>().Object,
                new Moq.Mock<Shared.Auditing.IAuditTrailWriter>().Object, _cache, _time);
            return new IamController(
                new IamService(db, new Shared.Auditing.AuditTrailWriter(db), _cache, new Repository<Enterprise>(db), _time),
                Evaluator(), new Repository<User>(db), mfa, _cache, _time)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } }
            };
        }

        private static string CodeOf(IActionResult result) =>
            JsonSerializer.SerializeToElement(((ObjectResult)result).Value).GetProperty("code").GetString()!;

        [Fact]
        public async Task StepUpEndpoint_WithoutATokenId_AsksToSignInAgain()
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Sid, _admin.Id.ToString()), new Claim(ClaimTypes.GroupSid, _enterprise.Id.ToString())], "test"));

            Assert.Equal("token_refresh_required", CodeOf(await Controller(principal).StepUp(new StepUpRequest { Code = "123456" })));
        }

        [Fact]
        public async Task StepUpEndpoint_WithNullBody_ReturnsBadRequest()
        {
            var result = await Controller(Token(_admin)).StepUp(null!);
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task StepUpEndpoint_RefusesAMalformedOrWrongCode()
        {
            var controller = Controller(Token(_admin));

            Assert.Equal("mfa_invalid", CodeOf(await controller.StepUp(new StepUpRequest { Code = "12" })));
            Assert.Equal("mfa_invalid", CodeOf(await controller.StepUp(new StepUpRequest { Code = "123456" })));
            Assert.Null(await StepUp.VerifiedAtAsync(_cache, Token(_admin)));
        }

        [Fact]
        public async Task IamController_ReadEndpointsAndStepUpLifecycleReturnTenantScopedResults()
        {
            var clerk = new User
            {
                Id = Guid.NewGuid(), FirstName = "Caio", LastName = "Reis", UserName = "caio", Email = "caio@loja.com", Password = "x",
                AccessLevel = (short)EnumAccessLevel.Employee, EnterpriseId = _enterprise.Id, IsActive = true
            };
            await _database.SeedAsync(clerk);
            var controller = Controller(Token(_admin));

            Assert.IsType<OkObjectResult>(controller.Catalog());
            Assert.IsType<OkObjectResult>(await controller.Access(clerk.Id));
            Assert.IsType<OkObjectResult>(await controller.Holders(RbacPermissions.Iam.Manage));
            Assert.IsType<OkObjectResult>(await controller.Reviews());
            Assert.IsType<OkObjectResult>(await controller.Me());

            await SteppedUp();
            Assert.IsType<NoContentResult>(await controller.EndStepUp());
            Assert.Null(await StepUp.VerifiedAtAsync(_cache, Token(_admin)));
            var missing = Assert.IsType<ObjectResult>(await controller.Access(Guid.NewGuid()));
            Assert.Equal(StatusCodes.Status404NotFound, missing.StatusCode);
        }

        [Fact]
        public async Task Me_ShowsWhatIsHeldAndWhatWorksUntilWhen()
        {
            await SteppedUp(ago: TimeSpan.FromMinutes(1));

            var me = Assert.IsType<MyAccessDTO>(Assert.IsType<OkObjectResult>(await Controller(Token(_admin)).Me()).Value);

            Assert.Contains(RbacPermissions.Payroll.Read, me.Holds);
            Assert.Contains(RbacPermissions.Payroll.Read, me.Usable);
            Assert.Equal(_time.GetUtcNow() + TimeSpan.FromMinutes(14), me.SensitiveUntil);
            Assert.Equal(_time.GetUtcNow() + TimeSpan.FromMinutes(4), me.PrivilegedUntil);
            Assert.Equal("privileged", me.Tiers[RbacPermissions.Payroll.Read]);
        }

        [Fact]
        public async Task EndStepUp_LocksAgain()
        {
            await SteppedUp();

            Assert.IsType<NoContentResult>(await Controller(Token(_admin)).EndStepUp());
            Assert.Equal(PermissionDecision.StepUpRequired, await Decide(_admin, RbacPermissions.Bills.Settle));
        }
    }
}
