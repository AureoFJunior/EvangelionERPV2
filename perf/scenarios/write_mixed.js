// §6.3 — the write path: order create (every role that can), customer create (Supervisor+),
// product update (opt-in). Rows created here are named with the `E2E ` prefix so
// ./scripts/e2e-cleanup.sh --apply removes them afterwards.
import http from 'k6/http';
import { BASE, stages, tokens, ids, auth, okWrite, pickOne, pickMany, think, baseThresholds } from '../lib/common.js';

const ROLES = ['manager', 'supervisor', 'employee'];
const UPDATE_PRODUCTS = __ENV.PERF_PRODUCT_UPDATE === '1';

export const options = {
    stages: stages(),
    thresholds: Object.assign({}, baseThresholds, {
        'http_req_duration{route:order_create}': ['p(95)<800'],
        'http_req_duration{route:customer_create}': ['p(95)<800'],
        'http_req_duration{route:product_update}': ['p(95)<800'],
    }),
};

export function setup() {
    const t = tokens(ROLES.concat(['admin']));
    const seed = ids(t.admin, 50);
    return { tokens: t, ids: seed };
}

export default function (data) {
    const role = ROLES[__VU % ROLES.length];
    const t = data.tokens[role];

    // ponytail: quantity is fixed at 1 because order creation draws down StorageQuantity — a long
    // write run against thin stock starts answering 400 and the run measures the reject path.
    // Seed stock high (spec §5) or raise this once a stock-neutral test order type exists.
    const prices = [10.5, 25.0, 7.25];
    const order = {
        customerId: pickOne(data.ids.customers),
        paymentScheduledDate: new Date(Date.now() + 7 * 864e5).toISOString(),
        status: 0, // EnumOrderStatus.Pending
        items: pickMany(data.ids.products, 3).map((productId, i) => ({
            productId, quantity: 1, value: prices[i],
        })),
    };
    okWrite(http.post(`${BASE}/Order/InsertOrder`, JSON.stringify(order), auth(t, 'order_create')), 'order_create');
    think();

    if (role === 'employee') return; // customers.create is Supervisor and up

    const stamp = `${__VU}-${__ITER}-${Date.now()}`;
    const customer = {
        name: `E2E perf ${stamp}`,
        email: `e2e.perf.${stamp}@example.invalid`,
        phoneNumber: '11999990000',
        adress: 'E2E perf street, 100',
        isActive: true,
    };
    okWrite(http.post(`${BASE}/Customer/AddCustomer`, JSON.stringify(customer), auth(t, 'customer_create')), 'customer_create');
    think();

    // Opt-in: this rewrites real product rows with their own current values. Harmless in a
    // disposable local DB, never something to fire blind, hence the flag.
    if (UPDATE_PRODUCTS && role === 'manager') {
        const p = pickOne(data.ids.products);
        const detail = http.get(`${BASE}/Product/GetProduct/${p}`, auth(t, 'product_read'));
        if (detail.status === 200) {
            const d = detail.json();
            const body = {
                id: d.id,
                name: d.name,
                description: d.description,
                defaultValue: d.defaultValue,
                storageQuantity: d.storageQuantity,
                unitOfMeasure: d.unitOfMeasure,
                isExternal: d.isExternal,
                isService: d.isService,
                isActive: true,
            };
            okWrite(http.put(`${BASE}/Product/UpdateProduct`, JSON.stringify(body), auth(t, 'product_update')), 'product_update');
        }
        think();
    }
}
