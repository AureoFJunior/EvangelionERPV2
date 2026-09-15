// P1 - find the knee.
//
// Every other scenario here uses a closed model: N virtual users, each waiting for its own response
// before sending again. That model cannot find a saturation point, because when the server slows
// down the load slows down with it - the test quietly throttles itself and reports a comfortable
// latency for an overloaded system.
//
// This one is an OPEN model: ramping-arrival-rate holds a target requests-per-second regardless of
// how the server is coping, and k6 allocates VUs as needed. If the API cannot keep up, the queue
// forms in the load generator where it is visible, latency climbs, and the knee is the point where
// p95 leaves its budget.
//
// The role mix is weighted the way the real user population is, not evenly. read_heavy splits three
// roles with __VU % 3, which is why a third of its Dashboard and SalesPerformance calls came back
// 403: Supervisor is denied both by RbacRolePermissionMap, correctly. Here Supervisor simply does
// not visit endpoints it cannot see, so a 403 in this run is a genuine fault and not a design.
import http from 'k6/http';
import { BASE, tokens, auth, okRead, status403, baseThresholds } from '../lib/common.js';

// Roughly the population of a 10 000-user ERP: mostly employees, a thin management layer.
// Employee is deliberately absent. RbacRolePermissionMap gives Employee neither Products.Read nor
// Customers.Read, so a 70% Employee mix spent the first run generating 54 294 correct 403s and
// measuring the authorization path instead of the endpoints. Supervisor is the lowest role that can
// actually read this data, so it stands in for the bulk of the population.
const MIX = [
    { role: 'supervisor', weight: 70 },
    { role: 'manager', weight: 25 },
    { role: 'admin', weight: 5 },
];

const ROLES = MIX.map(m => m.role);
const PICK = [];
MIX.forEach(m => { for (let i = 0; i < m.weight; i++) PICK.push(m.role); });

export const options = {
    scenarios: {
        knee: {
            executor: 'ramping-arrival-rate',
            startRate: 20,
            timeUnit: '1s',
            // Headroom to allocate: if the API stalls, k6 needs spare VUs to keep the arrival rate
            // honest. Running out of VUs would make this a closed model again without saying so -
            // watch dropped_iterations, which is the signal that it happened.
            preAllocatedVUs: 200,
            maxVUs: 2000,
            stages: [
                { duration: '2m', target: 40 },
                { duration: '2m', target: 80 },
                { duration: '2m', target: 120 },
                { duration: '2m', target: 180 },
                { duration: '2m', target: 240 },
                { duration: '2m', target: 300 },
                { duration: '1m', target: 0 },
            ],
        },
    },
    thresholds: Object.assign({}, baseThresholds, {
        // Deliberately NOT a pass/fail gate on latency. This run exists to find where p95 breaks,
        // so a breach is the result, not a failure. Only real faults fail the run.
        'status_5xx': ['count==0'],
        'status_403': ['count==0'],
    }),
};

export function setup() {
    return { tokens: tokens(ROLES) };
}

export default function (data) {
    const role = PICK[Math.floor(Math.random() * PICK.length)];
    const t = data.tokens[role];

    // One "action" is a small burst of calls, which is what a screen actually costs - a single
    // request per iteration would understate the work behind a page view.
    // pageNumber, not page - and includePictures defaults to TRUE, which returns base64 images and
    // turns a list call into a multi-megabyte response. The first run used `page` and the wrong
    // action names, so 165 379 requests 404'd and measured nothing.
    okRead(http.get(`${BASE}/Product/GetProducts?pageNumber=1&pageSize=20&includePictures=false`, auth(t, 'products_list')), 'products_list');

    if (role === 'supervisor' || role === 'admin') {
        okRead(http.get(`${BASE}/Customer/GetCustomers?pageNumber=1&pageSize=20`, auth(t, 'customers_list')), 'customers_list');
    }

    // Manager and above only. Supervisor and Employee are denied these by RBAC, so asking would
    // measure the 403 path rather than the endpoint.
    if (role === 'manager' || role === 'admin') {
        okRead(http.get(`${BASE}/Dashboard/Today`, auth(t, 'dashboard_today')), 'dashboard_today');
    } else {
        okRead(http.get(`${BASE}/Order/GetOrders?pageNumber=1&pageSize=20`, auth(t, 'orders_list')), 'orders_list');
    }
}

export function handleSummary(data) {
    const m = data.metrics;
    const line = (k) => (m[k] ? m[k].values : {});
    const dur = line('http_req_duration');
    const summary = {
        requests: (line('http_reqs').count || 0),
        rps: Math.round((line('http_reqs').rate || 0) * 10) / 10,
        p95_ms: Math.round(dur['p(95)'] || 0),
        p99_ms: Math.round(dur['p(99)'] || 0),
        failed_pct: Math.round((line('http_req_failed').rate || 0) * 10000) / 100,
        dropped_iterations: line('dropped_iterations').count || 0,
        status_403: line('status_403').count || 0,
        status_429: line('status_429').count || 0,
        status_5xx: line('status_5xx').count || 0,
    };
    return {
        stdout: '\nP1 KNEE SUMMARY ' + JSON.stringify(summary, null, 2) + '\n',
        '/out/p1-summary.json': JSON.stringify(summary, null, 2),
    };
}
