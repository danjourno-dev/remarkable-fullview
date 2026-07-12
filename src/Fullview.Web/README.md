# Fullview.Web

React SPA (Vite + TypeScript) for fast add/edit of all data, needs-review triage,
recipe editor, and device status. Talks to the same `POST /sync` endpoint the device
uses (B5) — same outbox/cursor/LWW model, just running in a browser tab instead of
on-device.

## Local dev

```
npm install
cp .env.example .env.local   # fill in VITE_API_BASE_URL
npm run dev
```

The API key is never baked into the build — a public CloudFront-hosted SPA can't
keep a build-time secret out of the JS it ships to every visitor. Instead the app
shows a login screen (`AuthGate`) that asks for the API key at runtime and stores
it in `localStorage`; see `docs/device-setup.md`'s "API authentication" section.

## Build / deploy

```
npm run build     # outputs dist/
npm run lint
npm test
```

CI (`.github/workflows/cd-web.yml`) builds `dist/` and syncs it to the S3 bucket
behind CloudFront on every push to `main` that touches this directory.
