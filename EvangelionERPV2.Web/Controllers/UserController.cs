using AutoMapper;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.UserModule.Application.Interface;
using EvangelionERPV2.UserModule.Application.Token;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Text.Json;
using System.Security.Claims;

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
        private static readonly HttpClient _httpClient = new HttpClient();

        public UserController(IUserService<Shared.Entities.User> userService,
            EvangelionERPV2.Shared.Repositories.IRepository<Shared.Entities.User> userRepository,
            IMapper mapper,
            IConfiguration configuration,
            AWSKMSKeyProvider kmsProvider,
            TokenService tokenService)
        {
            _userService = userService;
            _userRepository = userRepository;
            _mapper = mapper;
            _configuration = configuration;
            _kmsProvider = kmsProvider;
            _tokenService = tokenService;
        }

        /// <summary>
        /// Log into the system and get the API token.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost]
        [AllowAnonymous]
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
                    throw new Exception();

                UserDTO loggedUser = new UserDTO()
                {
                    Id = user.Id,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    Email = user.Email,
                    BirthDate = user.BirthDate,
                    ProfilePicture = user.ProfilePicture,
                    Token = token,
                    RefreshToken = refreshToken,
                    Enterprise = user.Enterprise,
                    ActualTheme = user.ActualTheme,
                    AccessLevel = user.AccessLevel,
                    Language = user.Language
                };

                return Ok(loggedUser);
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("User not found", exnf);
                return NoContent();
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error when logging", ex);
                return Problem("Error when logging");
            }
        }

        /// <summary>
        /// Accepts a Google IdToken (obtained by the frontend SSO flow), validates it,
        /// creates the user if necessary and returns application's JWT + refresh token.
        /// This endpoint must be anonymous because the frontend calls it before it has the app token.
        /// </summary>
        [HttpPost]
        [AllowAnonymous]
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
                if (string.IsNullOrEmpty(token))
                    throw new Exception("Token generation failed.");

                return Ok(BuildUserDto(user, token, refreshToken));
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error when logging with Google", ex);
                return Problem("Error when logging with Google");
            }
        }

        /// <summary>
        /// Accepts an OAuth authorization code, exchanges it for a Google id_token,
        /// then validates and logs the user in.
        /// </summary>
        [HttpPost]
        [AllowAnonymous]
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
                    Log.Logger.Error("Google token exchange failed: {Error} {Description}", tokenResponse.Error, tokenResponse.ErrorDescription);
                    return Unauthorized(tokenResponse.ErrorDescription ?? "Invalid Google authorization code.");
                }

                if (string.IsNullOrWhiteSpace(tokenResponse.IdToken))
                {
                    return Unauthorized("Google id_token was not returned.");
                }

                var user = await _userService.LoginToSSOAsync(tokenResponse.IdToken);
                var (token, refreshToken) = await GenerateTokensAsync(user);
                if (string.IsNullOrEmpty(token))
                    throw new Exception("Token generation failed.");

                return Ok(BuildUserDto(user, token, refreshToken));
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error when logging with Google code", ex);
                return Problem("Error when logging with Google code");
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

        private static UserDTO BuildUserDto(Shared.Entities.User user, string token, string refreshToken)
        {
            return new UserDTO
            {
                Id = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                BirthDate = user.BirthDate,
                ProfilePicture = user.ProfilePicture,
                Token = token,
                RefreshToken = refreshToken,
                Enterprise = user.Enterprise,
                ActualTheme = user.ActualTheme,
                AccessLevel = user.AccessLevel,
                Language = user.Language
            };
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
        public async Task<IActionResult> GetUsers()
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                IEnumerable<Shared.Entities.User> users = await _userRepository.GetAllAsync();
                if (users == null)
                    return NoContent();

                IEnumerable<UserDTO> userDTO = _mapper.Map<IEnumerable<UserDTO>>(users);
                return Ok(userDTO);
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("Enterprises not found", exnf);
                return NoContent();
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error when getting Enterprises", ex);
                return Problem(ex.Message);
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
                Shared.Entities.User user = await _userRepository.GetByIdAsync(id);
                if (user == null)
                    return NoContent();

                UserDTO userDTO = _mapper.Map<UserDTO>(user);
                return Ok(userDTO);
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("User not found", exnf);
                return NoContent();
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error when getting User", ex);
                return Problem(ex.Message);
            }
        }

        /// <summary>
        /// Add a new user
        /// </summary>
        /// <param name="user">User to be added</param>
        /// <returns>The added user</returns>
        [HttpPost]
        [AllowAnonymous]
        [ProducesResponseType(typeof(UserDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddUser([FromBody] Shared.Entities.User user)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                Shared.Entities.User createdUser = await _userService.CreateAsync(user);
                return Ok(createdUser);
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error when adding User", ex);
                return Problem(ex.Message);
            }
        }

        /// <summary>
        /// Update an user
        /// </summary>
        /// <param name="user">User to be updated</param>
        /// <returns>The updated user</returns>
        [HttpPut]
        [ProducesResponseType(typeof(UserDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateUser([FromBody] Shared.Entities.User user)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                Shared.Entities.User updatedUser = _userService.Update(user);

                if (updatedUser == null)
                    return NoContent();

                return Ok(user);
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error when updating User", ex);
                return Problem(ex.Message);
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
        [ProducesResponseType(typeof(UserDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public IActionResult UpdateTheme([FromBody] UpdateThemeRequest request)
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

                var dto = _mapper.Map<UserDTO>(updatedUser);
                return Ok(dto);
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error when updating user theme", ex);
                return Problem(ex.Message);
            }
        }

        /// <summary>
        /// Update the current user's language preference.
        /// </summary>
        [HttpPut]
        [ProducesResponseType(typeof(UserDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public IActionResult UpdateLanguage([FromBody] UpdateLanguageRequest request)
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

                var dto = _mapper.Map<UserDTO>(updatedUser);
                return Ok(dto);
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error when updating user language", ex);
                return Problem(ex.Message);
            }
        }

        /// <summary>
        /// Update the current user's profile picture.
        /// </summary>
        [HttpPut]
        [ProducesResponseType(typeof(UserDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public IActionResult UpdateProfilePicture([FromBody] UpdateProfilePictureRequest request)
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

                user.ProfilePicture = request?.ProfilePicture?.Trim() ?? string.Empty;

                var updatedUser = _userService.Update(user);
                if (updatedUser == null)
                    return NoContent();

                var dto = _mapper.Map<UserDTO>(updatedUser);
                return Ok(dto);
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error when updating user profile picture", ex);
                return Problem(ex.Message);
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

                Shared.Entities.User user = _userService.Delete(id);
                if (user == null)
                    return NoContent();

                return Ok(user);
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error when deleting User", ex);
                return Problem(ex.Message);
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
            catch (Exception ex)
            {
                Log.Logger.Error("Error when getting Access Levels", ex);
                return Problem(ex.Message);
            }
        }
    }
}
