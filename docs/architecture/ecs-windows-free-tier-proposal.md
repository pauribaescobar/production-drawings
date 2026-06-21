# ECS Windows Free Tier Proposal

## Goal

Run the Solid Edge `.dft` conversion POC on AWS without using Vercel for the Windows part, while keeping the cloud cost as close to free tier as possible.

## Decision

Use **Amazon ECS on EC2 Windows**, not Fargate, for the DFT worker.

## Important

This cannot be guaranteed to stay free tier in all cases.

Free Tier depends on:

* AWS account eligibility
* active Free Tier period
* using only Free Tier-eligible resources
* staying within the published usage limits

## Why not Vercel

Vercel is a build and serverless platform. It does not provide a Windows host for running Windows CAD conversion tooling.

## Why not ECS on Fargate

Windows Fargate is supported, but it is billed per vCPU and memory, plus Windows license cost. That is not a free-tier path.

## Proposed architecture

```text
Mac dev machine
  ↓
Web UI / parser / orchestration
  ↓
ECS service endpoint
  ↓
Windows ECS task on EC2
  ↓
SolidEdgeCommunity.Reader POC
  ↓
EMF output
```

## Free-tier strategy

The only practical way to stay near free tier is:

1. Use an AWS account that is still eligible for EC2 Free Tier.
2. Launch a **Windows EC2 instance that is free-tier eligible**.
3. Join that instance to an ECS cluster as a container instance.
4. Run the POC as a Windows container or Windows process on that host.

AWS documents that EC2 Free Tier includes Windows instances for eligible accounts, and ECS documents Windows container instances on EC2.

## Important constraint

This is only free-tier friendly if the EC2 instance type and account eligibility actually qualify.

If the account is outside Free Tier eligibility, the architecture still works, but it is no longer free.

## Recommended implementation shape

### Option A, preferred

* ECS service on EC2 Windows
* ECS-optimized Windows AMI
* free-tier-eligible EC2 instance type
* one worker task for conversion

### Option B, fallback

* plain Windows EC2 instance
* Docker or service wrapper
* same reader POC

## AWS CLI profile rule

All AWS CLI commands in this project should use:

```bash
aws --profile pau-develops
```

Do not rely on the default AWS profile.

## Suggested bootstrap commands

```bash
aws --profile pau-develops sts get-caller-identity
aws --profile pau-develops ecs list-clusters
aws --profile pau-develops ec2 describe-instance-types --filters Name=free-tier-eligible,Values=true
```

## What to validate next

1. Confirm whether the AWS account is still EC2 Free Tier eligible.
2. Confirm a Windows EC2 instance type that is actually free-tier eligible.
3. Confirm the ECS-optimized Windows AMI can boot on that instance type.
4. Confirm the SolidEdgeCommunity.Reader POC runs on that Windows host.
5. Confirm EMF export works with a real sample `.dft`.

## Risks

* Windows Free Tier eligibility may not cover the exact host configuration needed for the POC.
* ECS-optimized Windows AMIs may not fit comfortably on the smallest free-tier instance.
* If the POC needs more RAM than a micro instance offers, the architecture remains valid but stops being free-tier only.

## Cost-control guardrails

* Use AWS Budgets and Free Tier alerts.
* Stop the instance when not testing.
* Avoid Fargate for the Windows worker.
* Avoid ALB/NAT gateways unless strictly necessary.
* Avoid EBS or extra storage beyond the minimum required.
