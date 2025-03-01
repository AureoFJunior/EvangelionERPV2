using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using AutoMapper;
using Serilog;
using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using MimeKit;

namespace EvangelionERPV2.Web.Controllers
{
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class EmailController : Controller
    {
        private readonly IEmailService<Email> _emailService;
        private readonly IMapper _mapper;

        public EmailController(IEmailService<Email> emailService,
            IMapper mapper)
        {
            _emailService = emailService;
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

        /// <summary>
        /// Send email.
        /// </summary>
        /// <param name="email">The email entire object to send.</param>
        /// <returns></returns>
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SendEmail([FromBody] MimeMessage email)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                await _emailService.SendEmail(email);

                return Ok("Emails sent");
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error when sending Emails", ex);
                return Problem(ex.Message);
            }
        }

        /// <summary>
        /// Send monthly email.
        /// </summary>
        /// <returns></returns>
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SendMonthEmail()
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                await _emailService.SendMonthEmail();

                return Ok("Monthly Emails sent");
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error when sending Emails", ex);
                return Problem(ex.Message);
            }
        }
    }
}
