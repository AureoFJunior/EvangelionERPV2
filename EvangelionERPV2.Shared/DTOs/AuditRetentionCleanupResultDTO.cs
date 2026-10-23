namespace EvangelionERPV2.Shared.DTOs;

public sealed record AuditRetentionCleanupResultDto(
    int RetentionDays,
    DateTime CutoffDateUtc,
    int DeletedCount);
