using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// One supervisor override attempted at a till, successful or not.
    /// </summary>
    /// <remarks>
    /// The refused ones are the point. A run of failures against one supervisor is somebody
    /// guessing a PIN, and because the till counts the failures it is told about rather than the
    /// ones it saw, the lockout follows the supervisor across every terminal instead of only the
    /// one being guessed at.
    ///
    /// No PIN, no verifier, no password: this table exists to be read.
    /// </remarks>
    [Index(nameof(EnterpriseId), nameof(ClientOverrideId), IsUnique = true)]
    [Table("PdvOverrideAudit")]
    public class PdvOverrideAudit : BaseEntity
    {
        public Guid EnterpriseId { get; set; }

        /// <summary>
        /// The override as the terminal numbered it. Unique per enterprise, so a till resending its
        /// backlog after a lost response records the attempt once rather than twice.
        /// </summary>
        public Guid ClientOverrideId { get; set; }

        /// <summary>Who was asked to authorise. Set even when they failed to.</summary>
        public Guid SupervisorUserId { get; set; }

        /// <summary>Who was ringing the sale and needed the authorisation.</summary>
        public Guid OperatorUserId { get; set; }

        [MaxLength(64)]
        public string TerminalId { get; set; } = string.Empty;

        /// <summary>The basket this happened in, as the terminal numbers it. Empty when no sale was open.</summary>
        public Guid ClientSaleId { get; set; }

        /// <summary>The permission being exercised, e.g. <c>pdv.item.void</c>.</summary>
        [MaxLength(64)]
        public string Permission { get; set; } = string.Empty;

        public bool Succeeded { get; set; }

        /// <summary>When it happened, per the terminal clock, in UTC.</summary>
        public DateTime OccurredAt { get; set; }

        public static PdvOverrideAudit Record(
            Guid enterpriseId,
            Guid clientOverrideId,
            Guid supervisorUserId,
            Guid operatorUserId,
            string terminalId,
            Guid clientSaleId,
            string permission,
            bool succeeded,
            DateTime occurredAt) =>
            new()
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                ClientOverrideId = clientOverrideId,
                SupervisorUserId = supervisorUserId,
                OperatorUserId = operatorUserId,
                TerminalId = terminalId,
                ClientSaleId = clientSaleId,
                Permission = permission,
                Succeeded = succeeded,
                OccurredAt = occurredAt
            };
    }
}
