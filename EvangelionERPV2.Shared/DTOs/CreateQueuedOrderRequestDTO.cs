namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// Machine-to-machine payload used by the order worker to replay a queued order.
    /// Carries the tenant and user captured when the order was enqueued, because the
    /// worker authenticates as the self-API service account and its token does not
    /// reflect the original caller's tenant.
    /// </summary>
    public sealed class CreateQueuedOrderRequestDTO
    {
        public Guid EnterpriseId { get; set; }
        public Guid UserId { get; set; }
        public Guid CustomerId { get; set; }
        public DateTime PaymentScheduledDate { get; set; }
        public int Status { get; set; }
        public IEnumerable<OrderLineItemRequestDTO> Items { get; set; } = [];

        /// <summary>
        /// The broker's message id, carried so the API can refuse to create the same order twice.
        /// Empty when the order did not come from the queue.
        /// </summary>
        public string MessageId { get; set; } = string.Empty;
    }
}
