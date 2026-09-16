# OSS readiness report

Prepared **2026-09-16**, baseline **759ba66**. Baseline/main HEAD ancestry: **15 commits**;
all locally reachable refs during the audit: **28 commits**, with **no tags**.
This report describes the local uncommitted working tree. Nothing was pushed; no commit, Issue/PR,
GitHub Release, equipment deployment or OpenAI application was created.

## Overall status

**Ready for maintainer review and local hardware-free evaluation. Conditionally prepared for an OSS
application; not a production-ready industrial release and not yet backed by a published Hosted CI run.**
The confirmed pre-commit blockers were remediated; build, tests, dependency scans and targeted secret checks passed again. This is a scoped commit-readiness conclusion, not production approval.
The repository now has a reproducible local demo, meaningful tests, source-backed capability statements,
open-source policies and reviewable security changes. Maintainer identity, external usage and operational
readiness cannot be manufactured by repository cleanup.

## Critical issues remaining

- **Deployment-dependent credential exposure:** captured bearer/refresh material and public credentials
  existed in older revisions. Working-tree removal is complete for the patterns checked; Git history
  still contains them. Whether they remain valid in a deployment is unknown. Any maintainer who deployed
  those values must rotate/revoke them before exposure. No external credentials or Git history were changed.
- No newly identified Critical item remains unaddressed in the isolated synthetic-demo path, within
  this source review and test scope. This is not a penetration-test or safety certification conclusion.
- Secure OPC UA policy/mode enforcement, certificate provisioning and interoperability need an authorized
  lab review before any claim of secure production acquisition; collectors remain explicitly Partial.

## Build and test results

Environment: Windows x64, isolated **.NET SDK 8.0.425**, **Node 24.19.0 / npm 12.0.2**.
The SDK archive was checked against Microsoft's published SHA-512. CI targets Ubuntu, .NET 8 and Node 22;
that different host has not yet executed the changed workflow. Commands below ran from the repository
root unless a UI directory is specified. The isolated dotnet executable is equivalent to `dotnet`.

| Check | Actual result | Boundary |
|---|---|---|
| `dotnet restore IntelliMaint.sln` | PASS | NuGet audit includes transitive dependencies; vulnerability warnings are errors |
| `dotnet build IntelliMaint.sln -c Release --no-restore` | PASS, 0 errors, 2 warnings | Existing CS8602 warnings in OpcUaSubscriptionManager:91 and TimescaleDb/AlarmRepository:258 |
| `dotnet test IntelliMaint.sln -c Release --no-build` | **147 unit + 41 integration passed; 0 failed, 0 skipped** | Rerun after remediation; includes real API/SignalR request-log redaction; no real PLC, OPC UA server or TimescaleDB service |
| `dotnet build tools/DataMigration/DataMigration.csproj -c Release` | PASS, 0 warnings/errors | Build only; no real migration was attempted |
| `dotnet publish src/Host.Api -c Release` | PASS | Local artifact, not a tested container or published release |
| `npm ci` (UI) | PASS | npm 12 blocked esbuild's optional install-script execution; native package still built successfully |
| `npm run type-check` (UI) | PASS | TypeScript |
| `npm run lint` (UI) | PASS | Real ESLint, zero warnings; legacy no-any/no-unused enforcement is not enabled |
| `npm test` (UI) | **2 passed** | Protected-route regression tests; not comprehensive UI coverage |
| `npm run build` (UI) | PASS | Existing bundle-size warning remains; largest chunk about 634 kB minified, not a runtime benchmark |
| `npm audit --audit-level=moderate` | PASS, **0 reported vulnerabilities** | Registry/advisory snapshot at validation time |
| `dotnet list ... package --vulnerable --include-transitive` | PASS, **no reported vulnerable packages** in solution or DataMigration | Not proof of absence of vulnerabilities |
| `node tools/demo/smoke.mjs` | PASS | Actual local HTTP login, 401 without auth, five signals, history, alarm and health |
| Browser sign-in and Dashboard | PASS | Real rendered trend, alarm and health score; actual screenshot in README |
| `docker compose config --quiet` | PASS for root and both legacy Compose files | Official Compose 5.5.1 standalone parser; no Docker engine installed |
| Docker build/start/smoke | **NOT RUN locally** | Requires a Docker host; CI contains build/up/smoke/down steps |
| GitHub Actions / CodeQL hosted execution | **NOT RUN** | Changes are local and were not pushed |
| actionlint 1.7.12 | PASS for all workflows | Embedded shellcheck/pyflakes unavailable; actionlint's own checks ran |
| YAML and Mermaid | PASS: 10 YAML files; 1 Mermaid diagram | YAML parser and Mermaid parser, not screenshot-based syntax inference |
| Relative Markdown links/anchors | PASS | All existing Markdown files checked by tools/check-docs.mjs; external URLs are not recursively checked |
| Changed legacy utilities | PowerShell syntax PASS | Hardware/data-mutating legacy scripts were not executed; they are not the CI suite |

