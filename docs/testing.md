# Testing

## Hardware-free checks

`dotnet test IntelliMaint.sln -c Release` runs xUnit unit tests and SQLite API integration tests.
The CRUD fixture uses a test-only administrator handler; `DemoSecurityTests` separately exercises
real JWT and Edge authentication, first-run initialization, synthetic telemetry, threshold alarms,
history and health. `DemoAndFftTests` covers signal labelling/alarm cycling and a known-frequency FFT.
`RequestLoggingTests` performs an authenticated SignalR negotiation against the real API host, captures
application console logs, and verifies the known test JWT and other sensitive query values are absent.
It also checks request-start/request-finish messages, paths and ordinary query values remain visible.

`npm ci`, `npm run type-check`, `npm run lint`, `npm test` and `npm run build` run in `intellimaint-ui`.
Vitest currently covers protected-route behavior; this is intentionally a small initial frontend suite,
not a claim of broad UI coverage. Run `npm audit --audit-level=moderate` to check current advisories.

While the demo is running, `node tools/demo/smoke.mjs` checks the actual HTTP process. It reads local
credentials without printing them. It checks five current values, historical data, a generated alarm
and a numerical health score. The root Compose CI job performs the same check after image builds.

## Industrial and database validation

No default test contacts a PLC, KEPServerEX, RSLinx, private network or private certificate store.
Do not label a mocked test as protocol interoperability. Future hardware tests should use
`[Trait("Category", "Hardware")]`, explicit opt-in endpoints and separate lab workflows. Record
controller/server version, firmware, security mode, tag types, sample intervals, errors and recovery.

SQLite tests do not validate TimescaleDB. The latter needs a fresh-container schema test, API CRUD,
time-series queries, restore/migration tests and Edge acquisition on a known dataset before promotion.
Current CI does not start or validate TimescaleDB; neither the SQLite suite nor the synthetic source is
an OPC UA/PLC protocol simulator, real-hardware test or production validation.
FFT sinusoid correctness is separate from fault sensitivity/specificity or remaining-life accuracy.

CI YAML has no continue-on-error or shell fallbacks hiding failed lint/tests. Hosted-run status is only
established after a maintainer pushes/reviews the changes. See [actual results](OSS_READINESS_REPORT.md).
