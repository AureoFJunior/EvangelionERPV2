using Microsoft.AspNetCore.Mvc;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authorization;
using AutoMapper;
using Serilog;
using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using System.Security.Claims;
using MimeKit;
using System.Net;
using System.Net.Sockets;

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
        [Authorize(Policy = "rbac:" + RbacPermissions.Email.Send)]
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

                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

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
        [Authorize(Policy = "rbac:" + RbacPermissions.Email.Send)]
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

            if (await _emailService.TrySendQueuedEmail(email))
                return Ok("Emails sent");

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
            var recipientCount = message.To.Mailboxes.Count() +
                                 message.Cc.Mailboxes.Count() +
                                 message.Bcc.Mailboxes.Count();
            if (recipientCount > MaxManualEmailRecipients)
                return BadRequest($"Recipient count must be {MaxManualEmailRecipients} or lower.");
            if ((message.Subject ?? string.Empty).Length > MaxManualEmailSubjectLength)
                return BadRequest($"Subject must be {MaxManualEmailSubjectLength} characters or fewer.");

            try
            {
                await _emailService.SendEmail(message, enterpriseId);

                return Ok("Emails sent");
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Sends a signed queued email payload while preserving the tenant captured when it was enqueued.
        /// </summary>
        [Authorize(Policy = "rbac:" + RbacPermissions.Email.Send)]
        [HttpPost]
        [RequestSizeLimit(MaxEmailConfigRequestBodySizeInBytes)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SendQueuedEmail([FromBody] string queuedEmail)
        {
            if (string.IsNullOrWhiteSpace(queuedEmail))
                return BadRequest("Queued email payload is required.");

            var sent = await _emailService.TrySendQueuedEmail(queuedEmail);
            if (!sent)
                return BadRequest("Queued email payload must be a valid signed queued email message.");

            return Ok("Queued email sent");
        }

        /// <summary>
        /// Send monthly email.
        /// </summary>
        /// <returns></returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Email.Send)]
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SendMonthEmail()
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                await _emailService.SendMonthEmail(enterpriseId);

                return Ok("Monthly Emails sent");
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Send the monthly billing email to every enterprise that has monthly billing enabled.
        /// This is the scheduled broadcast path used by the background worker; unlike
        /// <see cref="SendMonthEmail"/> it is intentionally not scoped to the caller's tenant.
        /// Restricted to the self-API machine caller so interactive tenant admins cannot
        /// enqueue billing emails for every enterprise.
        /// </summary>
        /// <returns></returns>
        [Authorize(Policy = RbacPolicies.Machine.SelfApiBroadcast)]
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SendMonthlyBillingBroadcast()
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

                await _emailService.SendMonthEmail(null);

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
        [Authorize(Policy = "rbac:" + RbacPermissions.Email.Manage)]
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

                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                email.EnterpriseId = enterpriseId;
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
        [Authorize(Policy = "rbac:" + RbacPermissions.Email.Send)]
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SendStockEmail()
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                await _emailService.SendStockEmail(enterpriseId);

                return Ok("Weekly Stock Emails sent");
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Send the weekly stock email to every enterprise that has an email address.
        /// This is the scheduled broadcast path used by the background worker; unlike
        /// <see cref="SendStockEmail"/> it is intentionally not scoped to the caller's tenant.
        /// Restricted to the self-API machine caller so interactive tenant admins cannot
        /// enqueue stock emails for every enterprise.
        /// </summary>
        /// <returns></returns>
        [Authorize(Policy = RbacPolicies.Machine.SelfApiBroadcast)]
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SendWeeklyStockBroadcast()
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

                await _emailService.SendStockEmail(null);

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

            if (!IsAllowedSmtpPort(email.Port))
                return false;

            if (HasHeaderControlCharacters(email.HostName) ||
                HasHeaderControlCharacters(email.UserName) ||
                HasHeaderControlCharacters(email.Password))
            {
                return false;
            }

            if (Uri.CheckHostName(email.HostName) == UriHostNameType.Unknown)
                return false;

            if (IsBlockedSmtpHost(email.HostName))
                return false;

            return MailboxAddress.TryParse(email.UserName, out _);
        }

        private static bool IsAllowedSmtpPort(int port)
        {
            return port is 25 or 465 or 587;
        }

        private static bool IsBlockedSmtpHost(string host)
        {
            var normalizedHost = (host ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedHost))
                return true;

            if (normalizedHost.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!IPAddress.TryParse(normalizedHost, out var ip))
                return false;

            return IsRestrictedAddress(ip);
        }

        private static bool IsRestrictedAddress(IPAddress address)
        {
            if (IPAddress.IsLoopback(address))
                return true;

            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                var bytes = address.GetAddressBytes();
                var first = bytes[0];
                var second = bytes[1];

                if (first == 10 || first == 127 || first == 0)
                    return true;

                if (first == 172 && second >= 16 && second <= 31)
                    return true;

                if (first == 192 && second == 168)
                    return true;

                if (first == 169 && second == 254)
                    return true;

                if (first >= 224)
                    return true;

                return false;
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal)
                    return true;

                var bytes = address.GetAddressBytes();
                var first = bytes[0];
                if ((first & 0xFE) == 0xFC)
                    return true;

                return false;
            }

            return true;
        }

        private static bool HasHeaderControlCharacters(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            return value.Contains('\r') || value.Contains('\n');
        }
    }
}
