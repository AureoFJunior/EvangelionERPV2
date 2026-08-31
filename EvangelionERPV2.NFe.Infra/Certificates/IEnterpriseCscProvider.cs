namespace EvangelionERPV2.NFeModule.Infra.Certificates
{
    /// <summary>Resolves the tenant's CSC. See <see cref="EnterpriseCscProvider"/> for why it is not a column.</summary>
    public interface IEnterpriseCscProvider
    {
        Task<string> GetAsync(Guid enterpriseId, CancellationToken cancellationToken = default);

        /// <summary>Drops the cached value after a rotation.</summary>
        void Invalidate(Guid enterpriseId);
    }
}
