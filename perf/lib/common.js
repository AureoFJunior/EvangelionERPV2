// Shared rig for every scenario in perf/scenarios.
//
// Routes are spelled the way the controllers actually route them: paging is path segments
// (`[HttpGet("{pageNumber?}/{pageSize?}")]`), not query string, and ReportsController is the one
// controller with a flat `api/v1/reports` route instead of `[controller]/[action]`.
import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend, Counter } from 'k6/metrics';

export const BASE = __ENV.BASE_URL || 'http://localhost:8082/api/v1';
export const LEVEL = __ENV.LEVEL || 'smoke';

// VU counts per level, from docs/performance/SPEC-stress-test.md §7.
const LEVELS = {
    smoke: [{ duration: '60s', target: 1 }],
    baseline: [{ duration: '30s', target: 10 }, { duration: '5m', target: 10 }, { duration: '30s', target: 0 }],
    load: [{ duration: '1m', target: 50 }, { duration: '10m', target: 50 }, { duration: '1m', target: 0 }],
    stress: [{ duration: '13m', target: 300 }, { duration: '2m', target: 0 }],
    spike: [{ duration: '1m', target: 10 }, { duration: '30s', target: 300 }, { duration: '2m', target: 300 }, { duration: '30s', target: 10 }, { duration: '1m', target: 10 }],
    soak: [{ duration: '2m', target: 30 }, { duration: '56m', target: 30 }, { duration: '2m', target: 0 }],
};

export function stages() {
    const s = LEVELS[LEVEL];
    if (!s) throw new Error(`unknown LEVEL "${LEVEL}" — one of: ${Object.keys(LEVELS).join(', ')}`);
    return s;
}

// Response size is a first-class metric here (spec §8.3), so it gets its own trend per route
// instead of being inferred from data_received / http_reqs at the end.
export const respBytes = new Trend('resp_bytes');
export const status401 = new Counter('status_401');
export const status403 = new Counter('status_403');
export const status429 = new Counter('status_429');
export const status5xx = new Counter('status_5xx');

export function track(res, route) {
    respBytes.add(res.body ? res.body.length : 0, { route });
    if (res.status === 401) status401.add(1, { route });
    if (res.status === 403) status403.add(1, { route });
    if (res.status === 429) status429.add(1, { route });
    if (res.status >= 500) status5xx.add(1, { route });
    return res;
}

// Reads return 204 when the page is empty, so "ok" is 200 or 204 — not just 200.
export function okRead(res, route) {
    check(res, { [`${route}: 200/204`]: (r) => r.status === 200 || r.status === 204 });
    return track(res, route);
}

export function okWrite(res, route) {
    check(res, { [`${route}: 2xx`]: (r) => r.status >= 200 && r.status < 300 });
    return track(res, route);
}

export function think() {
    sleep(0.5 + Math.random() * 1.5);
}

export function auth(token, route, extra) {
    return {
        headers: Object.assign({ Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' }, extra || {}),
        tags: { route },
    };
}

// Credentials come from the environment. No defaults for passwords on purpose.
export function creds(role) {
    const u = __ENV[`${role.toUpperCase()}_USER`];
    const p = __ENV[`${role.toUpperCase()}_PASS`];
    if (!u || !p) throw new Error(`missing ${role.toUpperCase()}_USER / ${role.toUpperCase()}_PASS`);
    return { userName: u, password: p };
}

// Minted once in setup(), never inside the iteration loop: User/LogInto carries its own 100/min
// limit that the perf override in §3 does not raise.
export function login(role) {
    const body = Object.assign(creds(role), { recaptchaToken: '' });
    const res = http.post(`${BASE}/User/LogInto`, JSON.stringify(body), {
        headers: { 'Content-Type': 'application/json' },
        tags: { route: 'auth_login' },
    });
    track(res, 'auth_login');
    if (res.status !== 200) throw new Error(`login failed for ${role}: ${res.status} ${res.body}`);
    const token = res.json('token');
    if (!token) throw new Error(`login for ${role} returned no token`);
    return token;
}

export function tokens(roles) {
    const out = {};
    roles.forEach((r) => { out[r] = login(r); });
    return out;
}

// Scenarios need real ids and this repo has no seeding endpoint, so they are read off the API with
// an admin token in setup(). Whatever the DB holds is what gets exercised — seed it first (spec §5).
export function ids(token, take) {
    const n = take || 50;
    const opt = auth(token, 'setup_ids');
    const products = http.get(`${BASE}/Product/GetProducts/1/${n}?includePictures=false`, opt);
    const customers = http.get(`${BASE}/Customer/GetCustomers/1/${n}`, opt);
    // Active rows only. The list endpoints return soft-deleted products and customers, but order
    // creation requires IsActive == true, so an unfiltered sample turns a slice of every write run
    // into 400s that look like load failures and are not.
    const pick = (res, what) => {
        if (res.status !== 200) throw new Error(`setup: ${what} returned ${res.status} — seed the database first`);
        const list = res.json();
        if (!list || !list.length) throw new Error(`setup: no ${what} in this tenant — seed the database first`);
        const active = list.filter((x) => x.isActive === true).map((x) => x.id);
        if (!active.length) throw new Error(`setup: no active ${what} in this tenant — seed the database first`);
        return active;
    };
    return { products: pick(products, 'products'), customers: pick(customers, 'customers') };
}

export function pickOne(arr) {
    return arr[Math.floor(Math.random() * arr.length)];
}

// Distinct picks. An order with the same product on two lines is refused outright
// ("Order has duplicated items"), so sampling with replacement quietly turns a slice of every
// write run into 400s.
export function pickMany(arr, n) {
    const pool = arr.slice();
    const out = [];
    while (out.length < n && pool.length) {
        out.push(pool.splice(Math.floor(Math.random() * pool.length), 1)[0]);
    }
    return out;
}

// Shared floor for every scenario: no 5xx, under 1% failures, and the load generator itself must
// not be the bottleneck (spec §11).
export const baseThresholds = {
    http_req_failed: ['rate<0.01'],
    status_5xx: ['count==0'],
    http_req_blocked: ['p(95)<5'],
};
