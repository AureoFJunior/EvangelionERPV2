variable "aws_region" {
  description = "AWS region"
}

variable "subnets" {
  description = "List of subnet IDs for ECS service"
  type        = list(string)
}

variable "security_groups" {
  description = "List of security group IDs for ECS service"
  type        = list(string)
}
