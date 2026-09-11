\# Architectural Decisions Log



\## A brief note



\*on my approach to this build: planning took up the majority of my time, arguably more than it should have. Because I wasn't entirely comfortable with tools like Claude Code, Codex, or Antigravity yet, I initially tried building the project using my custom Hermes agent. It resulted in significant drift between the architecture and the actual output, so I wiped the slate clean and restarted in Antigravity.\*



\*Due to starting over and a few everyday disruptions, getting this submission over the finish line took longer than I would have liked. I aimed to balance two main constraints: keeping execution to \*\*4-6 focused hours\*\* and \*\*stopping at a sensible point without gold-plating it\*\*. Ultimately, I prioritized delivering a solid, working prototype.\*

\*I will admit to a moment of panic. The embarrassment of missing the 4–6 hour target and restarting the project from scratch in different IDEs almost convinced me to pull out of the assessment entirely to avoid failing the live session.\*



\*Rita called me on the 10th and gave me the reality check I needed. She helped me realize I was overthinking everything: I had a working prototype I was proud of, and walking away would mean forfeiting the opportunity to prove I am the best fit for this role. I decided to tie up loose ends, make sure it works, add this entry, and to submit.\*



\*With that said, here are the core decisions I made and why:\*



\_\_\_



1\. `ASP.NET Core Blazor Server` — \*\*UI \& Component Layer\*\*

&#x20;  \* \*\*Why:\*\* Provides real-time interactive UI over SignalR without the complexity of managing separate REST endpoints, leveraging full C# type safety across the stack. Using `Razor Pages` requires custom code for live page updates, and an SPA using `React` or `Angular` necessitates Node or npm, breaking the zero-config rule.



2\. `SQLite` with `Entity Framework` — \*\*Local Persistence\*\*

&#x20;  \* \*\*Why:\*\*  Zero-config first run (`dotnet run`); strong relational schema separating `Submission` events from `FileAnalysis` scan records (FR-08); expressive LINQ aggregations for dashboard metrics (FR-11).



3\. "Hash-First Lookup" Two-Phase Pipeline - \*\*VirusTotal API Quota Optimization Strategy\*\*

&#x20;  \* \*\*Why:\*\* Results in doing only 2-3 API requests per upload, conserving 50–75% of daily API quota (500/day) by avoiding unnecessary uploads and polling for already-scanned files. It also speeds up queue drainage significantly.



4\. `Database-Polled Outbox` — \*\*Background Queue Architecture (Gate 4)\*\*



&#x20;  \* \*\*Why:\*\* The database row IS the queue — `Status`/`CurrentStage` fully describe outstanding work, so a crash needs no second in-memory queue to rehydrate, just a startup reclaim of `InProgress` rows. Rejected an in-memory `Channel` + startup sweeper (same rehydration problem, plus a redundant moving part) and Hangfire/Quartz (heavy dependency and schema for a scoped prototype).



5\. `ApiRateLimiter` as an `HttpClient` `DelegatingHandler` — \*\*Per-Call Rate Limiting (FR-05)\*\*



&#x20;  \* \*\*Why:\*\* An early version paced once per dispatched job, but a single unknown file makes 3 sequential VirusTotal calls (lookup, upload, poll), so a burst of new files could exceed 4 req/min. Moving the pacing into a shared handler enforces the limit on every outbound call, not every job, regardless of caller.



6\. Persisted `ApiCall` log with a `QuotaGuard` — \*\*Daily Quota Enforcement\*\*



&#x20;  \* \*\*Why:\*\* The free tier's 500/day cap needed tracking somewhere durable — an in-memory counter resets on restart, silently blowing the daily budget after a bounce. Logging one row per call also doubles as the dashboard's quota widget data source (FR-11).



7\. Transient-only Polly retry with `Retry-After` support — \*\*Failure Handling (FR-06)\*\*



&#x20;  \* \*\*Why:\*\* Retrying every HTTP failure, including a bad API key (401), wastes all 3 attempts and \~14s of backoff on something that will never succeed. Narrowed the predicate to 429/408/5xx/timeout. Everything else now fails straight to `Failed` with a reason instead.



8\. Startup reclaim of every `InProgress` row — \*\*Restart Resilience (FR-10)\*\*



&#x20;  \* \*\*Why:\*\* The dispatcher's normal poll query only re-picks up jobs parked mid-poll. A crash during the hash lookup or upload stage left a row stuck `InProgress` forever. A one-time startup sweep resets those, resuming polling if an upload had already produced an analysis ID, otherwise restarting the job from the top.



9\. Hand-rolled inline SVG/CSS charts — \*\*Dashboard Visualisation (FR-11)\*\*



&#x20;  \* \*\*Why:\*\* A charting library would need vendoring or a CDN reference; inline SVG keeps the "clone and `dotnet run`" promise with zero external assets and no CSP considerations, at the cost of hover/zoom interactivity.



10\. Component-level polling over SignalR push — \*\*Live Status Updates (FR-09)\*\*



&#x20;   \* \*\*Why:\*\* The brief explicitly allows simple polling, and a Blazor Server circuit already re-renders pushed state changes for free. A 4-second `PeriodicTimer` on the Submissions page gets live-feeling updates without opening a second real time channel alongside the one Blazor already holds.



11\. Scale Honesty — \*\*What Breaks First at 10x Submission Volume\*\*



&#x20;   \* \*\*Why:\*\* The rate limiter and quota guard are in process, so a second app instance would double the real VirusTotal request rate; `SubmissionService` buffers each whole upload in memory before hashing, real pressure under a burst of large files; and a crash mid-upload during restart-reclaim can produce a duplicate VirusTotal analysis for that file. First changes at scale: a shared rate limiter/quota store, single-pass streaming hashing, and moving off SQLite for write concurrency.



12\. With More Time — \*\*Priority List\*\*



&#x20;   \* \*\*Why:\*\* SignalR push instead of polling; unit tests for `AnalyticsService`; a scheduled `uploads/` retention sweep (files persist indefinitely today); structured logging (Serilog) with error notifications; an adaptive backoff that reacts to `Retry-After` more broadly than just 429s.



13\. AI Usage — \*\*Where It Helped, Where It Was Wrong\*\*



&#x20;   \* \*\*Why:\*\* Fastest wins were the EF model/DbContext scaffolding and the Razor form markup. Two things it got wrong and I overrode: it placed the 4-req/min rate-limit check once per dispatched job instead of once per HTTP call, letting a single new-file scan fire 3 unpaced requests; and it left an unused in-memory `Channel` queue wired into `SubmissionService` after the dispatcher had already switched to polling the database directly.



14\. Testing Boundaries (VT-03) — \*\*What Was Deliberately Not Tested\*\*



&#x20;   \* \*\*Why:\*\* Covered the logic that would actually catch a regression — rate-limiter spacing, transient-vs-fatal retry classification, the scan state machine, SHA-256 deduplication, and one end-to-end upload-to-completed flow, all offline with a faked VirusTotal client. Deliberately skipped: Blazor markup (low regression value, churns on every UI tweak), the real VirusTotal HTTP contract (would break the offline requirement), ClosedXML's byte output and the SVG chart rendering (library/rendering boundaries), and the UTC-midnight quota reset (wall-clock dependent, and the counting logic itself is trivial and already covered).

