using EvangelionERPV2.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.Entities
{
    [Index(nameof(EnterpriseId), nameof(Cean))]
    [Index(nameof(EnterpriseId), nameof(Sku))]
    public class Product : BaseEntity
    {
        public Product() { }

        public Product(string name, string description, decimal defaultValue, decimal storageQuantity, bool isExternal, bool isService, string? pictureAdress, Guid enterpriseId)
        {
            Name = name;
            Description = description;
            DefaultValue = defaultValue;
            StorageQuantity = storageQuantity;
            IsExternal = isExternal;
            IsService = isService;
            PictureAdress = pictureAdress;
            EnterpriseId = enterpriseId;
        }

        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal DefaultValue { get; set; } = 0;

        /// <summary>
        /// What the unit costs the company. Null means "nobody has told us yet" and is not the same
        /// as zero: a zero cost would report the whole sale price as margin, which is a lie the
        /// reports would then repeat. Every margin figure is null while this is null.
        /// </summary>
        public decimal? CostPrice { get; set; } = null;

        [Column(TypeName = "decimal(18,4)")]
        public decimal StorageQuantity { get; set; } = 0;
        public string UnitOfMeasure { get; set; } = nameof(EnumUnitOfMeasure.Unit);
        public bool IsExternal { get; set; } = false;
        public bool IsService { get; set; } = false;
        public string? PictureAdress { get; set; }

        /// <summary>
        /// The shop's own code for the item, as printed on a shelf label or an internal barcode.
        /// </summary>
        [MaxLength(40)]
        public string? Sku { get; set; }

        #region Fiscal (NFe/NFCe)

        [MaxLength(8)]
        public string? Ncm { get; set; }

        [MaxLength(7)]
        public string? Cest { get; set; }

        /// <summary>GTIN. "SEM GTIN" is emitted when null.</summary>
        [MaxLength(14)]
        public string? Cean { get; set; }

        [MaxLength(4)]
        public string? CfopIntra { get; set; }

        [MaxLength(4)]
        public string? CfopInter { get; set; }

        /// <summary>Origem da mercadoria, 0..8.</summary>
        public byte? Origem { get; set; }

        [MaxLength(6)]
        public string? UnidadeTributavel { get; set; }

        /// <summary>Used when the enterprise's CRT is 3 (Regime Normal).</summary>
        [MaxLength(3)]
        public string? Cst { get; set; }

        /// <summary>Used when the enterprise's CRT is 1, 2 or 4 (Simples Nacional / MEI).</summary>
        [MaxLength(3)]
        public string? Csosn { get; set; }

        [Column(TypeName = "decimal(5,4)")]
        public decimal? AliquotaIcms { get; set; }

        [MaxLength(2)]
        public string? CstPis { get; set; }

        [Column(TypeName = "decimal(5,4)")]
        public decimal? AliquotaPis { get; set; }

        [MaxLength(2)]
        public string? CstCofins { get; set; }

        [Column(TypeName = "decimal(5,4)")]
        public decimal? AliquotaCofins { get; set; }

        /// <summary>
        /// pRedBC — how much of the ICMS base this item's tax treatment removes, as a fraction
        /// (0.2667 is a 26,67% reduction), like every other rate here.
        /// </summary>
        [Column(TypeName = "decimal(5,4)")]
        public decimal? ReducaoBaseIcms { get; set; }

        /// <summary>cBenef, required by some UFs when the CST is exempt.</summary>
        [MaxLength(10)]
        public string? CodigoBeneficioFiscal { get; set; }

        #endregion

        [ForeignKey(nameof(Enterprise))]
        public Guid? EnterpriseId { get; set; } = null;

        [JsonIgnore]
        public virtual Enterprise? Enterprise { get; set; } = null;

        [JsonIgnore]
        public virtual IEnumerable<OrderedProduct>? OrderedProduct { get; set; } = new List<OrderedProduct>();

        [JsonIgnore]
        public virtual IEnumerable<PayableBillProduct>? PayableBillProducts { get; set; } = new List<PayableBillProduct>();
    }
}
