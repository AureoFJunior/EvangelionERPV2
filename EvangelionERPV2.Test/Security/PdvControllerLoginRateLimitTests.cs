using EvangelionERPV2.PdvModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.UserModule.Application.Token;
using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Linq.Expressions;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// Without a limit this endpoint is a password oracle: a till sits on a shop floor, the
    /// credential on it is assumed to leak, and nothing else stands between a guesser and every
    /// cashier's password.
    /// </summary>
    /// <remarks>
    /// The counters are process-static, as the reset-password limiter they copy is, so every test
    /// here uses a terminal id of its own. Sharing one would make these tests each other's load.
    /// </remarks>
    public class PdvControllerLoginRateLimitTests
    {
        private const int MaxFailedAttempts = 5;

        [Fact]
        public async Task Login_IsRateLimited_PerUserName()
        {
            var terminalId = UniqueTerminalId();
            var controller = CreateController(terminalId);

            for (var attempt = 0; attempt < MaxFailedAttempts; attempt++)
                Assert.IsType<UnauthorizedResult>(await Attempt(controller, "caixa01"));

            Assert.Equal(StatusCodes.Status429TooManyRequests,
                Assert.IsType<StatusCodeResult>(await Attempt(controller, "caixa01")).StatusCode);
        }

        [Fact]
        public async Task Login_IsRateLimited_PerTerminal_EvenAcrossUserNames()
        {
            // The one that matters: limiting per username alone lets an attacker spray one password
            // across every cashier's account from a single till and never trip a thing.
            var terminalId = UniqueTerminalId();
            var controller = CreateController(terminalId);

            for (var attempt = 0; attempt < MaxFailedAttempts; attempt++)
                Assert.IsType<UnauthorizedResult>(await Attempt(controller, $"user{attempt}"));

            Assert.Equal(StatusCodes.Status429TooManyRequests,
                Assert.IsType<StatusCodeResult>(await Attempt(controller, "another")).StatusCode);
        }

        [Fact]
        public async Task Login_DoesNotCountOneTerminalsFailuresAgainstAnother()
        {
            var noisyTerminal = CreateController(UniqueTerminalId());
            var quietTerminal = CreateController(UniqueTerminalId());

            for (var attempt = 0; attempt < MaxFailedAttempts + 1; attempt++)
                await Attempt(noisyTerminal, "caixa01");

            // A revoked or guessed-at till must not lock out the one next to it.
            Assert.IsType<UnauthorizedResult>(await Attempt(quietTerminal, "caixa01"));
        }

        private static Task<IActionResult> Attempt(PdvController controller, string userName) =>
            controller.Login(new PdvLoginRequestDTO { UserName = userName, Password = "wrong" });

        private static string UniqueTerminalId() => $"CAIXA-{Guid.NewGuid():N}"[..24];

        /// <summary>
        /// A controller whose terminal credential is right and whose passwords are all wrong, so
        /// every attempt fails at the password and nothing reaches token generation.
        /// </summary>
        private static PdvController CreateController(string terminalId)
        {
            var terminalRepository = new Mock<IRepository<PdvTerminal>>();
            terminalRepository
                .Setup(x => x.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Expression<Func<PdvTerminal, bool>>>()))
                .ReturnsAsync([PdvTerminal.Enroll(Guid.NewGuid(), terminalId, "cred")]);

            var userRepository = new Mock<IRepository<User>>();
            userRepository
                .Setup(x => x.GetAllAsyncByFilter(
                    It.IsAny<bool>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<Expression<Func<User, bool>>>(),
                    It.IsAny<Expression<Func<User, object>>>()))
                .ReturnsAsync([]);

            var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

            var controller = new PdvController(
                new Mock<IPdvService>(MockBehavior.Strict).Object,
                terminalRepository.Object,
                userRepository.Object,
                new PasswordLoginService(userRepository.Object),
                new TokenService(new Mock<IRepository<RefreshToken>>(MockBehavior.Strict).Object, configuration))
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };

            controller.ControllerContext.HttpContext.Request.Headers["X-Pdv-Terminal"] = $"{terminalId}:cred";

            return controller;
        }
    }
}
