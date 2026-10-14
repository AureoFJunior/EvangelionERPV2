using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// What a variant family owns beyond its rows: today, its own photo — the one that shows the
    /// whole family (every colour together, the product in use), shown on its card and gallery. Each
    /// variant keeps its own photo on <see cref="Product.PictureAdress"/>. <c>Id</c> is the
    /// <see cref="Product.VariantGroupId"/> the rows share; a family without a photo has no row here.
    /// </summary>
    [Table("ProductVariantGroup")]
    public class ProductVariantGroup : BaseEntity
    {
        public Guid EnterpriseId { get; set; }

        /// <summary>Encrypted S3 key, like <see cref="Product.PictureAdress"/>.</summary>
        public string? PictureAdress { get; set; }
    }
}
