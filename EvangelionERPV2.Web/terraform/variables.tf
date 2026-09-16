variable "aws_region" {
  description = "AWS region"
  default     = "us-east-1"
}

variable "aws_ecr_repository_repository_url" {
  description = "AWS ECR repository URL"
  default     = "381492289606.dkr.ecr.us-east-1.amazonaws.com/evangelionerpv2-repository"
}

variable "aws_iam_role_name" {
  description = "AWS IAM Role name"
  default     = "ecs_task_execution_role"
}

variable "aws_iam_role_arn" {
  description = "AWS IAM Role ARN"
  default     = "arn:aws:iam::381492289606:role/ecs_task_execution_role"
}

variable "vpc_id" {
  description = "VPC the ECS tasks run in"
  default     = "vpc-03cc585af2c953a66"
}

variable "task_subnets" {
  description = "Subnets for the awsvpc task ENIs"
  type        = list(string)
  default     = ["subnet-0003c61110d0f854a", "subnet-053500b7cbfec64ab"]
}

