# 08 - Windows Worker Productionization

## Goal

Convert the isolated `SolidEdgeCommunity.Reader` experiment into a production-ready Windows conversion worker that can be called from the web application.

## Why this exists

The current experiment proves the likely DFT-to-EMF path, but it is still a scaffold. The project now needs a real worker boundary that the web app can invoke.

## Inputs

* `experiments/solidedge-reader-poc/`
* `docs/architecture/windows-self-hosted-public-app.md`
* `TECH_SPEC.md`

## Scope

* turn the experiment into a reusable worker module or service
* define a stable input/output contract
* implement local invocation from the web app, using a private local boundary
* support DFT input and generated artifact output
* preserve the possibility of moving the worker out of process later

## Expected responsibilities

### Worker

* read one or more `.dft` files
* export EMF or the chosen vector intermediate
* apply annotations
* produce the final PDF
* return structured success/failure data

### Integration boundary

* accept a job payload from the web app
* emit progress and final status
* write generated artifacts to a known output location
* keep the worker private to the host
* avoid exposing a public API surface

## Implementation notes

* keep the worker Windows-only
* do not expose it as a broad public API
* keep file-based or `localhost`-based invocation simple
* keep the code path replaceable if the worker later moves to another host
* align the worker output paths with the single-host job workspace documented in the deployment task

## Acceptance criteria

* the worker can be called from the web app
* the worker no longer exists only as a toy experiment
* the generated output path and error handling are defined
* the worker can run on a single Windows host with the app
* the worker contract matches the deployment model in `TECH_SPEC.md`
