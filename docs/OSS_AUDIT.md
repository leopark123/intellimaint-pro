# OSS audit — baseline

Audited 2026-09-16, before remediation, at commit `759ba66`.
This is a source and configuration audit, not a penetration test or hardware certification.
All 428 tracked files (105,588 text/extracted DOCX lines) were inventoried and scanned;
entry points, service registration, protocol collectors, persistence, authentication,
UI routes, test fixtures and delivery workflows were inspected. Generated build files
and historical planning documents are included in the inventory, not treated as proof
of working capabilities. No source files were changed before this report.

## Critical

### C1 — Predictable credentials on a fresh database

- **Issue:** Public default users/passwords and signing material can be used as deployment credentials.
- **Evidence:** `src/Infrastructure/Sqlite/SchemaManager.cs:323` seeds a known admin hash;
  `docker/init-scripts/02-schema.sql:237` and `07-seed-data.sql` seed known accounts;
  `docker/docker-compose.yml:11,48` provides password/key fallbacks;
  API and Edge `appsettings.json` contain connection passwords. README and login UI display a default password.
- **Impact:** Fresh installations can expose privileged access; a public signing key permits forged tokens.
- **Recommended Fix:** Remove default seeds/fallbacks; require random JWT material and an explicit first-run
  administrator password; fail closed; remove credential examples from executable scripts. Existing deployments
  must rotate credentials and tokens. Do not rewrite Git history automatically.

### C2 — OPC UA server certificate validation bypass

- **Issue:** All certificate validation errors are accepted.
- **Evidence:** `src/Infrastructure/Protocols/OpcUa/OpcUaSessionManager.cs:89,113` sets auto-accept and `e.Accept = true`.
- **Impact:** The client can trust an unintended server, compromising telemetry integrity and credentials.
- **Recommended Fix:** Trust-store validation by default; document certificate provisioning and require
  lab verification of secure endpoints before deployment. Do not change PLC control logic.

## High

### H1 — Anonymous Edge mutation and configuration disclosure

- **Issue:** Network accessibility is treated as authorization.
- **Evidence:** `HealthEndpoints.cs:34` allows anonymous health writes;
  `EdgeConfigEndpoints.cs:14-65` has no authorization on reads/heartbeat.
- **Impact:** Unauthenticated callers can inject status and inspect configuration.
- **Recommended Fix:** Authenticate reads and require a scoped credential for Edge status writes;
  update Edge clients and test unauthorized requests.

### H2 — README and historical reports overstate validation

- **Issue:** Production badges, <200 ms latency, 72h warning and downtime/labor/cost reductions lack reproducible evidence.
- **Evidence:** Baseline README capability/ROI tables; `DEPLOYMENT_REPORT.md` and historical project analyses.
  No benchmark fixture or field dataset substantiates these claims.
- **Impact:** Misleads industrial adopters and grant reviewers.
- **Recommended Fix:** Replace claims with source-backed statuses; label historical reports unverified and remove
  unsupported promotional figures. Treat prediction/diagnostics as experimental until validated against labelled data.

### H3 — No LICENSE file despite MIT declaration

- **Issue:** README MIT link is broken; contributor/security/conduct policies and issue/PR templates are absent.
- **Evidence:** Tracked-file inventory; README License section. No existing license file was found.
- **Impact:** Reuse terms and maintenance expectations are unclear.
- **Recommended Fix:** Add MIT text consistent with the existing declaration; ask the rights holder to confirm
  ownership before public release. Apache-2.0 would be a new licensing decision, not silently substituted.

### H4 — False-positive CI and incomplete frontend

- **Issue:** Lint is an echo command, type/lint failures are swallowed, no UI unit-test script exists.
- **Evidence:** `intellimaint-ui/package.json`; `.github/workflows/ci.yml:65-69`;
  four routed analysis pages import static `mock*` exports from common components.
- **Impact:** A green check need not mean buildable/tested software; incomplete pages appear as real product features.
- **Recommended Fix:** Real lint and unit tests, strict failure propagation, explicit synthetic/experimental labels preserving existing code.

### H5 — Startup and hardware-free path are inconsistent

- **Issue:** API defaults to TimescaleDB; Edge always registers TimescaleDB; README says local SQLite works with no setup.
- **Evidence:** `src/Host.Api/appsettings.json`, `src/Host.Edge/Program.cs`; Docker ports differ from README;
  Docker UI install invokes a Git-dependent prepare script. Production Kestrel did override the development binding.
- **Impact:** Fresh clones cannot follow the documented shortest path.
- **Recommended Fix:** Verify a self-contained SQLite demo with generated local secrets, labelled synthetic telemetry,
  alarms and health; verify container binding and fix build scripts; document separate industrial setup.

### H6 — Dependency audit findings

- **Issue:** Baseline `npm ci --ignore-scripts` reports 18 vulnerabilities (1 low, 8 moderate, 9 high).
- **Evidence:** Actual installation on 2026-09-16; exact advisory details require `npm audit` review.
- **Impact:** Dependency risk remains even if the application builds.
- **Recommended Fix:** Inspect advisories and upgrade compatibly; test lockfile changes; report unresolved findings.

## Medium

### M1 — Store-and-forward is not an end-to-end feature

