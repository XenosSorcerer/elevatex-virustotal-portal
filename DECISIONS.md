\# Architectural Decisions Log



\## A brief note 



\*on my approach to this build: planning took up the majority of my time, arguably more than it should have. Because I wasn't entirely comfortable with tools like Claude Code, Codex, or Antigravity yet, I initially tried building the project using my custom Hermes agent. It resulted in significant drift between the architecture and the actual output, so I wiped the slate clean and restarted in Antigravity.\*



\*Due to starting over and a few everyday disruptions, getting this submission over the finish line took longer than I would have liked. I aimed to balance two main constraints: keeping execution to \*\*4-6 focused hours\*\* and \*\*stopping at a sensible point without gold-plating it\*\*. Ultimately, I prioritized delivering a solid, working prototype and stopping where it made sense.\*



\*With that said, here are the core decisions I made and why:\*



\_\_\_



1\. `ASP.NET Core Blazor Server` — \*\*UI \& Component Layer\*\*

&#x20;  \* \*\*Why:\*\* Provides real-time interactive UI over SignalR without the complexity of managing separate REST endpoints, leveraging full C# type safety across the stack. Using `Razor Pages` requires custom code for live page updates, and an SPA using `React` or `Angular` necessitates Node or npm, breaking the zero-config rule.





2\. `SQLite` with `Entity Framework` — \*\*Local Persistence\*\*

&#x20;  \* \*\*Why:\*\*  Zero-config first run (`dotnet run`); strong relational schema separating `Submission` events from `FileAnalysis` scan records (FR-08); expressive LINQ aggregations for dashboard metrics (FR-11).

