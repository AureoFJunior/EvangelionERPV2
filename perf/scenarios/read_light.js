// §6.1 — cheap authenticated GETs, one VU journey per access level.
import http from 'k6/http';
import { BASE, stages, tokens, auth, okRead, think, baseThresholds } from '../lib/common.js';

const ROLES = ['admin', 'manager', 'supervisor', 'employee'];

export const options = {
    stages: stages(),
    thresholds: Object.assign({}, baseThresholds, {
        'http_req_duration{route:products_list}': ['p(95)<300'],
        'http_req_duration{route:customers_list}': ['p(95)<300'],
        'http_req_duration{route:orders_list}': ['p(95)<300'],
    }),
};

export function setup() {
    return { tokens: tokens(ROLES) };
}

export default function (data) {
    // Role is pinned to the VU, so a run spreads evenly across access levels and every sample is
    // tagged with the level that produced it.
    const role = ROLES[__VU % ROLES.length];
    const t = data.tokens[role];

    okRead(http.get(`${BASE}/Product/GetProducts/1/20?includePictures=false`,
        auth(t, 'products_list', { 'X-Role': role })), 'products_list');
    think();

    okRead(http.get(`${BASE}/Order/GetOrders/1/20`, auth(t, 'orders_list')), 'orders_list');
    think();

    // Employees hold no customers.read — asking anyway would score a 403 as a failure, so the
    // cheapest read they do have stands in.
    if (role === 'employee') {
        okRead(http.get(`${BASE}/Notification/Feed`, auth(t, 'notifications_feed')), 'notifications_feed');
    } else {
        okRead(http.get(`${BASE}/Customer/GetCustomers/1/20`, auth(t, 'customers_list')), 'customers_list');
    }
    think();
}
