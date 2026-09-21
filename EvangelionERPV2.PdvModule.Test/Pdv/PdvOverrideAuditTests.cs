using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.PdvModule.Test.Pdv;

/// <summary>
/// The record of a supervisor authorising something an operator could not do alone.
/// </summary>
public class PdvOverrideAuditTests
{
    [Fact]
    public void FailedOverrides_AreRecordedToo()
    {
        var audit = PdvOverrideAudit.Record(
            enterpriseId: Guid.NewGuid(),
            clientOverrideId: Guid.NewGuid(),
            supervisorUserId: Guid.NewGuid(),
            operatorUserId: Guid.NewGuid(),
            terminalId: "CAIXA-01",
            clientSaleId: Guid.NewGuid(),
            permission: "pdv.item.void",
            succeeded: false,
            occurredAt: DateTime.UtcNow);

        Assert.False(audit.Succeeded);
    }

    [Fact]
    public void Record_NeverCarriesThePin()
    {
        Assert.DoesNotContain(
            typeof(PdvOverrideAudit).GetProperties(),
            property => property.Name.Contains("Pin", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("Verifier", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Record_KeepsTheTerminalsOwnIdentifiers()
    {
        var clientOverrideId = Guid.NewGuid();
        var clientSaleId = Guid.NewGuid();

        var audit = PdvOverrideAudit.Record(
            enterpriseId: Guid.NewGuid(),
            clientOverrideId: clientOverrideId,
            supervisorUserId: Guid.NewGuid(),
            operatorUserId: Guid.NewGuid(),
            terminalId: "CAIXA-01",
            clientSaleId: clientSaleId,
            permission: "pdv.item.void",
            succeeded: true,
            occurredAt: DateTime.UtcNow);

        Assert.Equal(clientOverrideId, audit.ClientOverrideId);
        Assert.Equal(clientSaleId, audit.ClientSaleId);
        Assert.NotEqual(Guid.Empty, audit.Id);
    }
}
