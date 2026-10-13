// P1b - find the knee on the expensive paths.
//
// p1_knee found no knee: p95 sat flat at ~197ms from 85 to 751 req/s and the API peaked at 178%
// CPU. That result is real but narrow - it only exercised paginated 20-row lists, which are cheap.
// The earlier read_heavy run saturated the whole box at 98.7 req/s because it hit the aggregation
// endpoints, where SalesPerformance alone measured 1.68s p95.
//
// So the ceiling is set by the query mix, not by request volume, and this scenario puts the
// expensive endpoints in at a realistic share and pushes until something gives.
//
// One action per iteration, deliberately: with ramping-arrival-rate that makes the target rate the
// request rate directly, so the numbers in the stages below read as req/s with no mental arithmetic.
import http from 'k6/http';
import { BASE, tokens, auth, okRead, baseThresholds } from '../lib/common.js';

const ROLES = ['supervisor', 'manager', 'admin'];
const NOW = new Date();

// Share of actions, from the journey model. The three aggregation endpoints total 35%, which is
// higher than a real day but is the point of the exercise - it is where the cost lives.
const ACTIONS = [
    { name: 'products_list', weight: 30, tier: 'any' },
    { name: 'orders_list', weight: 20, tier: 'any' },
    { name: 'customers_list', weight: 15, tier: 'reader' },
    { name: 'reports_list', weight: 15, tier: 'any' },
    { name: 'dashboard_today', weight: 10, tier: 'manager' },
    { name: 'sales_performance', weight: 10, tier: 'manager' },
];

const PICK = [];
ACTIONS.forEach(a => { for (let i = 0; i < a.weight; i++) PICK.push(a); });

export const options = {
    scenarios: {
        knee: {
            executor: 'ramping-arrival-rate',
            startRate: 100,
            timeUnit: '1s',
            preAllocatedVUs: 400,
            // Generous: once latency climbs, VUs needed = rate x latency. At 2000/s and one second
            // of latency that is 2000 VUs. Running out would silently turn this back into a closed
            // model, so watch dropped_iterations in the summary.
            maxVUs: 5000,
            stages: [
                { duration: '1m', target: 200 },
                { duration: '2m', target: 500 },
                { duration: '2m', target: 900 },
                { duration: '2m', target: 1400 },
                { duration: '2m', target: 2000 },
                { duration: '1m', target: 0 },
            ],
        },
    },
    // No latency gate. A breach is the measurement, not a failure. Only genuine faults fail.
    thresholds: Object.assign({}, baseThresholds, {
        'status_5xx': ['count==0'],
        'status_403': ['count==0'],
    }),
};

export function setup() {
    return { tokens: tokens(ROLES) };
}

export default function (data) {
    const action = PICK[Math.floor(Math.random() * PICK.length)];

    // Pick a role that is actually permitted the action. Asking with a role RBAC denies would
    // measure the 403 path, which is what made the first p1_knee run unreadable.
    let role;
    if (action.tier === 'manager') role = Math.random() < 0.8 ? 'manager' : 'admin';
    else if (action.tier === 'reader') role = Math.random() < 0.8 ? 'supervisor' : 'admin';
    else role = ROLES[Math.floor(Math.random() * ROLES.length)];

    const t = data.tokens[role];
    let res;

    switch (action.name) {
        case 'products_list':
            res = http.get(`${BASE}/Product/GetProducts?pageNumber=1&pageSize=20&includePictures=false`, auth(t, 'products_list'));
            break;
        case 'orders_list':
            res = http.get(`${BASE}/Order/GetOrders?pageNumber=1&pageSize=20`, auth(t, 'orders_list'));
            break;
        case 'customers_list':
            res = http.get(`${BASE}/Customer/GetCustomers?pageNumber=1&pageSize=20`, auth(t, 'customers_list'));
            break;
        case 'reports_list':
            // ReportsController carries [Route(".../reports")] with no action segment, so it is
            // /reports and NOT /Reports/GetReports - the latter 404s.
            res = http.get(`${BASE}/reports`, auth(t, 'reports_list'));
            break;
        case 'dashboard_today':
            res = http.get(`${BASE}/Dashboard/Today`, auth(t, 'dashboard_today'));
            break;
        case 'sales_performance':
            // year and month are required; without them the endpoint answers 400.
            res = http.get(`${BASE}/SalesPerformance/GetPerformance?year=${NOW.getFullYear()}&month=${NOW.getMonth() + 1}`, auth(t, 'sales_performance'));
            break;
    }

    okRead(res, action.name);
}

export function handleSummary(data) {
    const m = data.metrics;
    const v = (k) => (m[k] ? m[k].values : {});
    const dur = v('http_req_duration');
    const summary = {
        requests: v('http_reqs').count || 0,
        rps_avg: Math.round((v('http_reqs').rate || 0) * 10) / 10,
        p95_ms: Math.round(dur['p(95)'] || 0),
        max_ms: Math.round(dur.max || 0),
        failed_pct: Math.round((v('http_req_failed').rate || 0) * 10000) / 100,
        dropped_iterations: v('dropped_iterations').count || 0,
        status_403: v('status_403').count || 0,
        status_429: v('status_429').count || 0,
        status_5xx: v('status_5xx').count || 0,
    };
    return {
        stdout: '\nP1b HEAVY KNEE SUMMARY ' + JSON.stringify(summary, null, 2) + '\n',
        '/out/p1b-summary.json': JSON.stringify(summary, null, 2),
    };
}
