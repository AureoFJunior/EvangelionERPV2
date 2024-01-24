provider "aws" {
  region = var.aws_region
}

# Create an Amazon ECR repository
resource "aws_ecr_repository" "evangelionerpv2_repository" {
  name = "evangelionerpv2-repository"

  lifecycle {
    ignore_changes = [image_scanning_configuration, image_tag_mutability] # Skip and ignore if already exists
  }
}

# Create an ECS cluster
resource "aws_ecs_cluster" "evangelionerpv2_cluster" {
  name = "evangelionerpv2-cluster"
}

# IAM role for ECS task execution
resource "aws_iam_role" "ecs_task_execution_role" {
  name = "ecs_task_execution_role"
  
  assume_role_policy = jsonencode({
    Version = "2012-10-17",
    Statement = [
      {
        Action = "sts:AssumeRole",
        Effect = "Allow",
        Principal = {
          Service = "ecs-tasks.amazonaws.com",
        },
      },
    ],
  })
}

# Attach the AmazonECSTaskExecutionRolePolicy policy to the IAM role
resource "aws_iam_role_policy_attachment" "ecs_task_execution_attachment" {
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
  role       = aws_iam_role.ecs_task_execution_role.name
}

# Create an ECS task definition
resource "aws_ecs_task_definition" "evangelionerpv2_task_definition" {
  family                   = "evangelionerpv2-task-family"
  network_mode             = "awsvpc"
  requires_compatibilities = ["FARGATE"]
  cpu                      = "256"
  memory                   = "512"
  
  execution_role_arn = aws_iam_role.ecs_task_execution_role.arn

  container_definitions = jsonencode([{
    name  = "evangelionerpv2-container"
    image = "${aws_ecr_repository.evangelionerpv2_repository.repository_url}:latest"
    cpu   = 256
    memory = 512
  }])

   lifecycle {
    ignore_changes = [execution_role_arn, container_definitions]  # Add other attributes as needed
  }
}

# Create an ECS service
resource "aws_ecs_service" "evangelionerpv2_service" {
  name            = "evangelionerpv2-service"
  cluster         = aws_ecs_cluster.evangelionerpv2_cluster.id
  task_definition = aws_ecs_task_definition.evangelionerpv2_task_definition.arn
  launch_type     = "FARGATE"
  desired_count   = 1

  lifecycle {
    ignore_changes = [name, cluster, task_definition, launch_type, desired_count]  # Skip and ignore if already exists
  }
}