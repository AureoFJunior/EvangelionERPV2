using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// Monotonic nNF counter, one row per (enterprise, model, series, environment).
    /// </summary>
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
