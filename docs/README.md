# Documentation

Start with the current guides below. Uppercase version reports, commercial plans and the older API
notes are historical context; they are not test results or production/deployment evidence.

| Guide | Purpose |
|---|---|
| [Installation](installation.md) | Local demo and optional container paths |
| [Configuration](configuration.md) | Exact environment variables, secrets, provider selection |
| [Development](development.md) | Build, code structure and contribution workflow |
| [Architecture](architecture.md) | Actual process/data flow and incomplete paths |
| [Protocols](protocols.md) | OPC UA and libplctag connection behavior |
| [Testing](testing.md) | Hardware-free tests and verification limits |
| [Troubleshooting](troubleshooting.md) | Startup, authentication, data and container failures |
| [Feature matrix](FEATURE_MATRIX.md) | Implemented / Partial / Experimental / Planned |
| [OSS audit](OSS_AUDIT.md) | Findings before remediation |
| [OSS readiness](OSS_READINESS_REPORT.md) | Actual results and remaining blockers |
| [Release checklist](RELEASE_CHECKLIST.md) | Pre-1.0 release gates |
| [Application draft](CODEX_OSS_APPLICATION.md) | Unsubmitted English/Chinese application material |

The running Development API exposes Swagger at `/swagger`; source endpoint registrations take
precedence over old API examples. Never use historical account/password examples.
