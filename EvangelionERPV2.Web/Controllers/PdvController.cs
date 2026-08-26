using EvangelionERPV2.PdvModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Security;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.UserModule.Application.Token;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Security.Claims;
using System.Security.Cryptography;

namespace EvangelionERPV2.Web.Controllers
{
    /// <summary>
    /// What a till talks to. Every action is written to be safe to call twice, because a terminal
    /// on a bad connection cannot tell a lost response from a refused one and will resend.
    /// </summary>
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class PdvController : Controller
    {
        // A sale carries its lines and its tenders and nothing else — no images, no attachments.
        // Small enough that anything larger is a bug or an attack, either way not worth reading.
        private const int MaxSaleRequestBodySizeInBytes = 256 * 1024;
        private const int MaxBatchRequestBodySizeInBytes = 2 * 1024 * 1024;

        // A terminal id and nothing else. Anything larger is a bug or an attack.
        private const int MaxEnrollRequestBodySizeInBytes = 4 * 1024;
        private const int MaxTerminalIdLength = 64;

        // A username, a password and nothing else.
        private const int MaxLoginRequestBodySizeInBytes = 4 * 1024;

        // Six to twelve digits.
        private const int MaxPinRequestBodySizeInBytes = 4 * 1024;

        // A shop with more supervisors than this has a different problem; the cap stops one
        // terminal's sync from pulling an unbounded page of users into memory.
        private const int MaxSupervisorVerifiers = 500;

        // Every user in the enterprise, active or not, so revocations travel with the snapshot.
        private const int MaxSyncStateUsers = 2000;

        // One override attempt and nothing else.
        private const int MaxOverrideRequestBodySizeInBytes = 4 * 1024;

        // How far back the failed-override feed reaches, and how many rows of it travel. The till
        // only needs enough to run a lockout window, not the year's audit trail.
        private static readonly TimeSpan FailedOverrideFeedWindow = TimeSpan.FromHours(24);
        private const int MaxFailedOverridesInSyncState = 500;

        /// <summary>Header a till identifies itself with: <c>terminalId:credential</c>.</summary>
        private const string TerminalHeaderName = "X-Pdv-Terminal";

        // Terminal ids are unique per enterprise, not globally, so one id can name a till in more
        // than one tenant. The cap stops a crafted id from pulling an unbounded page into memory.
        private const int MaxTerminalLoginCandidates = 20;

        private const int DefaultCatalogPageSize = 500;
        private const int MaxCatalogPageSize = 1000;

        // A terminal that has been off for a week sends its backlog in one call; more than this and
        // it takes two, which keeps one bad batch from occupying a request thread indefinitely.
        private const int MaxSalesPerBatch = 200;

        // Two counters, because either one alone leaves a hole: per terminal-and-operator stops a
        // password being guessed, per terminal stops one password being sprayed across every
        // cashier's account from the same till.
        private static readonly TimeSpan LoginRateLimitWindow = TimeSpan.FromMinutes(15);
        private const int LoginRateLimitMaxFailedAttempts = 5;

        private static readonly FailedAttemptLimiter _terminalLoginLimiter =
            new(LoginRateLimitWindow, LoginRateLimitMaxFailedAttempts);

        private static readonly FailedAttemptLimiter _operatorLoginLimiter =
            new(LoginRateLimitWindow, LoginRateLimitMaxFailedAttempts);

        private readonly IPdvService _pdvService;
        private readonly IRepository<PdvTerminal> _terminalRepository;
        private readonly IRepository<Shared.Entities.User> _userRepository;
        private readonly IRepository<PdvOverrideAudit> _overrideAuditRepository;
        private readonly PasswordLoginService _passwordLogin;
        private readonly TokenService _tokenService;

        public PdvController(
            IPdvService pdvService,
            IRepository<PdvTerminal> terminalRepository,
            IRepository<Shared.Entities.User> userRepository,
            IRepository<PdvOverrideAudit> overrideAuditRepository,
            PasswordLoginService passwordLogin,
            TokenService tokenService)
        {
            _pdvService = pdvService;
            _terminalRepository = terminalRepository;
            _userRepository = userRepository;
            _overrideAuditRepository = overrideAuditRepository;
            _passwordLogin = passwordLogin;
            _tokenService = tokenService;
        }

        /// <summary>
        /// Enrolls a till and returns its credential, once.
        /// </summary>
        /// <remarks>
        /// Supervisor tier, not operator: adding a terminal that can reach the login endpoint is a
        /// management action. The credential is generated here and never stored in the clear, so
        /// this response is the only time anyone sees it.
        /// </remarks>
        [HttpPost]
        [RequestSizeLimit(MaxEnrollRequestBodySizeInBytes)]
        [Authorize(Policy = "rbac:" + RbacPermissions.Pdv.CashMovement)]
        [ProducesResponseType(typeof(EnrollTerminalResponseDTO), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(409)]
        public async Task<IActionResult> EnrollTerminal([FromBody] EnrollTerminalRequestDTO request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var terminalId = (request?.TerminalId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(terminalId) || terminalId.Length > MaxTerminalIdLength)
                return BadRequest("terminalId is required.");

            var existing = await _terminalRepository.GetAllAsync(
                x => x.EnterpriseId == enterpriseId && x.TerminalId == terminalId);

            // Re-enrolling silently would mint a second credential for a till that already has one
            // and leave the first working. Rotation should be an explicit act, not a side effect.
            if (existing.Any())
                return Conflict($"Terminal [{terminalId}] is already enrolled.");

            var credential = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var terminal = PdvTerminal.Enroll(enterpriseId, terminalId, credential);

            await _terminalRepository.CreateAsync(terminal);
            await _terminalRepository.CommitAsync();

            Log.Logger.Information(
                "PDV terminal enrolled. TerminalId={TerminalId}", terminalId);

            return Ok(new EnrollTerminalResponseDTO
            {
                TerminalId = terminal.TerminalId,
                Credential = credential
            });
        }

        /// <summary>
        /// Logs an operator in at an enrolled till.
        /// </summary>
        /// <remarks>
        /// Separate from <c>UserController.LogInto</c> because a terminal has no browser and cannot
        /// solve a reCAPTCHA. The enrolled terminal credential replaces that human-presence check;
        /// it authorises nothing on its own, every permission still comes from the operator's role.
        ///
        /// The token issued here is stamped <see cref="PdvTokenScope"/>, so a credential lifted off
        /// a shop-floor machine reaches the till and only the till.
        ///
        /// Every rejection returns the same bare 401. Telling "no such terminal" apart from "wrong
        /// password" would turn this into an oracle for enumerating terminal ids and usernames.
        /// </remarks>
        [HttpPost]
        [AllowAnonymous]
        [RequestSizeLimit(MaxLoginRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(PdvLoginResponseDTO), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> Login([FromBody] PdvLoginRequestDTO request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

            if (request == null ||
                string.IsNullOrWhiteSpace(request.UserName) ||
                string.IsNullOrWhiteSpace(request.Password))
                return BadRequest("userName and password are required.");

            if (!TryReadTerminalHeader(out var terminalId, out var credential))
                return Unauthorized();

            var terminalKey = FailedAttemptLimiter.StableKey(terminalId);
            var operatorKey = FailedAttemptLimiter.StableKey(
                $"{terminalId}:{PasswordLoginService.NormalizeUserName(request.UserName).ToLowerInvariant()}");

            if (_terminalLoginLimiter.IsLimited(terminalKey) || _operatorLoginLimiter.IsLimited(operatorKey))
                return StatusCode(StatusCodes.Status429TooManyRequests);

            var terminal = await ResolveTerminalAsync(terminalId, credential);
            if (terminal == null)
                return RegisterFailedLogin(terminalKey, operatorKey);

            var login = await _passwordLogin.AuthenticateAsync(request.UserName, request.Password);
            if (login.User == null)
                return RegisterFailedLogin(terminalKey, operatorKey);

            // The terminal says only "this caller is a till we enrolled". An operator from another
            // company logging in here would read this company's catalog.
            if (login.User.EnterpriseId != terminal.EnterpriseId)
                return RegisterFailedLogin(terminalKey, operatorKey);

            var permissions = PdvTokenScope.PermissionsFor(login.User.AccessLevel);
            if (permissions.Count == 0)
                return RegisterFailedLogin(terminalKey, operatorKey);

            // The operator proved who they are, so their counter is spent. The terminal's is not:
            // clearing it would let a guesser reset the till's budget with one valid login.
            _operatorLoginLimiter.Clear(operatorKey);

            terminal.LastSeenAt = DateTime.UtcNow;
            _terminalRepository.Update(terminal);
            await _terminalRepository.CommitAsync();

            var token = _tokenService.GenerateToken(BuildPdvClaims(login.User, terminal.EnterpriseId));
            var refreshToken = _tokenService.GenerateRefreshToken();
            await _tokenService.SaveRefreshTokenAsync(login.User.Id, refreshToken);

            return Ok(new PdvLoginResponseDTO
            {
                Token = token,
                RefreshToken = refreshToken,
                UserId = login.User.Id,
                EnterpriseId = terminal.EnterpriseId,
                Permissions = permissions
            });
        }

        /// <summary>
        /// Every rejection is one failed attempt against both counters. Always returns 401: which
        /// of the checks failed is exactly what an attacker is trying to learn.
        /// </summary>
        private IActionResult RegisterFailedLogin(string terminalKey, string operatorKey)
        {
            _terminalLoginLimiter.RegisterFailure(terminalKey);
            _operatorLoginLimiter.RegisterFailure(operatorKey);
            return Unauthorized();
        }

        /// <summary>
        /// Splits the <c>X-Pdv-Terminal</c> header into its two halves.
        /// </summary>
        private bool TryReadTerminalHeader(out string terminalId, out string credential)
        {
            terminalId = string.Empty;
            credential = string.Empty;

            // Header form is "terminalId:credential". A credential is base64 and may contain '+'
            // and '/' but never ':', so the first colon is the separator.
            var header = Request.Headers[TerminalHeaderName].ToString();
            var separator = header.IndexOf(':');
            if (separator <= 0 || separator == header.Length - 1 || separator > MaxTerminalIdLength)
                return false;

            terminalId = header[..separator];
            credential = header[(separator + 1)..];
            return true;
        }

        /// <summary>
        /// Returns the terminal the credential authenticates, or null. Logs every use of a terminal
        /// credential, accepted or not — never the credential itself.
        /// </summary>
        private async Task<PdvTerminal?> ResolveTerminalAsync(string terminalId, string credential)
        {
            // The credential is what picks the tenant: the login carries no discriminator, and the
            // same terminal id can name a till in more than one enterprise.
            var candidates = (await _terminalRepository.GetAllAsync(
                1, MaxTerminalLoginCandidates, x => x.TerminalId == terminalId)).ToList();

            var terminal = candidates.FirstOrDefault(x => x.Verify(credential));

            Log.Logger.Information(
                "PDV terminal credential used. TerminalId={TerminalId} Accepted={Accepted}",
                terminalId, terminal != null);

            return terminal;
        }

        /// <summary>
        /// The claim list <c>TokenService.GenerateToken(User, bool)</c> builds, plus the till scope.
        /// The types must match that method or every existing authorization handler fails on these
        /// tokens.
        /// </summary>
        private static IEnumerable<Claim> BuildPdvClaims(Shared.Entities.User user, Guid enterpriseId)
        {
            var userId = user.Id.ToString();

            return
            [
                new Claim(ClaimTypes.Name, userId),
                new Claim(ClaimTypes.GivenName, user.FirstName),
                new Claim(ClaimTypes.Surname, user.LastName),
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Sid, userId),
                new Claim("uid", userId),
                new Claim(ClaimTypes.GroupSid, enterpriseId.ToString()),
                new Claim(PdvTokenScope.ClaimType, PdvTokenScope.ClaimValue)
            ];
        }

        /// <summary>
        /// Sets the caller's own till override PIN.
        /// </summary>
        /// <remarks>
        /// Self-service and nobody else's: a PIN a manager could set for a cashier would be a PIN
        /// the manager knows, which defeats the point of an override being attributable. Only the
        /// verifier is stored, and the PIN is never logged.
        /// </remarks>
        [HttpPost]
        [RequestSizeLimit(MaxPinRequestBodySizeInBytes)]
        [ProducesResponseType(204)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> SetPdvPin([FromBody] SetPdvPinRequestDTO request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

            var pin = request?.Pin ?? string.Empty;

            // Six digits is the floor because the verifier is handed to terminals, where anything
            // shorter is guessable offline whatever the iteration count.
            if (pin.Length is < 6 or > 12 || !pin.All(char.IsAsciiDigit))
                return BadRequest("Pin must be 6 to 12 digits.");

            var userId = TryGetUserId();
            if (!userId.HasValue || !TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var user = await _userRepository.GetByIdAsync(userId.Value);
            if (user == null || user.EnterpriseId != enterpriseId)
                return Unauthorized();

            user.PdvPinVerifier = Pbkdf2Verifier.Create(pin);
            _userRepository.Update(user);
            await _userRepository.CommitAsync();

            Log.Logger.Information("PDV override PIN set. UserId={UserId}", userId.Value);

            return NoContent();
        }

        /// <summary>
        /// Returns the supervisors a till may ask to authorise an override, with the verifier it
        /// checks their PIN against.
        /// </summary>
        /// <remarks>
        /// The verifiers leave the server on purpose. An override is needed precisely when the
        /// network is down — a check that requires the ERP is a check that fails when it matters.
        /// A verifier is not a PIN, which is the whole reason it is a PBKDF2 hash.
        ///
        /// Only users who hold a <c>pdv.*</c> grant an operator does not are listed: sending every
        /// employee's verifier would put the whole staff's PINs on every shop floor.
        /// </remarks>
        [HttpGet]
        [Authorize(Policy = "rbac:" + RbacPermissions.Pdv.Operate)]
        [ProducesResponseType(typeof(IEnumerable<SupervisorVerifierDTO>), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        public async Task<IActionResult> GetSupervisorVerifiers()
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var users = await _userRepository.GetAllAsync(
                1,
                MaxSupervisorVerifiers,
                x => x.EnterpriseId == enterpriseId &&
                     x.IsActive == true &&
                     x.PdvPinVerifier != null);

            var verifiers = users
                .Where(user => IsSupervisorTier(user.AccessLevel))
                .Select(user => new SupervisorVerifierDTO
                {
                    UserId = user.Id,
                    DisplayName = $"{user.FirstName} {user.LastName}".Trim(),
                    PinVerifier = user.PdvPinVerifier!,
                    Permissions = PdvTokenScope.PermissionsFor(user.AccessLevel)
                })
                .ToList();

            return Ok(verifiers);
        }

        /// <summary>
        /// Records one override a till reports, refused ones included.
        /// </summary>
        /// <remarks>
        /// Idempotent on <c>ClientOverrideId</c>, like <c>SyncSale</c>: a terminal that loses the
        /// response resends, and an audit trail that double-counts refusals would lock a supervisor
        /// out over a flaky connection.
        ///
        /// The PIN never reaches here. The till checks it against the cached verifier and reports
        /// the outcome.
        /// </remarks>
        [HttpPost]
        [RequestSizeLimit(MaxOverrideRequestBodySizeInBytes)]
        [Authorize(Policy = "rbac:" + RbacPermissions.Pdv.Operate)]
        [ProducesResponseType(typeof(RegisterOverrideResponseDTO), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        public async Task<IActionResult> RegisterOverride([FromBody] RegisterOverrideRequestDTO request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var operatorUserId = TryGetUserId();
            if (operatorUserId == null)
                return Unauthorized();

            if (request == null ||
                request.ClientOverrideId == Guid.Empty ||
                request.SupervisorUserId == Guid.Empty ||
                string.IsNullOrWhiteSpace(request.Permission))
                return BadRequest("clientOverrideId, supervisorUserId and permission are required.");

            var terminalId = (request.TerminalId ?? string.Empty).Trim();
            if (terminalId.Length > MaxTerminalIdLength)
                return BadRequest("terminalId is too long.");

            var existing = await _overrideAuditRepository.GetAllAsync(
                1, 1, x => x.EnterpriseId == enterpriseId && x.ClientOverrideId == request.ClientOverrideId);

            if (existing.Any())
            {
                return Ok(new RegisterOverrideResponseDTO
                {
                    ClientOverrideId = request.ClientOverrideId,
                    WasAlreadyRecorded = true
                });
            }

            await _overrideAuditRepository.CreateAsync(PdvOverrideAudit.Record(
                enterpriseId,
                request.ClientOverrideId,
                request.SupervisorUserId,
                operatorUserId.Value,
                terminalId,
                request.ClientSaleId,
                request.Permission.Trim(),
                request.Succeeded,
                request.OccurredAt));

            await _overrideAuditRepository.CommitAsync();

            Log.Logger.Information(
                "PDV override recorded. TerminalId={TerminalId} Permission={Permission} Succeeded={Succeeded}",
                terminalId, request.Permission, request.Succeeded);

            return Ok(new RegisterOverrideResponseDTO
            {
                ClientOverrideId = request.ClientOverrideId,
                WasAlreadyRecorded = false
            });
        }

        /// <summary>
        /// The permission snapshot and revocation list a till refreshes on every sync.
        /// </summary>
        /// <remarks>
        /// Scoped to the caller's enterprise throughout: a till handed another tenant's operators
        /// would be handed their PIN verifiers with them.
        /// </remarks>
        [HttpGet]
        [Authorize(Policy = "rbac:" + RbacPermissions.Pdv.Operate)]
        [ProducesResponseType(typeof(PdvSyncStateDTO), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        public async Task<IActionResult> GetSyncState()
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var users = await _userRepository.GetAllAsync(
                1, MaxSyncStateUsers, x => x.EnterpriseId == enterpriseId);

            var operators = users
                .Where(user => user.IsActive == true && PdvTokenScope.PermissionsFor(user.AccessLevel).Count > 0)
                .Select(user => new PdvOperatorDTO
                {
                    UserId = user.Id,
                    EnterpriseId = enterpriseId,
                    UserName = user.UserName,
                    DisplayName = $"{user.FirstName} {user.LastName}".Trim(),
                    Permissions = PdvTokenScope.PermissionsFor(user.AccessLevel),

                    // Only someone who can authorise an override needs a verifier on a shop floor.
                    PinVerifier = IsSupervisorTier(user.AccessLevel) ? user.PdvPinVerifier : null
                })
                .ToList();

            var revokedUserIds = users
                .Where(user => user.IsActive != true)
                .Select(user => user.Id)
                .ToList();

            // A terminal only ever sees its own refusals, so the lockout it runs would be per till
            // rather than per supervisor — and the guesser walks to the next one.
            var since = DateTime.UtcNow.Subtract(FailedOverrideFeedWindow);
            var failedOverrides = (await _overrideAuditRepository.GetAllAsync(
                    1,
                    MaxFailedOverridesInSyncState,
                    x => x.EnterpriseId == enterpriseId && !x.Succeeded && x.OccurredAt >= since))
                .Select(audit => new PdvFailedOverrideDTO
                {
                    SupervisorUserId = audit.SupervisorUserId,
                    OccurredAt = audit.OccurredAt
                })
                .ToList();

            return Ok(new PdvSyncStateDTO
            {
                ServerTimeUtc = DateTime.UtcNow,
                Operators = operators,
                RevokedUserIds = revokedUserIds,
                FailedOverrides = failedOverrides
            });
        }

        /// <summary>
        /// Whether this role holds any till permission an ordinary operator does not — which is
        /// what "supervisor tier" means here, rather than one named role.
        /// </summary>
        private static bool IsSupervisorTier(short accessLevel) =>
            PdvTokenScope.PermissionsFor(accessLevel).Except(OperatorPdvPermissions).Any();

        private static readonly HashSet<string> OperatorPdvPermissions =
            [.. PdvTokenScope.PermissionsFor((short)EnumAccessLevel.Employee)];

        [HttpPost]
        [Authorize(Policy = "rbac:" + RbacPermissions.Pdv.OpenSession)]
        [ProducesResponseType(typeof(CashSessionDTO), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> OpenSession([FromBody] OpenCashSessionRequestDTO request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var userId = TryGetUserId();
            if (userId == null)
                return Unauthorized();

            var session = await _pdvService.OpenSessionAsync(request, enterpriseId, userId.Value);
            return Ok(ToDto(session));
        }

        [HttpPost]
        [Authorize(Policy = "rbac:" + RbacPermissions.Pdv.CloseSession)]
        [ProducesResponseType(typeof(CashSessionSummaryDTO), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(204)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> CloseSession([FromBody] CloseCashSessionRequestDTO request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var userId = TryGetUserId();
            if (userId == null)
                return Unauthorized();

            try
            {
                return Ok(await _pdvService.CloseSessionAsync(request, enterpriseId, userId.Value));
            }
            catch (NotFoundDatabaseException)
            {
                return NoContent();
            }
        }

        [HttpPost]
        [Authorize(Policy = "rbac:" + RbacPermissions.Pdv.CashMovement)]
        [ProducesResponseType(typeof(CashMovementDTO), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(204)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> RegisterMovement([FromBody] CashMovementRequestDTO request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var userId = TryGetUserId();
            if (userId == null)
                return Unauthorized();

            try
            {
                var movement = await _pdvService.RegisterMovementAsync(request, enterpriseId, userId.Value);

                return Ok(new CashMovementDTO
                {
                    Id = movement.Id,
                    CashSessionId = movement.CashSessionId,
                    Type = movement.Type,
                    Amount = movement.Amount,
                    Reason = movement.Reason,
                    UserId = movement.UserId,
                    ReferenceId = movement.ReferenceId,
                    CreatedAt = movement.CreatedAt
                });
            }
            catch (NotFoundDatabaseException)
            {
                return NoContent();
            }
        }

        /// <summary>
        /// Records one till sale. Idempotent on <c>ClientSaleId</c>.
        /// </summary>
        /// <remarks>
        /// A resend of a sale that already landed answers 200 with the same order id and
        /// <c>WasAlreadyRecorded</c> set, not a conflict — the terminal did the right thing by
        /// retrying and should be told so plainly.
        /// </remarks>
        [HttpPost]
        [RequestSizeLimit(MaxSaleRequestBodySizeInBytes)]
        [Authorize(Policy = "rbac:" + RbacPermissions.Pdv.Operate)]
        [ProducesResponseType(typeof(PdvSaleSyncResultDTO), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(409)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> SyncSale([FromBody] PdvSaleSyncRequestDTO request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var userId = TryGetUserId();
            if (userId == null)
                return Unauthorized();

            return Ok(await _pdvService.SyncSaleAsync(request, enterpriseId, userId.Value));
        }

        /// <summary>
        /// A terminal's backlog after it comes back online.
        /// </summary>
        /// <remarks>
        /// Sales are processed in the order given and a failure stops the batch rather than
        /// skipping past it: they are a queue, and the ones behind may depend on the ones in front.
        /// Whatever succeeded before the stop is still reported, so the terminal can clear those
        /// and resend the rest.
        /// </remarks>
        [HttpPost]
        [RequestSizeLimit(MaxBatchRequestBodySizeInBytes)]
        [Authorize(Policy = "rbac:" + RbacPermissions.Pdv.Operate)]
        [ProducesResponseType(typeof(PdvSaleSyncBatchResultDTO), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> SyncSaleBatch([FromBody] PdvSaleSyncBatchRequestDTO request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var userId = TryGetUserId();
            if (userId == null)
                return Unauthorized();

            var sales = (request.Sales ?? []).ToList();
            if (sales.Count > MaxSalesPerBatch)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

            var results = new List<PdvSaleSyncResultDTO>(sales.Count);

            foreach (var sale in sales)
            {
                try
                {
                    results.Add(await _pdvService.SyncSaleAsync(sale, enterpriseId, userId.Value));
                }
                catch (Exception ex)
                {
                    Log.Logger.Error("PDV batch stopped at sale [{ClientSaleId}]. ErrorType={ErrorType}",
                        sale.ClientSaleId, ex.GetType().Name);

                    // Report what did land rather than failing the whole call: those sales are
                    // recorded, and the terminal must be able to stop resending them.
                    break;
                }
            }

            return Ok(new PdvSaleSyncBatchResultDTO { Results = results });
        }

        [HttpGet("{clientSessionId}")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Pdv.ReadReports)]
        [ProducesResponseType(typeof(CashSessionSummaryDTO), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(204)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetSessionSummary(Guid clientSessionId)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            try
            {
                // The reading is for a supervisor, who is not the person counting the drawer, so it
                // carries the expected figure. The blind count is enforced at CloseSession, which
                // takes the operator's number before it answers with this.
                return Ok(await _pdvService.GetSessionSummaryAsync(clientSessionId, enterpriseId, includeExpectedCash: true));
            }
            catch (NotFoundDatabaseException)
            {
                return NoContent();
            }
        }

        [HttpGet("{terminalId}")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Pdv.Operate)]
        [ProducesResponseType(typeof(CashSessionDTO), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(204)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetOpenSession(string terminalId)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var session = await _pdvService.GetOpenSessionAsync(terminalId, enterpriseId);
            return session == null ? NoContent() : Ok(ToDto(session));
        }

        /// <summary>
        /// Products changed since the terminal last asked, for its local mirror.
        /// </summary>
        /// <remarks>
        /// No cost or margin here at any page size. The till needs a sale price to charge and the
        /// fiscal fields to build an NFC-e line; what the shop pays its suppliers has no business
        /// sitting on a machine in the shop floor, behind a login every cashier knows.
        /// </remarks>
        [HttpGet]
        [Authorize(Policy = "rbac:" + RbacPermissions.Pdv.Operate)]
        [ProducesResponseType(typeof(PdvCatalogPageDTO), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetCatalog([FromQuery] DateTime? since, [FromQuery] int? pageSize)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var size = Math.Clamp(pageSize ?? DefaultCatalogPageSize, 1, MaxCatalogPageSize);
            return Ok(await _pdvService.GetCatalogAsync(since, enterpriseId, size));
        }

        private static CashSessionDTO ToDto(Shared.Entities.CashSession session) => new()
        {
            Id = session.Id,
            TerminalId = session.TerminalId,
            UserId = session.UserId,
            OpenedAt = session.OpenedAt,
            ClosedAt = session.ClosedAt,
            OpeningFloat = session.OpeningFloat,
            CountedAmount = session.CountedAmount,
            ExpectedAmount = session.ExpectedAmount,
            Difference = session.Difference,
            Status = session.Status
        };

        private bool TryGetEnterpriseId(out Guid enterpriseId)
        {
            var claimValue = User.FindFirst(ClaimTypes.GroupSid)?.Value;
            return Guid.TryParse(claimValue, out enterpriseId) && enterpriseId != Guid.Empty;
        }

        private Guid? TryGetUserId()
        {
            var claimValue = User.FindFirst(ClaimTypes.Sid)?.Value
                ?? User.FindFirst("uid")?.Value
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (Guid.TryParse(claimValue, out var userId) && userId != Guid.Empty)
                return userId;

            return null;
        }
    }
}
