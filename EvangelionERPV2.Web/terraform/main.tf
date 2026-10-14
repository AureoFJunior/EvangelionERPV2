terraform {
  # State was local and, by 2026-09-17, absent entirely: terraform described a cluster and three
  # task definitions while managing none of them, because whatever state an earlier apply produced
  # was lost. That is also why `terraformDestroy.yml` could not have deleted the cluster - a destroy
  # against empty state destroys nothing.
  #
  # Create the bucket once by hand before the first init, then import what already exists rather
  # than letting terraform recreate it:
  #
  #   aws s3api create-bucket --bucket evangelion-tfstate --region us-east-1
  #   aws s3api put-bucket-versioning --bucket evangelion-tfstate \
  #     --versioning-configuration Status=Enabled
  #   terraform init
  backend "s3" {
    bucket       = "evangelion-tfstate"
    key          = "prd/terraform.tfstate"
    region       = "us-east-1"
    encrypt      = true
    use_lockfile = true
  }
}

provider "aws" {
  region = var.aws_region
}

# Ubuntu 24.04 arm64. Looked up rather than hard-coded: the id differs per region, and the x86_64
# image will not boot on a t4g.
data "aws_ami" "ubuntu_arm64" {
  count       = var.instance_ami == "" ? 1 : 0
  most_recent = true
  owners      = ["099720109477"] # Canonical

  filter {
    name   = "name"
    values = ["ubuntu/images/hvm-ssd-gp3/ubuntu-noble-24.04-arm64-server-*"]
  }

  filter {
    name   = "virtualization-type"
    values = ["hvm"]
  }
}

locals {
  instance_ami = var.instance_ami != "" ? var.instance_ami : one(data.aws_ami.ubuntu_arm64[*].id)
}

# --- Networking ------------------------------------------------------------------------------

# Named for tasks because it began as the ECS task group. It is now the instance's group, and its
# job is unchanged: be the thing the database trusts. See the RDS group below - membership here is
# what grants database access, which is why it has almost no ingress of its own.
#
# It replaced sg-047e646753efd8eae, the VPC default group, which allowed every protocol on every
# port from 0.0.0.0/0. Nothing was exposed only because no instance ever used it.
resource "aws_security_group" "evangelionerpv2_tasks" {
  name        = "evangelionerpv2-tasks"
  description = "Application instance. Only 80/443 from the internet; 9101 must never be opened here."
  vpc_id      = var.vpc_id

  ingress {
    description = "HTTP, and the Let's Encrypt HTTP-01 challenge"
    from_port   = 80
    to_port     = 80
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }

  ingress {
    description = "HTTPS"
    from_port   = 443
    to_port     = 443
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }

  # Absent unless you ask for it. SSM Session Manager reaches the box without an inbound rule, so
  # the default is no SSH at all rather than SSH from anywhere.
  dynamic "ingress" {
    for_each = var.ssh_ingress_cidr != "" ? [var.ssh_ingress_cidr] : []
    content {
      description = "SSH from one address"
      from_port   = 22
      to_port     = 22
      protocol    = "tcp"
      cidr_blocks = [ingress.value]
    }
  }

  # Deliberately not opened: 9101 (Prometheus, unauthenticated, carries an enterprise_id label),
  # 5672/15672, 6379, 1433. Those stay on the Docker network or inside the VPC.

  egress {
    description = "RDS, S3, Secrets Manager, Upstash, CloudAMQP, Grafana Cloud, package mirrors"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = { Name = "evangelionerpv2-tasks" }
}

# One rule, and the source is a security group rather than a CIDR so it keeps working when the
# instance is replaced. This is what lets the database stay private.
resource "aws_security_group" "evangelionerpv2_rds" {
  name        = "evangelionerpv2-rds"
  description = "SQL Server. Reachable only from the application instance."
  vpc_id      = var.vpc_id

  ingress {
    description     = "SQL Server from the application instance only"
    from_port       = 1433
    to_port         = 1433
    protocol        = "tcp"
    security_groups = [aws_security_group.evangelionerpv2_tasks.id]
  }

  tags = { Name = "evangelionerpv2-rds" }
}

# --- The instance ----------------------------------------------------------------------------

# No access keys on the box: the SDK reads these from instance metadata, which is why
# .env.ec2.example leaves AWS_PROFILE empty and sets AWS_EC2_METADATA_DISABLED=false.
resource "aws_iam_role" "app_instance" {
  name = "evangelionerpv2-instance"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "ec2.amazonaws.com" }
      Action    = "sts:AssumeRole"
    }]
  })
}

# Scoped to the prd prefix and the one bucket. Not an admin key, and not a human's.
resource "aws_iam_role_policy" "app_instance_secrets" {
  name = "evangelionerpv2-instance-secrets"
  role = aws_iam_role.app_instance.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = ["secretsmanager:GetSecretValue"]
        Resource = ["arn:aws:secretsmanager:${var.aws_region}:*:secret:evangelion/prd/*"]
      },
      {
        Effect   = "Allow"
        Action   = ["s3:GetObject", "s3:PutObject", "s3:DeleteObject"]
        Resource = ["arn:aws:s3:::evangelionerpv2images/*"]
      }
    ]
  })
}

