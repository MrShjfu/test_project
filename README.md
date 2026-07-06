# Helm — NTG Platform

## Quickstart
1. `docker compose up -d --wait`        # Postgres 17 + RabbitMQ 4
2. `npm ci`
3. `dotnet build Helm.sln`
4. `npx nx serve api`                   # API on http://localhost:5000
5. `npx nx serve web-internal`          # after Task 11
Auth (local): `dotnet user-jwts create --project apps/api/Helm.Host` — see docs/architecture/engineering-rules.md §5.
Architecture rules: CLAUDE.md (hard rules) + docs/.
