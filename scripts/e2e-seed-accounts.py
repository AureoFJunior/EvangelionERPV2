#!/usr/bin/env python3
"""Provision two disposable browser-test tenants in the dedicated local E2E database.

Start the isolated database first with `./scripts/e2e-stack-up.sh`. Dry-run by default; apply with
`python3 scripts/e2e-seed-accounts.py --apply`.
Credentials are written to the ignored .env.e2e file with mode 0600; source it before Cypress.
"""
import argparse
import base64
import hashlib
import os
import secrets
import subprocess
import uuid
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
CONTAINER = "evangelionerpv2_e2e_sql"
VOLUME = "evangelionerpv2_e2e_sql_data"
DATABASE = "ERP_E2E"
ENV_FILE = ROOT / ".env.e2e"
PBKDF2_ITERATIONS = 100_000
FIXTURES = (
    ("Tenant A", "e2e.cypress.admin", "e2e.cypress.admin@example.com"),
    ("Tenant B", "e2e.cypress.tenantb", "e2e.cypress.tenantb@example.com"),
)


def password_hash(password):
    salt = os.urandom(16)
    key = hashlib.pbkdf2_hmac("sha256", password.encode(), salt, PBKDF2_ITERATIONS, 32)
    return f"PBKDF2${PBKDF2_ITERATIONS}${base64.b64encode(salt).decode()}${base64.b64encode(key).decode()}"


def sql_quote(value):
    return "'" + value.replace("'", "''") + "'"


def verify_target():
    result = subprocess.run(
        ["docker", "inspect", "--format", "{{.Name}}|{{range .Mounts}}{{.Name}}{{end}}", CONTAINER],
        check=True, capture_output=True, text=True,
    ).stdout.strip()
    expected = f"/{CONTAINER}|{VOLUME}"
    if result != expected:
        raise SystemExit(f"Refusing unexpected SQL target ({result!r}); expected the isolated E2E container/volume.")


def run_sql(query):
    command = [
        "docker", "exec", "-i", CONTAINER, "sh", "-lc",
        'exec /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" '
        f"-d {DATABASE} -b -I -h -1 -W -i /dev/stdin",
    ]
    # Keep SQL values and the SQL password out of host/container process arguments.
    result = subprocess.run(command, input=query, capture_output=True, text=True)
    if result.returncode:
        raise SystemExit(result.stderr.strip() or result.stdout.strip() or "E2E SQL provisioning failed.")
    return result.stdout.strip()


def build_sql(credentials):
    statements = [
        "SET NOCOUNT ON;",
        "IF DB_NAME() <> 'ERP_E2E' THROW 51000, 'Wrong database; refusing E2E seed.', 1;",
        "BEGIN TRANSACTION;",
    ]
    for index, (tenant_name, username, email) in enumerate(FIXTURES):
        label = f"E2E Cypress {tenant_name}"
        statements.append(
            f"DECLARE @enterprise_{index} uniqueidentifier = "
            f"(SELECT TOP 1 Id FROM dbo.Enterprise WHERE Name = {sql_quote(label)});"
        )
        statements.append(
            f"IF @enterprise_{index} IS NULL BEGIN "
            f"SET @enterprise_{index} = NEWID(); "
            f"INSERT INTO dbo.Enterprise (Id, Name, PhoneNumber, Email, Adress, ShouldSendMonthlyBilling, "
            f"CreatedAt, IsActive, IamMode) VALUES (@enterprise_{index}, {sql_quote(label)}, '', "
            f"{sql_quote('e2e.' + username + '@example.com')}, '', 0, SYSUTCDATETIME(), 1, 0); END;"
        )
        statements.append(
            f"UPDATE dbo.Enterprise SET Name = {sql_quote(label)}, PhoneNumber = '11999990000', "
            f"Email = {sql_quote('e2e.' + username + '@example.com')}, Adress = 'E2E Test Street', "
            f"ShouldSendMonthlyBilling = 0, IsActive = 1, IamMode = 0, UpdatedAt = SYSUTCDATETIME() "
            f"WHERE Id = @enterprise_{index};"
        )
        user_password = sql_quote(password_hash(credentials[username]))
        statements.append(
            f"IF EXISTS (SELECT 1 FROM dbo.[User] WHERE UserName = {sql_quote(username)}) "
            f"UPDATE dbo.[User] SET EnterpriseId = @enterprise_{index}, Password = {user_password}, "
            f"AccessLevel = 0, IsActive = 1, IsLogged = 0, IsServiceAccount = 0, MustChangePassword = 0, "
            f"MfaEnabled = 0, UpdatedAt = SYSUTCDATETIME() WHERE UserName = {sql_quote(username)}; "
            f"ELSE INSERT INTO dbo.[User] (Id, FirstName, LastName, UserName, Password, Email, BirthDate, "
            f"IsLogged, ActualTheme, ProfilePicture, EnterpriseId, AccessLevel, Language, CreatedAt, "
            f"IsActive, IsServiceAccount, PhoneNumber, MfaEnabled, MustChangePassword, IamVersion) VALUES "
            f"(NEWID(), 'E2E', {sql_quote(tenant_name)}, {sql_quote(username)}, {user_password}, "
            f"{sql_quote(email)}, '2000-01-01', 0, 0, '', @enterprise_{index}, 0, 0, SYSUTCDATETIME(), "
            f"1, 0, '', 0, 0, 0);"
        )
    statements.append("COMMIT TRANSACTION;")
    return "\n".join(statements)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply", action="store_true", help="write the two test tenants and users")
    args = parser.parse_args()

    verify_target()
    run_sql("IF DB_NAME() <> 'ERP_E2E' THROW 51000, 'Wrong database; refusing E2E seed.', 1; SELECT 'database verified';")
    if not args.apply:
        print(f"Target verified: {CONTAINER}/{DATABASE} on {VOLUME}; no database changes made.")
        print("Run with --apply to create/update the two reserved Cypress fixture accounts.")
        return

    credentials = {username: secrets.token_urlsafe(24) for _, username, _ in FIXTURES}
    run_sql(build_sql(credentials))

    lines = [
        "CYPRESS_E2E_ISOLATED=true",
        "CYPRESS_BASE_URL=http://localhost:8081",
        "CYPRESS_API_BASE_URL=http://localhost:8083/api/v1",
        "EXPO_PUBLIC_API_BASE_URL=http://localhost:8083/api/v1",
        f"CYPRESS_ADMIN_USERNAME={FIXTURES[0][1]}",
        f"CYPRESS_ADMIN_PASSWORD={credentials[FIXTURES[0][1]]}",
        f"CYPRESS_TENANT_B_USERNAME={FIXTURES[1][1]}",
        f"CYPRESS_TENANT_B_PASSWORD={credentials[FIXTURES[1][1]]}",
    ]
    ENV_FILE.write_text("\n".join(lines) + "\n", encoding="utf-8")
    ENV_FILE.chmod(0o600)
    print(f"Created the two E2E fixture tenants; credentials saved to {ENV_FILE} (mode 0600).")
    print(f"Source it before starting the frontend and Cypress: set -a; . '{ENV_FILE}'; set +a")


if __name__ == "__main__":
    main()
