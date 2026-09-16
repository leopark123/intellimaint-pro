# Repository instructions for agents

## Purpose and architecture

IntelliMaint Pro is pre-1.0 industrial telemetry/condition-monitoring software. Core contains contracts;
Application contains heuristic analytics; Infrastructure contains SQLite/TimescaleDB repositories,
protocol readers, security and pipeline services. Host.Api exposes REST/SignalR, Host.Edge runs read
collectors into the TimescaleDB pipeline, and intellimaint-ui is React/TypeScript. The API-only synthetic
demo writes to SQLite. Read README.md, docs/FEATURE_MATRIX.md and docs/architecture.md before changing scope.

## Build and verify

Run from the repository root:

```sh
dotnet restore IntelliMaint.sln
dotnet build IntelliMaint.sln -c Release --no-restore
dotnet test IntelliMaint.sln -c Release --no-build
npm ci --prefix intellimaint-ui
npm run type-check --prefix intellimaint-ui
npm run lint --prefix intellimaint-ui
npm test --prefix intellimaint-ui
npm run build --prefix intellimaint-ui
node tools/check-docs.mjs
```

Use `node tools/demo/init.mjs`, then `node tools/demo/run.mjs` and a second terminal
`node tools/demo/run.mjs ui` for local synthetic checks. With the API running, execute
`node tools/demo/smoke.mjs`. Docker-enabled environments can run `docker compose config --quiet`,
`docker compose build`, `docker compose up -d` and the same smoke test. Do not delete persistent volumes
unless they are explicitly disposable test data. See docs/testing.md for fixture boundaries.

## Scope and safety

- Do not modify PLC write operations, machine-control commands, safety interlocks, emergency stops or
  protection logic without a task explicitly requesting the change. Never automatically deploy those
  changes: require qualified human review and onsite validation. Existing code is read-oriented.
- Do not connect to plant networks, real controllers, private servers or equipment as a routine test.
  Use the synthetic source or mocks. Protocol/hardware validation is separately authorized, documented lab work.
- Do not bypass OPC UA certificate checks, expose anonymous mutation endpoints, weaken role policies,
  restore default accounts or commit secrets. Never print `.env`, access/refresh tokens or private keys.
- Do not run historical data migration/seed scripts against real databases without a scoped request and backup.
- Do not rewrite history, push, publish releases, deploy or submit funding/program applications unless
  the user explicitly requests that action. Ordinary local build, test and review work is allowed.

## Contribution standards

Prefer small changes; preserve public contracts unless the task calls for a documented migration.
Use cancellation tokens for async operations, structured logging and existing naming/style. Keep code
formatting localized. Add meaningful regression tests and negative authentication cases for relevant fixes.
Do not remove failing tests or mask failures to make CI green. Record commands, results and untested
boundaries; source review is not runtime validation and synthetic tests are not hardware certification.

Use Implemented / Partial / Experimental / Planned consistently. Do not invent users, organizations,
Stars, deployments, benchmarks or prediction accuracy. Label synthetic/static UI data visibly. Legacy
docs are context only; resolve conflicts using code and current docs. Never treat a TODO as a working feature.

Agents may triage issues, propose/fix bugs, review PRs, add tests, improve docs, refactor within scope,
prepare release artifacts and review security. Keep application material a draft until independently
verifiable maintainer/usage evidence is supplied. Summarize security changes and remaining risks explicitly.
