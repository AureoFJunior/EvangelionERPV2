provider "aws" {
  region = var.aws_region
}

# Create an ECS cluster
resource "aws_ecs_cluster" "evangelionerpv2_cluster" {
  name = "evangelionerpv2-cluster"
}

# Security group for the task ENIs.
#
# This exists because the alternative was the VPC's default group, which main.tf used to point at
# (sg-047e646753efd8eae). That group allows ALL protocols on ALL ports from 0.0.0.0/0 - verified
# 2026-09-16 - so a task launched with it publishes every port it listens on to the internet. The
# application listens on 8080 and, since the observability work, on 9101: an unauthenticated
# Prometheus endpoint whose series carry an enterprise_id label.
#
# Nothing was exposed at the time of writing only because the cluster did not exist and the group
# was attached to no interface. That is luck, not a control.
#
# Ingress is empty on purpose. Nothing reaches a task directly; a load balancer in front adds its
# own rule referencing this group as a source. 9101 must never appear here - the scraper shares the
# task's network namespace (see the agent sidecar in the readiness spec) and does not cross the ENI.
resource "aws_security_group" "evangelionerpv2_tasks" {
  name        = "evangelionerpv2-tasks"
  description = "ECS task ENIs. No direct ingress; metrics port 9101 must never be opened here."
  vpc_id      = var.vpc_id

  # Outbound is unrestricted: the tasks reach SQL Server, Redis, the broker, AWS Secrets Manager
  # and Grafana Cloud. Narrowing this is worthwhile but needs those endpoints pinned down first.
  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = {
    Name = "evangelionerpv2-tasks"
  }
}

# Attach the AmazonECSTaskExecutionRolePolicy policy to the IAM role
resource "aws_iam_role_policy_attachment" "ecs_task_execution_attachment" {
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
  role       = var.aws_iam_role_name
}

# Create an ECS task definition
resource "aws_ecs_task_definition" "evangelionerpv2_task_definition" {
  family                   = "evangelionerpv2-task-family"
  network_mode             = "awsvpc"
  requires_compatibilities = ["FARGATE"]
  cpu                      = "256"
  memory                   = "1024"

  execution_role_arn = var.aws_iam_role_arn

  container_definitions = jsonencode([{
    name   = "evangelionerpv2-container"
    image  = "${var.aws_ecr_repository_repository_url}:latest"
    cpu    = 256
    memory = 1024
  }])
}

# Worker task definition (Email)
resource "aws_ecs_task_definition" "evangelionerpv2_worker_email_task_definition" {
  family                   = "evangelionerpv2-worker-email-task-family"
  network_mode             = "awsvpc"
  requires_compatibilities = ["FARGATE"]
  cpu                      = "256"
  memory                   = "1024"

  execution_role_arn = var.aws_iam_role_arn

  container_definitions = jsonencode([{
    name   = "evangelionerpv2-worker-email"
    image  = "${var.aws_ecr_repository_repository_url}:worker-email-latest"
    cpu    = 256
    memory = 1024
  }])
}

# Worker task definition (Order)
resource "aws_ecs_task_definition" "evangelionerpv2_worker_order_task_definition" {
  family                   = "evangelionerpv2-worker-order-task-family"
  network_mode             = "awsvpc"
  requires_compatibilities = ["FARGATE"]
  cpu                      = "256"
  memory                   = "1024"

  execution_role_arn = var.aws_iam_role_arn

  container_definitions = jsonencode([{
    name   = "evangelionerpv2-worker-order"
    image  = "${var.aws_ecr_repository_repository_url}:worker-order-latest"
    cpu    = 256
    memory = 1024
  }])
}
