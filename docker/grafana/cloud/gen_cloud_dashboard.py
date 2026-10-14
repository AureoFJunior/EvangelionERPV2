#!/usr/bin/env python3
"""Generates the Grafana Cloud dashboard.

Grafana Cloud receives a deliberately narrow slice of what the local Prometheus holds: the
remote_write keep-filter in docker/prometheus/prometheus.yml ships 26 series and drops the rest,
because the raw request histogram alone is 14 000-24 000 series against a 10 000 free-tier cap,
and because the opportunity-radar gauges carry enterprise_id and must not leave the machine.

So a Cloud dashboard CANNOT use the metric names a local one uses. Every query here is written
against a shipped series, and there is a test at the bottom of this file listing which those are.
Anything referencing http_request_duration_seconds_bucket, http_requests_received_total or
evangelion_opportunity_* will render "No data" in Cloud for ever, by design.

Output imports through Dashboards > New > Import > paste JSON. The __inputs block makes Grafana
prompt for the datasource rather than hard-coding a uid that differs between stacks.
"""
import json
import os

# The 26 series the keep-filter lets through. Queries are checked against this list.
SHIPPED = {
    "up",
    "http_requests:rate5m",
    "http_requests:error_ratio5m",
    "http_request_duration_seconds:by_controller",
    "evangelion_health_check_status", "evangelion_pending_migrations",
    "evangelion_queue_depth", "evangelion_dlq_depth",
    "dotnet_collection_count_total", "dotnet_total_memory_bytes",
    "process_cpu_seconds_total", "process_working_set_bytes", "process_private_memory_bytes",
    "process_resident_memory_bytes", "process_virtual_memory_bytes",
    "process_virtual_memory_max_bytes", "process_start_time_seconds",
    "process_num_threads", "process_open_fds", "process_max_fds", "process_open_handles",
    "process_network_receive_bytes_total", "process_network_transmit_bytes_total",
    "system_runtime_dotnet_monitor_lock_contentions",
    "system_runtime_dotnet_thread_pool_queue_length",
    "system_runtime_dotnet_thread_pool_thread_count",
    "system_runtime_dotnet_thread_pool_work_item_count",
}

DS = {"type": "prometheus", "uid": "${DS_PROMETHEUS}"}
PLUGIN = "12.0.0"

CUSTOM = {
    "drawStyle": "line", "lineInterpolation": "linear", "barAlignment": 0,
    "lineWidth": 1, "fillOpacity": 8, "gradientMode": "none",
    "spanNulls": False, "showPoints": "never", "pointSize": 5,
    "stacking": {"mode": "none", "group": "A"},
    "axisPlacement": "auto", "axisLabel": "", "axisColorMode": "text",
    "axisBorderShow": False, "axisCenteredZero": False,
    "scaleDistribution": {"type": "linear"},
    "hideFrom": {"tooltip": False, "viz": False, "legend": False},
    "thresholdsStyle": {"mode": "off"}, "insertNulls": False,
}

_used = []


def target(expr, legend=None, ref="A", instant=False):
    _used.append(expr)
    t = {"refId": ref, "datasource": DS, "expr": expr, "editorMode": "code",
         "range": not instant, "instant": instant, "format": "time_series"}
    if legend:
        t["legendFormat"] = legend
    return t


def ts(i, title, targets, x, y, w=12, h=8, unit="short", desc="", maxv=None, fill=8, lw=1):
    defaults = {"unit": unit, "color": {"mode": "palette-classic"},
                "custom": dict(CUSTOM, fillOpacity=fill, lineWidth=lw), "mappings": [],
                "thresholds": {"mode": "absolute", "steps": [{"color": "green", "value": None}]}}
    if maxv is not None:
        defaults["max"] = maxv
    return {"id": i, "type": "timeseries", "title": title, "description": desc,
            "datasource": DS, "pluginVersion": PLUGIN,
            "gridPos": {"h": h, "w": w, "x": x, "y": y},
            "fieldConfig": {"defaults": defaults, "overrides": []},
            "options": {"legend": {"showLegend": True, "displayMode": "list",
                                   "placement": "bottom", "calcs": []},
                        "tooltip": {"mode": "multi", "sort": "desc", "hideZeros": False}},
            "targets": targets}


