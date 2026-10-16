using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// One photo in a product row's gallery (up to eight). Position 0 is the cover and is mirrored onto
    /// <see cref="Product.PictureAdress"/>, <see cref="Product.PictureWidth"/> and <see cref="Product.PictureHeight"/>.
    /// </summary>
    [Index(nameof(ProductId), nameof(Position))]
    [Table("ProductImage")]
    public class ProductImage : BaseEntity
    {
        public Guid ProductId { get; set; }
        public Product? Product { get; set; }
        public Guid EnterpriseId { get; set; }
        public int Position { get; set; }

        /// <summary>Encrypted S3 key, like <see cref="Product.PictureAdress"/>.</summary>
        public string Adress { get; set; } = string.Empty;

        public int? Width { get; set; }
        public int? Height { get; set; }
    }
}
