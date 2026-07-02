using AutoMapper;
using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.UserModule.Application.Interface;
using EvangelionERPV2.UserModule.Application.Token;
using EvangelionERPV2.Web.FluentValidator;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Serilog;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EvangelionERPV2.Web.Controllers
{
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class UserController : Controller
    {
        private readonly IUserService<Shared.Entities.User> _userService;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Shared.Entities.User> _userRepository;
        private readonly IMapper _mapper;
        private readonly IConfiguration _configuration;
        private readonly AWSKMSKeyProvider _kmsProvider;
        private readonly TokenService _tokenService;
        private readonly IEmailService<EmailStructure> _emailService;
        private readonly IRecaptchaVerifier _recaptchaVerifier;
        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly ConcurrentDictionary<string, ResetPasswordRateLimitEntry> _resetPasswordRateLimit = new();
        private static readonly object _resetPasswordRateLimitSync = new();
        private static readonly TimeSpan _resetPasswordRateLimitWindow = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan _resetPasswordRateLimitCleanupInterval = TimeSpan.FromMinutes(1);
        private static long _lastResetPasswordRateLimitCleanupTicks;
        private const int ResetPasswordRateLimitMaxFailedAttempts = 5;
        private const int ResetPasswordRateLimitMaxEntries = 5000;
        private const int MaxProfilePictureSizeInBytes = 5 * 1024 * 1024;
        private const int MaxProfilePictureRequestBodySizeInBytes = 8 * 1024 * 1024;
        private const int MaxPasswordResetRequestBodySizeInBytes = 16 * 1024;
        private const int MaxAnonymousLoginRequestBodySizeInBytes = 32 * 1024;
        private const int MaxUserWriteRequestBodySizeInBytes = 64 * 1024;
        private const int MaxGoogleAuthorizationCodeLength = 2048;
        private const int MaxGoogleRedirectUriLength = 2048;
        private static readonly Regex GooglePkceCodeVerifierRegex = new("^[A-Za-z0-9\\-._~]{43,128}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private const int DefaultPageNumber = 1;
        private const int DefaultPageSize = 50;
        private const int MaxPageSize = 200;
        private const int MaxPageSizeWithPictures = 50;

        public UserController(IUserService<Shared.Entities.User> userService,
            EvangelionERPV2.Shared.Repositories.IRepository<Shared.Entities.User> userRepository,
            IMapper mapper,
            IConfiguration configuration,
            AWSKMSKeyProvider kmsProvider,
            TokenService tokenService,
            IEmailService<EmailStructure> emailService,
            IRecaptchaVerifier recaptchaVerifier)
        {
            _userService = userService;
            _userRepository = userRepository;
            _mapper = mapper;
            _configuration = configuration;
            _kmsProvider = kmsProvider;
            _tokenService = tokenService;
            _emailService = emailService;
            _recaptchaVerifier = recaptchaVerifier;
        }

        /// <summary>
        /// Log into the system and get the API token.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost]
        [AllowAnonymous]
        [RequestSizeLimit(MaxAnonymousLoginRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(UserDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> LogInto([FromBody] LoginRequestDTO request)
        {
            try
            {
                var isSelfApiMachineLogin = await IsSelfApiMachineLoginAsync(request);

                var recaptchaError = await VerifyRecaptchaAsync(request?.RecaptchaToken);
                if (recaptchaError != null && !isSelfApiMachineLogin)
                    return recaptchaError;

                if (request == null || string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
                    return BadRequest("userName and password are required.");

                var normalizedUserName = NormalizeUserName(request.UserName);
                if (string.IsNullOrWhiteSpace(normalizedUserName))
                    return BadRequest("userName and password are required.");

                var user = await ResolveUniqueActivePasswordLoginUserAsync(normalizedUserName);
                if (user == null)
                    return Unauthorized();

                var isValidPassword = SharedFunctions.VerifyPassword(request.Password, user.Password, out var needsRehash);
                if (!isValidPassword)
                    return Unauthorized();

                if (!await CanIssueTokensToUserAsync(user))
                    return Unauthorized();

                if (needsRehash)
                    user.Password = SharedFunctions.HashPassword(request.Password);

                user.IsLogged = 1;
                _userService.Update(user);

                var (token, refreshToken) = await GenerateTokensAsync(user, isSelfApiMachineLogin);
                if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(refreshToken))
                    throw new InvalidOperationException("Token generation failed.");

                Log.Logger.Information(
                    "Password login succeeded. EmailId={EmailId}",
                    GetLogSafeEmailIdentifier(user.Email ?? user.UserName));

                return Ok(await BuildUserDtoAsync(user, token, refreshToken));
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("User not found. ErrorType={ErrorType}", exnf.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Accepts a Google IdToken (obtained by the frontend SSO flow), validates it,
        /// creates the user if necessary and returns application's JWT + refresh token.
        /// This endpoint must be anonymous because the frontend calls it before it has the app token.
        /// </summary>
        [HttpPost]
        [AllowAnonymous]
        [RequestSizeLimit(MaxAnonymousLoginRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(UserDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> LoginWithGoogle([FromBody] JsonElement payload)
        {
            try
            {

                string? idToken = null;
                if (payload.ValueKind == JsonValueKind.String)
                {
                    idToken = payload.GetString();
                }
                else if (payload.ValueKind == JsonValueKind.Object &&
                         payload.TryGetProperty("idToken", out var tokenElement))
                {
                    idToken = tokenElement.GetString();
                }

                if (string.IsNullOrWhiteSpace(idToken))
                {
                    return BadRequest("idToken is required.");
                }

                var user = await _userService.LoginToSSOAsync(idToken);
                if (!await CanIssueTokensToUserAsync(user))
                {
                    Log.Logger.Warning(
                        "Google login rejected for inactive account. EmailId={EmailId}",
                        GetLogSafeEmailIdentifier(user?.Email));
                    return Unauthorized();
                }

                var (token, refreshToken) = await GenerateTokensAsync(user);
                if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(refreshToken))
                    throw new InvalidOperationException("Token generation failed.");

                Log.Logger.Information(
                    "Google login succeeded. EmailId={EmailId}",
                    GetLogSafeEmailIdentifier(user.Email));

                return Ok(await BuildUserDtoAsync(user, token, refreshToken));
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.Logger.Warning("Google login rejected. ErrorType={ErrorType}", ex.GetType().Name);
                return Unauthorized();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Accepts an OAuth authorization code, exchanges it for a Google id_token,
        /// then validates and logs the user in.
        /// </summary>
        [HttpPost]
        [AllowAnonymous]
        [RequestSizeLimit(MaxAnonymousLoginRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(UserDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> LoginWithGoogleCode([FromBody] GoogleCodeExchangeRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.RedirectUri))
            {
                return BadRequest("code and redirectUri are required.");
            }

            if (!IsValidGoogleAuthorizationCode(request.Code))
                return BadRequest("Invalid Google authorization code.");

            if (!IsAllowedGoogleRedirectUri(request.RedirectUri))
                return BadRequest("redirectUri is not allowed.");

            if (!IsValidGoogleCodeVerifier(request.CodeVerifier))
                return BadRequest("codeVerifier is required and must be a valid PKCE value.");

            try
            {
                var clientIdName = _configuration.GetSection("GoogleSettings")["ClientId"] ?? string.Empty;  
                var clientSecretName = _configuration.GetSection("GoogleSettings")["ClientSecret"] ?? string.Empty;

                var clientId = _kmsProvider.GetKMSKey(clientIdName);
                var clientSecret = _kmsProvider.GetKMSKey(clientSecretName);

                if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
                {
                    Log.Logger.Error("Google client settings are missing.");
                    return Problem("Unable to complete Google sign-in.");
                }

                var tokenResponse = await ExchangeGoogleCodeAsync(request, clientId, clientSecret);

                if (tokenResponse == null)
                {
                    return Problem("Unable to complete Google sign-in.");
                }

                if (!string.IsNullOrWhiteSpace(tokenResponse.Error))
                {
                    var safeProviderErrorCode = GetSafeGoogleProviderErrorCode(tokenResponse.Error);
                    Log.Logger.Warning("Google token exchange failed. ProviderError={ProviderError}", safeProviderErrorCode);
                    return Unauthorized("Invalid Google authorization code.");
                }

                if (string.IsNullOrWhiteSpace(tokenResponse.IdToken))
                {
                    return Unauthorized("Google id_token was not returned.");
                }

                var user = await _userService.LoginToSSOAsync(tokenResponse.IdToken);
                if (!await CanIssueTokensToUserAsync(user))
                {
                    Log.Logger.Warning(
                        "Google code login rejected for inactive account. EmailId={EmailId}",
                        GetLogSafeEmailIdentifier(user?.Email));
                    return Unauthorized();
                }

                var (token, refreshToken) = await GenerateTokensAsync(user);
                if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(refreshToken))
                    throw new InvalidOperationException("Token generation failed.");

                Log.Logger.Information(
                    "Google code login succeeded. EmailId={EmailId}",
                    GetLogSafeEmailIdentifier(user.Email));

                return Ok(await BuildUserDtoAsync(user, token, refreshToken));
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.Logger.Warning("Google code login rejected. ErrorType={ErrorType}", ex.GetType().Name);
                return Unauthorized();
            }
            catch (Exception)
            {
                throw;
            }
        }

        private static async Task<GoogleTokenResponse?> ExchangeGoogleCodeAsync(
            GoogleCodeExchangeRequest request,
            string clientId,
            string clientSecret)
        {
            var payload = new Dictionary<string, string>
            {
                ["code"] = request.Code,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = request.RedirectUri,
                ["grant_type"] = "authorization_code",
                ["code_verifier"] = request.CodeVerifier
            };

            using var content = new FormUrlEncodedContent(payload);
            var response = await _httpClient.PostAsync("https://oauth2.googleapis.com/token", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            var tokenResponse = JsonSerializer.Deserialize<GoogleTokenResponse>(
                responseBody,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (!response.IsSuccessStatusCode && tokenResponse == null)
            {
                tokenResponse = new GoogleTokenResponse
                {
                    Error = response.StatusCode.ToString(),
                    ErrorDescription = "Google token exchange failed."
                };
            }

            return tokenResponse;
        }

        private async Task<(string Token, string RefreshToken)> GenerateTokensAsync(Shared.Entities.User user, bool isMachineClient = false)
        {
            var token = _tokenService.GenerateToken(user, isMachineClient);
            var refreshToken = _tokenService.GenerateRefreshToken();
            await _tokenService.SaveRefreshTokenAsync(user.Id, refreshToken);
            return (token, refreshToken);
        }

        private async Task<Shared.Entities.User?> ResolveUniqueActivePasswordLoginUserAsync(string normalizedUserName)
        {
            if (string.IsNullOrWhiteSpace(normalizedUserName))
                return null;

            var normalizedUserNameLower = normalizedUserName.ToLowerInvariant();
            var matchingUsers = (await _userRepository.GetAllAsyncByFilter(
                    descending: false,
                    pageNumber: 1,
                    pageSize: 2,
                    predicate: x =>
                        x.IsActive == true &&
                        x.UserName != null &&
                        x.UserName.ToLower() == normalizedUserNameLower,
                    orderBy: x => x.Id))
                .ToList();

            if (matchingUsers.Count != 1)
                return null;

            return matchingUsers[0];
        }

        private static string NormalizeUserName(string? userName)
        {
            return (userName ?? string.Empty).Trim();
        }

        private async Task<bool> CanIssueTokensToUserAsync(Shared.Entities.User? user)
        {
            if (user == null || user.IsActive != true || !user.EnterpriseId.HasValue || user.EnterpriseId.Value == Guid.Empty)
                return false;

            var enterpriseRepository = HttpContext?.RequestServices?.GetService(typeof(EvangelionERPV2.Shared.Repositories.IRepository<Enterprise>))
                as EvangelionERPV2.Shared.Repositories.IRepository<Enterprise>;
            if (enterpriseRepository == null)
                return false;

            var enterprise = await enterpriseRepository.GetByIdAsync(user.EnterpriseId.Value);
            return enterprise != null && enterprise.IsActive == true;
        }

        private async Task<UserDTO> BuildUserDtoAsync(Shared.Entities.User user, string token, string refreshToken)
        {
            var profilePictureBase64 = string.IsNullOrWhiteSpace(user.ProfilePicture)
                ? string.Empty
                : await _userService.GetProfilePictureBase64Async(user.ProfilePicture);

            return new UserDTO
            {
                Id = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                BirthDate = user.BirthDate,
                ProfilePicture = profilePictureBase64,
                Token = token,
                RefreshToken = refreshToken,
                Enterprise = user.Enterprise,
                ActualTheme = user.ActualTheme,
                AccessLevel = user.AccessLevel,
                Language = user.Language
            };
        }

        private async Task<IEnumerable<UserDTO>> ToUserDtosAsync(IEnumerable<Shared.Entities.User> users, bool includePictures = false)
        {
            var userList = users?.ToList() ?? new List<Shared.Entities.User>();
            if (userList.Count == 0)
                return Enumerable.Empty<UserDTO>();

            var userDtos = new UserDTO[userList.Count];
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 8
            };

            await Parallel.ForEachAsync(Enumerable.Range(0, userList.Count), parallelOptions, async (index, _) =>
            {
                userDtos[index] = await ToUserDtoAsync(userList[index], includePictures);
            });

            return userDtos;
        }

        private async Task<UserDTO> ToUserDtoAsync(Shared.Entities.User user, bool includePicture = true)
        {
            var dto = _mapper.Map<UserDTO>(user);

            if (!includePicture || string.IsNullOrWhiteSpace(user.ProfilePicture))
            {
                dto.ProfilePicture = string.Empty;
                return dto;
            }

            dto.ProfilePicture = await _userService.GetProfilePictureBase64Async(user.ProfilePicture);
            return dto;
        }


        /// <summary>
        /// Return all the users.
        /// </summary>
        /// <returns></returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Users.Read)]
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<UserDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetUsers(int? pageNumber = null, int? pageSize = null, [FromQuery] bool includePictures = false)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var (normalizedPageNumber, normalizedPageSize) = PaginationExtensions.NormalizePagination(pageNumber, pageSize, MaxPageSize);
                if (includePictures && normalizedPageSize > MaxPageSizeWithPictures)
                    normalizedPageSize = MaxPageSizeWithPictures;

                var users = await _userRepository.GetAllAsync(normalizedPageNumber, normalizedPageSize, x =>
                    x.IsActive == true &&
                    x.EnterpriseId.HasValue &&
                    x.EnterpriseId.Value == enterpriseId);

                if (users == null || !users.Any())
                    return NoContent();

                return Ok(await ToUserDtosAsync(users, includePictures));
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("Enterprises not found. ErrorType={ErrorType}", exnf.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Get a user.
        /// </summary>
        /// <param name="id">Id of the user</param>
        /// <returns>The user that match with the id parameter.</returns>
        [Authorize(Policy = RbacPolicies.Users.ReadSelfOrRead)]
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(UserDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetUser(Guid id)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                Shared.Entities.User user = await _userRepository.GetByIdAsync(id);
                if (user == null || user.IsActive != true || user.EnterpriseId != enterpriseId)
                    return NoContent();

                return Ok(await ToUserDtoAsync(user));
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("User not found. ErrorType={ErrorType}", exnf.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Add a new user
        /// </summary>
        /// <param name="user">User to be added</param>
        /// <returns>The added user</returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Users.Create)]
        [HttpPost]
        [RequestSizeLimit(MaxUserWriteRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(UserDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddUser([FromBody] Shared.Entities.User user)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                user.EnterpriseId = enterpriseId;
                user.AccessLevel = (short)EnumAccessLevel.Employee;
                user.IsLogged = 0;

                Shared.Entities.User createdUser = await _userService.CreateAsync(user);
                return Ok(await ToUserDtoAsync(createdUser));
            }
            catch (ArgumentException ex) when (IsDuplicateEmailException(ex))
            {
                return BadRequest("Email is already in use.");
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Update an user
        /// </summary>
        /// <param name="user">User to be updated</param>
        /// <returns>The updated user</returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Users.Update)]
        [HttpPut]
        [RequestSizeLimit(MaxUserWriteRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(UserDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateUser([FromBody] Shared.Entities.User user)
        {
            try
            {
                if (user == null)
                    return BadRequest("User payload is required.");

                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var existentUser = await _userRepository.GetByIdAsync(user.Id);
                if (existentUser == null || existentUser.IsActive != true || existentUser.EnterpriseId != enterpriseId)
                    return NoContent();

                if (!Enum.IsDefined(typeof(EnumAccessLevel), user.AccessLevel))
                    return BadRequest("Invalid access level.");

                user.EnterpriseId = existentUser.EnterpriseId;
                user.IsLogged = existentUser.IsLogged;
                user.Password = string.IsNullOrWhiteSpace(user.Password) ? existentUser.Password : user.Password;
                user.ProfilePicture = string.IsNullOrWhiteSpace(user.ProfilePicture) ? existentUser.ProfilePicture : user.ProfilePicture;

                Shared.Entities.User updatedUser = _userService.Update(user);

                if (updatedUser == null)
                    return NoContent();

                return Ok(await ToUserDtoAsync(updatedUser));
            }
            catch (ArgumentException ex) when (IsDuplicateEmailException(ex))
            {
                return BadRequest("Email is already in use.");
            }
            catch (Exception)
            {
                throw;
            }
        }

        public sealed class RequestPasswordResetRequest
        {
            public string Email { get; set; } = string.Empty;
            public string RecaptchaToken { get; set; } = string.Empty;
        }

        public sealed class ResetPasswordRequest
        {
            public string Email { get; set; } = string.Empty;
            public string Token { get; set; } = string.Empty;
            public string NewPassword { get; set; } = string.Empty;
        }

        [HttpPost]
        [AllowAnonymous]
        [RequestSizeLimit(MaxPasswordResetRequestBodySizeInBytes)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> RequestPasswordReset([FromBody] RequestPasswordResetRequest request)
        {
            try
            {
                var recaptchaError = await VerifyRecaptchaAsync(request?.RecaptchaToken);
                if (recaptchaError != null)
                    return recaptchaError;

                var email = request?.Email?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(email) &&
                    email.Length <= 254 &&
                    await SharedFunctions.IsEmailValid<string>(email))
                {
                    var (token, enterpriseId) = await _userService.CreatePasswordResetTokenContextAsync(email);
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        Log.Logger.Information(
                            "Password reset requested. EmailId={EmailId}",
                            GetLogSafeEmailIdentifier(email));
                        await TrySendPasswordResetEmailAsync(email, token, enterpriseId);
                    }
                }

                return Ok(new { message = "If the email exists, reset instructions were sent." });
            }
            catch (Exception)
            {
                throw;
            }
        }

        [HttpPost]
        [AllowAnonymous]
        [RequestSizeLimit(MaxPasswordResetRequestBodySizeInBytes)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            var rateLimitKey = BuildResetPasswordRateLimitKey(request?.Email);
            if (IsResetPasswordRateLimited(rateLimitKey))
            {
                return StatusCode(StatusCodes.Status429TooManyRequests, "Too many password reset attempts. Please try again later.");
            }

            try
            {
                if (request == null)
                {
                    RegisterResetPasswordFailure(rateLimitKey);
                    return BadRequest("Invalid password reset request.");
                }

                await _userService.ResetPasswordAsync(
                    request.Email ?? string.Empty,
                    request.Token ?? string.Empty,
                    request.NewPassword ?? string.Empty);

                ClearResetPasswordFailures(rateLimitKey);
                Log.Logger.Information(
                    "Password reset completed. EmailId={EmailId}",
                    GetLogSafeEmailIdentifier(request.Email));
                return Ok(new { message = "Password updated successfully." });
            }
            catch (ArgumentException)
            {
                RegisterResetPasswordFailure(rateLimitKey);
                return BadRequest("Invalid password reset request.");
            }
            catch (Exception)
            {
                throw;
            }
        }

        public sealed class UpdateThemeRequest
        {
            public short Theme { get; set; }
        }

        public sealed class UpdateLanguageRequest
        {
            public int Language { get; set; }
        }

        public sealed class UpdateProfilePictureRequest
        {
            public string? ProfilePicture { get; set; }
        }

        /// <summary>
        /// Update the current user's theme preference.
        /// </summary>
        [Authorize(Policy = "rbac:" + RbacPermissions.Users.UpdateSelf)]
        [HttpPut]
        [RequestSizeLimit(MaxUserWriteRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(UserDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateTheme([FromBody] UpdateThemeRequest request)
        {
            try
            {
                if (request == null)
                    return BadRequest("Theme is required.");

                var userId = TryGetUserId();
                if (!userId.HasValue)
                    return Unauthorized();

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var user = await _userRepository.GetByIdAsync(userId.Value);
                if (user == null || user.IsActive != true)
                    return NotFound();

                if (user.EnterpriseId != enterpriseId)
                    return Forbid();

                user.ActualTheme = request.Theme;

                var updatedUser = _userService.Update(user);
                if (updatedUser == null)
                    return NoContent();

                return Ok(await ToUserDtoAsync(updatedUser));
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Update the current user's language preference.
        /// </summary>
        [Authorize(Policy = "rbac:" + RbacPermissions.Users.UpdateSelf)]
        [HttpPut]
        [RequestSizeLimit(MaxUserWriteRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(UserDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateLanguage([FromBody] UpdateLanguageRequest request)
        {
            try
            {
                if (request == null)
                    return BadRequest("Language is required.");

                if (!Enum.IsDefined(typeof(EnumLanguage), request.Language))
                    return BadRequest("Invalid language value.");

                var userId = TryGetUserId();
                if (!userId.HasValue)
                    return Unauthorized();

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var user = await _userRepository.GetByIdAsync(userId.Value);
                if (user == null || user.IsActive != true)
                    return NotFound();

                if (user.EnterpriseId != enterpriseId)
                    return Forbid();

                user.Language = (short)request.Language;

                var updatedUser = _userService.Update(user);
                if (updatedUser == null)
                    return NoContent();

                return Ok(await ToUserDtoAsync(updatedUser));
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Update the current user's profile picture.
        /// </summary>
        [Authorize(Policy = "rbac:" + RbacPermissions.Users.UpdateSelf)]
        [HttpPut]
        [RequestSizeLimit(MaxProfilePictureRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(UserDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateProfilePicture([FromBody] UpdateProfilePictureRequest request)
        {
            try
            {
                var userId = TryGetUserId();
                if (!userId.HasValue)
                    return Unauthorized();

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var profilePicturePayload = request?.ProfilePicture;
                if (!string.IsNullOrWhiteSpace(profilePicturePayload))
                {
                    if (!Base64PayloadValidationHelper.IsValidBase64Payload(profilePicturePayload))
                        return BadRequest("ProfilePicture must be a valid base64 payload.");

                    if (!Base64PayloadValidationHelper.IsWithinDecodedSizeLimit(profilePicturePayload, MaxProfilePictureSizeInBytes))
                        return BadRequest("ProfilePicture must be 5 MB or smaller.");

                    if (!Base64PayloadValidationHelper.HasSupportedImageSignature(profilePicturePayload))
                        return BadRequest("ProfilePicture must be PNG, JPEG, GIF, or WEBP.");
                }

                var user = await _userRepository.GetByIdAsync(userId.Value);
                if (user == null || user.IsActive != true)
                    return NotFound();

                if (user.EnterpriseId != enterpriseId)
                    return Forbid();

                var updatedUser = await _userService.UpdateProfilePictureAsync(user, request?.ProfilePicture);
                if (updatedUser == null)
                    return NoContent();

                return Ok(await ToUserDtoAsync(updatedUser));
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Delete an user
        /// </summary>
        /// <param name="id">User's Id</param>
        /// <returns>The deleted user</returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Users.Delete)]
        [HttpDelete("{id}")]
        [ProducesResponseType(typeof(UserDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteUser(Guid id)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var userToDelete = await _userRepository.GetByIdAsync(id);
                if (userToDelete == null || userToDelete.IsActive != true || userToDelete.EnterpriseId != enterpriseId)
                    return NoContent();

                Shared.Entities.User user = _userService.Delete(id);
                if (user == null || user.EnterpriseId != enterpriseId)
                    return NoContent();

                return Ok(await ToUserDtoAsync(user));
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Return all the users.
        /// </summary>
        /// <returns></returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Users.ReadSelf)]
        [HttpGet]
        [ProducesResponseType(typeof(EnumAccessLevel), StatusCodes.Status200OK)]

        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetAccessLevel()
        {
            try
            {
                return Ok(Enum.GetValues(typeof(EnumAccessLevel))
                            .Cast<EnumAccessLevel>()
                            .Select(s => new { Id = (int) s, Name = s.ToString() }));
            }
            catch (Exception)
            {
                throw;
            }
        }

        private bool TryGetEnterpriseId(out Guid enterpriseId)
        {
            var claimValue = User.FindFirst(ClaimTypes.GroupSid)?.Value;
            return Guid.TryParse(claimValue, out enterpriseId) && enterpriseId != Guid.Empty;
        }

        private Guid? TryGetUserId()
        {
            var claimValue = User.FindFirst(ClaimTypes.Sid)?.Value
                             ?? User.FindFirst("uid")?.Value;

            if (Guid.TryParse(claimValue, out var userId) && userId != Guid.Empty)
                return userId;

            var legacyClaimValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(legacyClaimValue, out userId) && userId != Guid.Empty)
                return userId;

            return null;
        }

        private static string GetSafeGoogleProviderErrorCode(string? providerError)
        {
            var rawError = (providerError ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(rawError))
                return "unknown";

            var normalized = Regex.Replace(rawError, "[^a-zA-Z0-9_.-]+", string.Empty, RegexOptions.CultureInvariant);
            if (string.IsNullOrWhiteSpace(normalized))
                return "unknown";

            return normalized.Length <= 64
                ? normalized
                : normalized[..64];
        }

        private static bool IsDuplicateEmailException(ArgumentException exception)
        {
            return string.Equals(
                exception.Message?.Trim(),
                "Email is already in use.",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsValidGoogleAuthorizationCode(string? code)
        {
            var normalized = (code ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > MaxGoogleAuthorizationCodeLength)
                return false;

            foreach (var character in normalized)
            {
                if (character < 33 || character > 126)
                    return false;
            }

            return true;
        }

        private static bool IsValidGoogleCodeVerifier(string? codeVerifier)
        {
            var normalized = (codeVerifier ?? string.Empty).Trim();
            return GooglePkceCodeVerifierRegex.IsMatch(normalized);
        }

        private bool IsAllowedGoogleRedirectUri(string? redirectUri)
        {
            var normalized = (redirectUri ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > MaxGoogleRedirectUriLength)
                return false;

            if (!TryNormalizeAbsoluteUri(normalized, out var normalizedCandidateUri, out var candidateUri))
                return false;

            if (!IsAllowedRedirectUriScheme(candidateUri))
                return false;

            var explicitAllowList = GetConfiguredGoogleRedirectUris();
            if (explicitAllowList.Count > 0)
                return explicitAllowList.Contains(normalizedCandidateUri);

            var frontendBaseUrl = (_configuration.GetSection("Frontend")["BaseUrl"] ?? string.Empty).Trim();
            if (!TryNormalizeAbsoluteUri(frontendBaseUrl, out var normalizedFrontendUri, out var frontendUri))
                return false;

            if (!IsAllowedRedirectUriScheme(frontendUri))
                return false;

            return string.Equals(GetUriOrigin(candidateUri), GetUriOrigin(frontendUri), StringComparison.OrdinalIgnoreCase);
        }

        private HashSet<string> GetConfiguredGoogleRedirectUris()
        {
            var redirectUris = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var googleSection = _configuration.GetSection("GoogleSettings");

            foreach (var value in googleSection.GetSection("AllowedRedirectUris").GetChildren().Select(child => child.Value))
            {
                if (TryNormalizeAbsoluteUri(value, out var normalizedUri, out var uri) && IsAllowedRedirectUriScheme(uri))
                    redirectUris.Add(normalizedUri);
            }

            var legacySingleRedirectUri = googleSection["RedirectUri"];
            if (TryNormalizeAbsoluteUri(legacySingleRedirectUri, out var normalizedLegacyUri, out var legacyUri) &&
                IsAllowedRedirectUriScheme(legacyUri))
            {
                redirectUris.Add(normalizedLegacyUri);
            }

            return redirectUris;
        }

        private static bool TryNormalizeAbsoluteUri(string? value, out string normalizedAbsoluteUri, out Uri parsedUri)
        {
            normalizedAbsoluteUri = string.Empty;
            parsedUri = null!;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
                return false;

            var builder = new UriBuilder(uri)
            {
                Fragment = string.Empty
            };

            if (builder.Uri.IsDefaultPort)
                builder.Port = -1;

            var path = builder.Path;
            if (string.IsNullOrEmpty(path))
                path = "/";
            else if (path.Length > 1)
                path = path.TrimEnd('/');

            builder.Path = path;
            normalizedAbsoluteUri = builder.Uri.AbsoluteUri;
            parsedUri = builder.Uri;
            return true;
        }

        private static bool IsAllowedRedirectUriScheme(Uri uri)
        {
            if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
                return false;

            if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                return true;

            return IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address);
        }

        private static string GetUriOrigin(Uri uri)
        {
            if (uri.IsDefaultPort)
                return $"{uri.Scheme}://{uri.Host}".ToLowerInvariant();

            return $"{uri.Scheme}://{uri.Host}:{uri.Port}".ToLowerInvariant();
        }


        private string BuildResetPasswordRateLimitKey(string? email)
        {
            var callerIp = ResolveCallerIpAddress();
            var normalizedEmail = (email ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalizedEmail))
                normalizedEmail = "<empty>";

            return ComputeStableRateLimitKey($"{callerIp}:{normalizedEmail}");
        }

        private static string ComputeStableRateLimitKey(string source)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(source ?? string.Empty));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static string GetLogSafeEmailIdentifier(string? email)
        {
            var normalizedEmail = (email ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalizedEmail))
                return "empty";

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail));
            return Convert.ToHexString(bytes).ToLowerInvariant()[..12];
        }

        private async Task<IActionResult?> VerifyRecaptchaAsync(string? recaptchaToken)
        {
            var result = await _recaptchaVerifier.VerifyAsync(
                recaptchaToken,
                ResolveCallerIpAddress(),
                HttpContext?.RequestAborted ?? CancellationToken.None);

            return result.Status switch
            {
                RecaptchaVerificationStatus.Success => null,
                RecaptchaVerificationStatus.Missing => BadRequest(new { code = "RECAPTCHA_MISSING", message = "reCAPTCHA token is required." }),
                _ => BadRequest(new { code = "RECAPTCHA_FAILED", message = "reCAPTCHA verification failed." })
            };
        }

        /// <summary>
        /// Recognises the background workers' self-API service login and exempts it from the
        /// interactive reCAPTCHA gate. The caller must present the exact service-account
        /// credentials held in the <c>SelfAPIAuth</c> secret, so this proves possession of the
        /// machine secret rather than weakening bot protection for arbitrary accounts. The
        /// password itself is still verified against the database further down the login flow.
        /// </summary>
        private async Task<bool> IsSelfApiMachineLoginAsync(LoginRequestDTO? request)
        {
            if (request == null ||
                string.IsNullOrWhiteSpace(request.UserName) ||
                string.IsNullOrWhiteSpace(request.Password))
                return false;

            var secretReference = _configuration.GetSection("SelfAPIAuth").Value;
            if (string.IsNullOrWhiteSpace(secretReference))
                return false;

            string secret;
            try
            {
                secret = await _kmsProvider.GetKMSKeyAsync(
                    secretReference,
                    HttpContext?.RequestAborted ?? CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log.Logger.Warning("Unable to resolve SelfAPIAuth secret for machine login. ErrorType={ErrorType}", ex.GetType().Name);
                return false;
            }

            if (!SelfApiCredential.TryParse(secret, out var serviceUserName, out var servicePassword))
                return false;

            return FixedTimeEquals(NormalizeUserName(request.UserName), serviceUserName.Trim())
                && FixedTimeEquals(request.Password, servicePassword);
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(left ?? string.Empty),
                Encoding.UTF8.GetBytes(right ?? string.Empty));
        }

        private string ResolveCallerIpAddress()
        {
            var remoteIp = HttpContext?.Connection?.RemoteIpAddress?.ToString();
            return string.IsNullOrWhiteSpace(remoteIp) ? "unknown" : remoteIp.Trim();
        }

        private bool IsResetPasswordRateLimited(string key)
        {
            var now = DateTime.UtcNow;
            TryCleanupExpiredResetPasswordFailures(now);

            if (!_resetPasswordRateLimit.TryGetValue(key, out var entry))
                return false;

            if (now - entry.WindowStartedAt >= _resetPasswordRateLimitWindow)
            {
                _resetPasswordRateLimit.TryRemove(key, out _);
                return false;
            }

            return entry.FailedAttempts >= ResetPasswordRateLimitMaxFailedAttempts;
        }

        private void RegisterResetPasswordFailure(string key)
        {
            var now = DateTime.UtcNow;
            TryCleanupExpiredResetPasswordFailures(now);

            lock (_resetPasswordRateLimitSync)
            {
                if (!_resetPasswordRateLimit.ContainsKey(key) &&
                    _resetPasswordRateLimit.Count >= ResetPasswordRateLimitMaxEntries)
                {
                    TryRemoveOldestResetPasswordRateLimitEntry();
                }

                if (!_resetPasswordRateLimit.ContainsKey(key) &&
                    _resetPasswordRateLimit.Count >= ResetPasswordRateLimitMaxEntries)
                {
                    return;
                }

                _resetPasswordRateLimit.AddOrUpdate(
                    key,
                    _ => new ResetPasswordRateLimitEntry(now, 1),
                    (_, existing) => now - existing.WindowStartedAt >= _resetPasswordRateLimitWindow
                        ? new ResetPasswordRateLimitEntry(now, 1)
                        : existing with { FailedAttempts = existing.FailedAttempts + 1 });
            }
        }

        private static void TryRemoveOldestResetPasswordRateLimitEntry()
        {
            string? oldestKey = null;
            var oldestWindowStartedAt = DateTime.MaxValue;

            foreach (var entry in _resetPasswordRateLimit)
            {
                if (entry.Value.WindowStartedAt < oldestWindowStartedAt)
                {
                    oldestWindowStartedAt = entry.Value.WindowStartedAt;
                    oldestKey = entry.Key;
                }
            }

            if (!string.IsNullOrWhiteSpace(oldestKey))
                _resetPasswordRateLimit.TryRemove(oldestKey, out _);
        }

        private static void TryCleanupExpiredResetPasswordFailures(DateTime now)
        {
            var nowTicks = now.Ticks;
            var lastCleanupTicks = Interlocked.Read(ref _lastResetPasswordRateLimitCleanupTicks);
            if (lastCleanupTicks != 0 && nowTicks - lastCleanupTicks < _resetPasswordRateLimitCleanupInterval.Ticks)
                return;

            if (Interlocked.CompareExchange(ref _lastResetPasswordRateLimitCleanupTicks, nowTicks, lastCleanupTicks) != lastCleanupTicks)
                return;

            foreach (var entry in _resetPasswordRateLimit)
            {
                if (now - entry.Value.WindowStartedAt >= _resetPasswordRateLimitWindow)
                    _resetPasswordRateLimit.TryRemove(entry.Key, out _);
            }
        }

        private static void ClearResetPasswordFailures(string key)
        {
            _resetPasswordRateLimit.TryRemove(key, out _);
        }

        private async Task TrySendPasswordResetEmailAsync(string email, string token, Guid? enterpriseId)
        {
            try
            {
                if (!enterpriseId.HasValue || enterpriseId.Value == Guid.Empty)
                {
                    Log.Logger.Warning("Password reset email skipped due to missing tenant scope. EmailId={EmailId}", GetLogSafeEmailIdentifier(email));
                    return;
                }

                var resetLink = BuildPasswordResetLink();
                var body = string.IsNullOrWhiteSpace(resetLink)
                    ? $"Use this code to reset your password: <b>{token}</b><br/>Code expires in 15 minutes."
                    : $"Open this page to reset your password: <a href=\"{resetLink}\">{resetLink}</a><br/>" +
                      $"If needed, use this code manually: <b>{token}</b><br/>Code expires in 15 minutes.";

                var emailStructure = new EmailStructure(body, "Password reset", [email]);
                var message = await _emailService.CreateEmail(emailStructure, enterpriseId);
                await _emailService.SendEmail(message, enterpriseId);
            }
            catch (Exception ex)
            {
                var emailFingerprint = GetLogSafeEmailIdentifier(email);
                Log.Logger.Warning("Unable to send password reset e-mail. ErrorType={ErrorType} EmailId={EmailId}", ex.GetType().Name, emailFingerprint);
            }
        }

        private string? BuildPasswordResetLink()
        {
            var frontendBaseUrl = _configuration.GetSection("Frontend")["BaseUrl"];
            if (string.IsNullOrWhiteSpace(frontendBaseUrl) ||
                !Uri.TryCreate(frontendBaseUrl, UriKind.Absolute, out var _))
            {
                Log.Logger.Warning("Frontend:BaseUrl is missing or invalid. Sending password reset code without link.");
                return null;
            }

            return QueryHelpers.AddQueryString(frontendBaseUrl, new Dictionary<string, string?>
            {
                ["mode"] = "reset-password"
            });
        }

        private sealed record ResetPasswordRateLimitEntry(DateTime WindowStartedAt, int FailedAttempts);
    }
}
