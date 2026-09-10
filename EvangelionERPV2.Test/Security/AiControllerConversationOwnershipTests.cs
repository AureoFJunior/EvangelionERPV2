using EvangelionERPV2.AiModule.Application.Configs;
using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Claims;

namespace EvangelionERPV2.Test.Security
{
    public class AiControllerConversationOwnershipTests
    {
        private readonly Mock<IAiAgentService> _agentServiceMock = new();
        private readonly Mock<IRepository<AiConversation>> _conversationsMock = new(MockBehavior.Loose);
        private readonly Mock<IRepository<User>> _usersMock = new();

        [Fact]
        public async Task Chat_WhenSuppliedConversationBelongsToAnotherUser_StartsANewConversationInstead()
        {
            var enterpriseId = Guid.NewGuid();
            var callerUserId = Guid.NewGuid();
            var suppliedConversationId = Guid.NewGuid();
            var someoneElsesConversation = new AiConversation
            {
                Id = suppliedConversationId,
                EnterpriseId = enterpriseId,
                UserId = Guid.NewGuid(),
            };

            _conversationsMock
                .Setup(x => x.GetByIdAsync(suppliedConversationId))
                .ReturnsAsync(someoneElsesConversation);

            AiConversation? createdConversation = null;
            _conversationsMock
                .Setup(x => x.CreateAsync(It.IsAny<AiConversation>()))
                .Callback<AiConversation>(c => createdConversation = c)
                .ReturnsAsync((AiConversation c) => c);

            AiTurnRequest? capturedRequest = null;
            _agentServiceMock
                .Setup(x => x.RunTurnAsync(It.IsAny<AiTurnRequest>(), It.IsAny<CancellationToken>()))
                .Callback<AiTurnRequest, CancellationToken>((request, _) => capturedRequest = request)
                .ReturnsAsync(new AiTurnResult("ok", Array.Empty<string>(), 1, 1, 1));

            var controller = CreateController(enterpriseId, callerUserId);

            var response = await controller.Chat(
                new AiChatRequestDTO { ConversationId = suppliedConversationId, Message = "oi" },
                CancellationToken.None);

            Assert.IsType<OkObjectResult>(response);
            _conversationsMock.Verify(x => x.CreateAsync(It.IsAny<AiConversation>()), Times.Once);
            Assert.NotNull(createdConversation);
            Assert.NotEqual(suppliedConversationId, createdConversation!.Id);
            Assert.NotNull(capturedRequest);
            Assert.NotEqual(suppliedConversationId, capturedRequest!.ConversationId);
            Assert.Equal(createdConversation.Id, capturedRequest.ConversationId);
        }

        [Fact]
        public async Task Chat_WhenSuppliedConversationBelongsToTheCaller_ReusesItUnchanged()
        {
            var enterpriseId = Guid.NewGuid();
            var callerUserId = Guid.NewGuid();
            var suppliedConversationId = Guid.NewGuid();
            var ownConversation = new AiConversation
            {
                Id = suppliedConversationId,
                EnterpriseId = enterpriseId,
                UserId = callerUserId,
            };

            _conversationsMock
                .Setup(x => x.GetByIdAsync(suppliedConversationId))
                .ReturnsAsync(ownConversation);

            AiTurnRequest? capturedRequest = null;
            _agentServiceMock
                .Setup(x => x.RunTurnAsync(It.IsAny<AiTurnRequest>(), It.IsAny<CancellationToken>()))
                .Callback<AiTurnRequest, CancellationToken>((request, _) => capturedRequest = request)
                .ReturnsAsync(new AiTurnResult("ok", Array.Empty<string>(), 1, 1, 1));

            var controller = CreateController(enterpriseId, callerUserId);

            var response = await controller.Chat(
                new AiChatRequestDTO { ConversationId = suppliedConversationId, Message = "oi" },
                CancellationToken.None);

            Assert.IsType<OkObjectResult>(response);
            _conversationsMock.Verify(x => x.CreateAsync(It.IsAny<AiConversation>()), Times.Never);
            Assert.NotNull(capturedRequest);
            Assert.Equal(suppliedConversationId, capturedRequest!.ConversationId);
        }

