# Windows Self-Hosted Public App

## Goal

Run the full production-drawings application on a single Windows machine while exposing only the web application publicly.

## Why this exists

The DFT conversion worker needs a Windows runtime. If the project is self-hosted on Windows, the simplest model is to colocate the app and the worker on the same host and publish only the web surface.

## Recommended shape

```text
Internet
  ↓
Public web app
  ↓
Local Windows worker
  ↓
DFT -> EMF -> annotation -> PDF
```

## Public surface

Only the web application should be reachable from outside the machine.

The worker should remain private and be invoked locally by the app through one of:

* `localhost` HTTP on a non-public port
* an internal process call
* a local queue or job runner

The preferred implementation for this repository is `localhost` HTTP because it keeps the boundary narrow, makes the request flow explicit, and can later be moved to another host without changing the browser-facing contract.

## Responsibilities

### Web app

* accept uploads
* validate the request
* create a job workspace on local disk
* parse the order PDF into the shared parser output contract
* orchestrate the processing flow
* call the worker locally
* return the generated PDF

### Windows worker

* read the job payload and job workspace
* open the `.dft`
* export EMF or a comparable vector intermediate
* apply annotations
* generate the final PDF
* write the output PDF and structured status back to disk or the local response

## Deployment contract

The single-host deployment should use a predictable job directory layout, for example:

```text
C:\production-drawings\jobs\<jobId>\
  input\order.pdf
  input\drawings.zip
  parsed-order.json
  output\generated.pdf
  logs\worker.log
```

The web app owns the browser-facing request and the final download response.
The worker owns rendering and artifact generation only.

## Request flow

```text
Browser
  ↓
Public web app
  ↓
Create job workspace
  ↓
Parse order PDF
  ↓
Call local worker
  ↓
Worker renders PDF
  ↓
Web app returns download
```

## Why this model

* simplest deployment
* no cross-host networking required for the worker
* the public attack surface stays small
* the Windows-only tooling stays isolated

## Tradeoffs

* the whole system depends on one Windows host
* horizontal scaling is not the first version
* the web app and conversion pipeline are coupled at deployment time

## When to use this

Use this model when you want:

* a public web interface
* a single Windows deployment target
* minimal moving parts
* a private worker boundary on the same host

## When not to use this

Do not use this model if you need:

* multiple workers
* separate scaling for the UI and conversion pipeline
* public access to the conversion worker itself
* a load-balanced or multi-host worker tier
