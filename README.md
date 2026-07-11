# remarkable-fullview

An always-visible, distraction-free external brain for executive function,
built for a reMarkable 1 tablet. It shows what matters *now*, what's *next*,
and turns handwritten scribbles from the tablet's pen into structured
to-dos, shopping items, meals, and calendar entries — no WiFi required for
the board to keep working.

remarkable-fullview is opinionated about a few things:

- **Glanceable over interactive.** Every screen answers its question in
  under 3 seconds. The tablet is mostly read; heavier input happens on
  phone/web or via pen.
- **Now/Next beats calendars.** A persistent strip at the top of every
  screen shows the current time block, the next one, and time remaining —
  merged across both Work and Personal contexts regardless of which mode
  the board is showing.
- **Capture must be cheaper than remembering.** Write anything in the pen
  inbox and walk away; an AI vision pass files it into the right list.
- **Offline-first, always.** The board never blanks and never loses an
  input. Staleness is labelled, never hidden.

## Status

Early build — see [`docs/plans/implementation.md`](docs/plans/implementation.md)
for the full staged plan, and [`PROGRESS.md`](PROGRESS.md) for where the
build currently stands.

## Stack

- C#/.NET 8 everywhere: domain, rendering, API, infrastructure, and the
  on-device app.
- AWS (API Gateway + Lambda + DynamoDB), deployed via AWS CDK in C#.
- A minimal React web app for capture/management from a phone or browser.
- Anthropic's API for turning handwritten strokes into structured data.
- A reMarkable 1 (armhf Linux) as the primary always-on display.

## Repo layout

```
/src
  Fullview.Domain      shared entities + sync metadata
  Fullview.Rendering    ImageSharp screen renderer (shared device/server)
  Fullview.Api          Lambda handlers (sync, capture, auth)
  Fullview.Infra         CDK app (DynamoDB, API GW, Lambdas, S3, budgets/alarms)
  Fullview.Device        reMarkable 1 app (framebuffer render, touch input, sync)
  Fullview.Web           React SPA (capture, management, needs-review triage)
/tests                 mirrors src
/docs                  build plan, runbooks, device setup notes
```

## Building locally

Requires the .NET 8 SDK.

```
dotnet build Fullview.sln
dotnet test Fullview.sln
dotnet format Fullview.sln --verify-no-changes
```

## Deploying your own instance

Not yet documented — this section will be filled in during the
infrastructure and hardening stages of the build plan. Every deployment of
remarkable-fullview is fully independent: your own AWS account, your own Anthropic API
key, your own device. There is no shared backend and no multi-tenant mode.

## Security

No secrets or live endpoints live in this repository. See `SECURITY.md`
(added later in the build) for the full isolation guarantees and how to
report a vulnerability.

