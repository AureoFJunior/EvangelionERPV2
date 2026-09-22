using Asp.Versioning;
using EvangelionERPV2.EmailModule.Application.Services;
using EvangelionERPV2.OrderModule.Application.Services;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace EvangelionERPV2.Web.Controllers
{
    /// <summary>
    /// Operator control over the message queues.
    /// <para>
    /// Only one action today, and it exists because nothing else in the system can reach a
    /// dead-lettered message. A message that fails three times is moved to <c>&lt;queue&gt;.dlq</c>,
    /// which has no consumer: before this endpoint it was permanently stranded there, and
    /// <c>evangelion_dlq_depth</c> could only say how many were.
    /// </para>
    /// </summary>
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [Authorize]
    public class MessagingController : ControllerBase
    {
        /// <summary>
        /// A redrive replays real work - orders that will be created, e-mails that will be sent. The
        /// cap keeps an accidental request from replaying a whole backlog, and keeps the batch small
        /// enough that an operator can watch what it does.
        /// </summary>
        private const int MaxRedriveBatch = 500;

        private readonly IOrderRabbitMQManager _orderManager;
        private readonly IEmailRabbitMQManager _emailManager;

        public MessagingController(IOrderRabbitMQManager orderManager, IEmailRabbitMQManager emailManager)
        {
            _orderManager = orderManager;
            _emailManager = emailManager;
        }

        /// <summary>
        /// Moves dead-lettered messages back onto their work queue with the retry count reset.
        /// <para>
        /// Admin only. The work queues are not tenant-scoped - one broker carries every
        /// enterprise's orders - so a redrive replays messages belonging to all of them. That is why
        /// this is not available to Manager, who can otherwise see most of the system.
        /// </para>
        /// <para>
        /// Whether replaying is safe depends on the handler being idempotent, which is an open
        /// question for this pipeline. The endpoint is deliberately manual so that judgement stays
        /// with a person rather than a timer.
        /// </para>
        /// </summary>
        /// <param name="queue">Which pipeline to redrive: <c>order</c> or <c>email</c>.</param>
        /// <param name="maxMessages">Upper bound for this batch, capped at 500.</param>
        [Authorize(Policy = RbacPolicies.Messaging.Redrive)]
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> RedriveDeadLettered(string queue, int maxMessages = 50)
        {
            if (maxMessages <= 0)
                return BadRequest("maxMessages must be greater than zero.");

            if (maxMessages > MaxRedriveBatch)
                return BadRequest($"maxMessages is capped at {MaxRedriveBatch}.");

            var target = (queue ?? string.Empty).Trim().ToLowerInvariant();

            Log.Logger.Warning(
                "Dead-letter redrive requested. Queue={Queue} Max={Max} User={User}",
                target,
                maxMessages,
                User?.Identity?.Name ?? "unknown");

            var moved = target switch
            {
                "order" => await _orderManager.RedriveDeadLetteredAsync(maxMessages, HttpContext.RequestAborted),
                "email" => await _emailManager.RedriveDeadLetteredAsync(maxMessages, HttpContext.RequestAborted),
                _ => -1
            };

            if (moved < 0)
                return BadRequest("queue must be either 'order' or 'email'.");

            return Ok(new { queue = target, redriven = moved });
        }
    }
}
