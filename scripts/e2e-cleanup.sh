#!/usr/bin/env bash
# Removes the rows Cypress leaves behind.
#
# The API offers only soft deletes, so `cypress/support/e2e.js` teardown flips isActive = false and
# the rows stay forever. After a handful of runs the database carries dozens of dead customers,
# products, employees and orders — invisible in the UI, in the way of anyone reading the tables.
#
# Everything the suite seeds is named with an `E2E ` prefix; that prefix is the whole selector here.
# Dependants go first, in foreign-key order:
#
#   Boleto / DunningLog / NFeDocument / OrderedProduct  ->  Order
#   OrderedProduct / StockMovement / PayableBillProduct ->  Product
#   EmployeeAbsence / EmployeeVacationAccrual / PayrollBillDetail -> Employee
#   Order -> Customer
#
# Dry run by default: it prints what it would delete and changes nothing. Pass --apply to commit.
#
# Usage: ./scripts/e2e-cleanup.sh [--apply] [container] [database]

set -euo pipefail

APPLY=0
if [[ "${1:-}" == "--apply" ]]; then
  APPLY=1
  shift
fi

CONTAINER="${1:-evangelionerpv2_sqlserver}"
DATABASE="${2:-EvangelionERPV2}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [[ ! -f "$ROOT/.env.local" ]]; then
  echo "Missing .env.local — copy .env.local.example first." >&2
  exit 1
fi

SA_PASSWORD="$(grep -E '^SQLSERVER_SA_PASSWORD=' "$ROOT/.env.local" | cut -d= -f2-)"
if [[ -z "$SA_PASSWORD" ]]; then
  echo "SQLSERVER_SA_PASSWORD is not set in .env.local." >&2
  exit 1
fi

run_sql() {
  docker exec "$CONTAINER" /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -P "$SA_PASSWORD" -C -I -b -d "$DATABASE" -h -1 -W -Q "$1"
}

echo "==> E2E rows currently in $DATABASE"
run_sql "SET NOCOUNT ON;
SELECT 'Customer        : ' + CAST(COUNT(*) AS varchar) FROM Customer WHERE Name LIKE 'E2E %';
SELECT 'Product         : ' + CAST(COUNT(*) AS varchar) FROM Product  WHERE Name LIKE 'E2E %';
SELECT 'Employee        : ' + CAST(COUNT(*) AS varchar) FROM Employee WHERE Name LIKE 'E2E %';
SELECT 'Order           : ' + CAST(COUNT(*) AS varchar) FROM [Order] o JOIN Customer c ON c.Id = o.CustomerId WHERE c.Name LIKE 'E2E %';
SELECT 'Boleto          : ' + CAST(COUNT(*) AS varchar) FROM Boleto b JOIN [Order] o ON o.Id = b.OrderId JOIN Customer c ON c.Id = o.CustomerId WHERE c.Name LIKE 'E2E %';
SELECT 'OrderedProduct  : ' + CAST(COUNT(*) AS varchar) FROM OrderedProduct op WHERE op.OrderId IN (SELECT o.Id FROM [Order] o JOIN Customer c ON c.Id = o.CustomerId WHERE c.Name LIKE 'E2E %') OR op.ProductId IN (SELECT Id FROM Product WHERE Name LIKE 'E2E %');
SELECT 'StockMovement   : ' + CAST(COUNT(*) AS varchar) FROM StockMovement WHERE ProductId IN (SELECT Id FROM Product WHERE Name LIKE 'E2E %');
SELECT 'PayableBillProduct (real bills touching an E2E product): ' + CAST(COUNT(*) AS varchar) FROM PayableBillProduct WHERE ProductId IN (SELECT Id FROM Product WHERE Name LIKE 'E2E %');"

if [[ "$APPLY" -ne 1 ]]; then
  echo
  echo "Dry run — nothing was deleted. Re-run with --apply to remove them."
  exit 0
fi

echo "==> Backing up $DATABASE first"
docker exec "$CONTAINER" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA_PASSWORD" -C \
  -Q "BACKUP DATABASE [$DATABASE] TO DISK='/var/opt/mssql/data/${DATABASE}_before_e2e_cleanup.bak' WITH INIT, COMPRESSION;"

echo "==> Deleting"
# One transaction: a half-cleaned graph is worse than an uncleaned one, and XACT_ABORT makes any
# error roll the whole thing back instead of leaving orphans behind a failed FK.
run_sql "SET NOCOUNT ON; SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @customers TABLE (Id uniqueidentifier PRIMARY KEY);
DECLARE @products  TABLE (Id uniqueidentifier PRIMARY KEY);
DECLARE @employees TABLE (Id uniqueidentifier PRIMARY KEY);
DECLARE @orders    TABLE (Id uniqueidentifier PRIMARY KEY);

INSERT INTO @customers SELECT Id FROM Customer WHERE Name LIKE 'E2E %';
INSERT INTO @products  SELECT Id FROM Product  WHERE Name LIKE 'E2E %';
INSERT INTO @employees SELECT Id FROM Employee WHERE Name LIKE 'E2E %';
INSERT INTO @orders    SELECT Id FROM [Order] WHERE CustomerId IN (SELECT Id FROM @customers);

DELETE FROM Boleto         WHERE OrderId   IN (SELECT Id FROM @orders);
DELETE FROM DunningLog     WHERE OrderId   IN (SELECT Id FROM @orders);
DELETE FROM NFeDocument    WHERE OrderId   IN (SELECT Id FROM @orders);
DELETE FROM OrderedProduct WHERE OrderId   IN (SELECT Id FROM @orders)
                              OR ProductId IN (SELECT Id FROM @products);

DELETE FROM StockMovement       WHERE ProductId IN (SELECT Id FROM @products);
DELETE FROM PayableBillProduct  WHERE ProductId IN (SELECT Id FROM @products);

DELETE FROM EmployeeAbsence         WHERE EmployeeId IN (SELECT Id FROM @employees);
DELETE FROM EmployeeVacationAccrual WHERE EmployeeId IN (SELECT Id FROM @employees);
DELETE FROM PayrollBillDetail       WHERE EmployeeId IN (SELECT Id FROM @employees);

DELETE FROM [Order] WHERE Id IN (SELECT Id FROM @orders);
DELETE FROM Customer WHERE Id IN (SELECT Id FROM @customers);
DELETE FROM Product  WHERE Id IN (SELECT Id FROM @products);
DELETE FROM Employee WHERE Id IN (SELECT Id FROM @employees);

COMMIT TRANSACTION;
SELECT 'deleted';"

echo "==> Left behind"
run_sql "SET NOCOUNT ON;
SELECT 'Customer : ' + CAST(COUNT(*) AS varchar) FROM Customer WHERE Name LIKE 'E2E %';
SELECT 'Product  : ' + CAST(COUNT(*) AS varchar) FROM Product  WHERE Name LIKE 'E2E %';
SELECT 'Employee : ' + CAST(COUNT(*) AS varchar) FROM Employee WHERE Name LIKE 'E2E %';
SELECT 'Order    : ' + CAST(COUNT(*) AS varchar) FROM [Order] o JOIN Customer c ON c.Id = o.CustomerId WHERE c.Name LIKE 'E2E %';"
