using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.UserModule.Application.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Linq.Expressions;
using UserEntity = EvangelionERPV2.Shared.Entities.User;

namespace EvangelionERPV2.UserModule.Test.User
{
    // APL-02 / APL-05: who a Sign in with Apple token is, and linking it from the profile.
    public class AppleSignInServiceTests
    {
        private const string Nonce = AppleTestTokens.RawNonce;
        private readonly AppleTestTokens _apple = new();
        private readonly List<UserEntity> _users = new();
        private readonly Mock<IRepository<UserEntity>> _repository = new();
        private readonly AppleSignInService _service;

        public AppleSignInServiceTests()
        {
            _repository.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<UserEntity, bool>>>()))
                .ReturnsAsync((Expression<Func<UserEntity, bool>> p) => _users.Where(p.Compile()).ToList());
            _repository.Setup(r => r.Update(It.IsAny<UserEntity>())).Returns((UserEntity u) => u);
            _service = new AppleSignInService(_apple.Validator(), _repository.Object);
        }

        private UserEntity AddUser(string email, string? appleSubject = null, bool active = true)
        {
            var user = new UserEntity { Id = Guid.NewGuid(), Email = email, AppleSubject = appleSubject, IsActive = active };
            _users.Add(user);
            return user;
        }

        [Fact]
        public async Task SignIn_ByAppleId_FindsTheLinkedUserWhateverTheEmail()
        {
            var ana = AddUser("ana@erp.com", appleSubject: "001234.abcdef.0001");

            var result = await _service.SignInAsync(_apple.Token(email: "x1y2@privaterelay.appleid.com"), Nonce);

            Assert.Equal(AppleSignInOutcome.SignedIn, result.Outcome);
            Assert.Same(ana, result.User);
        }

        [Fact]
        public async Task SignIn_ByVerifiedEmail_LinksTheOnlyActiveMatch()
        {
            var ana = AddUser("Ana@Example.com");

            var result = await _service.SignInAsync(_apple.Token(), Nonce);

            Assert.Equal(AppleSignInOutcome.SignedIn, result.Outcome);
            Assert.Same(ana, result.User);
            Assert.Equal("001234.abcdef.0001", ana.AppleSubject);
            _repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SignIn_WithAPrivateRelayEmail_IsNotLinked()
        {
            AddUser("x1y2@privaterelay.appleid.com");

            var result = await _service.SignInAsync(_apple.Token(email: "x1y2@privaterelay.appleid.com"), Nonce);

            Assert.Equal(AppleSignInOutcome.NotLinked, result.Outcome);
            Assert.All(_users, u => Assert.Null(u.AppleSubject));
        }

        [Fact]
        public async Task SignIn_WithAnUnverifiedEmail_IsNotLinked()
        {
            AddUser("ana@example.com");

            Assert.Equal(AppleSignInOutcome.NotLinked, (await _service.SignInAsync(_apple.Token(emailVerified: "false"), Nonce)).Outcome);
        }

        [Fact]
        public async Task SignIn_WhenTwoActiveUsersShareTheEmail_IsNotLinked()
        {
            AddUser("ana@example.com");
            AddUser("ana@example.com");

            Assert.Equal(AppleSignInOutcome.NotLinked, (await _service.SignInAsync(_apple.Token(), Nonce)).Outcome);
            Assert.All(_users, u => Assert.Null(u.AppleSubject));
        }

        [Fact]
        public async Task SignIn_ByEmailOfAnInactiveUser_IsNotLinked()
        {
            AddUser("ana@example.com", active: false);

            Assert.Equal(AppleSignInOutcome.NotLinked, (await _service.SignInAsync(_apple.Token(), Nonce)).Outcome);
        }

        [Fact]
        public async Task SignIn_ByEmailOfAUserLinkedToAnotherAppleId_IsNotLinkedAndKeepsTheirLink()
        {
            var ana = AddUser("ana@example.com", appleSubject: "someone-else");

            Assert.Equal(AppleSignInOutcome.NotLinked, (await _service.SignInAsync(_apple.Token(), Nonce)).Outcome);
            Assert.Equal("someone-else", ana.AppleSubject);
        }

        [Fact]
        public async Task SignIn_WithNoMatchingUser_IsNotLinkedAndCreatesNobody()
        {
            Assert.Equal(AppleSignInOutcome.NotLinked, (await _service.SignInAsync(_apple.Token(), Nonce)).Outcome);
            _repository.Verify(r => r.CreateAsync(It.IsAny<UserEntity>()), Times.Never);
        }

        [Fact]
        public async Task SignIn_WithAnInvalidToken_IsRejected()
        {
            AddUser("ana@example.com", appleSubject: "001234.abcdef.0001");

            Assert.Equal(AppleSignInOutcome.InvalidToken, (await _service.SignInAsync(_apple.Token(), "wrong-nonce-0123456789abcdef")).Outcome);
        }

        [Fact]
        public async Task SignIn_WhenAParallelLinkTookTheAppleId_IsNotLinked()
        {
            var ana = AddUser("ana@example.com");
            _repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new DbUpdateException("IX_User_AppleSubject"));

            Assert.Equal(AppleSignInOutcome.NotLinked, (await _service.SignInAsync(_apple.Token(), Nonce)).Outcome);
            Assert.Null(ana.AppleSubject);
        }

        [Fact]
        public async Task Link_StoresTheAppleIdOnTheUser()
        {
            var ana = AddUser("ana@erp.com");

            Assert.Equal(AppleLinkOutcome.Linked, await _service.LinkAsync(ana, _apple.Token(email: null), Nonce));
            Assert.Equal("001234.abcdef.0001", ana.AppleSubject);
        }

        [Fact]
        public async Task Link_AnAppleIdAnotherUserHas_IsRefused()
        {
            AddUser("bia@erp.com", appleSubject: "001234.abcdef.0001");
            var ana = AddUser("ana@erp.com");

            Assert.Equal(AppleLinkOutcome.LinkedToAnotherUser, await _service.LinkAsync(ana, _apple.Token(), Nonce));
            Assert.Null(ana.AppleSubject);
        }

        [Fact]
        public async Task Link_WithAnInvalidToken_IsRefused()
        {
            var ana = AddUser("ana@erp.com");

            Assert.Equal(AppleLinkOutcome.InvalidToken, await _service.LinkAsync(ana, _apple.Token(audience: "com.someone.else"), Nonce));
            Assert.Null(ana.AppleSubject);
        }

        [Fact]
        public async Task Unlink_ClearsTheAppleId()
        {
            var ana = AddUser("ana@erp.com", appleSubject: "001234.abcdef.0001");

            await _service.UnlinkAsync(ana);

            Assert.Null(ana.AppleSubject);
            _repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
