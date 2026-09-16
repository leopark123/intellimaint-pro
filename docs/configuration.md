# Configuration

The hosts use standard .NET configuration: base appsettings, environment-specific appsettings,
environment variables (`__` for nesting), then command-line arguments. `JWT_SECRET_KEY` is a special
explicit override checked before `Jwt:SecretKey`. `.env` is not implicitly loaded by .NET.
`tools/demo/run.mjs` imports the root `.env` into its child process; Docker Compose uses interpolation.

| Variable | Meaning | Default / requirement |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | API environment | Set Development for local demo; production otherwise |
| `DatabaseProvider` | `Sqlite` or `TimescaleDb` | API base: Sqlite; Production config: TimescaleDb |
| `Edge__DatabasePath` | SQLite file | Base API: `data/intellimaint.db`; helper: `data/demo.db` |
| `ConnectionStrings__TimescaleDb` | Full Npgsql connection string | Required for TimescaleDB/standalone Edge; blank in source |
| `JWT_SECRET_KEY` | Signing secret | Required; 32+ random characters; helper generates 48 random bytes as hex |
| `ADMIN_USERNAME` | First administrator | Required for empty user table; 3–50 letters/digits/underscores |
| `ADMIN_PASSWORD` | First administrator password | Required for empty table; 16+ characters, at most 72 UTF-8 bytes |
| `Demo__Enabled` | In-process synthetic source | false; allowed only with Development + Sqlite |
| `VITE_DEMO_MODE` | UI Synthetic Demo Data banner | false; build-time in containers |
| `Edge__ApiKey` | Shared status/config credential | Optional; API rejects it unless 32+ characters and exact match |
| `Edge__ApiBaseUrl` | Edge status/config API URL | Configure explicitly for your lab |
| `Protocols__LibPlcTag__Enabled` | Enable PLC collector | Shipped Edge file: false |
| `Protocols__LibPlcTag__SimulationMode` | Simulated tag reader | Shipped file: true; check DB-derived configuration too |
| `Protocols__OpcUa__Enabled` | Enable OPC UA collector | false |
| `Cors__AllowedOrigins__0` | First allowed production UI origin | Empty production list; same-origin proxy preferred |

`DATABASE_URL`, `DATABASE_PROVIDER`, `JWT_SECRET` and the old README expiration names are not aliases.
JWT durations use `Jwt__AccessTokenMinutes` and `Jwt__RefreshTokenDays` (15 minutes / 7 days in source).

Remove `ADMIN_PASSWORD` from long-lived production environments after first initialization. Reusing
the variable does not reset existing accounts. Demo helper `.env` retains it so the demo can be signed
into and smoke-tested. Secure the file with OS account permissions; never attach it to an issue.
Keep generated PKI files under ignored `pki/`. Never store real endpoint passwords in tracked JSON.

No production credentials are supplied. SQL migration tooling reads `ConnectionStrings__TimescaleDb`
and requires an explicit SQLite path. Legacy scripts read `ADMIN_PASSWORD`/`POSTGRES_PASSWORD` where
applicable and are manual utilities, not the CI test suite.
