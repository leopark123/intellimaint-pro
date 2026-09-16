# Feature matrix

Source-reviewed on 2026-09-16 against baseline `759ba66` plus the local OSS changes.
**Implemented** means a working code path, not industrial certification. **Partial** means
missing integration or provider coverage. **Experimental** means runnable heuristics or
synthetic demonstrations without field accuracy evidence. **Planned** means no usable implementation.
Actual execution results are recorded in [OSS readiness](OSS_READINESS_REPORT.md).

| Capability | Status | Source evidence | Validation boundary |
|---|---|---|---|
| .NET 8 REST API | Implemented | `src/Host.Api/Program.cs`, `Endpoints/` | API integration suite uses local SQLite |
| React 18 UI | Implemented | `intellimaint-ui/src/router`, `src/api` | Current production build and route tests passed; demo browser checked; no field usability evidence |
| Telemetry queries, latest values, aggregates | Implemented | `TelemetryEndpoints`, both telemetry repositories | SQLite demo and integration validation; no throughput claim |
| SignalR live updates | Implemented | `TelemetryHub`, `TelemetryBroadcastService`, UI `api/signalr.ts` | Authenticated hub; no latency SLA |
| JWT, refresh tokens, role policies | Implemented | `JwtService`, `AuthService`, user repositories | Single-instance token blacklist; not a multi-tenant identity system |
| Alarm rules, threshold/rate/offline/volatility evaluation, acknowledgement | Implemented | `Infrastructure/Pipeline/*EvaluatorService`, `AlarmEndpoints` | Threshold synthetic end-to-end path; aggregated acknowledgement count remains incomplete |
| Health scoring and historical health | Implemented | `HealthAssessmentService`, `HealthScoreCalculator`, snapshots | Heuristic score; insufficient data must not be treated as proof of equipment health |
| Historical trend charts | Implemented | UI `DataExplorer`, telemetry aggregate API | Synthetic/local data is sufficient to exercise it |
| Edge Worker acquisition and database pipeline | Implemented | `Host.Edge/Program.cs`, `PipelineServiceExtensions` | Original standalone Worker uses TimescaleDB; hardware tests are separate |
| OPC UA read acquisition | Partial | `OpcUaCollector`, session/subscription managers and type mapper | Subscription with polling fallback and reconnect code exist; no reproducible server/hardware interoperability result supplied |
| Allen-Bradley over EtherNet/IP via libplctag | Partial | `LibPlcTagCollector`, connection pool and tag reader | Read-oriented CIP tag acquisition and simulation exist; no verified controller/firmware matrix |
| Generic EtherNet/IP adapter/scanner or PLC writes | Planned | No corresponding public implementation found | Do not infer this from libplctag read support |
| SQLite | Implemented | `Infrastructure/Sqlite` | Primary no-hardware/demo and API-test provider |
| PostgreSQL + TimescaleDB | Partial | `Infrastructure/TimescaleDb`, Docker SQL migrations | Substantial provider exists; clean-container migration/runtime parity requires real service validation |
| FFT spectrum calculation | Implemented | `MotorFftAnalyzer` | Numerical code; synthetic sinusoid tests do not establish diagnostic accuracy |
| Motor fault diagnosis | Experimental | `MotorFaultDetectionService`, baseline/mode services | Hand-coded spectral/statistical rules, no labelled field evaluation |
| Trend extrapolation and remaining useful life | Experimental | `TrendPredictionService`, `RulPredictionService` | Regression/degradation heuristics; no guaranteed warning horizon |
| Predictive-maintenance/work-order UI | Experimental | `pages/PredictiveMaintenance`, common mock datasets | Static synthetic examples; no persisted work-order workflow |
| Anomaly detection / model optimization / knowledge graph UI | Experimental | Corresponding pages and common mock datasets | Synthetic interface demonstrations, not trained production models |
| Production ML anomaly models / model training / knowledge graph backend | Planned | No trained model artifacts or implementing service found | Roadmap only |
| Store-and-forward upload | Partial | `StoreAndForwardService`, `FileRollingStore` | Worker does not call SendAsync and batch HTTP route is absent |
| MQTT, OPC DA, Modbus TCP/RTU | Planned | Options/contracts and TODOs only | No enabled collector/publisher implementation |
| Mobile application, multi-tenancy | Planned | No implementation found | Roadmap only |
| Hardware-free synthetic motor demo | Implemented | `Host.Api/Services/SyntheticDemoService.cs`, `tools/demo/` | Added by this change; uses repositories and real alarm/health services, not a protocol interoperability test |

The code contains `TelemetryDispatcher.RegisterTarget` throwing `NotImplementedException`;
its active registration path is `CreateTargetChannel`. Alarm-group acknowledged statistics
contain a TODO. Neither is silently counted as complete API coverage.

No screenshots, customers, installations, download counts, performance or ROI measurements
are inferred from these statuses. See [protocol behavior](protocols.md) before any lab trial.
