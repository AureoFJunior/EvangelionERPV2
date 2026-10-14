// §6.2 — aggregation and payload size. Manager tier and up; this is where resp_bytes matters.
import http from 'k6/http';
import { BASE, stages, tokens, auth, okRead, think, baseThresholds } from '../lib/common.js';

const ROLES = ['admin', 'manager', 'supervisor'];
const NOW = new Date();

export const options = {
    stages: stages(),
    thresholds: Object.assign({}, baseThresholds, {
        'http_req_duration{route:dashboard_today}': ['p(95)<1200'],
        'http_req_duration{route:reports_list}': ['p(95)<1200'],
        'http_req_duration{route:sales_performance}': ['p(95)<1200'],
        'http_req_duration{route:products_list_pictures}': ['p(95)<1200'],
        // Payload budgets, spec §8.3.
        'resp_bytes{route:dashboard_today}': ['p(95)<524288'],
        'resp_bytes{route:products_list_pictures}': ['p(95)<2097152'],
    }),
};

export function setup() {
    return { tokens: tokens(ROLES) };
}

export default function (data) {
    const role = ROLES[__VU % ROLES.length];
    const t = data.tokens[role];

    okRead(http.get(`${BASE}/Dashboard/Today`, auth(t, 'dashboard_today')), 'dashboard_today');
    think();

    okRead(http.get(`${BASE}/reports`, auth(t, 'reports_list')), 'reports_list');
    think();

    okRead(http.get(`${BASE}/SalesPerformance/GetPerformance?year=${NOW.getFullYear()}&month=${NOW.getMonth() + 1}`,
        auth(t, 'sales_performance')), 'sales_performance');
    think();

    // The one request whose cost is the payload, not the query: base64 pictures inline.
    okRead(http.get(`${BASE}/Product/GetProducts/1/200?includePictures=true`,
        auth(t, 'products_list_pictures')), 'products_list_pictures');
    think();
}
