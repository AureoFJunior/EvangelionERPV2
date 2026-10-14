// §6.4 — terminal traffic. Read-only by default: a till pulling catalog deltas and checking its
// session, which is what a shop floor does all day and after a reconnect.
//
// The write half (SyncSaleBatch) is opt-in because it needs an enrolled terminal with an OPEN cash
// session, and nothing in this rig opens one. Set PERF_PDV_SYNC=1 plus PDV_SESSION_ID to run it.
import http from 'k6/http';
import { BASE, stages, auth, okRead, okWrite, track, think, pickOne, ids, tokens, baseThresholds } from '../lib/common.js';

const TERMINAL = __ENV.PDV_TERMINAL;   // "terminalId:credential", the X-Pdv-Terminal header form
const SYNC = __ENV.PERF_PDV_SYNC === '1';
const SESSION_ID = __ENV.PDV_SESSION_ID;

export const options = {
    stages: stages(),
    thresholds: Object.assign({}, baseThresholds, {
        'http_req_duration{route:pdv_catalog}': ['p(95)<1500'],
        'http_req_duration{route:pdv_open_session}': ['p(95)<1500'],
        'http_req_duration{route:pdv_sync_batch}': ['p(95)<1500'],
    }),
};

export function setup() {
    if (!TERMINAL) throw new Error('PDV_TERMINAL="terminalId:credential" is required — enroll a terminal first');
    if (SYNC && !SESSION_ID) throw new Error('PERF_PDV_SYNC=1 needs PDV_SESSION_ID from an open cash session');

    const terminalId = TERMINAL.split(':')[0];
    const body = { userName: __ENV.PDV_USER, password: __ENV.PDV_PASS };
    const res = http.post(`${BASE}/Pdv/Login`, JSON.stringify(body), {
        headers: { 'Content-Type': 'application/json', 'X-Pdv-Terminal': TERMINAL },
        tags: { route: 'pdv_login' },
    });
    track(res, 'pdv_login');
    if (res.status !== 200) throw new Error(`pdv login failed: ${res.status} ${res.body}`);

    const token = res.json('token');
    // Product ids for the sale payload come from the till's own catalog, so the scenario never
    // needs an admin token to run.
    const admin = SYNC ? tokens(['admin']).admin : null;
    return { token, terminalId, products: SYNC ? ids(admin, 25).products : [] };
}

export default function (data) {
    const t = data.token;
    const since = new Date(Date.now() - 864e5).toISOString();

    okRead(http.get(`${BASE}/Pdv/GetCatalog?since=${since}&pageSize=200`, auth(t, 'pdv_catalog')), 'pdv_catalog');
    think();

    okRead(http.get(`${BASE}/Pdv/GetOpenSession/${data.terminalId}`, auth(t, 'pdv_open_session')), 'pdv_open_session');
    think();

    if (!SYNC) return;

    // 25 sales per batch, the size a till accumulates during a short outage. Kestrel caps the body
    // at 256 KB (RequestLimits:MaxRequestBodySizeBytes) — a much larger batch is a 413, not a
    // latency sample.
    const sales = [];
    for (let i = 0; i < 25; i++) {
        sales.push({
            clientSaleId: uuid(),
            clientSessionId: SESSION_ID,
            terminalId: data.terminalId,
            soldAtLocal: new Date().toISOString(),
            discountAmount: 0,
            surchargeAmount: 0,
            items: [{ productId: pickOne(data.products), quantity: 1, value: 9.9 }],
            payments: [{ method: 0, amount: 9.9, changeGiven: 0 }],
        });
    }
    okWrite(http.post(`${BASE}/Pdv/SyncSaleBatch`, JSON.stringify({ sales }), auth(t, 'pdv_sync_batch')), 'pdv_sync_batch');
    think();
}

// Sale ids are client-generated and the sync is idempotent on them, so a fresh v4 per sale is the
// whole requirement.
function uuid() {
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
        const r = (Math.random() * 16) | 0;
        return (c === 'x' ? r : (r & 0x3) | 0x8).toString(16);
    });
}
