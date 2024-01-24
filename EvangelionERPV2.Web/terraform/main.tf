provider "aws" {
  region = var.aws_region
}

# Create an Amazon ECR repository
resource "aws_ecr_repository" "evangelionerpv2_repository" {
  name = "evangelionerpv2-repository"
}

# Attach the AmazonECSTaskExecutionRolePolicy policy to the IAM role
resource "aws_iam_role_policy_attachment" "ecs_task_execution_attachment" {
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
  role       = var.aws_ecr_repository_id
}

# Create an ECS task definition
resource "aws_ecs_task_definition" "evangelionerpv2_task_definition" {
  family                   = "evangelionerpv2-task-family"
  network_mode             = "awsvpc"
  requires_compatibilities = ["FARGATE"]
  cpu                      = "256"
  memory                   = "512"
  
  execution_role_arn = var.aws_iam_role_arn

  container_definitions = jsonencode([{
    name  = "evangelionerpv2-container"
    image = "${var.aws_ecr_repository_repository_url}:latest"
    cpu   = 256
    memory = 512
  }])
}

# Create an ECS service
resource "aws_ecs_service" "evangelionerpv2_service" {
  name            = "evangelionerpv2-service"
  cluster         = aws_ecs_cluster.evangelionerpv2_cluster.id
  task_definition = aws_ecs_task_definition.evangelionerpv2_task_definition.arn
  launch_type     = "FARGATE"
  desired_count   = 1
}