def stat(i, title, targets, x, y, w=6, h=5, unit="short", desc="",
         mappings=None, steps=None, size=40):
    return {"id": i, "type": "stat", "title": title, "description": desc,
            "datasource": DS, "pluginVersion": PLUGIN,
            "gridPos": {"h": h, "w": w, "x": x, "y": y},
            "fieldConfig": {"defaults": {
                "unit": unit, "mappings": mappings or [], "color": {"mode": "thresholds"},
                "thresholds": {"mode": "absolute",
                               "steps": steps or [{"color": "green", "value": None}]}},
                "overrides": []},
            "options": {"reduceOptions": {"calcs": ["lastNotNull"], "fields": "", "values": False},
                        "colorMode": "background", "graphMode": "none", "justifyMode": "auto",
                        "textMode": "auto", "wideLayout": True, "showPercentChange": False,
                        "percentChangeColorMode": "standard",
                        "text": {"titleSize": 14, "valueSize": size}},
            "targets": targets}


def row(i, title, y):
    return {"id": i, "type": "row", "title": title, "collapsed": False,
            "gridPos": {"h": 1, "w": 24, "x": 0, "y": y}, "panels": []}


UP_MAP = [{"type": "value", "options": {
    "0": {"text": "Down", "color": "red", "index": 0},
    "1": {"text": "Up", "color": "green", "index": 1}}}]
HEALTH_MAP = [{"type": "value", "options": {
    "0": {"text": "Down", "color": "red", "index": 0},
    "0.5": {"text": "Degraded", "color": "orange", "index": 1},
    "1": {"text": "Healthy", "color": "green", "index": 2}}}]

panels = [
    row(100, "Health", 0),
    stat(1, "API up", [target('min(up{job="api"})', instant=True)], 0, 1, w=5,
         mappings=UP_MAP, size=30,
         desc="up{job=\"api\"} arrives through remote_write, so this is the local Prometheus's view "
              "of the API, not a check Cloud performs itself."),
    stat(2, "Dependencies", [target("evangelion_health_check_status", "{{check}}", instant=True)],
         5, 1, w=9, mappings=HEALTH_MAP, size=22,
         desc="1 healthy, 0.5 degraded, 0 unhealthy. Degraded is what the database check reports "
              "for pending migrations, and the alert fires below 1 so it catches both."),
    stat(3, "Pending migrations", [target("max(evangelion_pending_migrations)", instant=True)],
         14, 1, w=5,
         steps=[{"color": "green", "value": None}, {"color": "red", "value": 1}],
         desc="Non-zero means requests touching the new tables answer 500 with 'Invalid column "
              "name'. Nothing applies migrations automatically."),
    stat(4, "Uptime", [target('time() - max(process_start_time_seconds{job="api"})', instant=True)],
         19, 1, w=5, unit="s"),

    row(101, "Traffic", 6),
    ts(5, "Requests per second, by controller",
       [target("sum by (controller) (http_requests:rate5m)", "{{controller}}")], 0, 7, unit="reqps",
       desc="From the http_requests:rate5m recording rule. The raw counter "
            "http_requests_received_total is NOT shipped to Cloud - it is part of the cardinality "
            "that would not fit the free tier."),
    ts(6, "Responses per second, by status",
       [target("sum by (code) (http_requests:rate5m)", "{{code}}")], 12, 7, unit="reqps",
       desc="429 is the IP rate limiter working, not a fault. 403 is RBAC refusing a role, which "
            "is also correct behaviour - neither belongs in the error ratio."),
    ts(7, "5xx ratio",
       [target("http_requests:error_ratio5m", "5xx / all")], 0, 15, unit="percentunit", maxv=0.1,
       desc="Flat zero is the healthy reading. The rule carries `or vector(0)` so a healthy API "
            "draws a zero line instead of rendering 'No data', which looks like a broken panel."),
    ts(8, "Latency",
       [target("histogram_quantile(0.5, sum by (le) (http_request_duration_seconds:by_controller))", "p50", "A"),
        target("histogram_quantile(0.95, sum by (le) (http_request_duration_seconds:by_controller))", "p95", "B"),
        target("histogram_quantile(0.99, sum by (le) (http_request_duration_seconds:by_controller))", "p99", "C")],
       12, 15, unit="s",
       desc="No rate() here: http_request_duration_seconds:by_controller is a recording rule that "
            "is already a rate of buckets. Wrapping it in rate() again would return nothing."),
    ts(9, "Slowest controllers, p95",
       [target("histogram_quantile(0.95, sum by (controller, le) (http_request_duration_seconds:by_controller))",
               "{{controller}}")], 0, 23, w=24, unit="s",
       desc="Per-controller only. Per-action detail exists solely in the local Prometheus - "
            "aggregating it away is what makes this fit inside the free tier."),

    row(102, "Queues", 31),
    ts(10, "Queue depth", [target("evangelion_queue_depth", "{{queue}}")], 0, 32,
       desc="Depth above zero while the consume rate is flat is the WorkerStalled condition: the "
            "worker is up, healthy, and processing nothing."),
    ts(11, "Dead-letter queue depth", [target("evangelion_dlq_depth", "{{queue}}")], 12, 32,
       fill=15, lw=2,
       desc="Nothing consumes the DLQ. Anything here stays until somebody redrives it by hand, so "
            "any non-zero value is worth looking at."),

    row(103, "Runtime", 40),
    ts(12, "CPU cores used", [target("rate(process_cpu_seconds_total[5m])", "{{job}}")], 0, 41,
       desc="Seconds of CPU per second, so 1.0 is one full core saturated."),
    ts(13, "Memory",
       [target("process_working_set_bytes", "working set - {{job}}", "A"),
        target("dotnet_total_memory_bytes", "managed heap - {{job}}", "B")], 12, 41, unit="bytes",
       desc="Working set climbing while the managed heap stays flat is unmanaged memory or "
            "fragmentation, not an ordinary object leak."),
    ts(14, "GC collections per second",
       [target("rate(dotnet_collection_count_total[5m])", "gen {{generation}}")], 0, 49,
       desc="A rising gen 2 rate is the one that matters; gen 0 churn is normal."),
    ts(15, "Thread pool",
       [target("system_runtime_dotnet_thread_pool_queue_length", "queue length", "A"),
        target("system_runtime_dotnet_thread_pool_thread_count", "threads", "B"),
        target("rate(system_runtime_dotnet_monitor_lock_contentions[5m])", "lock contentions/s", "C")],
       12, 49,
       desc="A queue length that does not return to zero is thread-pool starvation, usually "
            "something blocking on async."),
]

