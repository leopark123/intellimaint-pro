# Development

Use the SDK selected by `global.json` and Node 22.12+. From the repository root:

```sh
dotnet restore IntelliMaint.sln
dotnet build IntelliMaint.sln -c Release --no-restore
dotnet test IntelliMaint.sln -c Release --no-build
dotnet build tools/DataMigration/DataMigration.csproj -c Release
npm ci --prefix intellimaint-ui
npm run type-check --prefix intellimaint-ui
npm run lint --prefix intellimaint-ui
npm test --prefix intellimaint-ui
npm run build --prefix intellimaint-ui
node tools/check-docs.mjs
```

For interactive development follow [installation](installation.md); `npm run dev` in the UI directory
also works against a configured API on port 5000. The demo wrapper ensures the synthetic banner flag
and credentials are available. No Git hook is installed during dependency installation.
Optional hook installation: `npm run hooks:install --prefix intellimaint-ui`; inspect `.husky/pre-commit` first.

Keep contracts in Core, domain calculations in Application, persistence/protocols in Infrastructure
and HTTP wiring in Host.Api. Use existing C# async/CancellationToken conventions, nullable annotations
and explicit DI lifetimes. Keep React components functional and TypeScript strict. ESLint checks
correctness; strict no-any/no-unused enforcement is not yet adopted across legacy code.

Changes should include regression evidence proportional to risk, especially authentication, SQL,
channel/backpressure behavior, certificates and numerical algorithms. Do not change controller write
semantics, safety interlocks or protection logic without qualified review and onsite validation.
See [AGENTS.md](../AGENTS.md) and [contribution workflow](../CONTRIBUTING.md).
