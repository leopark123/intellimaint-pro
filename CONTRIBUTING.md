# Contributing

Start with the [README](README.md), [feature matrix](docs/FEATURE_MATRIX.md) and
[development guide](docs/development.md). The hardware-free demo is the default contribution environment.

## Issues and pull requests

Search existing issues, then use the bug or feature template. Include the commit, OS/runtime versions,
reproduction steps, expected/actual behavior and sanitized logs. Label synthetic observations as such.
Security reports follow [SECURITY.md](SECURITY.md), not a public issue with exploit details.

Keep a PR focused. Explain the user-visible change, relevant source paths, tests actually run and remaining
limitations. Update configuration and documentation when behavior changes. Do not claim PLC compatibility,
performance, accuracy, adoption or ROI without a reproducible environment and evidence. No mandatory CLA
is configured; submit only work you have the right to contribute under the repository's MIT license.

## Local checks

```sh
dotnet restore IntelliMaint.sln
dotnet build IntelliMaint.sln -c Release --no-restore
dotnet test IntelliMaint.sln -c Release --no-build
npm ci --prefix intellimaint-ui
npm run type-check --prefix intellimaint-ui
npm run lint --prefix intellimaint-ui
npm test --prefix intellimaint-ui
npm run build --prefix intellimaint-ui
node tools/check-docs.mjs
```

Use C# nullable annotations, async cancellation, structured logs without credentials, and the existing
Core → Application → Infrastructure → Host dependency boundaries. Use TypeScript and the existing
React component/API patterns. Avoid formatting unrelated files. Add a regression test for a bug fix;
include negative authorization tests when changing authentication. Hardware tests must be opt-in and
document controller/server versions, certificates, topology, read-only access and failure scenarios.

Changes involving PLC writes, machine control, safety interlocks, emergency stops or protection logic
require a qualified human review and onsite validation before deployment. CI is not a safety certificate.
Never deploy or connect to operational equipment as part of a normal contribution or agent task.

Maintainers review scope, evidence and CI before merging. No response SLA or support organization is
promised. See [Code of Conduct](CODE_OF_CONDUCT.md) and [release checklist](docs/RELEASE_CHECKLIST.md).
