namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// The only field the enterprise filter reads. The tenant is taken from the token, and the sort
    /// column is always the key once it is set, so nothing else in the body could reach the query.
    /// </summary>
    public sealed class EnterpriseFilterRequestDTO
    {
        public string? Name { get; set; }
    }
}
