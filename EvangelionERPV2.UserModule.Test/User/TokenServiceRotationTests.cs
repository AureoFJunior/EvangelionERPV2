using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.UserModule.Application.Token;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace EvangelionERPV2.UserModule.Test.User
{
    // BIO-01: a refresh token is spent once; a rotated token that comes back revokes the session.
    [Collection(SharedFunctionsKeyCollection.Name)]
    public class TokenServiceRotationTests
    {
        private readonly List<RefreshToken> _tokens = new();
        private readonly Mock<IRepository<RefreshToken>> _repository = new();
        private readonly TokenService _service;
        private readonly Guid _userId = Guid.NewGuid();

        public TokenServiceRotationTests()
        {
            _repository.Setup(r => r.GetAllAsyncByFilter(It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<int?>(),
                    It.IsAny<Expression<Func<RefreshToken, bool>>>(), It.IsAny<Expression<Func<RefreshToken, object>>>()))
                .ReturnsAsync((bool _, int? _, int? size, Expression<Func<RefreshToken, bool>> p, Expression<Func<RefreshToken, object>> _) =>
                    _tokens.Where(p.Compile()).Take(size ?? int.MaxValue).ToList());
            _repository.Setup(r => r.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
                .Returns((Func<Task> op, CancellationToken _) => op());
            _repository.Setup(r => r.ExecuteInTransactionAsync(It.IsAny<Func<Task<string?>>>(), It.IsAny<CancellationToken>()))
                .Returns((Func<Task<string?>> op, CancellationToken _) => op());
            _repository.Setup(r => r.Update(It.IsAny<RefreshToken>())).Returns((RefreshToken t) => t);
            _repository.Setup(r => r.CreateAsync(It.IsAny<RefreshToken>()))
                .ReturnsAsync((RefreshToken t) => { _tokens.Add(t); return t; });

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["JwtSettings:RefreshTokenDays"] = "7" })
                .Build();
            _service = new TokenService(_repository.Object, configuration);
        }

        private static string Hash(string token) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        private RefreshToken Issue(string raw, DateTime? expiresAt = null, string kind = RefreshTokenKind.Session)
        {
            var token = new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = _userId,
                TokenHash = Hash(raw),
                ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(7),
                IsActive = true,
                Kind = kind
            };
            _tokens.Add(token);
            return token;
        }

        private IEnumerable<RefreshToken> Active => _tokens.Where(t => t.IsActive == true && t.RevokedAt == null);

        [Fact]
        public async Task Rotate_SpendsTheTokenAndIssuesANewOneForTheSameUser()
        {
            var old = Issue("old-token");

            var rotation = await _service.RotateRefreshTokenAsync("old-token");

            Assert.NotNull(rotation);
            Assert.Equal(_userId, rotation!.UserId);
            Assert.NotEqual("old-token", rotation.RefreshToken);
            Assert.NotNull(old.RevokedAt);
            Assert.False(old.IsActive);
            Assert.Equal(Hash(rotation.RefreshToken), old.ReplacedByTokenHash);
            var fresh = Assert.Single(Active);
            Assert.Equal(Hash(rotation.RefreshToken), fresh.TokenHash);
            Assert.True(fresh.ExpiresAt > DateTime.UtcNow.AddDays(6));
        }

        [Fact]
        public async Task Rotate_TheSameTokenTwice_FailsTheSecondTimeAndRevokesTheSession()
        {
            Issue("old-token");
            var first = await _service.RotateRefreshTokenAsync("old-token");

            Assert.Null(await _service.RotateRefreshTokenAsync("old-token"));

            Assert.Empty(Active);
            Assert.Null(await _service.RotateRefreshTokenAsync(first!.RefreshToken));
        }

        [Fact]
        public async Task Rotate_AReusedToken_LogsTheReuse()
        {
            Issue("old-token");
            await _service.RotateRefreshTokenAsync("old-token");
            var sink = new CollectingSink();
            var previous = Log.Logger;
            Log.Logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
            try
            {
                await _service.RotateRefreshTokenAsync("old-token");
            }
            finally
            {
                Log.Logger = previous;
            }

            var reuse = Assert.Single(sink.Events, e => e.MessageTemplate.Text.StartsWith("Refresh token reuse detected"));
            Assert.Equal(LogEventLevel.Warning, reuse.Level);
            Assert.Equal(_userId.ToString(), reuse.Properties["UserId"].ToString());
        }

        [Fact]
        public async Task Rotate_ATokenRevokedBySigningInElsewhere_FailsWithoutTouchingTheNewSession()
        {
            var phone = Issue("phone-token");
            phone.RevokedAt = DateTime.UtcNow;
            phone.IsActive = false;
            var web = Issue("web-token");

            Assert.Null(await _service.RotateRefreshTokenAsync("phone-token"));

            Assert.Equal(web, Assert.Single(Active));
        }

        [Fact]
        public async Task Rotate_AnExpiredToken_Fails()
        {
            Issue("old-token", DateTime.UtcNow.AddSeconds(-1));

            Assert.Null(await _service.RotateRefreshTokenAsync("old-token"));
        }

        [Theory]
        [InlineData("never-issued")]
        [InlineData("")]
        [InlineData(null)]
        public async Task Rotate_AnUnknownToken_Fails(string? raw)
        {
            Issue("old-token");

            Assert.Null(await _service.RotateRefreshTokenAsync(raw));
            Assert.Single(Active);
        }

        [Fact]
        public async Task Rotate_WhenAParallelRotationWonTheUniqueIndex_FailsInsteadOfThrowing()
        {
            Issue("old-token");
            _repository.Setup(r => r.CreateAsync(It.IsAny<RefreshToken>()))
                .ThrowsAsync(new DbUpdateException("Cannot insert duplicate key row in object 'dbo.RefreshToken' with unique index 'IX_RefreshToken_UserId_Active'."));

            Assert.Null(await _service.RotateRefreshTokenAsync("old-token"));
        }
    
        [Theory]
        [InlineData(RefreshTokenKind.Till)]
        [InlineData(RefreshTokenKind.Machine)]
        [InlineData(RefreshTokenKind.Legacy)]
        public async Task Rotate_RefusesEveryTokenThatIsNotAnInteractiveSession_AndLeavesItAlone(string kind)
        {
            var token = Issue("till-token", kind: kind);

            Assert.Null(await _service.RotateRefreshTokenAsync("till-token"));

            Assert.Null(token.RevokedAt);
            Assert.Equal(token, Assert.Single(Active));
        }

        [Fact]
        public async Task Rotate_IssuesTheReplacementAsASessionToken()
        {
            Issue("old-token");

            var rotation = await _service.RotateRefreshTokenAsync("old-token");

            Assert.Equal(RefreshTokenKind.Session, Assert.Single(Active, t => t.TokenHash == Hash(rotation!.RefreshToken)).Kind);
        }

        private sealed class CollectingSink : ILogEventSink
        {
            public List<LogEvent> Events { get; } = new();
            public void Emit(LogEvent logEvent)
            {
                lock (Events)
                    Events.Add(logEvent);
            }
        }
    }
}
