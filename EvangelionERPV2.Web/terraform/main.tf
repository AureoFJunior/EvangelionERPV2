provider "aws" {
  region = var.aws_region
}

# Create an ECS cluster
resource "aws_ecs_cluster" "evangelionerpv2_cluster" {
  name = "evangelionerpv2-cluster"
}

# Attach the AmazonECSTaskExecutionRolePolicy policy to the IAM role
resource "aws_iam_role_policy_attachment" "ecs_task_execution_attachment" {
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
  role       = var.aws_iam_role_name
}

# Create an ECS task definition
resource "aws_ecs_task_definition" "evangelionerpv2_task_definition" {
  family                   = "evangelionerpv2-task-family"
  requires_compatibilities = ["FARGATE"]
  cpu                      = "256"
  memory                   = "512"
  
  execution_role_arn = aws_iam_role.ecs_task_execution_role.arn

  container_definitions = jsonencode([{
    name  = "evangelionerpv2-container"
    image = "${aws_ecr_repository.evangelionerpv2_repository.repository_url}:latest"
    cpu   = 256
    memory = 512
    networkMode = "awsvpc"
    networkConfiguration = {
      subnets = ["subnet-0003c61110d0f854a", "subnet-053500b7cbfec64ab"]
      securityGroups = ["sg-047e646753efd8eae"]
    }
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