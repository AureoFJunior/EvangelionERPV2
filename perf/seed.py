#!/usr/bin/env python3
"""Creates what perf/scenarios need: one account per access level, and enough customers, products
and orders for the numbers to mean anything.

Everything that the API can do is done through the API. The single exception is the bootstrap
account: creating a user needs a token, getting a token needs a user, and this repo has no
anonymous sign-up — so `perf.admin` is inserted straight into the local database with a PBKDF2
hash in the format SharedFunctions.HashPassword writes. Every other row here is a real HTTP call
against the running API, validation and all.

Idempotent: re-running tops the counts back up and reuses accounts that already work.

    ./perf/seed.py                       # spec §5 targets
    ./perf/seed.py --customers 50 --products 100 --orders 200
    ./perf/seed.py --users-only
"""
import argparse
import base64
import concurrent.futures
import hashlib
import json
import os
import random
import secrets
import string
import subprocess
import sys
import time
import urllib.error
import urllib.request
import uuid
from datetime import datetime, timedelta, timezone

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ENV_PERF = os.path.join(ROOT, 'perf', '.env.perf')
CONTAINER = os.environ.get('PERF_SQL_CONTAINER', 'evangelionerpv2_sqlserver')
DATABASE = os.environ.get('PERF_DATABASE', 'EvangelionERPV2')
PREFIX = 'E2E perf'

# Mirrors SharedFunctions: PBKDF2-SHA256, 16-byte salt, 32-byte key, 100k iterations.
PBKDF2_ITER = 100_000

ROLES = {'admin': 0, 'manager': 1, 'supervisor': 2, 'employee': 3}


# ---------------------------------------------------------------- database (bootstrap only)

def sa_password():
    with open(os.path.join(ROOT, '.env.local')) as fh:
        for line in fh:
            if line.startswith('SQLSERVER_SA_PASSWORD='):
                return line.split('=', 1)[1].strip()
    sys.exit('SQLSERVER_SA_PASSWORD missing from .env.local')


def sql(query, password):
    out = subprocess.run(
        ['docker', 'exec', CONTAINER, '/opt/mssql-tools18/bin/sqlcmd',
         '-S', 'localhost', '-U', 'sa', '-P', password, '-C', '-I', '-b',
         '-d', DATABASE, '-h', '-1', '-W', '-Q', 'SET NOCOUNT ON; ' + query],
        capture_output=True, text=True)
    if out.returncode != 0:
        sys.exit(f'sqlcmd failed: {out.stderr.strip() or out.stdout.strip()}')
    return [l.strip() for l in out.stdout.splitlines() if l.strip()]


def hash_password(plain):
    salt = os.urandom(16)
    key = hashlib.pbkdf2_hmac('sha256', plain.encode(), salt, PBKDF2_ITER, 32)
    return f'PBKDF2${PBKDF2_ITER}${base64.b64encode(salt).decode()}${base64.b64encode(key).decode()}'


def new_password():
    alphabet = string.ascii_letters + string.digits
    return 'Perf' + ''.join(secrets.choice(alphabet) for _ in range(16))


def bootstrap_admin(sa):
    """Ensures perf.admin exists and its password is one we know. The only direct write here."""
    enterprise = sql("SELECT TOP 1 CONVERT(nvarchar(36), Id) FROM Enterprise ORDER BY CreatedAt;", sa)
    if not enterprise:
        sys.exit('no Enterprise row — this database is empty, nothing to seed against')
    enterprise_id = enterprise[0]

    password = new_password()
    stored = hash_password(password).replace("'", "''")
    existing = sql("SELECT CONVERT(nvarchar(36), Id) FROM [User] WHERE UserName = 'perf.admin';", sa)

    if existing:
        sql(f"UPDATE [User] SET Password = '{stored}', IsActive = 1, AccessLevel = 0, "
            f"EnterpriseId = '{enterprise_id}', UpdatedAt = SYSUTCDATETIME() WHERE UserName = 'perf.admin';", sa)
        print('bootstrap: reset perf.admin password')
    else:
        sql(
            "INSERT INTO [User] (Id, FirstName, LastName, UserName, Password, Email, BirthDate, "
            "IsLogged, ActualTheme, ProfilePicture, EnterpriseId, AccessLevel, Language, CreatedAt, "
            "IsActive, IsServiceAccount, PhoneNumber) VALUES ("
            f"'{uuid.uuid4()}', 'Perf', 'Admin', 'perf.admin', '{stored}', 'perf.admin@local.perf', "
            f"'1990-01-01', 0, 0, '', '{enterprise_id}', 0, 0, SYSUTCDATETIME(), 1, 0, '');", sa)
        print('bootstrap: created perf.admin')

    return password, enterprise_id


# ---------------------------------------------------------------- API

