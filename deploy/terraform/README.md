# Terraform

Owns the AWS resources the proxy depends on (spec §10): ElastiCache (Phase 3), the S3 key ring
bucket and KMS key (Phase 3), the `/proxy/audit` CloudWatch log group with explicit retention
(Phase 4), and IAM roles for service accounts. No RDS, no Firehose.

Nothing is needed before Phase 3.
