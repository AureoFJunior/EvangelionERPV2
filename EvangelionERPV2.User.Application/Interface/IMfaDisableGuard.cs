namespace EvangelionERPV2.UserModule.Application.Interface
{
    /// <summary>Coordinates MFA removal with tenant IAM's last-manager invariant.</summary>
    public interface IMfaDisableGuard
    {
        Task LockEnterpriseForMfaChangeAsync(Guid enterpriseId);
        Task EnsureMfaCanBeDisabledAsync(Guid enterpriseId, Guid userId);
    }
}
