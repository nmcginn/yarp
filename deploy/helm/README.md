# Helm chart

`proxy/` deploys the proxy: one Deployment with three container ports, a data plane Service (the
only thing the ALB may target), an internal ClusterIP Service for the status UI, a PDB, an HPA, and
two ConfigMaps rendered from the repository's configuration.

## Rendering

Route and environment files are copied into the chart first — Helm can only read files inside the
chart directory:

```bash
deploy/helm/stage-config.sh dev
helm template proxy deploy/helm/proxy --set image.tag=$(git rev-parse --short HEAD) \
  | deploy/helm/verify-render.py
```

`verify-render.py` checks the rendered manifests (not the values) for the invariants of spec §2 and
§10: three replicas or more, `maxUnavailable: 0`, PDB `minAvailable: 2`, the checksum annotation,
probes on the ops listener, and — the one that matters most — that no Ingress or TargetGroupBinding
references port 8081.

## Config-only changes

The pod template carries `checksum/routes` and `checksum/environment` annotations. A change to any
route file changes the checksum and rolls the pods; there is no hot reload
([ADR 0002](../../docs/decisions/0002-rolling-restart-over-hot-reload.md)). A pod that receives
invalid configuration exits before it listens, never becomes ready, and stalls the rollout with the
old pods still serving.

## Terraform

`../terraform/` will own ElastiCache, the S3 key ring bucket, the KMS key, the `/proxy/audit` log
group with explicit retention, and IAM roles for service accounts (spec §10). Nothing is needed
before Phase 3.
