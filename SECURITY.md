# Security policy

This is a pre-1.0 evaluation project. No supported production release or security-support SLA has been
established. Use the current reviewed branch for development, and treat historical versions as unreviewed.

## Reporting

Use the repository's **Security → Report a vulnerability** if GitHub private reporting is enabled:
[security page](https://github.com/leopark123/intellimaint-pro/security).
If that control is unavailable, open an issue asking for a private reporting channel **without** exploit
details, credentials, plant identifiers or sensitive logs. No unverified security mailbox is advertised.
Include affected revision, reproduction steps in an isolated environment, impact and a proposed fix.
Maintainers should acknowledge and coordinate remediation privately before disclosure; no timing guarantee
is claimed. Enabling private reporting is a maintainer prerequisite in the release checklist.

## Credentials and upgrades

- Empty databases require explicit `ADMIN_USERNAME` and `ADMIN_PASSWORD`; there is no built-in account.
  Existing databases are never silently reset. Remove bootstrap password variables after initialization.
- JWT signing material must be unique and at least 32 characters. Use randomly generated values; do not
  reuse example strings. Keep `.env`, database files, tokens and certificate private keys out of Git.
- Older revisions contained reusable password/signing examples and `scripts/login.json` with captured
  access/refresh tokens. Removing the file does not revoke them. If deployed, rotate signing keys,
  administrator/database passwords and invalidate refresh tokens. Assess any copies or forks. This work
  did not contact a deployment, rotate external credentials or rewrite Git history.
- The Edge status key (`Edge__ApiKey`) is a shared credential with limited API policies. It is not
  per-device identity, tenant isolation or a substitute for TLS. Rotate it and restrict network access.
- Review dependency audit results on each change. A clean scanner report is not proof of absence of vulnerabilities.

## Deployment boundary

The root demo Compose stack binds loopback and uses Development with synthetic data. Do not expose it
to the internet or connect it to production equipment. Use HTTPS, an authenticated network boundary,
reviewed CORS origins, secure secret injection, backups and access controls for any separate lab deployment.
SignalR access tokens can appear in transport query strings; reverse proxies must redact them from logs.
The API logger redacts sensitive QueryString properties before console/file output while retaining
ordinary request details. The regression suite checks a real authenticated SignalR request. This does
not clean older log files or external proxy logs; assess previously recorded credentials separately.
The UI stores authentication state in the browser; XSS prevention and dependency maintenance matter.

OPC UA validation now rejects untrusted/expired certificates instead of accepting every error. Provision
and verify application/server certificates before a secure session; see [protocols](docs/protocols.md).
The disabled `SecurityPolicy=None` example is suitable only for an isolated lab with anonymous access.
Exact policy/mode enforcement and secure server interoperability remain unverified.

Known limitations: readiness currently checks the host, not every dependency; background worker failure
may not stop the host; JWT revocation is process-local; refresh tokens are stored in the database;
rate limits and proxy trust require deployment review; TimescaleDB runtime parity and offline upload
are incomplete. Review [readiness](docs/OSS_READINESS_REPORT.md) before use.

Do not automatically modify or deploy PLC writes, machine control, safety interlocks, emergency stops
or protection logic. Human engineering review and onsite validation are mandatory for those changes.
