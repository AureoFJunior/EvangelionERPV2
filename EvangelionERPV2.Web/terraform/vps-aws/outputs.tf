output "vps_user_name" {
  description = "Create its access key with: aws iam create-access-key --user-name <this>"
  value       = aws_iam_user.vps.name
}

output "backup_s3_uri" {
  description = "BACKUP_S3_URI for the VPS .env."
  value       = "s3://${aws_s3_bucket.backups.bucket}/sql"
}

output "ecr_repository_uri" {
  description = "The repository the VPS pulls from; WEB_IMAGE and the worker images live under it."
  value       = data.aws_ecr_repository.app.repository_url
}
