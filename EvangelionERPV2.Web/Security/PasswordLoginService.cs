using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;

namespace EvangelionERPV2.Web.Security
{
    public enum PasswordLoginFailure
    {
        None,
        NotFound,

        /// <summary>
        /// The same username and password are active in more than one tenant, so the password
        /// cannot pick the account. Logging either one in would be a coin toss between two
        /// companies' data.
        /// </summary>
        Ambiguous
    }

    public sealed record PasswordLoginResult(
        Shared.Entities.User? User,
        bool NeedsRehash,
        PasswordLoginFailure Failure);

    /// <summary>
    /// Verifies a username and password against every tenant that could own that username.
    /// </summary>
    public sealed class PasswordLoginService
    {
        private const int MaxPasswordLoginCandidates = 20;

        private readonly IRepository<Shared.Entities.User> _userRepository;

        public PasswordLoginService(IRepository<Shared.Entities.User> userRepository)
        {
            _userRepository = userRepository;
        }

        public static string NormalizeUserName(string? userName) => (userName ?? string.Empty).Trim();

        public async Task<PasswordLoginResult> AuthenticateAsync(string? userName, string? password)
        {
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
                return new PasswordLoginResult(null, false, PasswordLoginFailure.NotFound);

            var normalizedUserName = NormalizeUserName(userName);
            if (string.IsNullOrWhiteSpace(normalizedUserName))
                return new PasswordLoginResult(null, false, PasswordLoginFailure.NotFound);

            var candidates = await GetActivePasswordLoginCandidatesAsync(normalizedUserName);

            Shared.Entities.User? user = null;
            var needsRehash = false;
            var ambiguous = false;

            foreach (var candidate in candidates)
            {
                if (!SharedFunctions.VerifyPassword(password, candidate.Password, out var candidateNeedsRehash))
                    continue;

                if (user != null)
                {
                    user = null;
                    ambiguous = true;
                    break;
                }

                user = candidate;
                needsRehash = candidateNeedsRehash;
            }

            if (user == null)
                return new PasswordLoginResult(null, false,
                    ambiguous ? PasswordLoginFailure.Ambiguous : PasswordLoginFailure.NotFound);

            return new PasswordLoginResult(user, needsRehash, PasswordLoginFailure.None);
        }

        private async Task<IReadOnlyList<Shared.Entities.User>> GetActivePasswordLoginCandidatesAsync(string normalizedUserName)
        {
            if (string.IsNullOrWhiteSpace(normalizedUserName))
                return Array.Empty<Shared.Entities.User>();

            var normalizedUserNameLower = normalizedUserName.ToLowerInvariant();
            return (await _userRepository.GetAllAsyncByFilter(
                    descending: false,
                    pageNumber: 1,
                    pageSize: MaxPasswordLoginCandidates,
                    predicate: x =>
                        x.IsActive == true &&
                        x.UserName != null &&
                        x.UserName.ToLower() == normalizedUserNameLower,
                    orderBy: x => x.Id))
                .ToList();
        }
    }
}
