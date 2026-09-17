# Phase 2. Nothing here is created while var.enable_ecs is false.
#
# Kept rather than deleted, and kept here rather than in a parked directory, so it stays validated
# and reviewed instead of rotting. Triggers for flipping the variable are in
# docs/engineering/SPEC-production-readiness.md section 9.6 - roughly: more than about five
# enterprises, deploy downtime that stops being acceptable, or a second engineer who should not
# need to know how to reach one box.
#
# What is still missing before this is a deployment rather than a shape: a service per task
# definition, an ALB with a target group and listener, and network_configuration pointing at
# aws_security_group.evangelionerpv2_tasks. Redis and RabbitMQ would also move to ElastiCache and
# Amazon MQ, because two containers on one box do not survive becoming several tasks - the hosting
# section keeps both addressed through Secrets Manager so that move is a value change, not a code
# change.

resource "aws_ecs_cluster" "evangelionerpv2_cluster" {
  count = var.enable_ecs ? 1 : 0
  name  = "evangelionerpv2-cluster"
}

resource "aws_iam_role_policy_attachment" "ecs_task_execution_attachment" {
  count      = var.enable_ecs ? 1 : 0
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
  role       = var.aws_iam_role_name
}

resource "aws_ecs_task_definition" "evangelionerpv2_task_definition" {
  count                    = var.enable_ecs ? 1 : 0
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

resource "aws_ecs_task_definition" "evangelionerpv2_worker_email_task_definition" {
  count                    = var.enable_ecs ? 1 : 0
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

resource "aws_ecs_task_definition" "evangelionerpv2_worker_order_task_definition" {
  count                    = var.enable_ecs ? 1 : 0
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
