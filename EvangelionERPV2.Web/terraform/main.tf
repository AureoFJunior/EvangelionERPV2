terraform {
    provider "aws" {
      region = var.aws_region
    }

    # Create an Amazon ECR repository
    resource "aws_ecr_repository" "evangelionerpv2_repository" {
      name = "evangelionerpv2-repository"
    }

    # Create an ECS cluster
    resource "aws_ecs_cluster" "evangelionerpv2_cluster" {
      name = "evangelionerpv2-cluster"
    }

    # Create an ECS task definition
    resource "aws_ecs_task_definition" "evangelionerpv2_task_definition" {
      family                   = "evangelionerpv2-task-family"
      network_mode             = "awsvpc"
      requires_compatibilities = ["FARGATE"]
      cpu                      = "256"
      memory                   = "512"

      container_definitions = jsonencode([{
        name  = "evangelionerpv2-container"
        image = "${aws_ecr_repository.evangelionerpv2_repository.repository_url}:latest"
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
}