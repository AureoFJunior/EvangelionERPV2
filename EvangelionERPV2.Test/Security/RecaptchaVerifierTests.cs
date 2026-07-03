using Amazon.SecretsManager;
using AutoMapper;
using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.UserModule.Application.Interface;
using EvangelionERPV2.UserModule.Application.Token;
using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;

namespace EvangelionERPV2.Test.Security
{
    public class RecaptchaVerifierTests
    {
        [Fact]
        public async Task LogInto_WhenRecaptchaEnabledAndTokenMissing_ReturnsBadRequest()
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RecaptchaSettings:Enabled"] = "true"
            }).Build();
            var userService = new Mock<IUserService<User>>(MockBehavior.Strict);
            var kmsProvider = new AWSKMSKeyProvider(new Mock<IAmazonSecretsManager>(MockBehavior.Strict).Object, configuration);
            var tokenService = new TokenService(new Mock<IRepository<RefreshToken>>(MockBehavior.Strict).Object, configuration);

            var controller = new UserController(
                userService.Object,
                new Mock<IRepository<User>>(MockBehavior.Strict).Object,
                new Mock<IMapper>(MockBehavior.Strict).Object,
                configuration,
                kmsProvider,
                tokenService,
                new Mock<IEmailService<EmailStructure>>(MockBehavior.Strict).Object,
                new RecaptchaVerifier(new HttpClient(), configuration, kmsProvider))
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };

            var result = await controller.LogInto(new LoginRequestDTO
            {
                UserName = "someone",
                Password = "Password1!",
                RecaptchaToken = string.Empty
            });

            Assert.IsType<BadRequestObjectResult>(result);
            userService.Verify(x => x.Update(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task VerifyAsync_WhenDisabled_ReturnsSuccessWithoutNetworkCall()
        {
            var verifier = CreateVerifier(enabled: false, secretKey: null);

            var result = await verifier.VerifyAsync(token: null, remoteIp: null);

            Assert.Equal(RecaptchaVerificationStatus.Success, result.Status);
        }

        [Fact]
        public async Task VerifyAsync_WhenEnabledAndTokenMissing_ReturnsMissing()
        {
            var verifier = CreateVerifier(enabled: true, secretKey: "plain:dummy-secret");

            var result = await verifier.VerifyAsync(token: "   ", remoteIp: "127.0.0.1");

            Assert.Equal(RecaptchaVerificationStatus.Missing, result.Status);
        }

        [Fact]
        public async Task VerifyAsync_WhenEnabledAndSecretMissing_FailsClosed()
        {
            var verifier = CreateVerifier(enabled: true, secretKey: null);

            var result = await verifier.VerifyAsync(token: "client-token", remoteIp: "127.0.0.1");

            Assert.Equal(RecaptchaVerificationStatus.Failed, result.Status);
        }

        private static RecaptchaVerifier CreateVerifier(bool enabled, string? secretKey)
        {
            var settings = new Dictionary<string, string?>
            {
                ["RecaptchaSettings:Enabled"] = enabled ? "true" : "false"
            };
            if (secretKey != null)
                settings["RecaptchaSettings:SecretKey"] = secretKey;

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            var kmsProvider = new AWSKMSKeyProvider(new Mock<IAmazonSecretsManager>(MockBehavior.Strict).Object, configuration);

            return new RecaptchaVerifier(new HttpClient(), configuration, kmsProvider);
        }
    }
}
