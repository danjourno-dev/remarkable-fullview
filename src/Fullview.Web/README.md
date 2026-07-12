# Fullview.Web

React SPA (Vite + TypeScript) for fast add/edit of all data, needs-review triage,
recipe editor, and device status. Talks to the same `POST /sync` endpoint the device
uses (B5) — same outbox/cursor/LWW model, just running in a browser tab instead of
on-device.

## Local dev

```
npm install
cp .env.example .env.local   # fill in VITE_API_BASE_URL / VITE_API_KEY
npm run dev
```

`VITE_API_KEY` ships the shared single-user v1 API key to the browser bundle — see
`docs/device-setup.md`'s "API authentication" section for why that's an accepted
v1 tradeoff (Cognito is v2).

## Build / deploy

```
npm run build     # outputs dist/
npm run lint
npm test
```

CI (`.github/workflows/cd-web.yml`) builds `dist/` and syncs it to the S3 bucket
behind CloudFront on every push to `main` that touches this directory.
