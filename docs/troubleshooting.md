# Troubleshooting

| Symptom | Check / corrective action |
|---|---|
| `dotnet` reports no SDK | Install .NET 8.0.4xx SDK; a runtime alone cannot build |
| Demo init says `.env` exists | Existing credentials were preserved; inspect that file rather than regenerating over a database |
| JWT configuration fails | Set a unique 32+ character `JWT_SECRET_KEY`; known legacy default keys are rejected even in Development |
| Empty database fails startup | Set valid ADMIN_USERNAME and a 16+ character ADMIN_PASSWORD before starting |
| Login returns 400 | Username permits letters, digits and underscores; inspect non-secret response text |
| Login returns 401 after changing `.env` | Bootstrap does not reset existing users; use the original password or the authenticated account workflow |
| API tries PostgreSQL in a local demo | Use the root demo helper; verify DatabaseProvider and environment-specific config |
| Port is occupied | Stop the other local process/stack; API 5000 and Vite 3000 are defaults |
| Vite shows no data | Confirm API 5000, JWT login, Synthetic Demo Data banner and the synthetic device in the selector |
| Health history initially empty | Background snapshots start after approximately 10 seconds and recur every 60 seconds |
| Edge status returns 401/403 | Set the same strong Edge__ApiKey on API and Edge; X-Edge-Key cannot administer user accounts |
| OPC UA certificate rejected | Verify the intended endpoint and provision its certificate/issuer in pki/trusted or pki/issuers; never restore accept-all validation |
| libplctag BAD_TAG / TYPE_MISMATCH | Check symbolic address, CIP type and routing path; disabled bad tags require configuration correction/reload |
| Docker API cannot connect | Check container logs and configured credentials; Compose does not rotate existing database passwords |
| Old PostgreSQL volume missing schema columns | Back up data and review migrations; init scripts are not rerun automatically |
| `/health/ready` is OK but collector failed | This is currently host liveness only; inspect Edge health/status and logs |

Attach versions, the failing command and redacted logs to a bug report. Remove access/refresh tokens,
connection strings, personal data, plant IPs and proprietary tag names. Do not upload `.env` or PKI keys.
