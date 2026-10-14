using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.UserModule.Application.Services
{
    public enum AppleSignInOutcome { InvalidToken, NotLinked, SignedIn }

    public enum AppleLinkOutcome { InvalidToken, LinkedToAnotherUser, Linked }

    public sealed record AppleSignInResult(AppleSignInOutcome Outcome, User? User = null);

    /// <summary>
    /// Finds the ERP user behind a Sign in with Apple token. Apple's <c>sub</c> wins; otherwise a
    /// verified, non-relay email that belongs to exactly one active user links that user. Nobody is
    /// created: ERP accounts are provisioned by an administrator.
    /// </summary>
    public sealed class AppleSignInService
    {
        private const string PrivateRelayDomain = "@privaterelay.appleid.com";

        private readonly AppleIdTokenValidator _validator;
        private readonly IRepository<User> _users;

        public AppleSignInService(AppleIdTokenValidator validator, IRepository<User> users)
        {
            _validator = validator;
            _users = users;
        }

        public async Task<AppleSignInResult> SignInAsync(string? identityToken, string? rawNonce)
        {
            var identity = await _validator.ValidateAsync(identityToken, rawNonce);
            if (identity == null)
                return new AppleSignInResult(AppleSignInOutcome.InvalidToken);

            var linked = (await _users.GetAllAsync(u => u.AppleSubject == identity.Subject)).ToList();
            if (linked.Count == 1)
                return new AppleSignInResult(AppleSignInOutcome.SignedIn, linked[0]);

            if (!identity.EmailVerified || identity.IsPrivateRelay || string.IsNullOrWhiteSpace(identity.Email)
                || identity.Email.EndsWith(PrivateRelayDomain, StringComparison.OrdinalIgnoreCase))
                return new AppleSignInResult(AppleSignInOutcome.NotLinked);

            var email = identity.Email.Trim().ToLowerInvariant();
            var matches = (await _users.GetAllAsync(u => u.Email != null && u.Email.ToLower() == email && u.IsActive == true)).ToList();
            if (matches.Count != 1 || matches[0].AppleSubject != null)
                return new AppleSignInResult(AppleSignInOutcome.NotLinked);

            return await TrySetSubjectAsync(matches[0], identity.Subject)
                ? new AppleSignInResult(AppleSignInOutcome.SignedIn, matches[0])
                : new AppleSignInResult(AppleSignInOutcome.NotLinked);
        }

        public async Task<AppleLinkOutcome> LinkAsync(User user, string? identityToken, string? rawNonce)
        {
            var identity = await _validator.ValidateAsync(identityToken, rawNonce);
            if (identity == null)
                return AppleLinkOutcome.InvalidToken;

            if ((await _users.GetAllAsync(u => u.AppleSubject == identity.Subject && u.Id != user.Id)).Any())
                return AppleLinkOutcome.LinkedToAnotherUser;

            return await TrySetSubjectAsync(user, identity.Subject) ? AppleLinkOutcome.Linked : AppleLinkOutcome.LinkedToAnotherUser;
        }

        public async Task UnlinkAsync(User user)
        {
            user.AppleSubject = null;
            _users.Update(user);
            await _users.CommitAsync();
        }

        /// <summary>False when the unique index says another user took this Apple id meanwhile.</summary>
        private async Task<bool> TrySetSubjectAsync(User user, string subject)
        {
            var previous = user.AppleSubject;
            user.AppleSubject = subject;
            _users.Update(user);
            try
            {
                await _users.CommitAsync();
                return true;
            }
            catch (DbUpdateException)
            {
                user.AppleSubject = previous;
                _users.DetachEntity(user);
                return false;
            }
        }
    }
}
