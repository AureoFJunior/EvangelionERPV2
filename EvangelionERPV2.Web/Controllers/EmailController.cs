using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using AutoMapper;
using Serilog;
using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using System.Security.Claims;
using MimeKit;

namespace EvangelionERPV2.Web.Controllers
{
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class EmailController : Controller
    {
        private const int MaxEmailConfigRequestBodySizeInBytes = 64 * 1024;
        private const int MaxManualEmailRecipients = 50;
        private const int MaxManualEmailSubjectLength = 200;
        private const int MaxManualEmailBodyLength = 20000;
        private const int MaxEmailHostNameLength = 255;
        private const int MaxEmailUserNameLength = 320;
        private const int MaxEmailPasswordLength = 512;
        private readonly IEmailService<EmailStructure> _emailService;
        private readonly IRepository<Shared.Entities.User> _userRepository;
        private readonly IMapper _mapper;

        public EmailController(IEmailService<EmailStructure> emailService,
            IRepository<Shared.Entities.User> userRepository,
            IMapper mapper)
        {
            _emailService = emailService;
            _userRepository = userRepository;
            _mapper = mapper;
        }

        /// <summary>
        /// Send a manual made email.
        /// </summary>
        /// <param name="email">The email entire object to send.</param>
        /// <returns></returns>
        [HttpPost]
        [RequestSizeLimit(MaxEmailConfigRequestBodySizeInBytes)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SendManualEmail([FromBody] EmailStructure email)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var callerAccess = await ResolveAccessLevelAsync(TryGetUserId(), enterpriseId);
                if (!IsAdminAccess(callerAccess))
                    return Forbid();

                if (email == null)
                    return BadRequest("Email payload is required.");

                var recipients = (email.RecipientEmails ?? Enumerable.Empty<string>())
                    .Select(x => (x ?? string.Empty).Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (recipients.Count == 0)
                    return BadRequest("At least one recipient is required.");

                if (recipients.Count > MaxManualEmailRecipients)
                    return BadRequest($"Recipient count must be {MaxManualEmailRecipients} or lower.");

                if ((email.Subject ?? string.Empty).Length > MaxManualEmailSubjectLength)
                    return BadRequest($"Subject must be {MaxManualEmailSubjectLength} characters or fewer.");

                if ((email.Body ?? string.Empty).Length > MaxManualEmailBodyLength)
                    return BadRequest($"Body must be {MaxManualEmailBodyLength} characters or fewer.");

                email.RecipientEmails = recipients;

                if (!ModelState.IsValid) return BadRequest(ModelState);

                var enterprise = new Enterprise
                {
                    Id = enterpriseId
                };

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
        [RequestSizeLimit(MaxEmailConfigRequestBodySizeInBytes)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SendEmail([FromBody] string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return BadRequest("Email payload is required.");

            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var callerAccess = await ResolveAccessLevelAsync(TryGetUserId(), enterpriseId);
            if (!IsAdminAccess(callerAccess))
                return Forbid();

            MimeMessage message;
            try
            {
                using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(email)))
                {
                    message = MimeMessage.Load(stream);
                }
            }
            catch
            {
                return BadRequest("Email payload must be a valid MIME message.");
            }

            if (!message.From.Mailboxes.Any() || !message.To.Mailboxes.Any())
                return BadRequest("Email payload must include valid From and To headers.");
            if (message.To.Mailboxes.Count() > MaxManualEmailRecipients)
                return BadRequest($"Recipient count must be {MaxManualEmailRecipients} or lower.");
            if ((message.Subject ?? string.Empty).Length > MaxManualEmailSubjectLength)
                return BadRequest($"Subject must be {MaxManualEmailSubjectLength} characters or fewer.");

            try
            {
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

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var callerAccess = await ResolveAccessLevelAsync(TryGetUserId(), enterpriseId);
                if (!IsAdminAccess(callerAccess))
                    return Forbid();

                await _emailService.SendMonthEmail(enterpriseId);

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
        [RequestSizeLimit(MaxEmailConfigRequestBodySizeInBytes)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddEmail([FromBody] Email email)
        {
            try
            {                
                if (email == null)
                    return BadRequest("Email payload is required.");

                email.HostName = (email.HostName ?? string.Empty).Trim();
                email.UserName = (email.UserName ?? string.Empty).Trim();
                email.Password = (email.Password ?? string.Empty).Trim();

                if (!IsValidEmailSettingsPayload(email))
                    return BadRequest("Invalid email settings payload.");

                if (!ModelState.IsValid) return BadRequest(ModelState);

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var callerAccess = await ResolveAccessLevelAsync(TryGetUserId(), enterpriseId);
                if (!IsAdminAccess(callerAccess))
                    return Forbid();

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

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var callerAccess = await ResolveAccessLevelAsync(TryGetUserId(), enterpriseId);
                if (!IsAdminAccess(callerAccess))
                    return Forbid();

                await _emailService.SendStockEmail(enterpriseId);

                return Ok("Weekly Stock Emails sent");
            }
            catch (Exception)
            {
                throw;
            }
        }

        private bool TryGetEnterpriseId(out Guid enterpriseId)
        {
            var claimValue = User?.FindFirst(ClaimTypes.GroupSid)?.Value;
            return Guid.TryParse(claimValue, out enterpriseId) && enterpriseId != Guid.Empty;
        }

        private Guid? TryGetUserId()
        {
            var claimValue = User?.FindFirst(ClaimTypes.Sid)?.Value
                             ?? User?.FindFirst("uid")?.Value;

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

        private static bool IsValidEmailSettingsPayload(Email email)
        {
            if (string.IsNullOrWhiteSpace(email.HostName) ||
                string.IsNullOrWhiteSpace(email.UserName) ||
                string.IsNullOrWhiteSpace(email.Password))
            {
                return false;
            }

            if (email.HostName.Length > MaxEmailHostNameLength ||
                email.UserName.Length > MaxEmailUserNameLength ||
                email.Password.Length > MaxEmailPasswordLength)
            {
                return false;
            }

            if (email.Port is < 1 or > 65535)
                return false;

            if (HasHeaderControlCharacters(email.HostName) ||
                HasHeaderControlCharacters(email.UserName) ||
                HasHeaderControlCharacters(email.Password))
            {
                return false;
            }

            if (Uri.CheckHostName(email.HostName) == UriHostNameType.Unknown)
                return false;

            return MailboxAddress.TryParse(email.UserName, out _);
        }

        private static bool HasHeaderControlCharacters(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            return value.Contains('\r') || value.Contains('\n');
        }
    }
}
