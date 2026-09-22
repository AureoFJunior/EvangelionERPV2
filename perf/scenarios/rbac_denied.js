// §6.5 — what a refusal costs. An Employee token against Manager-only routes: the assertion is
// 403 (never 200), and the latency budget is tight because a deny that queries the database first
// is a denial-of-service lever.
import http from 'k6/http';
import { check } from 'k6';
import { BASE, stages, tokens, auth, track, think, baseThresholds } from '../lib/common.js';

export const options = {
    stages: stages(),
    thresholds: Object.assign({}, baseThresholds, {
        'http_req_duration{route:deny_payroll}': ['p(95)<100'],
        'http_req_duration{route:deny_payables}': ['p(95)<100'],
        'http_req_duration{route:deny_order_delete}': ['p(95)<100'],
        // Every request in this scenario is meant to be refused, so the usual failure gate is off
        // and "was it actually a 403" is the gate instead.
        http_req_failed: ['rate<=1'],
        checks: ['rate==1'],
    }),
};

export function setup() {
    return { tokens: tokens(['employee']) };
}

function denied(res, route) {
    check(res, { [`${route}: 403`]: (r) => r.status === 403 });
    return track(res, route);
}

export default function (data) {
    const t = data.tokens.employee;

    denied(http.get(`${BASE}/Payroll/GetPayroll`, auth(t, 'deny_payroll')), 'deny_payroll');
    think();

    denied(http.get(`${BASE}/PayableBills/GetPayableBills/1/20`, auth(t, 'deny_payables')), 'deny_payables');
    think();

    // A Guid that names nothing: if RBAC ever lets this through, it deletes nothing, and the check
    // still fails loudly because the status is not 403.
    denied(http.del(`${BASE}/Order/DeleteOrder?id=00000000-0000-0000-0000-000000000001`,
        null, auth(t, 'deny_order_delete')), 'deny_order_delete');
    think();
}
