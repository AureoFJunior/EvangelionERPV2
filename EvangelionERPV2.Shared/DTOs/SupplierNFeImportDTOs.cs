using Microsoft.AspNetCore.Http;

namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>The supplier's NF-e XML, as uploaded.</summary>
    public class ImportSupplierNFeRequestDTO
    {
        public IFormFile? File { get; set; }
    }

    /// <summary>
    /// A line of the nota that matched no product in the inventory.
    /// </summary>
    /// <remarks>
    /// Reported, never auto-created. A product invented from a supplier's description arrives with
    /// no NCM, no CFOP and no cost, and then quietly takes part in issuing our own notes.
    /// </remarks>
    public sealed class SupplierNFeUnmatchedItemDTO
    {
        public string Code { get; set; } = string.Empty;
        public string? Ean { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal UnitValue { get; set; }
        public string UnitOfMeasure { get; set; } = string.Empty;
    }

    /// <summary>What the import did.</summary>
    public sealed class SupplierNFeImportResultDTO
    {
        /// <summary>The first payable created — the earliest instalment.</summary>
        public Guid PayableBillId { get; set; }

        /// <summary>Every payable this nota produced: one per duplicata.</summary>
        public List<Guid> PayableBillIds { get; set; } = [];
        public string AccessKey { get; set; } = string.Empty;
        public string Number { get; set; } = string.Empty;
        public DateTime IssuedAt { get; set; }
        /// <summary>The first instalment's due date.</summary>
        public DateTime DueDate { get; set; }

        /// <summary>The whole nota: the sum of every instalment created.</summary>
        public decimal Amount { get; set; }

        public Guid SupplierId { get; set; }
        public string SupplierName { get; set; } = string.Empty;

        /// <summary>True when this XML had already been imported; the bill returned is the existing one.</summary>
        public bool AlreadyImported { get; set; }

        /// <summary>True when the emitter was unknown and a supplier record was created for it.</summary>
        public bool SupplierCreated { get; set; }

        public int MatchedItemCount { get; set; }

        /// <summary>
        /// Matched products that were uncosted and took this nota's unit value as their cost.
        /// Never a repricing: a product that already had a cost is left alone.
        /// </summary>
        public int CostedProductCount { get; set; }
        public List<SupplierNFeUnmatchedItemDTO> UnmatchedItems { get; set; } = [];
    }
}
