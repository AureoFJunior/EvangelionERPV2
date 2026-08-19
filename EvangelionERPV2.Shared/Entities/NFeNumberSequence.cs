using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// Monotonic nNF counter, one row per (enterprise, model, series, environment).
    /// </summary>
    /// <remarks>
    /// Its own table rather than columns on <see cref="EnterpriseFiscalSettings"/> because a
    /// tenant legitimately runs several series and two environments, and because this is the
    /// hottest write in the fiscal module — it belongs on a narrow row of its own.
    ///
    /// Allocation is a single atomic <c>UPDATE ... OUTPUT deleted.NextNumber</c>, which is
    /// correct across API instances. The 256 process-static semaphores in NFeService are NOT
    /// sufficient here: they are in-process and keyed by order, while numbering must serialise
    /// per (enterprise, model, series).
    ///
    /// Gaps are expected and legal — a SEFAZ rejection burns its number and the remedy is
    /// inutilização (out of scope v1). Never reuse a burned number.
    ///
    /// The unique index is NOT filtered on IsActive: a soft-deleted counter row that let a
    /// second row start over at 1 would re-issue numbers already sent to SEFAZ.
    /// </remarks>
    [Table("NFeNumberSequence")]
    [Index(nameof(EnterpriseId), nameof(Model), nameof(Series), nameof(Environment), IsUnique = true)]
    public class NFeNumberSequence : BaseEntity
    {
        /// <summary>55 = NFe, 65 = NFCe.</summary>
        public short Model { get; set; }

        [MaxLength(3)]
        public string Series { get; set; } = "1";

        /// <summary>tpAmb. 1 = Produção, 2 = Homologação. The two number independently.</summary>
        public byte Environment { get; set; }

        /// <summary>Next nNF to hand out. Incremented in place by the allocation statement.</summary>
        public int NextNumber { get; set; } = 1;

        [ForeignKey(nameof(Enterprise))]
        public Guid EnterpriseId { get; set; }
        public virtual Enterprise? Enterprise { get; set; }
    }
}
