namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// The carteira split by how late it is — the report the contador and the bank both ask for.
    /// </summary>
    public class ReceivablesAgingDTO
    {
        public DateTime GeneratedAtUtc { get; set; }

        public ReceivablesAgingBucketDTO NotYetDue { get; set; } = new();
        public ReceivablesAgingBucketDTO Days1To30 { get; set; } = new();
        public ReceivablesAgingBucketDTO Days31To60 { get; set; } = new();
        public ReceivablesAgingBucketDTO Days61To90 { get; set; } = new();
        public ReceivablesAgingBucketDTO Days90Plus { get; set; } = new();

        public int TotalCount { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal TotalOverdueAmount { get; set; }

        public List<ReceivablesAgingCustomerDTO> Customers { get; set; } = [];
    }

    public class ReceivablesAgingBucketDTO
    {
        public int Count { get; set; }
        public decimal Amount { get; set; }
    }

    public class ReceivablesAgingCustomerDTO
    {
        public Guid? CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;

        public decimal NotYetDue { get; set; }
        public decimal Days1To30 { get; set; }
        public decimal Days31To60 { get; set; }
        public decimal Days61To90 { get; set; }
        public decimal Days90Plus { get; set; }
        public decimal Total { get; set; }
    }

    /// <summary>
    /// Who to chase, worst first. Contact details are here because chasing without them means
    /// opening a second screen for every row.
    /// </summary>
    public class DelinquentCustomerDTO
    {
        public Guid? CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;

        public int OverdueCount { get; set; }
        public decimal OverdueAmount { get; set; }
        public DateTime OldestDueDate { get; set; }
        public int DaysOverdue { get; set; }
    }

    /// <summary>Extrato: what one customer bought, paid and still owes over a window.</summary>
    public class CustomerStatementDTO
    {
        public Guid CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }

        /// <summary>Balance carried in from before the window, so the running total means something.</summary>
        public decimal OpeningBalance { get; set; }
        public decimal ClosingBalance { get; set; }

        public List<CustomerStatementEntryDTO> Entries { get; set; } = [];
    }

    public class CustomerStatementEntryDTO
    {
        public DateTime Date { get; set; }

        /// <summary>order, payment or refund.</summary>
        public string Kind { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Guid? OrderId { get; set; }

        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public decimal RunningBalance { get; set; }
    }
}
