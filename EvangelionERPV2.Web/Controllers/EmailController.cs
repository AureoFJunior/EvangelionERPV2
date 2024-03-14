using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using AutoMapper;
using EvangelionERPV2.Domain.DTOs;
using EvangelionERPV2.Domain.Models;
using EvangelionERPV2.Domain.Interfaces.Services;
using EvangelionERPV2.Domain.Interfaces.Repositories;
using Serilog;

namespace EvangelionERPV2.Web.Controllers
{
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class EmailController : Controller
    {
        private readonly IEmailService<Email> _emailService;
        private readonly IRepository<Email> _emailRepository;
        private readonly IMapper _mapper;

        public EmailController(IEmailService<Email> emailService,
            IRepository<Email> emailRepository,
            IMapper mapper)
        {
            _emailService = emailService;
            _emailRepository = emailRepository;
            _mapper = mapper;
        }

        /// <summary>
        /// Send a manual made email.
        /// </summary>
        /// <param name="email">The email entire object to send.</param>
        /// <param name="enterprise">The enterprise that will receive that email.</param>
        /// <returns></returns>
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SendManualEmail([FromBody] Email email, [FromBody] Enterprise enterprise)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                await _emailService.SendManualEmail(email, enterprise);

                return Ok("Emails sended to the emails queue");
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error when sending Emails", ex);
                return Problem(ex.Message);
            }
        }
    }
}