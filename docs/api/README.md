# API reference entry point

The running Development server exposes `/swagger`. Endpoint registrations are the authoritative
reference for paths, request fields and authorization. Older module notes in this folder are historical
and may contain stale examples; use Swagger to try a request with your local generated account.

| Area | Source | Boundary |
|---|---|---|
| Authentication | [AuthEndpoints](../../src/Host.Api/Endpoints/AuthEndpoints.cs) | JWT and refresh; no shared default account |
| Telemetry | [TelemetryEndpoints](../../src/Host.Api/Endpoints/TelemetryEndpoints.cs) | Latest/query/aggregate; debug is administrator-only |
| Alarms | [AlarmEndpoints](../../src/Host.Api/Endpoints/AlarmEndpoints.cs) | Authenticated query and authorized acknowledgement |
| Health assessment | [HealthAssessmentEndpoints](../../src/Host.Api/Endpoints/HealthAssessmentEndpoints.cs) | Heuristic score; not a safety assessment |
| Motor analytics | [MotorEndpoints](../../src/Host.Api/Endpoints/MotorEndpoints.cs) | Experimental diagnosis; no accuracy claim |
| Predictions | [PredictionEndpoints](../../src/Host.Api/Endpoints/PredictionEndpoints.cs) | Experimental extrapolation |
| Edge status/config | [EdgeConfigEndpoints](../../src/Host.Api/Endpoints/EdgeConfigEndpoints.cs) | Scoped Edge key or permitted JWT role |

Most API data routes require a Bearer access token. Edge status/config policies accept the separately
configured `X-Edge-Key`. Login/refresh and host liveness are not equivalent to authenticated data routes.
See [configuration](../configuration.md), [architecture](../architecture.md) and
[feature matrix](../FEATURE_MATRIX.md). Do not paste tokens or real plant payloads into issues.
