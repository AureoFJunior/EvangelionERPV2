variable "aws_region" {
  description = "AWS region"
  default     = "us-east-1"
}

variable "vpc_id" {
  description = "VPC the instance and database live in"
  default     = "vpc-03cc585af2c953a66"
}

variable "subnets" {
  description = "Subnets. Two availability zones, because an RDS subnet group requires it even for a Single-AZ instance."
  type        = list(string)
  default     = ["subnet-0003c61110d0f854a", "subnet-053500b7cbfec64ab"]
}

# --- Phase 1: the instance -----------------------------------------------------------------

variable "instance_type" {
  description = "Graviton. The application needs far less; whisper and piper set the 4 GB."
  default     = "t4g.medium"
}

variable "instance_ami" {
  description = <<-EOT
    Ubuntu 24.04 LTS, arm64. Empty means "look it up", which is what you want unless you are
    pinning a known-good image. The AMI id differs per region and per architecture, and the
    x86_64 image will not boot on t4g.
  EOT
  default     = ""
}

variable "root_volume_size" {
  description = "GB on the root volume."
  default     = 30
}

variable "ssh_ingress_cidr" {
  description = <<-EOT
    Your address, as a /32, if you want port 22 open at all. Empty is the recommended value:
    SSM Session Manager needs no inbound rule and no key to lose.
  EOT
  default     = ""
}

# --- Phase 1: the database -----------------------------------------------------------------

variable "db_instance_class" {
  description = "db.t3.micro is the smallest class RDS offers for SQL Server Express."
  default     = "db.t3.micro"
}

variable "db_allocated_storage" {
  description = "GB. SQL Server Express caps a single database at 10 GB regardless."
  default     = 20
}

variable "db_username" {
  description = "Master user. The password is generated and stored by AWS - see manage_master_user_password in main.tf."
  default     = "evangelion_admin"
}

variable "db_backup_retention_days" {
  description = "Days of automated backups. One box and one database means this is the whole recovery story."
  default     = 7
}

# --- Phase 2: ECS --------------------------------------------------------------------------

variable "enable_ecs" {
  description = <<-EOT
    Phase 2. False keeps the ECS resources in the configuration - validated, reviewed, one
    variable from live - without creating anything. See SPEC-production-readiness.md section 9.6
    for the triggers that make this true.
  EOT
  type        = bool
  default     = false
}

variable "aws_ecr_repository_repository_url" {
  description = "ECR repository the images are pushed to."
  default     = "381492289606.dkr.ecr.us-east-1.amazonaws.com/evangelionerpv2-repository"
}

variable "aws_iam_role_name" {
  description = "ECS task execution role name."
  default     = "ecs_task_execution_role"
}

variable "aws_iam_role_arn" {
  description = "ECS task execution role ARN."
  default     = "arn:aws:iam::381492289606:role/ecs_task_execution_role"
}
