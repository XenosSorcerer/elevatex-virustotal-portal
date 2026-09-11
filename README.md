# ElevateX — Suspicious File Submission & VirusTotal Analysis Portal

An internal Blazor Server app for security analysts to submit suspicious files for VirusTotal
analysis, track scan status, and review results — built to survive the free public API's real
constraints (4 req/min, 500 req/day, bursty submissions, resubmitted samples).

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A free [VirusTotal](https://www.virustotal.com/gui/join-us) account and API key (optional — see below)

## Getting started

```bash
git clone <repo-url>
cd elevatex-assessment
dotnet run --project src/ElevateX.Portal
```

The SQLite database self-provisions on first run (WAL mode enabled) — no SQL scripts, no
migrations, no manual setup. Open the URL printed in the console (`http://localhost:5005` by
default).

## Supplying a VirusTotal API key

Config key: `VirusTotal:ApiKey`. Pick one:

**User secrets (preferred — never committed).** The project is already `user-secrets init`'d:

```bash
dotnet user-secrets set "VirusTotal:ApiKey" "your-real-key-here" --project src/ElevateX.Portal
```

**`appsettings.Development.json`** (gitignored) — add under a `"VirusTotal"` section:

```json
{ "VirusTotal": { "ApiKey": "your-real-key-here" } }
```

**Environment variable** — no file needed:

```bash
$env:VirusTotal__ApiKey = "your-real-key-here"   # PowerShell
```

`src/ElevateX.Portal/appsettings.json` is tracked and intentionally ships with a placeholder —
please don't put a real key there.

**Running with no key at all:** the app still starts and is fully usable. Every submission gets
hashed, queued, and dispatched normally; VirusTotal calls will 401, and — because the retry
policy only retries genuinely transient failures — those submissions fail fast into a visible
`Failed` state with a reason, rather than retrying or hanging. It's a reasonable way to click
through Submit → Submissions → Dashboard without a key.

Other tunables live in the same `VirusTotal` config section (`RateLimitRequestsPerMinute`,
`DailyRequestCap`, `MaxPollAttempts`, `MaxTransientRetries`) — defaults match the free tier.

## Running the tests

```bash
dotnet test tests/ElevateX.Tests/ElevateX.Tests.csproj
```

24 tests, fully offline, no API key required, finishes in well under 15 seconds (the single
`WebApplicationFactory` integration test accounts for most of that). Covers the rate limiter,
the retry/transient-failure classification, the scan state machine, SHA-256 deduplication, and
one end-to-end upload → pipeline → completed flow. See `DECISIONS.md` for what was deliberately
left untested and why.

## What's in the app

| Page | Route | |
|---|---|---|
| Submit Sample | `/` | Upload with source, reason for suspicion, priority, target department; OpSec warning that VirusTotal submissions join its public corpus |
| Submissions | `/submissions` | Live-updating list — file, SHA-256, status, detection ratio; detail view with full VT report link; `.xlsx` export |
| Insights Dashboard | `/dashboard` | Submission volume, analysis-status breakdown, top sources with flagged counts, detection severity, live API quota usage |

## Architecture, in brief

- **Blazor Server** — one C# codebase, real-time UI over the existing SignalR circuit, no npm.
- **SQLite + EF Core**, self-provisioned via `EnsureCreated` + WAL.
- **Hash-first two-phase scan pipeline** — a VirusTotal hash lookup resolves already-known files
  in one call; upload + poll only happen for genuinely unseen files.
- **Database-polled background dispatcher** — the DB rows are the queue; nothing is lost on
  restart, and orphaned in-progress jobs are reclaimed at startup.
- **Per-call rate limiting** via an `HttpClient` `DelegatingHandler`, plus a persisted daily
  quota guard that pauses dispatch at the 500/day cap.
- **Bounded retries** on transient failures only (429/408/5xx/timeout), honouring `Retry-After`;
  everything else fails fast with a reason.

Full rationale, rejected alternatives, scale honesty, and AI-usage notes are in
[`DECISIONS.md`](DECISIONS.md).

## Project layout

```
src/ElevateX.Core/       domain entities, EF DbContext, VirusTotal client, scan pipeline,
                          background dispatcher, rate limiter, analytics, export
src/ElevateX.Portal/     Blazor Server UI (Submit / Submissions / Dashboard)
tests/ElevateX.Tests/    Unit/ + Integration/ — see "Running the tests" above
docs/                    the original assessment brief and this project's implementation plans
```

## Assumptions & notes

- No authentication or user management — a trusted internal tool, per the assessment brief.
- Single-instance assumptions: the rate limiter and quota guard are in-process, so running two
  instances against the same VirusTotal key would double the effective request rate.
- Uploaded binaries are kept under `uploads/` (gitignored), keyed by SHA-256, with no automatic
  retention sweep.
- Files over VirusTotal's 32 MB standard upload limit are hash-looked-up only, never uploaded.
- `elevatex_portal.db*` and `uploads/` are gitignored; delete them locally to reset all state.

## Favourite Punk / Emo / Hard-Rock band

Skunk Anansie. 🎸