        [Fact]
        public async Task Chat_WithNoConversationId_StartsAFreshThread()
        {
            var enterpriseId = Guid.NewGuid();
            var callerUserId = Guid.NewGuid();

            AiConversation? createdConversation = null;
            _conversationsMock
                .Setup(x => x.CreateAsync(It.IsAny<AiConversation>()))
                .Callback<AiConversation>(c => createdConversation = c)
                .ReturnsAsync((AiConversation c) => c);

            _agentServiceMock
                .Setup(x => x.RunTurnAsync(It.IsAny<AiTurnRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AiTurnResult("ok", Array.Empty<string>(), 1, 1, 1));

            var controller = CreateController(enterpriseId, callerUserId);

            var response = await controller.Chat(
                new AiChatRequestDTO { ConversationId = null, Message = "oi" },
                CancellationToken.None);

            Assert.IsType<OkObjectResult>(response);
            _conversationsMock.Verify(x => x.CreateAsync(It.IsAny<AiConversation>()), Times.Once);
            Assert.NotNull(createdConversation);
            _conversationsMock.Verify(x => x.GetByIdAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task Chat_WithATillScopedToken_OffersNoToolOutsideThePdvScope()
        {
            var enterpriseId = Guid.NewGuid();
            var callerUserId = Guid.NewGuid();

            AiTurnRequest? capturedRequest = null;
            _agentServiceMock
                .Setup(x => x.RunTurnAsync(It.IsAny<AiTurnRequest>(), It.IsAny<CancellationToken>()))
                .Callback<AiTurnRequest, CancellationToken>((request, _) => capturedRequest = request)
                .ReturnsAsync(new AiTurnResult("ok", Array.Empty<string>(), 1, 1, 1));

            _conversationsMock
                .Setup(x => x.CreateAsync(It.IsAny<AiConversation>()))
                .ReturnsAsync((AiConversation c) => c);

            var controller = CreateController(enterpriseId, callerUserId, pdvScoped: true);

            await controller.Chat(new AiChatRequestDTO { Message = "oi" }, CancellationToken.None);

            Assert.NotNull(capturedRequest);
            Assert.All(capturedRequest!.GrantedPermissions,
                permission => Assert.StartsWith(PdvTokenScope.PermissionPrefix, permission, StringComparison.Ordinal));
        }

        [Fact]
        public async Task Chat_WithAnOrdinaryToken_StillOffersToolsOutsideThePdvScope()
        {
            var enterpriseId = Guid.NewGuid();
            var callerUserId = Guid.NewGuid();

            AiTurnRequest? capturedRequest = null;
            _agentServiceMock
                .Setup(x => x.RunTurnAsync(It.IsAny<AiTurnRequest>(), It.IsAny<CancellationToken>()))
                .Callback<AiTurnRequest, CancellationToken>((request, _) => capturedRequest = request)
                .ReturnsAsync(new AiTurnResult("ok", Array.Empty<string>(), 1, 1, 1));

            _conversationsMock
                .Setup(x => x.CreateAsync(It.IsAny<AiConversation>()))
                .ReturnsAsync((AiConversation c) => c);

            var controller = CreateController(enterpriseId, callerUserId);

            await controller.Chat(new AiChatRequestDTO { Message = "oi" }, CancellationToken.None);

            Assert.NotNull(capturedRequest);
            Assert.Contains(capturedRequest!.GrantedPermissions,
                permission => !permission.StartsWith(PdvTokenScope.PermissionPrefix, StringComparison.Ordinal));
        }

        private AiController CreateController(Guid enterpriseId, Guid callerUserId, bool pdvScoped = false)
        {
            _usersMock
                .Setup(x => x.GetByIdAsync(callerUserId))
                .ReturnsAsync(new User
                {
                    Id = callerUserId,
                    EnterpriseId = enterpriseId,
                    AccessLevel = (short)EnumAccessLevel.Supervisor,
                    IsActive = true,
                });

            var settings = Options.Create(new AiSettings { Enabled = true });

            var controller = new AiController(
                _agentServiceMock.Object,
                _conversationsMock.Object,
                _usersMock.Object,
                settings);

            var claims = new List<Claim>
            {
                new(ClaimTypes.GroupSid, enterpriseId.ToString()),
                new(ClaimTypes.Sid, callerUserId.ToString()),
            };

            if (pdvScoped)
                claims.Add(new Claim(PdvTokenScope.ClaimType, PdvTokenScope.ClaimValue));
            var identity = new ClaimsIdentity(claims, "TestAuthType");
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity),
                },
            };

            return controller;
        }
    }
}
