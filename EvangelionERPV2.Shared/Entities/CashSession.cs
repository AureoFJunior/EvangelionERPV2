using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// One shift at one till: who opened it, with how much cash, and what was counted at the end.
    /// </summary>
    /// <remarks>
    /// A PDV cannot sell without one. That is the point — every sale, every sangria and every
    /// cancellation hangs off a session, so "where did the R$ 40 go" has exactly one place to look
    /// instead of being spread across order rows that never mention cash.
    ///
    /// The session is the unit of accountability, not the terminal and not the day. Two operators
    /// sharing a till across a shift change open two sessions, and a difference lands on whoever
    /// counted it.
    ///
    /// ponytail: no per-denomination breakdown (how many 50s, how many 10s). A single counted total
    /// is what a small shop actually types; add a child table if someone starts reconciling notes.
    /// </remarks>
    [Index(nameof(EnterpriseId), nameof(TerminalId), nameof(Status))]
    [Index(nameof(EnterpriseId), nameof(ClientSessionId), IsUnique = true)]
    [Table("CashSession")]
    public class CashSession : BaseEntity
    {
        public Guid EnterpriseId { get; set; }

        /// <summary>
        /// The id the terminal minted for this session, before it had any way to reach the server.
        /// </summary>
        /// <remarks>
        /// Deliberately not the primary key. A client-supplied value used as a PK would let one
        /// tenant's terminal collide with another tenant's row and attach its sales there; as a
        /// column with a unique index scoped to <see cref="EnterpriseId"/>, a collision can only
        /// ever be within the tenant that sent it, which is exactly the idempotency this is for.
        /// </remarks>
        public Guid ClientSessionId { get; set; }

        /// <summary>
        /// Which physical till. Free text chosen at terminal setup ("Caixa 01"), not a foreign key:
        /// the ERP never provisions terminals, the terminal announces itself.
        /// </summary>
        [MaxLength(64)]
        public string TerminalId { get; set; } = string.Empty;

        /// <summary>Who opened it. Cancellations and sangrias record their own operator separately.</summary>
        [ForeignKey(nameof(User))]
        public Guid UserId { get; set; }

        [JsonIgnore]
        public virtual User? User { get; set; }

        /// <summary>
        /// When the operator opened the till, as reported by the terminal and stored in UTC.
        /// Distinct from <see cref="BaseEntity.CreatedAt"/>, which is when the server heard about
        /// it — those differ by however long the terminal was offline.
        /// </summary>
        public DateTime OpenedAt { get; set; }

        public DateTime? ClosedAt { get; set; }

        /// <summary>Cash already in the drawer at open (fundo de troco).</summary>
        public decimal OpeningFloat { get; set; }

        /// <summary>
        /// What the operator counted at close. Null while open.
        /// </summary>
        /// <remarks>
        /// Written before <see cref="ExpectedAmount"/> is ever shown to them — a blind count is the
        /// only kind worth taking, because an operator who can see the target will hit the target.
        /// </remarks>
        public decimal? CountedAmount { get; set; }

        /// <summary>
        /// Opening float plus every cash movement, computed server-side at close. Null while open.
        /// </summary>
        public decimal? ExpectedAmount { get; set; }

        /// <summary>
        /// <see cref="CountedAmount"/> minus <see cref="ExpectedAmount"/>. Negative is a shortfall.
        /// Stored rather than derived so a later correction to a movement cannot silently rewrite
        /// what the operator was held to on the night.
        /// </summary>
        public decimal? Difference { get; set; }

        /// <summary><see cref="Enums.EnumCashSessionStatus"/>.</summary>
        public int Status { get; set; }

        [JsonIgnore]
        public virtual ICollection<CashMovement>? Movements { get; set; }
    }
}
