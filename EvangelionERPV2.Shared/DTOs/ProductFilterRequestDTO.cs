namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class ProductFilterRequestDTO
    {
        public string? Name { get; set; }
        public bool? IsActive { get; set; }

        /// <summary>Only these variant groups — how a screen completes a family that paging cut in two.</summary>
        public List<Guid>? VariantGroupIds { get; set; }

        /// <summary>Only products filed directly under this Limen category.</summary>
        public Guid? StorefrontCategoryId { get; set; }

        /// <summary>Only products without a Limen category; with <see cref="PublishedOnly"/>, the ones Limen shows as "Sem categoria".</summary>
        public bool Uncategorized { get; set; }

        public bool PublishedOnly { get; set; }
    }
}
