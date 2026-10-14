using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// One node of Limen's category tree: a department (no parent) or a category under one. One tree
    /// for the whole platform, edited by the Limen team, so it has no EnterpriseId and no tenant filter.
    /// Only a node without children holds products. SPEC-limen-categories.md.
    /// </summary>
    [Index(nameof(Slug), IsUnique = true)]
    [Index(nameof(ParentId), nameof(SortOrder))]
    [Table("StorefrontCategory")]
    public class StorefrontCategory : BaseEntity
    {
        [ForeignKey(nameof(Parent))]
        public Guid? ParentId { get; set; }

        public StorefrontCategory? Parent { get; set; }

        [MaxLength(60)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(60)]
        public string Slug { get; set; } = string.Empty;

        /// <summary>Phosphor icon name, from the allowlist in <c>StorefrontCategoryIcons</c>.</summary>
        [MaxLength(40)]
        public string Icon { get; set; } = "Tag";

        /// <summary>Meta description of the category page.</summary>
        [MaxLength(160)]
        public string? Description { get; set; }

        public int SortOrder { get; set; }
    }

    /// <summary>A slug a category had before a rename, so old links redirect instead of breaking.</summary>
    [Index(nameof(OldSlug), IsUnique = true)]
    [Table("StorefrontCategorySlugAlias")]
    public class StorefrontCategorySlugAlias : BaseEntity
    {
        [MaxLength(60)]
        public string OldSlug { get; set; } = string.Empty;

        [ForeignKey(nameof(Category))]
        public Guid CategoryId { get; set; }

        public StorefrontCategory? Category { get; set; }
    }
}
