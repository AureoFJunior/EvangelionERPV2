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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Serilog;
using System.Collections.Concurrent;
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
            IEmailService<EmailStructure> emailService)
        {
            _userService = userService;
            _userRepository = userRepository;
            _mapper = mapper;
            _configuration = configuration;
            _kmsProvider = kmsProvider;
            _tokenService = tokenService;
            _emailService = emailService;
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
                if (request == null || string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
                    return BadRequest("userName and password are required.");

                Shared.Entities.User? user = _userRepository.GetByCondition(x => x != null && x.UserName == request.UserName).FirstOrDefault();
                if (user == null)
                    return Unauthorized();

                var isValidPassword = SharedFunctions.VerifyPassword(request.Password, user.Password, out var needsRehash);
                if (!isValidPassword)
                    return Unauthorized();

                if (needsRehash)
                    user.Password = SharedFunctions.HashPassword(request.Password);

                user.IsLogged = 1;
                _userService.Update(user);

                var (token, refreshToken) = await GenerateTokensAsync(user);
                if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(refreshToken))
                    throw new InvalidOperationException("Token generation failed.");

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
                var (token, refreshToken) = await GenerateTokensAsync(user);
                if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(refreshToken))
                    throw new InvalidOperationException("Token generation failed.");

                return Ok(await BuildUserDtoAsync(user, token, refreshToken));
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

            try
            {
                var clientIdName = _configuration.GetSection("GoogleSettings")["ClientId"] ?? string.Empty;  
                var clientSecretName = _configuration.GetSection("GoogleSettings")["ClientSecret"] ?? string.Empty;

                var clientId = _kmsProvider.GetKMSKey(clientIdName);
                var clientSecret = _kmsProvider.GetKMSKey(clientSecretName);

                if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
                {
                    Log.Logger.Error("Google client settings are missing.");
                    return Problem("Google client settings are missing.");
                }

                var tokenResponse = await ExchangeGoogleCodeAsync(request, clientId, clientSecret);

                if (tokenResponse == null)
                {
                    return Problem("Unable to exchange Google code.");
                }

                if (!string.IsNullOrWhiteSpace(tokenResponse.Error))
                {
                    var safeProviderErrorCode = GetSafeGoogleProviderErrorCode(tokenResponse.Error);
                    Log.Logger.Warning("Google token exchange failed. ProviderError={ProviderError}", safeProviderErrorCode);
                    return Unauthorized(tokenResponse.ErrorDescription ?? "Invalid Google authorization code.");
                }

                if (string.IsNullOrWhiteSpace(tokenResponse.IdToken))
                {
                    return Unauthorized("Google id_token was not returned.");
                }

                var user = await _userService.LoginToSSOAsync(tokenResponse.IdToken);
                var (token, refreshToken) = await GenerateTokensAsync(user);
                if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(refreshToken))
                    throw new InvalidOperationException("Token generation failed.");

                return Ok(await BuildUserDtoAsync(user, token, refreshToken));
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
            };

            if (!string.IsNullOrWhiteSpace(request.CodeVerifier))
            {
                payload["code_verifier"] = request.CodeVerifier;
            }

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

        private async Task<(string Token, string RefreshToken)> GenerateTokensAsync(Shared.Entities.User user)
        {
            var token = _tokenService.GenerateToken(user);
            var refreshToken = _tokenService.GenerateRefreshToken();
            await _tokenService.SaveRefreshTokenAsync(user.Id, refreshToken);
            return (token, refreshToken);
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
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<UserDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetUsers(int? pageNumber = null, int? pageSize = null, [FromQuery] bool includePictures = false)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var callerId = TryGetUserId();
                var callerAccess = await ResolveAccessLevelAsync(callerId, enterpriseId);
                if (!IsAdminAccess(callerAccess))
                    return Forbid();

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

                var callerId = TryGetUserId();
                var callerAccess = await ResolveAccessLevelAsync(callerId, enterpriseId);

                Shared.Entities.User user = await _userRepository.GetByIdAsync(id);
                if (user == null || user.IsActive != true || user.EnterpriseId != enterpriseId)
                    return NoContent();

                var callerIsTargetUser = callerId.HasValue && callerId.Value == user.Id;
                if (!callerIsTargetUser && !IsAdminAccess(callerAccess))
                    return Forbid();

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
                if (!ModelState.IsValid) return BadRequest(ModelState);

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var callerId = TryGetUserId();
                var callerAccess = await ResolveAccessLevelAsync(callerId, enterpriseId);
                if (!IsAdminAccess(callerAccess))
                    return Forbid();

                user.EnterpriseId = enterpriseId;
                user.AccessLevel = (short)EnumAccessLevel.Employee;
                user.IsLogged = 0;

                Shared.Entities.User createdUser = await _userService.CreateAsync(user);
                return Ok(await ToUserDtoAsync(createdUser));
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
                if (!ModelState.IsValid) return BadRequest(ModelState);

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var callerId = TryGetUserId();
                var callerAccess = await ResolveAccessLevelAsync(callerId, enterpriseId);
                if (!IsAdminAccess(callerAccess))
                    return Forbid();

                var existentUser = await _userRepository.GetByIdAsync(user.Id);
                if (existentUser == null || existentUser.IsActive != true || existentUser.EnterpriseId != enterpriseId)
                    return NoContent();

                if (!Enum.IsDefined(typeof(EnumAccessLevel), user.AccessLevel))
                    return BadRequest("Invalid access level.");

                user.EnterpriseId = existentUser.EnterpriseId;
                user.IsLogged = existentUser.IsLogged;

                Shared.Entities.User updatedUser = _userService.Update(user);

                if (updatedUser == null)
                    return NoContent();

                return Ok(await ToUserDtoAsync(updatedUser));
            }
            catch (Exception)
            {
                throw;
            }
        }

        public sealed class RequestPasswordResetRequest
        {
            public string Email { get; set; } = string.Empty;
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
                var email = request?.Email?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(email) &&
                    email.Length <= 254 &&
                    await SharedFunctions.IsEmailValid<string>(email))
                {
                    var token = await _userService.CreatePasswordResetTokenAsync(email);
                    if (!string.IsNullOrWhiteSpace(token))
                        await TrySendPasswordResetEmailAsync(email, token);
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
                var userName = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrWhiteSpace(userName))
                    return Unauthorized();

                Shared.Entities.User? user = _userRepository
                    .GetByCondition(x => x != null && x.UserName == userName)
                    .FirstOrDefault();

                if (user == null)
                    return NotFound();

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
                if (!Enum.IsDefined(typeof(EnumLanguage), request.Language))
                    return BadRequest("Invalid language value.");

                var userName = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrWhiteSpace(userName))
                    return Unauthorized();

                Shared.Entities.User? user = _userRepository
                    .GetByCondition(x => x != null && x.UserName == userName)
                    .FirstOrDefault();

                if (user == null)
                    return NotFound();

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
                var userName = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrWhiteSpace(userName))
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

                Shared.Entities.User? user = _userRepository
                    .GetByCondition(x => x != null && x.UserName == userName)
                    .FirstOrDefault();

                if (user == null)
                    return NotFound();

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
        [HttpDelete("{id}")]
        [ProducesResponseType(typeof(UserDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteUser(Guid id)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var callerId = TryGetUserId();
                var callerAccess = await ResolveAccessLevelAsync(callerId, enterpriseId);
                if (!IsAdminAccess(callerAccess))
                    return Forbid();

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

            return null;
        }

        private async Task<short?> ResolveAccessLevelAsync(Guid? userId, Guid enterpriseId)
        {
            if (!userId.HasValue || enterpriseId == Guid.Empty)
                return null;

            var user = await _userRepository.GetByIdAsync(userId.Value);
            if (user == null || user.IsActive != true || user.EnterpriseId != enterpriseId)
                return null;

            return user.AccessLevel;
        }

        private static bool IsAdminAccess(short? accessLevel)
        {
            return accessLevel.HasValue && accessLevel.Value == (short)EnumAccessLevel.Admin;
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

        private async Task TrySendPasswordResetEmailAsync(string email, string token)
        {
            try
            {
                var resetLink = BuildPasswordResetLink();
                var body = string.IsNullOrWhiteSpace(resetLink)
                    ? $"Use this code to reset your password: <b>{token}</b><br/>Code expires in 15 minutes."
                    : $"Open this page to reset your password: <a href=\"{resetLink}\">{resetLink}</a><br/>" +
                      $"If needed, use this code manually: <b>{token}</b><br/>Code expires in 15 minutes.";

                var emailStructure = new EmailStructure(body, "Password reset", [email]);
                var message = await _emailService.CreateEmail(emailStructure);
                await _emailService.SendEmail(message);
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