dashboard = {
    "__inputs": [{"name": "DS_PROMETHEUS", "label": "Prometheus",
                  "description": "The Grafana Cloud Prometheus this stack writes to",
                  "type": "datasource", "pluginId": "prometheus", "pluginName": "Prometheus"}],
    "__requires": [{"type": "grafana", "id": "grafana", "name": "Grafana", "version": "11.0.0"},
                   {"type": "datasource", "id": "prometheus", "name": "Prometheus", "version": "1.0.0"}],
    "uid": "eva-cloud-overview",
    "title": "Evangelion ERP - Cloud overview",
    "description": "Built only on the 26 series that remote_write ships to Grafana Cloud. "
                   "Raw histograms, raw request counters and every enterprise_id-labelled series "
                   "stay on the local Prometheus by design - see docker/prometheus/prometheus.yml.",
    "tags": ["evangelion", "cloud"],
    "timezone": "browser", "editable": True, "schemaVersion": 41, "version": 1,
    "refresh": "1m", "graphTooltip": 0,
    "time": {"from": "now-6h", "to": "now"},
    "templating": {"list": []}, "annotations": {"list": []},
    "panels": panels,
}

here = os.path.dirname(os.path.abspath(__file__))
out = os.path.join(here, "evangelion-cloud-overview.json")
with open(out, "w") as f:
    json.dump(dashboard, f, indent=2)
    f.write("\n")

# Guard: every metric referenced must be one Cloud actually receives.
import re
FUNCS = {"sum", "by", "rate", "histogram_quantile", "min", "max", "time", "or", "vector",
         "clamp_min", "increase", "avg", "count"}
names = set()
for e in _used:
    # Strip quoted label values ("api") and duration literals ([5m]) before looking for metric
    # names, or the tokenizer reports them as unknown metrics.
    cleaned = re.sub(r'"[^"]*"', '', e)
    cleaned = re.sub(r"\[[0-9]+[smhd]\]", "", cleaned)
    # A metric name is an identifier NOT immediately preceded by a label-context character.
    for m in re.findall(r"(?<![\w:])[a-zA-Z_][a-zA-Z0-9_:]*", cleaned):
        if m in FUNCS:
            continue
        # label keys appear directly before = or after { / , - skip those
        if re.search(rf"[{{,]\s*{re.escape(m)}\s*=", cleaned) or re.search(rf"by\s*\([^)]*\b{re.escape(m)}\b", cleaned):
            continue
        names.add(m)
unknown = sorted(n for n in names if n not in SHIPPED)
print(f"{out}  {len([p for p in panels if p['type'] != 'row'])} panels")
print("metrics referenced:", len(names))
print("NOT shipped to Cloud:", unknown if unknown else "none - every query is safe")
