using Asp.Versioning;
using EvangelionERPV2.NFeModule.Application.Interface;
using EvangelionERPV2.PdvModule.Application.Interface;
using EvangelionERPV2.PdvModule.Application.Services;
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
        private const int MaxSaleRequestBodySizeInBytes = 256 * 1024;
        private const int MaxBatchRequestBodySizeInBytes = 2 * 1024 * 1024;

        private const int MaxEnrollRequestBodySizeInBytes = 4 * 1024;
        private const int MaxTerminalIdLength = 64;

        private const int MaxLoginRequestBodySizeInBytes = 4 * 1024;

        private const int MaxPinRequestBodySizeInBytes = 4 * 1024;

        private const int MaxSupervisorVerifiers = 500;

        private const int MaxSyncStateUsers = 2000;

        private const int MaxOverrideRequestBodySizeInBytes = 4 * 1024;

        private const int MaxChargeRequestBodySizeInBytes = 4 * 1024;

        private const int MaxWebhookRequestBodySizeInBytes = 64 * 1024;

        private static readonly TimeSpan FailedOverrideFeedWindow = TimeSpan.FromHours(24);
        private const int MaxFailedOverridesInSyncState = 500;

        /// <summary>Header a till identifies itself with: <c>terminalId:credential</c>.</summary>
        private const string TerminalHeaderName = "X-Pdv-Terminal";

        private const int MaxTerminalLoginCandidates = 20;

        private const int DefaultCatalogPageSize = 500;
        private const int MaxCatalogPageSize = 1000;

        /// <summary>Matches <c>ProductController.MaxPageSizeWithPictures</c>. Base64 inflates by a third.</summary>
        private const int MaxImagePageSize = 50;

        private const int MaxSalesPerBatch = 200;

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
        private readonly IRepository<Enterprise> _enterpriseRepository;
        private readonly INFeService<NFeDocument> _nfeService;
        private readonly INFeFiscalSettingsService _fiscalSettingsService;
        private readonly PasswordLoginService _passwordLogin;
        private readonly TokenService _tokenService;

        public PdvController(
            IPdvService pdvService,
            IRepository<PdvTerminal> terminalRepository,
            IRepository<Shared.Entities.User> userRepository,
            IRepository<PdvOverrideAudit> overrideAuditRepository,
            IRepository<Enterprise> enterpriseRepository,
            INFeService<NFeDocument> nfeService,
            INFeFiscalSettingsService fiscalSettingsService,
            PasswordLoginService passwordLogin,
            TokenService tokenService)
        {
            _pdvService = pdvService;
            _terminalRepository = terminalRepository;
            _userRepository = userRepository;
            _overrideAuditRepository = overrideAuditRepository;
            _enterpriseRepository = enterpriseRepository;
            _nfeService = nfeService;
            _fiscalSettingsService = fiscalSettingsService;
            _passwordLogin = passwordLogin;
            _tokenService = tokenService;
        }

        /// <summary>
        /// Enrolls a till and returns its credential, once.
        /// </summary>
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

            if (login.User.EnterpriseId != terminal.EnterpriseId)
                return RegisterFailedLogin(terminalKey, operatorKey);

            var permissions = PdvTokenScope.PermissionsFor(login.User.AccessLevel);
            if (permissions.Count == 0)
                return RegisterFailedLogin(terminalKey, operatorKey);

            _operatorLoginLimiter.Clear(operatorKey);

            terminal.LastSeenAt = DateTime.UtcNow;
            _terminalRepository.Update(terminal);
            await _terminalRepository.CommitAsync();

            var token = _tokenService.GenerateToken(BuildPdvClaims(login.User, terminal.EnterpriseId));
            var refreshToken = _tokenService.GenerateRefreshToken();
            await _tokenService.SaveRefreshTokenAsync(login.User.Id, refreshToken, RefreshTokenKind.Till);

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
        [HttpPost]
        [RequestSizeLimit(MaxPinRequestBodySizeInBytes)]
        [ProducesResponseType(204)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> SetPdvPin([FromBody] SetPdvPinRequestDTO request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

            if (PdvTokenScope.IsPdvScoped(User))
                return Forbid();

            var pin = request?.Pin ?? string.Empty;

            if (pin.Length is < 6 or > 12 || !pin.All(char.IsAsciiDigit))
                return BadRequest("Pin must be 6 to 12 digits.");

            var userId = TryGetUserId();
            if (!userId.HasValue || !TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var user = await _userRepository.GetByIdAsync(userId.Value);
            if (user == null || user.IsActive != true || user.EnterpriseId != enterpriseId)
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

                    PinVerifier = IsSupervisorTier(user.AccessLevel) ? user.PdvPinVerifier : null
                })
                .ToList();

            var revokedUserIds = users
                .Where(user => user.IsActive != true)
                .Select(user => user.Id)
                .ToList();

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

            var enterprise = await _enterpriseRepository.GetByIdAsync(enterpriseId);

            var fiscal = await _fiscalSettingsService.GetSettingsAsync(enterpriseId);

            return Ok(new PdvSyncStateDTO
            {
                ServerTimeUtc = DateTime.UtcNow,
                Operators = operators,
                RevokedUserIds = revokedUserIds,
                FailedOverrides = failedOverrides,
                PixEnabled = enterprise?.PixPaymentsEnabled == true
                             && !string.IsNullOrWhiteSpace(enterprise.StripeConnectedAccountId),
                NFCeEnabled = fiscal.Enabled && fiscal.NFCeEnabled
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
                return Ok(await _pdvService.GetSessionSummaryAsync(clientSessionId, enterpriseId, includeExpectedCash: true));
            }
            catch (NotFoundDatabaseException)
            {
                return NoContent();
            }
        }

        /// <summary>Every till enrolled for this shop, revoked ones included.</summary>
        [HttpGet]
        [Authorize(Policy = "rbac:" + RbacPermissions.Pdv.ReadReports)]
        [ProducesResponseType(typeof(IEnumerable<PdvTerminalDTO>), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        public async Task<IActionResult> GetTerminals()
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            return Ok(await _pdvService.GetTerminalsAsync(enterpriseId));
        }

        /// <summary>Retires a till, so its credential stops opening the login endpoint.</summary>
        [HttpPost("{terminalId}")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Pdv.CashMovement)]
        [ProducesResponseType(200)]
        [ProducesResponseType(204)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        public async Task<IActionResult> RevokeTerminal(Guid terminalId)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            return await _pdvService.RevokeTerminalAsync(terminalId, enterpriseId)
                ? Ok()
                : NoContent();
        }

        /// <summary>Every till currently open, so a supervisor can see the shop in one call.</summary>
        [HttpGet]
        [Authorize(Policy = "rbac:" + RbacPermissions.Pdv.ReadReports)]
        [ProducesResponseType(typeof(IEnumerable<CashSessionDTO>), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        public async Task<IActionResult> GetOpenSessions()
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            return Ok(await _pdvService.GetOpenSessionsAsync(enterpriseId));
        }

        /// <summary>Recent override attempts, newest first.</summary>
        [HttpGet]
        [Authorize(Policy = "rbac:" + RbacPermissions.Pdv.ReadReports)]
        [ProducesResponseType(typeof(IEnumerable<PdvOverrideAuditDTO>), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        public async Task<IActionResult> GetOverrideAudit(
            [FromQuery] DateTime? since, [FromQuery] bool? onlyRefused, [FromQuery] int take = 50)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            return Ok(await _pdvService.GetOverrideAuditAsync(enterpriseId, since, onlyRefused, take));
        }

        /// <summary>
        /// Issues the NFC-e for a sale the till already synced, and answers what it must print.
        /// </summary>
        [HttpPost("{clientSaleId}")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Pdv.Operate)]
        [ProducesResponseType(typeof(PdvFiscalDocumentDTO), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> IssueFiscalDocument(Guid clientSaleId, [FromQuery] string? contingencyReason = null)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var sale = await _pdvService.GetSaleAsync(clientSaleId, enterpriseId);
            if (sale == null)
                return NotFound();

            if (sale.Status != (int)EnumPdvSaleStatus.Synced || sale.OrderId == null)
                return BadRequest(new { message = "That sale has not been synced into an order yet." });

            var fiscal = await _fiscalSettingsService.GetSettingsAsync(enterpriseId);
            if (!fiscal.Enabled || !fiscal.NFCeEnabled)
                return BadRequest(new { message = "NFC-e issuance is not enabled for this enterprise." });

            var document = await _nfeService.IssueAsync(sale.OrderId.Value, enterpriseId, NFeDocumentType.NFCe);

            if (document == null)
                return BadRequest(new { message = "NFC-e issuance is not enabled for this enterprise." });

            var neverTransmitted = document.Status == NFeStatus.Error && document.TransmittedAtUtc == null;

            if (neverTransmitted && !string.IsNullOrWhiteSpace(contingencyReason))
            {
                var reason = contingencyReason.Trim();
                if (reason.Length is < 15 or > 256)
                {
                    return BadRequest(new
                    {
                        message = "A contingency reason must be between 15 and 256 characters."
                    });
                }

                document = await _nfeService.IssueAsync(
                    sale.OrderId.Value,
                    enterpriseId,
                    NFeDocumentType.NFCe,
                    new NFeContingency
                    {
                        EnteredAt = DateTime.UtcNow,
                        Justification = reason
                    }) ?? document;
            }

            return Ok(new PdvFiscalDocumentDTO
            {
                AccessKey = document.AccessKey,
                Number = document.Number,
                Series = document.Series,
                Status = (int)document.Status,
                QrCode = document.QrCode,
                UrlChave = document.UrlChave,
                IsContingency = document.TipoEmissao == 9,
                XMotivo = document.XMotivo
            });
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

        /// <summary>
        /// Product thumbnails, for a till building its picture cache.
        /// </summary>
        [HttpGet]
        [Authorize(Policy = "rbac:" + RbacPermissions.Pdv.Operate)]
        [ProducesResponseType(typeof(PdvProductImagePageDTO), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetProductImages([FromQuery] DateTime? since, [FromQuery] int? pageSize)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var size = Math.Clamp(pageSize ?? MaxImagePageSize, 1, MaxImagePageSize);
            return Ok(await _pdvService.GetProductImagesAsync(since, enterpriseId, size));
        }

        /// <summary>
        /// Raises a PIX charge for a basket, or returns the one already raised for it.
        /// </summary>
        [HttpPost]
        [RequestSizeLimit(MaxChargeRequestBodySizeInBytes)]
        [Authorize(Policy = "rbac:" + RbacPermissions.Pdv.Operate)]
        [ProducesResponseType(typeof(PdvChargeDTO), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> StartPixCharge([FromBody] StartPixChargeRequestDTO request)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            try
            {
                return Ok(await _pdvService.StartPixChargeAsync(request, enterpriseId));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// The charge as the ERP last recorded it.
        /// </summary>
        [HttpGet("{chargeId}")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Pdv.Operate)]
        [ProducesResponseType(typeof(PdvChargeDTO), 200)]
        [ProducesResponseType(204)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        public async Task<IActionResult> GetChargeStatus(Guid chargeId)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var charge = await _pdvService.GetChargeAsync(chargeId, enterpriseId);

            return charge == null ? NoContent() : Ok(charge);
        }

        /// <summary>Abandons a charge nobody paid.</summary>
        [HttpPost]
        [RequestSizeLimit(MaxChargeRequestBodySizeInBytes)]
        [Authorize(Policy = "rbac:" + RbacPermissions.Pdv.Operate)]
        [ProducesResponseType(typeof(PdvChargeDTO), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        public async Task<IActionResult> CancelPixCharge([FromBody] CancelPixChargeRequestDTO request)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            try
            {
                return Ok(await _pdvService.CancelPixChargeAsync(request.ChargeId, enterpriseId));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Where the provider tells us a charge settled.
        /// </summary>
        [HttpPost]
        [AllowAnonymous]
        [RequestSizeLimit(MaxWebhookRequestBodySizeInBytes)]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> StripeWebhook([FromServices] StripeWebhookHandler handler)
        {
            using var reader = new StreamReader(Request.Body);
            var json = await reader.ReadToEndAsync();

            var accepted = await handler.TryHandleAsync(json, Request.Headers["Stripe-Signature"]);

            return accepted ? Ok() : BadRequest();
        }

        private static CashSessionDTO ToDto(Shared.Entities.CashSession session) => new()
        {
            Id = session.Id,
            ClientSessionId = session.ClientSessionId,
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
