-- Volume fixture: bulk orders + boletos for one month, to exercise the monthly aggregates.
--
-- perf/seed.py creates orders through the API, one HTTP call each. That is right for 5,000 and
-- useless for 200,000, which is the volume that exposed the OutOfMemoryException in
-- SalesPerformance/GetPerformance and the 787 MiB growth in Dashboard/Today. This is the bulk path.
--
-- Run:
--   docker exec -i evangelionerpv2_sqlserver /opt/mssql-tools18/bin/sqlcmd \
--     -S localhost -U sa -P "$SQLSERVER_SA_PASSWORD" -C -I -d EvangelionERPV2 \
--     -i perf/seed-volume.sql
--
-- -I matters: Boleto has a filtered index, and inserting without QUOTED_IDENTIFIER ON fails.
--
-- Idempotent: re-running tops up to @OrderCount rather than duplicating. Set @Cleanup = 1 to remove
-- the fixture instead.

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @OrderCount     int              = 200000;
DECLARE @PeriodStart    datetime2        = '2026-07-01';
DECLARE @PeriodEnd      datetime2        = '2026-08-01';
DECLARE @CustomerId     uniqueidentifier = '48CECC99-3028-4269-BF3E-00DD4AD99271';
DECLARE @EnterpriseId   uniqueidentifier = '7350F50F-98D4-4854-B39E-6643F69F2A72';
DECLARE @WithBoletos    bit              = 1;
DECLARE @Cleanup        bit              = 0;

IF @Cleanup = 1
BEGIN
    DELETE b FROM Boleto b
    JOIN [Order] o ON o.Id = b.OrderId
    WHERE o.CustomerId = @CustomerId AND o.CreatedAt >= @PeriodStart AND o.CreatedAt < @PeriodEnd;

    DELETE FROM [Order]
    WHERE CustomerId = @CustomerId AND CreatedAt >= @PeriodStart AND CreatedAt < @PeriodEnd;

    PRINT 'Fixture removed.';
    RETURN;
END

-- Sellers are taken from the tenant rather than invented: SalesPerformance groups by UserId and
-- joins back for names, so a UserId with no matching user silently drops out of the result.
DECLARE @Users TABLE (RowNum int IDENTITY(1,1), UserId uniqueidentifier);
INSERT INTO @Users (UserId)
SELECT TOP 6 Id FROM [User] WHERE EnterpriseId = @EnterpriseId ORDER BY CreatedAt;

DECLARE @UserCount int = (SELECT COUNT(*) FROM @Users);
IF @UserCount = 0
BEGIN
    RAISERROR('No users found for the enterprise. Run perf/seed.py first.', 16, 1);
    RETURN;
END

DECLARE @Existing int = (
    SELECT COUNT(*) FROM [Order]
    WHERE CustomerId = @CustomerId AND CreatedAt >= @PeriodStart AND CreatedAt < @PeriodEnd);

DECLARE @Missing int = @OrderCount - @Existing;
PRINT CONCAT('Existing: ', @Existing, '  Inserting: ', CASE WHEN @Missing > 0 THEN @Missing ELSE 0 END);

IF @Missing <= 0
BEGIN
    PRINT 'Nothing to do.';
    RETURN;
END

DECLARE @DayCount int = DATEDIFF(DAY, @PeriodStart, @PeriodEnd);

DECLARE @New TABLE (
    Id          uniqueidentifier,
    UserId      uniqueidentifier,
    TotalValue  decimal(18,2),
    CreatedAt   datetime2,
    Payday      datetime2,
    Status      int);

-- Numbers from a cross join of sys.all_objects: no loop, no recursive CTE depth limit.
WITH Numbers AS (
    SELECT TOP (@Missing) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS N
    FROM sys.all_objects a CROSS JOIN sys.all_objects b
)
INSERT INTO @New (Id, UserId, TotalValue, CreatedAt, Payday, Status)
SELECT
    NEWID(),
    (SELECT UserId FROM @Users WHERE RowNum = (N % @UserCount) + 1),
    CAST(50 + (N % 4000) AS decimal(18,2)),
    DATEADD(SECOND, (N * 37) % 86400, DATEADD(DAY, N % @DayCount, @PeriodStart)),
    -- One in three is settled, which is what makes "sold" and "received" differ. If every order
    -- were paid the aggregate would return the same number twice and prove nothing.
    CASE WHEN N % 3 = 0 THEN DATEADD(DAY, (N % @DayCount) + 2, @PeriodStart) END,
    CASE WHEN N % 3 = 0 THEN 1 ELSE 0 END
FROM Numbers;

INSERT INTO [Order]
    (Id, Payday, PaymentScheduledDate, TotalValue, EnterpriseId, CustomerId, UserId,
     Status, CreatedAt, UpdatedAt, IsActive, RefundReason, RefundedAt, PaymentMethod)
SELECT
    Id, Payday, DATEADD(DAY, 30, CreatedAt), TotalValue, @EnterpriseId, @CustomerId, UserId,
    Status, CreatedAt, NULL, 1, NULL, NULL, NULL
FROM @New;

IF @WithBoletos = 1
BEGIN
    INSERT INTO Boleto
        (Id, OrderId, BankCode, OurNumber, DocumentNumber, IssueDate, DueDate, Amount,
         DigitableLine, BarCode, HtmlContent, CreatedAt, UpdatedAt, IsActive, Status,
         PaidAt, PaidAmount, SettlementProtocol)
    SELECT
        NEWID(), n.Id, 33,
        RIGHT('00000000' + CAST(ROW_NUMBER() OVER (ORDER BY n.Id) AS varchar(8)), 8),
        CAST(ROW_NUMBER() OVER (ORDER BY n.Id) AS varchar(10)),
        n.CreatedAt, DATEADD(DAY, 30, n.CreatedAt), n.TotalValue,
        REPLICATE('0', 47), REPLICATE('0', 44), '', n.CreatedAt, NULL, 1,
        1,  -- open; the daily panel counts these as receivables
        NULL, NULL, NULL
    FROM @New n;
END

SELECT
    (SELECT COUNT(*) FROM [Order]
     WHERE CustomerId = @CustomerId AND CreatedAt >= @PeriodStart AND CreatedAt < @PeriodEnd) AS Orders,
    (SELECT COUNT(*) FROM Boleto b JOIN [Order] o ON o.Id = b.OrderId
     WHERE o.CustomerId = @CustomerId AND o.CreatedAt >= @PeriodStart AND o.CreatedAt < @PeriodEnd) AS Boletos,
    (SELECT SUM(TotalValue) FROM [Order]
     WHERE CustomerId = @CustomerId AND CreatedAt >= @PeriodStart AND CreatedAt < @PeriodEnd) AS SoldTotal;
