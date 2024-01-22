using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using AutoMapper;
using EvangelionERPV2.Domain.DTOs;
using EvangelionERPV2.Domain.Models;
using EvangelionERPV2.Domain.Interfaces.Services;
using EvangelionERPV2.Domain.Interfaces.Repositories;
using EvangelionERPV2.Domain.Models.Token;
using Serilog;

namespace EvangelionERPV2.Web.Controllers
{
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class UserController : Controller
    {
        private readonly IUserService<User> _userService;
        private readonly IRepository<User> _userRepository;
        private readonly IMapper _mapper;

        public UserController(IUserService<User> userService,
            IRepository<User> userRepository,
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
                User? user = _userRepository.GetByCondition(x => x != null && x.UserName == userName && x.Password == password).FirstOrDefault();
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
            catch(Exception ex)
            {
                Log.Logger.Error("Error when logging", ex);
                return Problem("Error when logging");
            }
        }

        private static void GenerateToken(User user, out string token, out string refreshToken)
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
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetUsers()
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            IEnumerable<User> users = await _userRepository.GetAllAsync();
            if (users == null)
                return NoContent();

            IEnumerable<UserDTO> userDTO = _mapper.Map<IEnumerable<UserDTO>>(users);
            return Ok(userDTO);
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
            User user = await _userRepository.GetByIdAsync(id);
            if (user == null)
                return NoContent();

            IEnumerable<UserDTO> userDTO = _mapper.Map<IEnumerable<UserDTO>>(user);
            return Ok(userDTO);
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
            IEnumerable<User> users = await _userRepository.GetAllAsync();
            if (users == null)
                return NoContent();

            UserDTO? user = users.Where(x => x.IsLogged == 1).Select(x => new UserDTO { Id = x.Id, FirstName = x.FirstName, LastName = x.LastName, Email = x.Email, BirthDate = x.BirthDate, Token = "" }).FirstOrDefault();
            return Ok(user);
        }

        /// <summary>
        /// Add a new user
        /// </summary>
        /// <param name="user">User to be added</param>
        /// <returns>The added user</returns>
        [HttpPost]
        [AllowAnonymous]
        [ProducesResponseType(typeof(UserDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddUser([FromBody] User user)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            User createdUser = await _userService.CreateAsync(user);
            return Ok(createdUser);
        }

        /// <summary>
        /// Update an user
        /// </summary>
        /// <param name="user">User to be updated</param>
        /// <returns>The updated user</returns>
        [HttpPut]
        [ProducesResponseType(typeof(UserDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateUser([FromBody] User user)
        {
            if(!ModelState.IsValid) return BadRequest(ModelState);

            User updatedUser = _userService.Update(user);

            if (updatedUser == null)
                return NoContent();

            return Ok(user);
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
            if (!ModelState.IsValid) return BadRequest(ModelState);

            User user = _userService.Delete(id);
            if (user == null)
                return NoContent();

            return Ok(user);
        }
    }
}