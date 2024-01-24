variable "aws_region" {
	description = "AWS region"
	default = "us-east-1"
}

variable "aws_ecr_repository_id" {
	description = "AWS ECR repository ID"
	default = "381492289606"
}
variable "aws_ecr_repository_repository_url" {
	description = "AWS ECR repository URL"
	default = "381492289606.dkr.ecr.us-east-1.amazonaws.com/evangelionerpv2-repository"
}

variable "aws_iam_role_arn" {
	description = "AWS IAM Role ARN"
	default = "arn:aws:iam::381492289606:role/ecs_task_execution_role"
}