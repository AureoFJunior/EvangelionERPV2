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
        private readonly IEmailService<EmailStructure> _emailService;
        private readonly IMapper _mapper;

        public EmailController(IEmailService<EmailStructure> emailService,
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
        public async Task<IActionResult> SendManualEmail([FromBody] EmailStructure email, [FromBody] Enterprise enterprise)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                await _emailService.SendManualEmail(email, enterprise);

                return Ok("Emails sended to the emails queue");
            }
            catch (Exception)
            {
                throw;
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
        public async Task<IActionResult> SendEmail([FromBody] string email)
        {
            try
            {
                MimeMessage message;
                using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(email)))
                {
                    message = MimeMessage.Load(stream);
                }

                await _emailService.SendEmail(message);

                return Ok("Emails sent");
            }
            catch (Exception)
            {
                throw;
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
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Create Email.
        /// </summary>
        /// <param name="email">Add the email to be used for sending notifications.</param>
        /// <returns></returns>
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddEmail([FromBody] Email email)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                await _emailService.CreateAsync(email);

                return Ok("Email created successfully");
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Send stock email.
        /// </summary>
        /// <returns></returns>
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SendStockEmail()
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                await _emailService.SendStockEmail();

                return Ok("Weekly Stock Emails sent");
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
