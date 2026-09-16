# Architecture

`Core` contains contracts and interfaces; `Application` implements health, baseline, FFT, regression,
RUL, user and alarm services. Infrastructure supplies SQLite/TimescaleDB repositories, JWT signing,
OPC UA/libplctag clients and channel-based telemetry processing. Hosts wire dependencies and lifetimes.

## Standalone acquisition

`Host.Edge/Program.cs` registers TimescaleDB and both protocol collectors. Each implements `ICollector`
and `ITelemetrySource`. Worker tasks forward points to `TelemetryPipeline`; `TelemetryDispatcher`
fans out to database writes, threshold/rate/volatility alarms and last-data tracking. Offline detection
uses a timer. Full channels can drop points; there is no zero-loss guarantee.

API processes query the same database. `TelemetryBroadcastService` polls recent telemetry and sends
`ReceiveData` through the authenticated `/hubs/telemetry` hub. The React client subscribes to all data
or a device. API background tasks calculate health, learn baselines and run experimental diagnosis.

`StoreAndForwardService` and `FileRollingStore` exist, but the Worker does not call the send entry point
and `/api/telemetry/batch` is absent. Offline HTTP delivery is Partial. MQTT is a TODO.

## Local demo

In Development with `Demo__Enabled=true` and SQLite, `SyntheticDemoService` registers one explicitly
synthetic device, five tags and a threshold rule, seeds recent history, then writes a one-second signal
cycle. It calls the existing `AlarmEvaluatorService`, and the normal API services provide health and
SignalR. It never registers or contacts a hardware protocol collector.

## Persistence and identity

SQLite schema migrations run on startup. TimescaleDB needs SQL initialization from `docker/init-scripts`
and then verifies/extends the schema. The providers are separate implementations, not a promised
interchangeable production cluster.

JWT authenticates humans with Admin/Operator/Viewer policies. Bootstrap only creates an administrator
when the user table is empty and explicit credentials are present. Edge status/config traffic uses
`X-Edge-Key` only on Edge policies. It cannot administer users or change processing configuration.
The key is shared across configured Edge senders; per-device identities and automatic rotation are future work.

The blacklist is process-local. Multi-instance revocation, TLS termination and dependency-aware
readiness remain deployment work. `/health/live` and `/health/ready` currently indicate host liveness,
not end-to-end collection health. See [security](../SECURITY.md).
