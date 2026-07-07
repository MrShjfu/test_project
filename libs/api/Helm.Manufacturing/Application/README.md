# Application

Application services for the Manufacturing module: they orchestrate this module's domain, expose
synchronous reads to other modules via the `Helm.Manufacturing.Contracts` interfaces (batch-first,
`AsNoTracking`, DTOs never EF entities — ADR-001/004/009), and are the only place cross-module
reads and write orchestration live. Endpoints in `Api/` call these services rather than
duplicating query logic.