class Api:
    def __init__(self, base):
        self.base = base.rstrip('/')
        self.token = None

    def call(self, method, path, body=None, tolerate=()):
        url = f'{self.base}/{path.lstrip("/")}'
        data = json.dumps(body).encode() if body is not None else None
        req = urllib.request.Request(url, data=data, method=method,
                                     headers={'Content-Type': 'application/json'})
        if self.token:
            req.add_header('Authorization', f'Bearer {self.token}')
        try:
            with urllib.request.urlopen(req, timeout=120) as res:
                raw = res.read()
                if res.status == 204 or not raw:
                    return None
                try:
                    return json.loads(raw)
                except json.JSONDecodeError:
                    return raw.decode(errors='replace')
        except urllib.error.HTTPError as err:
            if err.code in tolerate:
                return None
            detail = err.read().decode(errors='replace')[:300]
            raise RuntimeError(f'{method} {path} -> {err.code} {detail}') from None

    def login(self, user, password):
        res = self.call('POST', '/User/LogInto',
                        {'userName': user, 'password': password, 'recaptchaToken': ''})
        return res['token'] if res else None


def read_env_perf():
    values = {}
    if os.path.exists(ENV_PERF):
        for line in open(ENV_PERF):
            line = line.strip()
            if line and not line.startswith('#') and '=' in line:
                k, v = line.split('=', 1)
                values[k.strip()] = v.strip()
    return values


def write_env_perf(values):
    lines = ['# Written by perf/seed.py. Local fixtures only — these accounts exist to be hammered.',
             f'BASE_URL={values.get("BASE_URL", "http://localhost:8082/api/v1")}', '']
    for role in ROLES:
        lines.append(f'{role.upper()}_USER={values.get(role.upper() + "_USER", "")}')
        lines.append(f'{role.upper()}_PASS={values.get(role.upper() + "_PASS", "")}')
    lines += ['', '# PDV only — enroll a terminal and open a cash session first.',
              f'PDV_USER={values.get("PDV_USER", "")}',
              f'PDV_PASS={values.get("PDV_PASS", "")}',
              f'PDV_TERMINAL={values.get("PDV_TERMINAL", "")}',
              f'PDV_SESSION_ID={values.get("PDV_SESSION_ID", "")}', '']
    with open(ENV_PERF, 'w') as fh:
        fh.write('\n'.join(lines))
    os.chmod(ENV_PERF, 0o600)


def ensure_role_user(api, role, level, env):
    """AddUser always creates an Employee, so the access level is a follow-up UpdateUser."""
    user_name = f'perf.{role}'
    email = f'perf.{role}@local.perf'
    password = new_password()

    users = api.call('GET', '/User/GetUsers?pageNumber=1&pageSize=500') or []
    existing = next((u for u in users if (u.get('email') or '').lower() == email), None)

    if existing is None:
        created = api.call('POST', '/User/AddUser', {
            'firstName': 'Perf', 'lastName': role.capitalize(), 'userName': user_name,
            'password': password, 'email': email, 'birthDate': '1990-01-01T00:00:00Z',
            'actualTheme': 0, 'language': 0,
        })
        existing = created
        action = 'created'
    else:
        action = 'updated'

    api.call('PUT', '/User/UpdateUser', {
        'id': existing['id'], 'firstName': 'Perf', 'lastName': role.capitalize(),
        'userName': user_name, 'email': email, 'birthDate': '1990-01-01T00:00:00Z',
        'accessLevel': level, 'actualTheme': 0, 'language': 0,
        'password': password, 'isActive': True,
    })

    env[role.upper() + '_USER'] = user_name
    env[role.upper() + '_PASS'] = password
    print(f'user: {action} {user_name} (access level {level})')


# ---------------------------------------------------------------- fixtures

def counts(sa):
    rows = sql(
        f"SELECT 'customers', COUNT(*) FROM Customer WHERE Name LIKE '{PREFIX}%' AND IsActive = 1 "
        f"UNION ALL SELECT 'products', COUNT(*) FROM Product WHERE Name LIKE '{PREFIX}%' AND IsActive = 1 "
        "UNION ALL SELECT 'orders', COUNT(*) FROM [Order];", sa)
    out = {}
    for row in rows:
        name, value = row.split()[0], row.split()[-1]
        out[name] = int(value)
    return out


def with_retries(fn, what, attempts=4):
    """Concurrent order creation deadlocks (SQL Server error 1205) and the API reports the loss as
    a 400, so a seeding run gives up on rows that only needed to be tried again. Retrying here
    keeps the fixtures complete; the deadlock itself is a finding for the stress test, not
    something this script should paper over silently."""
    for attempt in range(attempts):
        try:
            fn()
            return True
        except RuntimeError as err:
            if attempt == attempts - 1:
                print(f'  {what}: {err}')
                return False
            time.sleep(0.2 * (attempt + 1) + random.random() * 0.3)
    return False


def run_parallel(jobs, workers, label):
    done = failed = 0
    with concurrent.futures.ThreadPoolExecutor(max_workers=workers) as pool:
        for result in pool.map(lambda fn: fn(), jobs):
            if result is False:
                failed += 1
            done += 1
            if done % 100 == 0:
                print(f'  {label}: {done}/{len(jobs)}', flush=True)
    print(f'  {label}: {done} done, {failed} failed')
    return failed


