variable "aws_region" {
  description = "Where the secrets, the ECR repository and the backup bucket live. The app's Secrets Manager client is hard-coded to us-east-1."
  default     = "us-east-1"
}

variable "ecr_repository_name" {
  description = "The repository buildPushDeploy.yml pushes to (the ECR_EVANGELIONERPV2_REPOSITORY secret)."
  type        = string
}

variable "backup_bucket_name" {
  description = "Globally unique name for the SQL backup bucket. Goes into BACKUP_S3_URI on the VPS as s3://<name>/sql."
  type        = string
}

variable "vps_user_name" {
  description = "IAM user whose key lives in /opt/evangelion/.docker/aws/credentials on the VPS."
  default     = "evangelion-vps"
}

variable "aws_credentials_secret_name" {
  description = "The secret holding the S3 access key the app uses (EVA_AWS_SECRET_NAME)."
  default     = "AWSCredentials"
}

variable "nightly_retention_days" {
  description = "Days a nightly backup is kept."
  default     = 14
}

variable "monthly_retention_days" {
  description = "Days the first-of-the-month backup is kept."
  default     = 365
}

variable "images_to_keep" {
  description = "sha- builds kept per image in ECR. Each one is a release vps-deploy.sh can roll back to."
  default     = 15
}
