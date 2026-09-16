# Industrial protocol behavior

Both adapters have implementation code and build locally, but remain **Partial** without a reproducible
interoperability matrix. No KEPServerEX, RSLinx or physical PLC was contacted in this preparation.
The [hardware-free demo](../tools/demo/README.md) does not exercise either wire protocol.
OSS preparation changed certificate rejection behavior, disabled the shipped collectors by default,
and upgraded OPC UA/libplctag dependencies. Even though polling/reconnect algorithms were not edited,
library upgrades and certificate rejection can change connection/recovery results; authorized lab
regression tests are required before asserting compatibility. Legacy Compose also stopped automatically
loading SQL 07/08 device/address/alarm/baseline/sampling seeds into fresh databases.

## OPC UA

**Connection model.** `OpcUaSessionManager` maintains one session per endpoint ID. The collector creates
monitored items and receives subscription notifications; if subscription setup fails, its polling loop
reads configured nodes. Notifications/poll results become `TelemetryPoint` values through
`OpcUaTypeMapper`, then enter the common channel/persistence pipeline.

**Configuration.** The contract is `src/Core/Contracts/ProtocolOptions.cs` under `Protocols:OpcUa`.
`Enabled` is false in the shipped Edge settings. Each `Endpoints` entry uses `EndpointId`, `EndpointUrl`,
`SecurityPolicy`, `MessageSecurityMode`, optional `Username`/`Password`, `SessionTimeoutMs`, `Subscription`
and `Nodes`. Set credentials through environment variables such as
`Protocols__OpcUa__Endpoints__0__Password`, not a committed JSON file.
The disabled localhost example in `src/Host.Edge/appsettings.json` is an isolated-lab example only.

**Tag mapping.** A node maps `NodeId` to a stable application `TagId`; configure `ValueTypeHint`, `Unit`,
`SamplingIntervalMs`, `QueueSize` and `DiscardOldest`. Subscription options include publishing interval,
lifetime, keepalive and notification limits. These are requests to the server, not guaranteed sampling rates.
Preserve namespace/node identifiers from the actual server; do not assume a sample KEPServerEX node exists.

**Certificates/security.** The client uses directory stores relative to its working directory:
`pki/own`, `pki/trusted`, `pki/issuers`, `pki/rejected`. Provision its application certificate/private key
and verified peer/issuer certificates using your OPC UA tooling; this project does not supply a
certificate enrollment wizard. Trust only certificates whose identity and fingerprint have been checked
out of band. Validation errors are rejected. Never copy a rejected certificate into trust without review.
The current selector passes a boolean secure/nonsecure preference to `CoreClientUtils.SelectEndpoint`;
it does not explicitly enforce an exact configured policy/mode pair. Verify the negotiated endpoint in
a lab. Secure endpoint interoperability and client-certificate provisioning remain release blockers for
a secure-OPC-UA support claim. `None` is unencrypted and is not suitable for carrying real credentials.

**Errors/reconnect.** Failed initial sessions back off for 1, 2, 5, 10, 30 and then 60 seconds.
Bad keepalive status triggers the SDK reconnect handler with a 10-second interval. The collector logs
read/subscription failures and tracks health. Timestamp/type/status behavior needs server-specific tests;
no exactly-once delivery, loss-free reconnect or timing guarantee is asserted.

## Allen-Bradley / EtherNet/IP via libplctag

**Connection model.** `LibPlcTagCollector` configures PLC endpoints, groups tags by scan interval and uses
`LibPlcTagConnectionPool` / `LibPlcTagReader` for CIP tag reads. This is not a general EtherNet/IP scanner,
adapter implementation or control/write API. Options name ControlLogix, CompactLogix and Micro800, but
those names are not a tested controller/firmware compatibility list.

**Configuration.** `Protocols:LibPlcTag` uses `Enabled`, `SimulationMode`, and `Plcs`. Each PLC entry has
`PlcId`, `IpAddress`, `Path`, `Slot`, `PlcType`, `MaxConnections`, `ReadMode`, `TimeoutMs`, `Retry` and
`TagGroups`. Each group has `Name`, `ScanIntervalMs`, `BatchReadSize` and `Tags`. The shipped collector is
disabled and its sample endpoints are loopback. Confirm routing path and timeouts against an authorized
lab controller before enabling real reads. SimulationMode generates local values; it does not validate CIP.

**Tag mapping/polling.** `Name` is the controller tag, for example `Program:MainProgram.Motor.Current`;
`TagId` is the application ID, `CipType` describes storage (such as REAL/DINT/BOOL), and `Unit`/`ArrayLength`
provide metadata. Type mapping occurs in `LibPlcTagTypeMapper`. Batch/parallel option names must not be
read as proof of multi-tag network packet batching: the reader loops through async tag reads.

**Errors/reconnect.** `BAD_TAG` disables that tag; `NO_ROUTE` applies retry/backoff handling;
timeouts degrade health, and type mismatches are logged. Retry options include maximum retries,
initial/maximum delay and multiplier. Connection status and recovery are managed by the pool/collector.
No tested failover duration or throughput benchmark is supplied.

## Lab validation needed

Record exact server/controller/firmware and OS/native-library versions, authorized network topology,
read-only tags, certificate setup, configuration with secrets removed, and observed results. Test valid
values/types, malformed tags, disconnect/reconnect, timeout, access denial and certificate rejection.
Keep those tests opt-in. The default CI must remain free of hardware, private services and certificates.
Standalone Edge currently needs TimescaleDB; configuration examples are not a verified plant deployment.
