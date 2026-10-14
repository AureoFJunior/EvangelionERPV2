namespace EvangelionERPV2.BillsModule.Application.Interface
{
    public interface IAccountingExportService
    {
        /// <summary>One month of movement as a zip of CSVs, ready to hand to the accountant.</summary>
        Task<byte[]> BuildMonthlyAsync(Guid enterpriseId, int year, int month, CancellationToken cancellation = default);
    }
}