def seed_customers(api, missing, workers):
    def make(i):
        def job():
            stamp = f'{i}-{uuid.uuid4().hex[:8]}'
            return with_retries(lambda: api.call('POST', '/Customer/AddCustomer', {
                'name': f'{PREFIX} customer {stamp}',
                'email': f'perf.customer.{stamp}@example.invalid',
                'phoneNumber': '11999990000',
                'adress': f'{PREFIX} street, {i}',
                'creditLimit': 100000,
                'isActive': True,
            }), f'customer {i}')
        return job
    return run_parallel([make(i) for i in range(missing)], workers, 'customers')


def seed_products(api, missing, workers):
    def make(i):
        def job():
            stamp = f'{i}-{uuid.uuid4().hex[:8]}'
            return with_retries(lambda: api.call('POST', '/Product/AddProduct', {
                'name': f'{PREFIX} product {stamp}',
                'description': 'Load-test fixture',
                'defaultValue': round(random.uniform(5, 500), 2),
                'costPrice': round(random.uniform(1, 200), 2),
                # Stock is deliberately enormous: order creation draws it down, and a write
                # scenario that runs out starts measuring the 400 instead of the write.
                'storageQuantity': 1_000_000,
                'unitOfMeasure': 'Unit',
                'isExternal': False,
                'isService': False,
            }), f'product {i}')
        return job
    return run_parallel([make(i) for i in range(missing)], workers, 'products')


def seed_orders(api, missing, workers):
    # Active only: the list endpoints hand back soft-deleted rows, and order creation refuses them.
    customers = [c['id'] for c in (api.call('GET', '/Customer/GetCustomers/1/500') or [])
                 if c.get('isActive') is True]
    products = [p['id'] for p in (api.call('GET', '/Product/GetProducts/1/500?includePictures=false') or [])
                if p.get('isActive') is True]
    if not customers or not products:
        sys.exit('cannot seed orders without customers and products')

    def make(i):
        def job():
            # Spread over twelve months so the reports and the dashboard have a shape to aggregate.
            when = datetime.now(timezone.utc) - timedelta(days=random.randint(0, 365))
            # Sampled without replacement: the same product twice on one order is refused with
            # "Order has duplicated items".
            items = [{'productId': pid,
                      'quantity': random.randint(1, 3),
                      'value': round(random.uniform(5, 500), 2)}
                     for pid in random.sample(products, random.randint(1, 4))]
            return with_retries(lambda: api.call('POST', '/Order/InsertOrder', {
                'customerId': random.choice(customers),
                'paymentScheduledDate': when.isoformat(),
                'status': random.choice([0, 1, 2, 3, 4, 5]),
                'items': items,
            }), f'order {i}')
        return job
    return run_parallel([make(i) for i in range(missing)], workers, 'orders')


# ---------------------------------------------------------------- main

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--base', default=os.environ.get('BASE_URL', 'http://localhost:8082/api/v1'))
    ap.add_argument('--customers', type=int, default=500)
    ap.add_argument('--products', type=int, default=1000)
    ap.add_argument('--orders', type=int, default=5000)
    ap.add_argument('--concurrency', type=int, default=8)
    ap.add_argument('--users-only', action='store_true')
    args = ap.parse_args()

    sa = sa_password()
    env = read_env_perf()
    env['BASE_URL'] = args.base

    admin_password, enterprise_id = bootstrap_admin(sa)
    env['ADMIN_USER'] = 'perf.admin'
    env['ADMIN_PASS'] = admin_password
    print(f'enterprise: {enterprise_id}')

    api = Api(args.base)
    api.token = api.login('perf.admin', admin_password)
    if not api.token:
        sys.exit('bootstrap admin cannot log in — is the API up, and running in Development?')
    print('login: perf.admin ok')

    for role, level in ROLES.items():
        if role == 'admin':
            continue
        ensure_role_user(api, role, level, env)

    write_env_perf(env)
    print(f'wrote {ENV_PERF}')

    # Every role account is proven to work before anything else is seeded — a scenario that cannot
    # log in wastes a whole run.
    for role in ROLES:
        token = Api(args.base).login(env[role.upper() + '_USER'], env[role.upper() + '_PASS'])
        print(f'login check: {env[role.upper() + "_USER"]} -> {"ok" if token else "FAILED"}')
        if not token:
            sys.exit(1)

    if args.users_only:
        return

    have = counts(sa)
    print(f'existing: {have}')
    failures = 0
    if args.customers > have['customers']:
        failures += seed_customers(api, args.customers - have['customers'], args.concurrency)
    if args.products > have['products']:
        failures += seed_products(api, args.products - have['products'], args.concurrency)
    if args.orders > have['orders']:
        failures += seed_orders(api, args.orders - have['orders'], args.concurrency)

    print(f'final: {counts(sa)}')
    if failures:
        sys.exit(f'{failures} requests failed — see above')


if __name__ == '__main__':
    main()
