using Amazon.SecretsManager;
using AutoMapper;
using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.UserModule.Application.Interface;
using EvangelionERPV2.UserModule.Application.Token;
using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Reflection;

namespace EvangelionERPV2.Test.Security
{
    public class UserControllerResetPasswordRateLimitCleanupTests
    {
        [Fact]
        public void IsResetPasswordRateLimited_CleansUpExpiredEntries()
        {
            var controller = CreateController();
            var dictionary = GetResetPasswordRateLimitDictionary();

            ClearRateLimitState(dictionary);
            AddRateLimitEntry(dictionary, "expired", DateTime.UtcNow.AddHours(-1), 1);
            AddRateLimitEntry(dictionary, "active", DateTime.UtcNow, 1);

            var result = InvokeIsResetPasswordRateLimited(controller, "missing");

            Assert.False(result);
            Assert.False(ContainsRateLimitKey(dictionary, "expired"));
            Assert.True(ContainsRateLimitKey(dictionary, "active"));
        }

        [Fact]
        public void RegisterResetPasswordFailure_CleansUpExpiredEntriesBeforeTracking()
        {
            var controller = CreateController();
            var dictionary = GetResetPasswordRateLimitDictionary();

            ClearRateLimitState(dictionary);
            AddRateLimitEntry(dictionary, "expired", DateTime.UtcNow.AddHours(-1), 1);

            InvokeRegisterResetPasswordFailure(controller, "new-key");

            Assert.False(ContainsRateLimitKey(dictionary, "expired"));
            Assert.True(ContainsRateLimitKey(dictionary, "new-key"));
        }

        private static UserController CreateController()
        {
            var userService = new Mock<IUserService<User>>(MockBehavior.Strict).Object;
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict).Object;
            var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
            var mapper = new Mock<IMapper>(MockBehavior.Strict).Object;
            var kmsProvider = new AWSKMSKeyProvider(new Mock<IAmazonSecretsManager>(MockBehavior.Strict).Object, configuration);
            var refreshTokenRepository = new Mock<IRepository<RefreshToken>>(MockBehavior.Strict).Object;
            var tokenService = new TokenService(refreshTokenRepository, configuration);
            var emailService = new Mock<IEmailService<EmailStructure>>(MockBehavior.Strict).Object;

            return new UserController(
                userService,
                userRepository,
                mapper,
                configuration,
                kmsProvider,
                tokenService,
                emailService);
        }

        private static object GetResetPasswordRateLimitDictionary()
        {
            var field = typeof(UserController).GetField("_resetPasswordRateLimit", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(field);
            var dictionary = field!.GetValue(null);
            Assert.NotNull(dictionary);
            return dictionary!;
        }

        private static void ClearRateLimitState(object dictionary)
        {
            dictionary.GetType().GetMethod("Clear")!.Invoke(dictionary, null);
            var cleanupField = typeof(UserController).GetField("_lastResetPasswordRateLimitCleanupTicks", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(cleanupField);
            cleanupField!.SetValue(null, 0L);
        }

        private static void AddRateLimitEntry(object dictionary, string key, DateTime windowStartedAt, int failedAttempts)
        {
            var entryType = typeof(UserController).GetNestedType("ResetPasswordRateLimitEntry", BindingFlags.NonPublic);
            Assert.NotNull(entryType);

            var constructor = entryType!
                .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(ctor =>
                {
                    var parameters = ctor.GetParameters();
                    return parameters.Length == 2 &&
                           parameters[0].ParameterType == typeof(DateTime) &&
                           parameters[1].ParameterType == typeof(int);
                });
            Assert.NotNull(constructor);

            var entry = constructor!.Invoke([windowStartedAt, failedAttempts]);

            var tryAddMethod = dictionary.GetType().GetMethod("TryAdd");
            Assert.NotNull(tryAddMethod);

            var added = (bool)tryAddMethod!.Invoke(dictionary, [key, entry])!;
            Assert.True(added);
        }

        private static bool ContainsRateLimitKey(object dictionary, string key)
        {
            var containsKeyMethod = dictionary.GetType().GetMethod("ContainsKey");
            Assert.NotNull(containsKeyMethod);
            return (bool)containsKeyMethod!.Invoke(dictionary, [key])!;
        }

        private static bool InvokeIsResetPasswordRateLimited(UserController controller, string key)
        {
            var method = typeof(UserController).GetMethod("IsResetPasswordRateLimited", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            return (bool)method!.Invoke(controller, [key])!;
        }

        private static void InvokeRegisterResetPasswordFailure(UserController controller, string key)
        {
            var method = typeof(UserController).GetMethod("RegisterResetPasswordFailure", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            method!.Invoke(controller, [key]);
        }
    }
}
