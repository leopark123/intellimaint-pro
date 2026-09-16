# Installation

## Local, no hardware

Install Git, a .NET 8.0.4xx SDK and Node.js 22.12 or newer. From the repository root:

```sh
node tools/demo/init.mjs
dotnet restore IntelliMaint.sln
npm ci --prefix intellimaint-ui
node tools/demo/run.mjs
```

In another terminal, run `node tools/demo/run.mjs ui`. UI: `http://localhost:3000`.
Credentials are generated into the ignored root `.env`. API: `http://localhost:5000`.
Run `node tools/demo/smoke.mjs` while it is running. Stop with Ctrl+C.
The SQLite file resolves relative to the API content root; the helper uses `data/demo.db`.
No system service is installed. Remove or archive that dedicated demo file only after stopping the API
if you deliberately want to discard demo data. Existing databases are never silently reset.

## Docker demo

Requires Docker Engine/Desktop running Linux containers and Compose v2. After creating `.env`:

```sh
docker compose config --quiet
docker compose up --build
```

UI: `http://localhost:8080`; API: `http://localhost:5000`. Only loopback ports are published.
Use `docker compose down` to stop; the named volume is retained. Do not use this Development stack
as a public production deployment. Actual container execution status is in [readiness](OSS_READINESS_REPORT.md).

## Optional TimescaleDB and standalone Edge

This is a Partial path awaiting clean-container validation, not the recommended first demo.
Copy `docker/.env.example` to `docker/.env` and set all required secrets. Validate configuration:

```sh
docker compose --env-file docker/.env -f docker/docker-compose.yml config --quiet
docker compose --env-file docker/.env -f docker/docker-compose.yml up --build
```

The separate stack publishes UI 8080, API 5001 and PostgreSQL 5432 on loopback. Do not run both stacks
on the same ports simultaneously. SQL files 01–06 are mounted individually; optional 07/08 demo and
site-specific seeds are no longer applied automatically. Initialization scripts run only for a fresh
PostgreSQL volume. Back up an existing database and review migrations rather than recreating it.

For Edge, provide `ConnectionStrings__TimescaleDb`, `Edge__ApiBaseUrl=http://localhost:5001`, and matching
`Edge__ApiKey` in API and Edge environments; explicitly configure/enable a collector. Run:

```sh
dotnet run --project src/Host.Edge
```

Use an isolated lab and the [protocol guide](protocols.md). Production TLS, trusted proxy handling,
backup/restore and availability validation are not supplied by the local demo.
