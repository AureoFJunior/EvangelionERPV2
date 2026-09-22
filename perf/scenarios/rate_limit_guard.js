// §6.6 — proves the limiter is still armed, and that a flooding client does not take the API down
// for everyone else. Run this one WITHOUT the IpRateLimiting override from spec §3.
import http from 'k6/http';
import { check } from 'k6';
import { BASE, tokens, auth, track, status429 } from '../lib/common.js';

export const options = {
    scenarios: {
        // 400/min against a general-rule limit of 300/min: the last ~100 must be refused.
        flood: {
            executor: 'constant-arrival-rate',
            rate: 400,
            timeUnit: '1m',
            duration: '90s',
            preAllocatedVUs: 20,
            exec: 'flood',
        },
        // A different caller, hitting the public health endpoint throughout. Its latency is the
        // real assertion: one abusive IP must not become everyone's outage.
        bystander: {
            executor: 'constant-vus',
            vus: 1,
            duration: '90s',
            exec: 'bystander',
        },
    },
    thresholds: {
        'status_429': ['count>0'],
        'http_req_duration{route:health}': ['p(95)<300'],
        'http_req_failed{route:health}': ['rate==0'],
        checks: ['rate==1'],
    },
};

export function setup() {
    return { tokens: tokens(['employee']) };
}

export function flood(data) {
    const res = http.get(`${BASE}/Notification/Feed`, auth(data.tokens.employee, 'flood'));
    check(res, { 'flood: 200/204/429': (r) => [200, 204, 429].indexOf(r.status) !== -1 });
    track(res, 'flood');
}

export function bystander() {
    // /health is the one route outside the JWT gate, and it sits outside `api/v1`.
    const root = BASE.replace(/\/api\/v\d+$/, '');
    const res = http.get(`${root}/health`, { tags: { route: 'health' } });
    check(res, { 'health: 200': (r) => r.status === 200 });
    track(res, 'health');
}
