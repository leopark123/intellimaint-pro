# Changelog

## Unreleased — proposed 0.1.0

No tag or GitHub Release was created by the OSS preparation work. Baseline/main HEAD ancestry has 15 commits; all locally reachable refs during the audit have 28 commits. No tags were present.
Historical v38–v65 development labels are not treated as SemVer releases. Current assembly/UI development
metadata is `0.1.0-dev`; maintainers choose and validate the first public `v0.x.y` release.

### Added

- Opt-in SQLite hardware-free demo with five labelled synthetic motor signals, history and real alarm/health paths.
- First-run administrator bootstrap, scoped Edge authentication and negative authorization/demo regression tests.
- MIT license text consistent with the original README; contribution, conduct and security policies,
  issue/PR templates, maintainer instructions, audit, feature matrix and source-backed documentation.
- Real frontend lint/unit tests, strict CI build/test/audit, Docker smoke workflow and release-preparation checks.

### Changed

- Removed public credential seeds and configuration fallbacks; removed a tracked captured login response.
- OPC UA certificate validation fails closed; dependency advisories addressed and frontend/router major upgrades verified locally.
- Production SignalR details disabled; Edge health/configuration and telemetry diagnostics require authorization.
- Query `limit` can be omitted as documented. The unrelated blanket BadHttpRequestException-to-400 mapping was reverted during pre-commit review.
- Static analytics pages are labelled experimental/synthetic; stale version and fixed dashboard indicators removed.
- README performance, ROI and production claims replaced with explicit evidence boundaries.

### Upgrade notes

Existing users are preserved. Rotate legacy credentials separately; bootstrap does not rotate them.
Edge clients must supply a matching `Edge__ApiKey`; untrusted OPC UA servers now fail validation.
Industrial collectors now default to disabled. The legacy Compose stacks initialize SQL 01–06 only;
07/08 sample/site-specific devices, addresses, alarm rules, baselines and sampling configuration are
no longer automatically seeded into fresh databases. Existing database contents are not reset.
Protocol dependency upgrades can affect connection/recovery behavior and require separate lab regression
testing. The Synthetic Demo is a new opt-in feature, restricted to Development + SQLite. Current automated
integration tests use SQLite; current CI does not validate TimescaleDB.
Node.js 22.12+ is required. See [configuration](docs/configuration.md), [security](SECURITY.md) and
[readiness report](docs/OSS_READINESS_REPORT.md) for tested scope and unresolved issues.

### Pre-commit remediation

- Redacted sensitive request-query values at the API logger, including SignalR access tokens; a real
  authenticated negotiation regression test checks captured application logs and preserves ordinary request logging.
- Removed authentication-response/token-prefix output from reviewed utilities and removed historical
  signing material and predictable-account examples from current documentation/database-browser state.
- Repaired accidental document substitutions while distinguishing example parameters, design targets and
  unsupported historical estimates; restored the unrelated Dashboard skeleton count and Python shebang.
