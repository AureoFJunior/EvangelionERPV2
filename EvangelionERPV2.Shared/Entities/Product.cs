using EvangelionERPV2.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.Entities
{
    [Index(nameof(EnterpriseId), nameof(Cean))]
    [Index(nameof(EnterpriseId), nameof(Sku))]
    [Index(nameof(EnterpriseId), nameof(VariantGroupId))]
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

        public decimal? CostPrice { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal StorageQuantity { get; set; } = 0;
        public string UnitOfMeasure { get; set; } = nameof(EnumUnitOfMeasure.Unit);
        public bool IsExternal { get; set; } = false;
        public bool IsService { get; set; } = false;
        public string? PictureAdress { get; set; }

        [MaxLength(40)]
        public string? Sku { get; set; }

        public Guid? VariantGroupId { get; set; }

        [System.ComponentModel.DataAnnotations.MaxLength(60)]
        public string? VariantLabel { get; set; }

        public bool IsPublishedOnStorefront { get; set; }

        [ForeignKey(nameof(StorefrontCategory))]
        public Guid? StorefrontCategoryId { get; set; }

        [JsonIgnore]
        public StorefrontCategory? StorefrontCategory { get; set; }

        public decimal? CompareAtPrice { get; set; }
        public DateTime? PromotionStartedAt { get; set; }
        public DateTime? PromotionEndsAt { get; set; }
        public Guid? PromotionAttestedByUserId { get; set; }
        public DateTime? PromotionAttestedAt { get; set; }

        public bool PromotionValidAt(DateTime now) =>
            CompareAtPrice is { } compareAt && compareAt > DefaultValue && PromotionEndsAt > now;

        public static int DiscountPercent(decimal compareAt, decimal price) =>
            compareAt <= 0 ? 0 : (int)Math.Round((compareAt - price) / compareAt * 100, MidpointRounding.AwayFromZero);

        public void EndPromotion()
        {
            CompareAtPrice = null;
            PromotionStartedAt = null;
            PromotionEndsAt = null;
            PromotionAttestedByUserId = null;
            PromotionAttestedAt = null;
        }

        [MaxLength(8)] public string? Ncm { get; set; }
        [MaxLength(7)] public string? Cest { get; set; }
        [MaxLength(14)] public string? Cean { get; set; }
        [MaxLength(4)] public string? CfopIntra { get; set; }
        [MaxLength(4)] public string? CfopInter { get; set; }
        public byte? Origem { get; set; }
        [MaxLength(6)] public string? UnidadeTributavel { get; set; }
        [MaxLength(3)] public string? Cst { get; set; }
        [MaxLength(3)] public string? Csosn { get; set; }
        [Column(TypeName = "decimal(5,4)")] public decimal? AliquotaIcms { get; set; }
        [MaxLength(2)] public string? CstPis { get; set; }
        [Column(TypeName = "decimal(5,4)")] public decimal? AliquotaPis { get; set; }
        [MaxLength(2)] public string? CstCofins { get; set; }
        [Column(TypeName = "decimal(5,4)")] public decimal? AliquotaCofins { get; set; }
        [Column(TypeName = "decimal(5,4)")] public decimal? ReducaoBaseIcms { get; set; }
        [MaxLength(10)] public string? CodigoBeneficioFiscal { get; set; }

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