Backend TRX files are emitted under `tests/*/TestResults/` locally and uploaded by CI; the remediation
run is `remediation.trx` (the earlier `oss-final.trx` had 40 integration tests). Generated results
are ignored by Git. The [verification record](verification.json) preserves machine-readable totals and
validation limits without credentials. The [change manifest](OSS_CHANGE_MANIFEST.md) lists every changed,
new and removed file. No test was removed or failure masked to obtain these results.

### Failures found and corrected

The original backend built, but its 39 integration tests failed at fixture startup because SQLite/JWT
configuration was applied too late or absent. Unit tests originally passed (138). The fixture now applies
configuration at host creation, and the existing 39 tests pass. New tests exercise actual JWT/Edge handlers,
demo alarms/history, bootstrap rejection/preservation, a known FFT sinusoid and the native SQLite security
version floor. The original frontend built; it was incorrect to infer missing mock exports from page usage.
The actual issue was unlabelled synthetic pages and a fake lint script, both corrected.

A smoke test also exposed an omitted query-limit parameter returning 500. Making the request field optional
with a defined fallback fixed that path. The broader BadHttpRequestException-to-400 mapping was reverted
in pre-commit remediation; no general exception-handling redesign is included. The earlier demo was rerun.
Dependency updates were followed by complete backend tests and final native HTTP smoke validation.

## CI status

`ci.yml` performs backend restore/build/all tests, separate migration-tool build, frontend clean install,
type-check/lint/tests/build/audit, local doc-link checks and Docker image/start/smoke validation. Tests use
temporary SQLite and generated test credentials, with no company network or private certificate dependency.
Current CI does not start or validate TimescaleDB. Its Docker smoke checks the API path, not UI browser
behavior or all container health states.
The workflow has read-only contents permissions and does not publish artifacts outside workflow test outputs.
CodeQL remains a separate security workflow. Release preparation validates input and uploads a candidate
API build only; it does not tag or publish. **A passing hosted check on the final reviewed commit is still required.**

## Documentation and claims

- Replaced README's unsupported production-ready, latency, 72-hour warning, downtime/labor/cost savings,
  generic protocol coverage and commercialization statements with the source-backed feature matrix.
- Published no fabricated Star/download/user/customer/revenue/deployment/benchmark/contributor figures.
- Retired the unsupported deployment report and two commercial presentation/plan documents to short
  pointers. Removed binary commercial-analysis and conversation exports; originals remain in Git history.
- Removed unsupported ROI/market/price estimates and unmeasured historical performance/completeness
  figures where identified. Remaining historical technical plans are visibly marked as superseded context.
- Replaced missing API-document links with actual endpoint-source links; stale API notes are marked historical.
- Added architecture, installation, exact configuration, protocol behavior, testing, troubleshooting,
  contributing, security, conduct, release and agent guides. Actual UI screenshot is synthetic, clearly labelled.
- Adopted `0.1.0-dev` / a proposed future `v0.x.y` series. No historical label is relabelled as an actual release.

## Security changes and audit limits

1. Removed known-account seeds from SQLite/PostgreSQL scripts, public configuration secrets and login hints.
   Empty databases require a valid name and explicit strong bootstrap password; existing users are preserved.
   Failed bootstrap stops startup. JWT signing and validation now use the same secret resolver and fail closed.
2. Edge configuration reads and health/heartbeat writes require authentication. A fixed-time checked shared
   Edge key has scoped policies, not general administration rights. Existing Edge clients send the key.
3. OPC UA no longer accepts every certificate validation failure. No PLC write or control logic was changed.
4. Production SignalR detailed errors are disabled. Telemetry debug requires Admin. Nginx omits query strings
   and referrers from access logs to avoid logging SignalR tokens. API Hosting.Diagnostics query properties
   are now redacted before console/file output, with a real SignalR regression test. This does not scrub
   older logs or other proxies. Container example ports bind loopback.
5. Deleted captured login-response tokens and replaced JWT literals in three performance scripts with
   environment inputs; repaired JSON serialization in password-consuming PowerShell/Bash helpers. Reviewed
   authentication utilities now log status only, never responses or token prefixes. Historical signing
   material and predictable-account INSERT examples were removed from current documentation/browser state;
   the migration check that recognizes old password hashes to require rotation remains intact.
6. Removed generated/local-only files and ignored `.env`, PKI and local data. Scans cover tracked/current
   text plus baseline DOCX text and full Git history for known key/token/default patterns. No matching private
   key blocks/provider API keys remain in the checked current text. Residual private-IP-shaped matches are
   documentation/examples/UI placeholders and a Visual Studio version string, not active collector endpoints.
   Pattern scanning can miss secrets; no blanket 'secret-free' claim is made.
7. npm's original 18 reported vulnerabilities were resolved, including Vite/Vitest/ECharts/router updates.
   Backend updates include OPC UA `1.5.374.158`, libplctag `1.5.0`, System.Text.Json `8.0.6`,
   SQLitePCLRaw bundle `2.1.13`, xUnit `2.9.3`, and migration-tool Npgsql `8.0.6`. The actual loaded native
   SQLite is **3.53.3**, above the tested 3.50.2 security floor. No audit finding was suppressed.