# Session Manager. This is what makes "no port 22" practical rather than aspirational.
resource "aws_iam_role_policy_attachment" "app_instance_ssm" {
  role       = aws_iam_role.app_instance.name
  policy_arn = "arn:aws:iam::aws:policy/AmazonSSMManagedInstanceCore"
}

resource "aws_iam_instance_profile" "app_instance" {
  name = "evangelionerpv2-instance"
  role = aws_iam_role.app_instance.name
}

resource "aws_instance" "app" {
  ami                    = local.instance_ami
  instance_type          = var.instance_type
  subnet_id              = var.subnets[0]
  vpc_security_group_ids = [aws_security_group.evangelionerpv2_tasks.id]
  iam_instance_profile   = aws_iam_instance_profile.app_instance.name
  # The only public address is the reserved EIP attached below; never assign an extra ephemeral IP.
  associate_public_ip_address = false

  root_block_device {
    volume_size = var.root_volume_size
    volume_type = "gp3"
    encrypted   = true
  }

  # IMDSv2. The older endpoint hands credentials to anything that can make an outbound HTTP request
  # from the box, which is the classic path from a proxy bug to the instance role.
  metadata_options {
    http_tokens   = "required"
    http_endpoint = "enabled"
  }

  tags = { Name = "evangelion-prd" }
}

# Reserved rather than ephemeral: DNS and the database's trust both point here, and a plain public
# IP changes on stop/start.
resource "aws_eip" "app" {
  instance = aws_instance.app.id
  domain   = "vpc"
  tags     = { Name = "evangelion-prd" }
}

# --- The database ----------------------------------------------------------------------------

resource "aws_db_subnet_group" "evangelionerpv2" {
  name       = "evangelionerpv2"
  subnet_ids = var.subnets
  tags       = { Name = "evangelionerpv2" }
}

resource "aws_db_instance" "evangelionerpv2" {
  identifier     = "evangelionerpv2"
  engine         = "sqlserver-ex"
  instance_class = var.db_instance_class

  allocated_storage = var.db_allocated_storage
  storage_type      = "gp3"
  storage_encrypted = true

  username = var.db_username

  # No password in this file, in tfvars, or in state: RDS generates one and keeps it in Secrets
  # Manager. Read it from there when filling evangelion/prd/database.
  manage_master_user_password = true

  db_subnet_group_name   = aws_db_subnet_group.evangelionerpv2.name
  vpc_security_group_ids = [aws_security_group.evangelionerpv2_rds.id]

  # The reason the whole stack stayed in AWS. Do not flip this to reach the database from
  # somewhere else - add the caller to the instance security group instead.
  publicly_accessible = false

  backup_retention_period = var.db_backup_retention_days
  copy_tags_to_snapshot   = true

  # A destroy still leaves something to restore from. `terraformDestroy.yml` is a manual dispatch
  # away from anyone with write access to the repository.
  skip_final_snapshot       = false
  final_snapshot_identifier = "evangelionerpv2-final"

  auto_minor_version_upgrade = true
  apply_immediately          = false

  lifecycle {
    # The second guard, and the one that does not depend on anyone reading the first. Removing this
    # to run a destroy should feel like the deliberate act it is.
    prevent_destroy = true
  }

  tags = { Name = "evangelionerpv2" }
}

# --- Backups ---------------------------------------------------------------------------------

resource "aws_iam_role" "dlm" {
  name = "evangelionerpv2-dlm"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "dlm.amazonaws.com" }
      Action    = "sts:AssumeRole"
    }]
  })
}

resource "aws_iam_role_policy_attachment" "dlm" {
  role       = aws_iam_role.dlm.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSDataLifecycleManagerServiceRole"
}

# RDS keeps its own backups; this covers the other half - the box, its compose file and its .env.
# One instance with no snapshot is the one genuinely unacceptable version of this plan.
resource "aws_dlm_lifecycle_policy" "instance_snapshots" {
  description        = "Nightly snapshot of the application instance"
  execution_role_arn = aws_iam_role.dlm.arn
  state              = "ENABLED"

  policy_details {
    resource_types = ["INSTANCE"]
    target_tags    = { Name = "evangelion-prd" }

    schedule {
      name = "nightly"

      create_rule {
        interval      = 24
        interval_unit = "HOURS"
        times         = ["05:00"]
      }

      retain_rule {
        count = 7
      }

      copy_tags = true
    }
  }
}

# --- Outputs ---------------------------------------------------------------------------------

output "instance_public_ip" {
  description = "Point app.evangelionerp.com here."
  value       = aws_eip.app.public_ip
}

output "database_endpoint" {
  description = "Host for the evangelion/prd/database secret."
  value       = aws_db_instance.evangelionerpv2.address
}

output "database_password_secret_arn" {
  description = "Secrets Manager entry RDS generated for the master password."
  value       = aws_db_instance.evangelionerpv2.master_user_secret[0].secret_arn
}