- **Issue:** Worker forwards to the DB pipeline, not `StoreAndForwardService.SendAsync`; upload route has no matching API endpoint.
- **Evidence:** `src/Host.Edge/Program.cs:205`; `Services/StoreAndForwardService.cs` sends to `/api/telemetry/batch`;
  `TelemetryEndpoints.cs` maps only read routes.
- **Impact:** Offline delivery cannot be advertised as implemented.
- **Recommended Fix:** Mark Partial; design and test delivery semantics separately without changing industrial control paths here.

### M2 — Integration fixture and DB claims are misleading

- **Issue:** Fixture provides a temporary SQLite path but not the provider/JWT override; CI supplies PostgreSQL credentials.
- **Evidence:** `tests/Integration/ApiTestFixture.cs`; existing CI integration job.
- **Impact:** Tests can select the wrong database; a passing fixture is not TimescaleDB validation.
- **Recommended Fix:** Isolate API tests with SQLite; add a separately named real TimescaleDB service test before claiming provider parity.

### M3 — Release/version metadata is inconsistent

- **Issue:** README/API v65 versus UI 0.0.54; no tags in the cloned history; release workflow interpolates input into shell and publishes latest.
- **Evidence:** `git tag` empty; package manifest, Swagger registration, `.github/workflows/release.yml`.
- **Impact:** Consumers cannot identify a tested release; arbitrary manual versions need validation.
- **Recommended Fix:** Adopt one pre-1.0 SemVer for future releases, retain historical labels, validate tags,
  and require an intentional human release action after CI.

### M4 — Missing coverage for industrial and analytic guarantees

- **Issue:** Protocol implementations exist but no PLC/OPC UA interoperability or fault-accuracy evidence is supplied.
- **Evidence:** `tests/Unit` and `tests/Integration` inventory; FFT/trend/RUL implementations in `src/Application/Services`.
- **Impact:** Algorithm presence does not establish predictive accuracy, hardware compatibility or timing guarantees.
- **Recommended Fix:** Publish a feature matrix distinguishing code coverage, synthetic tests and field validation.

### M5 — Operational readiness and token limitations

- **Issue:** Readiness maps empty health checks; background exceptions are ignored; token blacklist is process-local;
  telemetry debug route exposes local paths and depends on SQLite in all provider modes.
- **Evidence:** `Host.Api/Program.cs`, `TokenBlacklistService.cs`, `TelemetryEndpoints.cs:37`.
- **Impact:** Healthy HTTP responses may mask stopped work; restart/multi-instance revocation differs; debug leaks implementation data.
- **Recommended Fix:** Restrict diagnostics; document liveness limits and single-instance auth; add dependency checks before production use.

## Low

### L1 — Repository hygiene and stale instructions

- **Issue:** Generated TS build files, local agent settings, ad-hoc login payloads, stale paths and planning notes are tracked.
- **Evidence:** `git ls-files`; `scripts/login.json`; `intellimaint-ui/*.tsbuildinfo`, `vite.config.js`;
  README `your-org` links and incorrect environment variable names.
- **Impact:** Confusing contribution workflow and accidental credential reuse.
- **Recommended Fix:** Remove generated/local-only artifacts, sanitize scripts, define current documentation entry points and AGENTS.md.

### L2 — Screenshots and community/usage evidence are absent

- **Issue:** No real UI screenshots or independently verifiable users/deployments/benchmarks are included.
- **Evidence:** Complete tracked-file inventory; baseline/main HEAD ancestry has 15 commits, while all locally reachable refs during the audit have 28 commits.
- **Impact:** Application material must not imply adoption or field reliability.
- **Recommended Fix:** State unavailable evidence plainly; capture only actual running UI if needed; never invent metrics.

## Validation boundary and follow-up

Initial host: Windows, Node 24.19.0, no .NET SDK or Docker command. An isolated .NET 8 SDK
was downloaded with its published SHA-512 checked; baseline builds are being run next.
Secret scan covered password/secret/token/API-key/connection-string/certificate/private-IP patterns
across the tracked tree, including DOCX text. No matching private-key blocks or provider key prefixes
were found; this is not proof that no secrets ever existed. Known defaults already occur in Git history:
rotation is required for anyone who used them. History was not rewritten.

Remediation and actual pass/fail results belong in `OSS_READINESS_REPORT.md`; baseline findings remain
here so reviewers can distinguish what existed from what changed.

### Follow-up baseline findings discovered during remediation

- **Critical — captured bearer material.** `scripts/login.json` contains a captured login response;
  `scripts/test-api-perf.ps1`, `test-health-perf.ps1` and `test-health-perf.sh` embed signed JWTs.
  Historical validity/reuse is unknown. Remove literals, require local environment credentials, and
  rotate/revoke credentials in any deployments that used them. File removal does not revoke tokens.
- **High — extra dependency surface.** The separate `tools/DataMigration` project (outside the solution)
  pins Npgsql 8.0.0 and reports GHSA-x9vc-6hfv-hg8c. Upgrade it and include it in build/dependency checks.
- **High — proxy token logging.** The Nginx `$request` format includes query strings, which can include
  SignalR bearer tokens. Log path/method without query strings or referrers. Review existing logs separately.

These addenda describe baseline source, not newly introduced credentials. Secret values are intentionally omitted.
