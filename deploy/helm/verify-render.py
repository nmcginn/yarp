#!/usr/bin/env python3
"""Assert invariants on rendered Helm output (spec §2, §10).

Usage: helm template proxy deploy/helm/proxy [--set ...] | deploy/helm/verify-render.py

Checks the rendered manifests, not the chart values, because the manifests are what reaches the
cluster. Port 8081 must never be reachable through the public ALB (security control 7).
"""
import sys

import yaml

STATUS_PORT = 8081
DATA_PLANE_PORT = 8080


def fail(message):
    print(f"verify-render: FAIL: {message}", file=sys.stderr)
    sys.exit(1)


docs = [d for d in yaml.safe_load_all(sys.stdin) if d]
by_kind = {}
for doc in docs:
    by_kind.setdefault(doc["kind"], []).append(doc)

deployments = by_kind.get("Deployment", [])
if len(deployments) != 1:
    fail(f"expected exactly one Deployment, found {len(deployments)}")
deployment = deployments[0]
spec = deployment["spec"]

rolling = spec.get("strategy", {}).get("rollingUpdate", {})
if rolling.get("maxUnavailable") != 0:
    fail("Deployment must roll with maxUnavailable: 0 (spec §10)")

annotations = spec["template"]["metadata"].get("annotations", {})
if "checksum/routes" not in annotations:
    fail("pod template must carry the checksum/routes annotation (spec §4.3)")
if "checksum/environment" not in annotations:
    fail("pod template must carry the checksum/environment annotation")

containers = spec["template"]["spec"]["containers"]
container = containers[0]
ports = {p["name"]: p["containerPort"] for p in container.get("ports", [])}
if ports.get("status") != STATUS_PORT or ports.get("data-plane") != DATA_PLANE_PORT:
    fail(f"unexpected container ports: {ports}")

for probe in ("livenessProbe", "readinessProbe"):
    port = container[probe]["httpGet"]["port"]
    if port not in ("ops", 8082):
        fail(f"{probe} must target the ops listener, got {port}")
if container["livenessProbe"]["httpGet"]["path"] != "/health/live":
    fail("livenessProbe must use /health/live (spec §8.3)")
if container["readinessProbe"]["httpGet"]["path"] != "/health/ready":
    fail("readinessProbe must use /health/ready (spec §8.3)")

pdbs = by_kind.get("PodDisruptionBudget", [])
if len(pdbs) != 1 or pdbs[0]["spec"].get("minAvailable") != 2:
    fail("expected one PodDisruptionBudget with minAvailable: 2 (spec §10)")

hpas = by_kind.get("HorizontalPodAutoscaler", [])
if hpas:
    if hpas[0]["spec"]["minReplicas"] < 3:
        fail("HPA minReplicas must be at least 3 (spec §10)")
elif spec.get("replicas", 0) < 3:
    fail("Deployment must have at least 3 replicas (spec §10)")

# Security control 7: only the data plane Service may be an Ingress/ALB backend.
status_services = set()
data_plane_services = set()
for svc in by_kind.get("Service", []):
    svc_ports = svc["spec"].get("ports", [])
    names = {(p.get("targetPort"), p.get("port")) for p in svc_ports}
    if any(tp in ("status", STATUS_PORT) or port == STATUS_PORT for tp, port in names):
        status_services.add(svc["metadata"]["name"])
        if svc["spec"].get("type", "ClusterIP") != "ClusterIP":
            fail(f"status Service {svc['metadata']['name']} must be ClusterIP, got {svc['spec'].get('type')}")
    else:
        data_plane_services.add(svc["metadata"]["name"])

for ingress in by_kind.get("Ingress", []):
    for rule in ingress["spec"].get("rules", []):
        for path in rule.get("http", {}).get("paths", []):
            backend = path["backend"]["service"]
            name = backend["name"]
            port = backend.get("port", {})
            if name in status_services:
                fail(f"Ingress {ingress['metadata']['name']} targets the status Service {name} (security control 7)")
            if port.get("number") == STATUS_PORT or port.get("name") == "status":
                fail(f"Ingress {ingress['metadata']['name']} targets port 8081 (security control 7)")
            if name not in data_plane_services:
                fail(f"Ingress {ingress['metadata']['name']} targets unknown Service {name}")

for tgb in by_kind.get("TargetGroupBinding", []):
    ref = tgb["spec"]["serviceRef"]
    if ref["name"] in status_services or ref.get("port") in (STATUS_PORT, "status"):
        fail("TargetGroupBinding targets the status listener (security control 7)")

print(f"verify-render: OK ({len(docs)} manifests; status Services: {sorted(status_services)}; "
      f"ALB-eligible Services: {sorted(data_plane_services)})")
