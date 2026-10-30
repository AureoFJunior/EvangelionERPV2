# What the HostGator VPS needs from AWS, and nothing else: an IAM user for the box, the bucket its
# nightly backups go to, and a lifecycle policy for the ECR repository it pulls from.
#
# A root of its own, with its own state, so it outlives the EC2 root one directory up. When the EC2
# instance and RDS are destroyed (terraformDestroy.yml), none of this is in that plan.
#
#   cd EvangelionERPV2.Web/terraform/vps-aws
#   cp terraform.tfvars.example terraform.tfvars   # and fill it in
#   terraform init && terraform apply
#   aws iam create-access-key --user-name "$(terraform output -raw vps_user_name)"
#
# The access key is created by hand, not here: a key made by terraform is stored in its state in
# plain text, and this state lives in a bucket other people can read.
terraform {
  required_version = ">= 1.10"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }

  # Same bucket as the EC2 root, a different key.
  backend "s3" {
    bucket       = "evangelion-tfstate"
    key          = "vps/terraform.tfstate"
    region       = "us-east-1"
    encrypt      = true
    use_lockfile = true
  }
}

provider "aws" {
  region = var.aws_region
}

data "aws_caller_identity" "current" {}

data "aws_ecr_repository" "app" {
  name = var.ecr_repository_name
}

locals {
  account_id = data.aws_caller_identity.current.account_id
  secret_arn = "arn:aws:secretsmanager:${var.aws_region}:${local.account_id}:secret"
}

# --- The VPS's identity ----------------------------------------------------------------------

resource "aws_iam_user" "vps" {
  name = var.vps_user_name
  tags = { Purpose = "Evangelion ERP on the HostGator VPS" }
}

# Read secrets, pull images, write backups. No push, no delete, no write anywhere else: a stolen
# key can read what the app reads, but cannot replace an image or erase a backup.
resource "aws_iam_user_policy" "vps" {
  name = "evangelion-vps"
  user = aws_iam_user.vps.name

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Sid    = "ReadAppSecrets"
        Effect = "Allow"
        Action = ["secretsmanager:GetSecretValue", "secretsmanager:DescribeSecret"]
        # Secrets Manager appends six random characters to every ARN, hence the trailing *.
        Resource = [
          "${local.secret_arn}:evangelion/prd/*",
          "${local.secret_arn}:evangelion/vps/*",
          "${local.secret_arn}:${var.aws_credentials_secret_name}-*",
        ]
      },
      {
        Sid      = "EcrLogin"
        Effect   = "Allow"
        Action   = ["ecr:GetAuthorizationToken"]
        Resource = ["*"] # The only resource this action accepts.
      },
      {
        Sid    = "EcrPull"
        Effect = "Allow"
        Action = [
          "ecr:BatchGetImage",
          "ecr:GetDownloadUrlForLayer",
          "ecr:BatchCheckLayerAvailability",
        ]
        Resource = [data.aws_ecr_repository.app.arn]
      },
      {
        Sid      = "WriteBackups"
        Effect   = "Allow"
        Action   = ["s3:PutObject", "s3:GetObject"]
        Resource = ["${aws_s3_bucket.backups.arn}/*"]
      },
      {
        Sid      = "ListBackups"
        Effect   = "Allow"
        Action   = ["s3:ListBucket"]
        Resource = [aws_s3_bucket.backups.arn]
      },
    ]
  })
}

# --- Backups ---------------------------------------------------------------------------------

resource "aws_s3_bucket" "backups" {
  bucket = var.backup_bucket_name

  lifecycle {
    # The only off-box copy of the database.
    prevent_destroy = true
  }
}

resource "aws_s3_bucket_public_access_block" "backups" {
  bucket                  = aws_s3_bucket.backups.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_server_side_encryption_configuration" "backups" {
  bucket = aws_s3_bucket.backups.id
  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

# Versioning is what stops a stolen key from destroying history: it can PutObject over a backup,
# but the version underneath survives until the noncurrent rule below expires it.
resource "aws_s3_bucket_versioning" "backups" {
  bucket = aws_s3_bucket.backups.id
  versioning_configuration {
    status = "Enabled"
  }
}

# Paths written by scripts/vps-db-backup.sh: sql/YYYY/MM/ nightly, sql-monthly/YYYY/MM/ on the 1st.
resource "aws_s3_bucket_lifecycle_configuration" "backups" {
  bucket     = aws_s3_bucket.backups.id
  depends_on = [aws_s3_bucket_versioning.backups]

  rule {
    id     = "nightly"
    status = "Enabled"
    filter {
      prefix = "sql/"
    }
    expiration {
      days = var.nightly_retention_days
    }
    noncurrent_version_expiration {
      noncurrent_days = 7
    }
  }

  rule {
    id     = "monthly"
    status = "Enabled"
    filter {
      prefix = "sql-monthly/"
    }
    expiration {
      days = var.monthly_retention_days
    }
    noncurrent_version_expiration {
      noncurrent_days = 7
    }
  }

  rule {
    id     = "abandoned-uploads"
    status = "Enabled"
    filter {}
    abort_incomplete_multipart_upload {
      days_after_initiation = 2
    }
  }
}

# --- Images ----------------------------------------------------------------------------------

# The repository itself is created by buildPushDeploy.yml ("Ensure ECR repository exists") and is
# only read here. Every build pushes four sha- images; without this the repository grows forever.
# The floating tags (latest, worker-*-latest) always sit on the newest build, so they are never
# among the expired ones.
resource "aws_ecr_lifecycle_policy" "app" {
  repository = data.aws_ecr_repository.app.name

  policy = jsonencode({
    rules = concat(
      [
        for index, prefix in ["sha-", "worker-order-sha-", "worker-email-sha-", "worker-notification-sha-"] : {
          rulePriority = index + 1
          description  = "Keep the last ${var.images_to_keep} ${prefix} builds"
          selection = {
            tagStatus     = "tagged"
            tagPrefixList = [prefix]
            countType     = "imageCountMoreThan"
            countNumber   = var.images_to_keep
          }
          action = { type = "expire" }
        }
      ],
      [
        {
          rulePriority = 10
          description  = "Drop untagged layers after a week"
          selection = {
            tagStatus   = "untagged"
            countType   = "sinceImagePushed"
            countUnit   = "days"
            countNumber = 7
          }
          action = { type = "expire" }
        }
      ]
    )
  })
}
