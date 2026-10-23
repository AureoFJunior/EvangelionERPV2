namespace EvangelionERPV2.Shared.DTOs;

public sealed record AuditRetentionCleanupResultDTO(
    int RetentionDays,
    DateTime CutoffDateUtc,
    int DeletedCount);
