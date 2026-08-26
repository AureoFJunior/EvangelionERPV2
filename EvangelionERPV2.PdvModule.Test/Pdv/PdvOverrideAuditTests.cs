using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.PdvModule.Test.Pdv;

/// <summary>
/// The record of a supervisor authorising something an operator could not do alone.
/// </summary>
/// <remarks>
/// Refused attempts are the ones worth having: a run of them against one supervisor is somebody
/// guessing a PIN, and the till counts what it has been told about to lock that supervisor out
/// across every terminal rather than just the one being guessed at.
/// </remarks>
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
        // A guard, not a formality: an audit row that captured the PIN would put every supervisor's
        // secret in a table that exists to be read.
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

        // The client id is what makes a resent row idempotent; the sale id is what ties the
        // override to the basket it happened in.
        Assert.Equal(clientOverrideId, audit.ClientOverrideId);
        Assert.Equal(clientSaleId, audit.ClientSaleId);
        Assert.NotEqual(Guid.Empty, audit.Id);
    }
}
