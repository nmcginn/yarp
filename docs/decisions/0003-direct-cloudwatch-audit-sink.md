# 0003. Audit events written directly to CloudWatch Logs

**Status:** Accepted (spec §8.1)
**Date:** 2026-09-04

## Context

Compliance audit events need a durable sink with retention and access controls independent of
operational application logs. The tempting design is a pluggable sink abstraction so the
destination can change later — Kinesis, S3, a SIEM — without touching the application.

## Decision

Audit events are written directly to a dedicated CloudWatch Logs group `/proxy/audit` via
`Serilog.Sinks.AwsCloudWatch`. Application logs go to stdout and the cluster log pipeline. The two
streams are never interleaved.

One log stream per pod, named by pod name — CloudWatch throttles `PutLogEvents` per stream, and a
shared stream across replicas will throttle under load and drop events. Writes are batched, the
in-memory queue is explicitly bounded, and `proxy_audit_events_dropped_total` is emitted and alarmed
on any nonzero value.

## Consequences

- Retention and IAM read access for compliance logs are controlled separately from application logs.
- Retention is set in Terraform to whatever compliance requires, not left at a default.
- Silent loss of compliance events is made loud: the drop counter is alarmed.
- The audit event schema is not yet final — [open question 2](../open-questions.md), the highest
  schedule risk in the project, must be answered first.

## What this lets us not build

No pluggable sink abstraction. When volume justifies it, the migration path to Kinesis is a
CloudWatch Logs subscription filter on `/proxy/audit` routing to Firehose → S3 — **no application
change**. That is why direct-to-CloudWatch is the right v1 choice rather than a compromise.
