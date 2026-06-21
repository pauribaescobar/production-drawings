# ECS Windows Free Tier Checklist

## Goal

Validate whether the Windows ECS POC can be run without leaving the AWS Free Tier envelope.

## Hard truth

There is no guarantee.

The project can only stay free tier if:

* the AWS account is eligible
* the chosen instance type is free-tier eligible
* usage stays under the limit
* no extra paid networking or storage is added

## Required AWS CLI profile

Use this profile for every command:

```bash
aws --profile pau-develops
```

## Step 1 - Confirm account eligibility

```bash
aws --profile pau-develops sts get-caller-identity
```

Then open the Billing console and check Free Tier eligibility and alerts.

## Step 2 - Check free-tier eligible EC2 instance types

```bash
aws --profile pau-develops ec2 describe-instance-types \
  --filters Name=free-tier-eligible,Values=true \
  --query "InstanceTypes[*].InstanceType" \
  --output text
```

Pick a Windows-compatible instance type from the returned set, then verify it is actually usable for a Windows ECS container host.

## Step 3 - Check Windows ECS-optimized AMIs

```bash
aws --profile pau-develops ec2 describe-images \
  --filters Name=free-tier-eligible,Values=true \
  --query "Images[*].[ImageId,Name,CreationDate]" \
  --output table
```

If the AMI search is empty or unclear, use the ECS Windows AMI docs to pick the current optimized AMI for the region.

## Step 4 - Launch a minimal Windows ECS host

Use:

* the smallest viable Windows instance
* one security group
* no load balancer
* no NAT gateway
* no extra disks beyond the minimum

## Step 5 - Join the instance to ECS

Configure the host as an ECS container instance and verify it registers in the cluster.

## Step 6 - Run the reader POC

Run the `SolidEdgeCommunity.Reader` experiment on that host and verify:

* a `.dft` opens
* sheets are enumerated
* EMF files are written

## Step 7 - Stop immediately after testing

Terminate or stop the instance after each test run.

## Guardrails

* Enable AWS Free Tier alerts.
* Enable AWS Budgets alerts.
* Do not add Fargate for the Windows worker.
* Do not add an application load balancer unless the POC truly needs it.
* Do not leave the instance running overnight.

## Success criteria

The POC is acceptable if:

* it runs on a Windows ECS host
* the monthly usage stays inside the Free Tier limits
* the bill remains at zero or within your acceptable testing budget

