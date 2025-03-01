using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using AutoMapper;
using Serilog;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.UserModule.Application.Interface;
using EvangelionERPV2.UserModule.Domain.Interface;
using EvangelionERPV2.UserModule.Application.Token;

namespace EvangelionERPV2.Web.Controllers
{
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class UserController : Controller
    {
        private readonly IUserService<Shared.Entities.User> _userService;
        private readonly IRepository<Shared.Entities.User> _userRepository;
        private readonly IMapper _mapper;

        public UserController(IUserService<Shared.Entities.User> userService,
            IRepository<Shared.Entities.User> userRepository,
            IMapper mapper)
        {
            _userService = userService;
            _userRepository = userRepository;
            _mapper = mapper;
        }

        /// <summary>
        /// Log into the system and get the API token.
        /// </summary>
        /// <param name="userName"></param>
        /// <param name="password"></param>
        /// <returns></returns>
        [HttpGet("{userName}/{password}")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(UserDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> LogInto(string userName, string password)
        {
            try
            {
                Shared.Entities.User? user = _userRepository.GetByCondition(x => x != null && x.UserName == userName && SharedFunctions.Decrypt(x.Password) == password).FirstOrDefault();
                if (user == null)
                    return NoContent();

                user.IsLogged = 1;
                _userService.Update(user);

                string token, refreshToken;
                GenerateToken(user, out token, out refreshToken);

                if (String.IsNullOrEmpty(token) || String.IsNullOrEmpty(refreshToken))
                    throw new Exception();

                user.IsLogged = 1;
                _userService.Update(user);

                UserDTO loggedUser = new UserDTO()
                {
                    Id = user.Id,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    Email = user.Email,
                    BirthDate = user.BirthDate,
                    Token = token,
                    RefreshToken = refreshToken
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

        private static void GenerateToken(Shared.Entities.User user, out string token, out string refreshToken)
        {
            token = TokenService.GenerateToken(user);
            refreshToken = TokenService.GenerateRefreshToken();
            TokenService.SaveRefreshToken(user.UserName, refreshToken);
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

                IEnumerable<UserDTO> userDTO = _mapper.Map<IEnumerable<UserDTO>>(user);
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
        ///  Get a logged user.
        /// </summary>
        /// <returns>The logged user.</returns>
        [HttpGet]
        [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> IsLogged()
        {
            try
            {
                IEnumerable<Shared.Entities.User> users = await _userRepository.GetAllAsync();
                if (users == null)
                    return NoContent();

                UserDTO? user = users.Where(x => x.IsLogged == 1).Select(x => new UserDTO { Id = x.Id, FirstName = x.FirstName, LastName = x.LastName, Email = x.Email, BirthDate = x.BirthDate, Token = "" }).FirstOrDefault();
                return Ok(user);
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
    }
}