Relevant primary advisories: [OPC UA](https://github.com/OPCFoundation/UA-.NETStandard/security/advisories/GHSA-h958-fxgg-g7w3),
[Npgsql](https://github.com/npgsql/npgsql/security/advisories/GHSA-x9vc-6hfv-hg8c),
[System.Text.Json](https://github.com/dotnet/runtime/security/advisories/GHSA-hh2w-p6rv-4g7w).
SQLite package behavior was checked against the [upstream library documentation](https://github.com/ericsink/SQLitePCL.raw).

## Demo status

The opt-in Development/SQLite source produces motor temperature, vibration, RPM, current and voltage every
second, plus five minutes of history. It uses real repositories, threshold alarm evaluation, API queries
and heuristic health calculation. The UI, device and alarm text say **Synthetic Demo Data**. Browser
verification confirmed live charts, the alarm and a health score. No real controller/server was used.
Four static analytics pages carry explicit experimental/synthetic labels. Fixed notification counts,
unimplemented work-order counts, fake percentage trends and the unconditional 'normal system' label were removed.

## Behavior changes requiring explicit review

- OPC UA certificate validation now rejects errors; collectors default to disabled.
- Protocol dependency upgrades may change connection/recovery results and need separate lab regression testing.
- Legacy Compose initializes SQL 01–06 only. SQL 07/08 device/address/alarm/baseline/sampling seeds no longer
  run automatically for fresh databases; existing database contents are not reset by this change.
- Synthetic Demo is a new opt-in Development/SQLite feature, not a protocol simulator or production mode.
- SQLite is the automated integration-tested provider. TimescaleDB is not validated by current CI.
- Pre-commit remediation restored the unrelated four-device Dashboard skeleton count and Python shebang;
  it did not edit PLC writes, control/interlock logic, industrial sampling/reconnect algorithms or analytics.
- Historical numeric claims were corrected in context: example engineering/synthetic parameters and design
  targets remain labelled; unsupported throughput, latency, compression and score comparisons were withdrawn
  or explicitly classified as unverified historical estimates. No production/ROI/adoption result was added.

## Release and OSS readiness

The project is reviewable and locally runnable as pre-1.0 OSS. MIT text follows the original README
declaration; source ownership and third-party notices still need maintainer confirmation. Community
policies do not imply an existing support team. Private vulnerability reporting/maintainer contact and
repository protection settings were not configured remotely.

Known non-blocking-for-demo gaps: two nullable warnings, large frontend chunks and only two UI unit tests.
Known blockers for broader claims: no real TimescaleDB migration/runtime test; no OPC UA/PLC interoperability
matrix; incomplete store-and-forward upload; heuristic diagnosis/RUL without field accuracy evidence;
host-only readiness, ignored background exceptions and process-local token revocation. Admin-only debug
still assumes SQLite, so its TimescaleDB behavior needs correction before provider-parity claims.

## Recommended before application

1. Review this working tree, commit logical changes and publish them deliberately; obtain a passing Hosted
   CI/Docker/CodeQL result for that exact commit. Resolve any Linux/container-only failures before citing CI.
2. Confirm the actual applicant's maintainer authority, license ownership and security-reporting contact.
3. Assess and rotate any deployed historical credentials; do not attach token material to public reports.
4. Supply genuine public usage/activity evidence if available; otherwise explicitly state it is unavailable.
5. Review the [unsubmitted application draft](CODEX_OSS_APPLICATION.md) and current official form. Its three
   short English answers contain **446 / 453 / 437** characters including spaces and punctuation.

**Application conclusion:** technically prepared for an honest maintainer review/draft, but it is too early
to claim all release gates or program eligibility are met. Repository cleanup cannot establish adoption or
acceptance. The official program decides eligibility; no application was sent.

## Recommended after application

Build opt-in protocol test fixtures and a reproducible TimescaleDB service test; finish offline delivery
semantics; add dependency-aware readiness/durable revocation; expand UI and authorization coverage; evaluate
analytics on consented labelled data; publish only reproducible benchmarks and real release/usage evidence.
These are engineering roadmap items regardless of the application outcome.

## Suggested commits (not executed)

1. `docs: record OSS audit and source-backed feature matrix`
2. `security: remove default credentials and secure Edge and OPC UA access`
3. `build: update vulnerable dependencies and enforce NuGet auditing`
4. `feat: add labelled hardware-free motor telemetry demo`
5. `test: isolate API fixtures and cover bootstrap demo and authorization`
6. `ci: add strict build test and Docker validation workflows`
7. `docs: add OSS policies setup guides and release checklist`
8. `docs: retire unsupported claims and prepare OSS application materials`

Use selective staging to separate related changes; keep manifests/dependency lockfiles and their tests
together. Maintainers may combine interdependent changes to keep each commit buildable. Do not publish
the old credentials when sharing a raw diff; review removals locally.
