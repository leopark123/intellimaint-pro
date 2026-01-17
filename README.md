# IntelliMaint Pro

> Industrial AI Predictive Maintenance Platform | 工业 AI 预测性维护平台

[![Version](https://img.shields.io/badge/version-v65-blue.svg)](./docs/CHANGELOG.md)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![React](https://img.shields.io/badge/React-18-61DAFB.svg)](https://reactjs.org/)
[![TypeScript](https://img.shields.io/badge/TypeScript-5.0-3178C6.svg)](https://www.typescriptlang.org/)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

---

## Overview | 概述

IntelliMaint Pro is an industrial AI predictive maintenance platform that monitors equipment in real-time, predicts failures before they occur, and enables condition-based maintenance.

IntelliMaint Pro 是一款工业 AI 预测性维护平台，实时监控设备运行状态，提前预警潜在故障，实现按状态维护。

### Core Capabilities | 核心能力

| Feature | Description | Status |
|---------|-------------|--------|
| **Real-time Monitoring** | SignalR push, <200ms latency | ✅ Production |
| **Smart Alarms** | 5 alarm types + intelligent aggregation | ✅ Production |
| **Health Assessment** | 0-100 health index, 4D scoring | ✅ Production |
| **Motor Diagnostics** | FFT spectrum, 15+ fault types | ✅ Production |
| **Trend Prediction** | 72h+ early warning | ✅ Production |

### Value Proposition | 价值主张

| Pain Point | Solution | ROI |
|------------|----------|-----|
| Unplanned downtime | 72h+ advance warning | -60% downtime |
| Manual inspection | Automated monitoring | -80% labor |
| Experience-dependent diagnosis | AI-powered analysis | Minutes vs hours |
| High maintenance cost | Condition-based maintenance | -30% cost |

---

## Quick Start | 快速开始

### Prerequisites | 前置条件

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 18+](https://nodejs.org/)
- [Docker](https://www.docker.com/) (optional)

### Option 1: Local Development | 本地开发

```bash
# Clone repository
git clone https://github.com/your-org/intellimaint-pro.git
cd intellimaint-pro

# Start backend (port 5000)
dotnet run --project src/Host.Api

# Start Edge collector (optional)
dotnet run --project src/Host.Edge

# Start frontend (port 3000)
cd intellimaint-ui
npm install
npm run dev
```

Access:
- Frontend: http://localhost:3000
- Backend API: http://localhost:5000
- Swagger: http://localhost:5000/swagger

### Option 2: Docker Deployment | Docker 部署

```bash
cd docker
cp .env.example .env
# Edit .env with your settings

docker-compose up -d
```

Services:
- Frontend: http://localhost:80
- Backend API: http://localhost:5000
- TimescaleDB: localhost:5432

### Default Accounts | 默认账号

| Role | Username | Password |
|------|----------|----------|
| Admin | admin | admin123 |
| Operator | operator | operator123 |
| Viewer | viewer | viewer123 |

---

## Tech Stack | 技术栈

### Backend
| Component | Technology |
|-----------|------------|
| Framework | .NET 8 Minimal API |
| Database | SQLite (dev) / TimescaleDB (prod) |
| Real-time | SignalR WebSocket |
| Auth | JWT + Refresh Token + RBAC |
| ORM | Dapper |

### Frontend
| Component | Technology |
|-----------|------------|
| Framework | React 18 + TypeScript |
| UI Library | Ant Design 5.x |
| State | Zustand |
| Charts | Recharts + ECharts |

### Industrial Protocols
| Protocol | Devices |
|----------|---------|
| OPC UA | Universal industrial standard |
| LibPlcTag | Allen-Bradley ControlLogix/CompactLogix |

---

## Architecture | 架构

```
┌─────────────────────────────────────────────────────────────┐
│                       User Layer                             │
│  ┌─────────────────────────────────────────────────────┐    │
│  │           React 18 + Ant Design + Recharts          │    │
│  │                   (port 3000)                       │    │
│  └──────────────────────┬──────────────────────────────┘    │
│                         │ HTTP / SignalR                     │
├─────────────────────────┼───────────────────────────────────┤
│                         ▼           Service Layer            │
│  ┌─────────────────────────────────────────────────────┐    │
│  │         .NET 8 Minimal API + SignalR Hub            │    │
│  │            JWT + RBAC + Rate Limiting               │    │
│  │                   (port 5000)                       │    │
│  └──────────────────────┬──────────────────────────────┘    │
│                         │                                    │
│  ┌──────────┬───────────┴───────────┬──────────────────┐    │
│  │          │                       │                  │    │
│  ▼          ▼                       ▼                  ▼    │
│ Health    Alarm      Telemetry     Motor       Trend        │
│ Engine    Engine     Pipeline      Diagnostics Prediction   │
│                                                             │
├─────────────────────────────────────────────────────────────┤
│                        Data Layer                            │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐       │
│  │   SQLite/    │  │   Pipeline   │  │  Protocols   │       │
│  │  TimescaleDB │  │   Channel    │  │  OPC UA      │       │
│  │              │  │   DbWriter   │  │  LibPlcTag   │       │
│  └──────────────┘  └──────────────┘  └──────────────┘       │
└─────────────────────────────────────────────────────────────┘
```

---

## Project Structure | 项目结构

```
intellimaint-pro/
├── src/
│   ├── Core/                      # Core layer - interfaces & contracts
│   │   ├── Abstractions/          # Interface definitions
│   │   └── Contracts/             # DTOs, entities, enums
│   │
│   ├── Infrastructure/            # Infrastructure layer
│   │   ├── Sqlite/                # SQLite repositories
│   │   ├── TimescaleDb/           # TimescaleDB repositories
│   │   ├── Pipeline/              # Data collection pipeline
│   │   │   ├── TelemetryDispatcher.cs
│   │   │   ├── AlarmEvaluatorService.cs
│   │   │   └── DbWriterLoop.cs
│   │   └── Protocols/             # Industrial protocols
│   │       ├── OpcUa/
│   │       └── LibPlcTag/
│   │
│   ├── Application/               # Application layer - business services
│   │   └── Services/
│   │       ├── HealthAssessmentService.cs    # Health scoring
│   │       ├── MotorFaultDetectionService.cs # Motor diagnostics
│   │       ├── TrendPredictionService.cs     # Trend prediction
│   │       └── AuthService.cs                # Authentication
│   │
│   ├── Host.Api/                  # API host (port 5000)
│   │   ├── Endpoints/             # Minimal API endpoints
│   │   ├── Hubs/                  # SignalR hubs
│   │   ├── Services/              # Background services
│   │   └── appsettings.json       # Configuration
│   │
│   └── Host.Edge/                 # Edge data collection service
│
├── intellimaint-ui/               # React frontend (port 3000)
│   └── src/
│       ├── api/                   # API client
│       ├── components/            # Shared components
│       ├── pages/                 # Page components
│       ├── hooks/                 # Custom hooks
│       └── store/                 # State management
│
├── tests/                         # Test projects
│   ├── Unit/                      # Unit tests
│   └── Integration/               # Integration tests
│
├── docker/                        # Docker configuration
│   ├── docker-compose.yml
│   ├── Dockerfile.api
│   ├── Dockerfile.ui
│   └── init-scripts/              # DB initialization
│
└── docs/                          # Documentation
```

---

## API Reference | API 参考

### Authentication | 认证

```bash
# Login
curl -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username": "admin", "password": "admin123"}'

# Response
{
  "token": "eyJhbG...",
  "refreshToken": "abc123...",
  "expiresAt": "2026-01-13T12:00:00Z"
}
```

### Core Endpoints | 核心端点

| Endpoint | Method | Description | Auth |
|----------|--------|-------------|------|
| `/api/auth/login` | POST | Login | Public |
| `/api/auth/refresh` | POST | Refresh token | Public |
| `/api/devices` | GET/POST | Device management | All/Admin |
| `/api/tags` | GET/POST | Tag management | All/Admin |
| `/api/telemetry/latest` | GET | Latest telemetry | All |
| `/api/telemetry/query` | GET | Historical query | All |
| `/api/alarms` | GET | Alarm list | All |
| `/api/alarms/{id}/ack` | POST | Acknowledge alarm | Operator+ |
| `/api/alarms/aggregated` | GET | Aggregated alarms | All |
| `/api/alarm-rules` | GET/POST | Alarm rules | All/Operator+ |
| `/api/health-assessment` | GET | Health scores | All |
| `/api/motor/diagnosis/{id}` | GET | Motor diagnostics | All |
| `/api/users` | GET/POST | User management | Admin |
| `/api/audit-logs` | GET | Audit logs | Admin |

### SignalR Hub

```typescript
// Connect to hub
const connection = new signalR.HubConnectionBuilder()
  .withUrl('/hubs/telemetry', {
    accessTokenFactory: () => token
  })
  .withAutomaticReconnect()
  .build();

// Subscribe to devices
await connection.invoke('SubscribeAll');
// or: await connection.invoke('SubscribeDevice', deviceId);

// Receive real-time data
connection.on('ReceiveData', (data: TelemetryPoint[]) => {
  console.log('Received:', data);
});
```

---

## Health Assessment | 健康评估

The health index (0-100) is calculated from 4 weighted dimensions:

| Dimension | Weight | Description |
|-----------|--------|-------------|
| Deviation | 35% | Z-Score from learned baseline |
| Trend | 25% | Parameter change rate analysis |
| Stability | 20% | Coefficient of variation |
| Alarm | 20% | Open alarm count penalty |

### Health Levels

| Level | Range | Action |
|-------|-------|--------|
| Healthy | 85-100 | Normal operation |
| Attention | 70-84 | Enhanced monitoring |
| Warning | 50-69 | Schedule maintenance |
| Critical | 0-49 | Immediate action |

---

## Motor Fault Detection | 电机故障诊断

### Supported Fault Types | 支持的故障类型

| Category | Faults | Detection Method |
|----------|--------|------------------|
| Electrical | Overcurrent, Undervoltage, Harmonics | Parameter deviation |
| Mechanical | Unbalance, Rotor eccentricity, Misalignment | Current analysis |
| Bearing | Outer race, Inner race, Rolling element, Cage | FFT spectrum (BPFO/BPFI/BSF/FTF) |
| Thermal | Overheating, Insulation aging | Temperature monitoring |

### Diagnosis Output

```json
{
  "motorId": 1,
  "healthScore": 78,
  "faults": [
    {
      "type": "BearingOuterRace",
      "severity": "Moderate",
      "confidence": 0.85,
      "description": "Bearing outer race fault detected at 3.5x BPFO"
    }
  ],
  "recommendations": [
    "Schedule bearing inspection within 2 weeks"
  ]
}
```

---

## Configuration | 配置

### appsettings.json

```json
{
  "DatabaseProvider": "Sqlite",
  "ConnectionStrings": {
    "Sqlite": "Data Source=intellimaint.db",
    "TimescaleDb": "Host=localhost;Database=intellimaint;..."
  },
  "Jwt": {
    "SecretKey": "your-secret-key-minimum-32-characters",
    "Issuer": "IntelliMaint",
    "Audience": "IntelliMaint",
    "AccessTokenExpirationMinutes": 15,
    "RefreshTokenExpirationDays": 7
  },
  "HealthAssessment": {
    "EvaluationIntervalSeconds": 60,
    "BaselineLearningDays": 7,
    "DefaultTagImportance": "Normal"
  },
  "MotorDiagnosis": {
    "Enabled": true,
    "IntervalMinutes": 5
  }
}
```

### Environment Variables | 环境变量

| Variable | Description | Default |
|----------|-------------|---------|
| `DATABASE_PROVIDER` | Sqlite or TimescaleDb | Sqlite |
| `JWT_SECRET_KEY` | JWT signing key (32+ chars) | Required |
| `ASPNETCORE_ENVIRONMENT` | Development/Production | Development |

---

## Security | 安全特性

| Feature | Description |
|---------|-------------|
| JWT Authentication | 15min access + 7day refresh tokens |
| RBAC Authorization | Admin / Operator / Viewer roles |
| Rate Limiting | 100 requests/60s per IP |
| Password Security | BCrypt hashing |
| Account Lockout | 5 failed attempts = 15min lock |
| Audit Logging | Full operation trail with IP |

---

## Development | 开发指南

### Run Tests | 运行测试

```bash
# Unit tests
dotnet test tests/Unit

# Integration tests
dotnet test tests/Integration

# All tests with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Code Style | 代码规范

**C#**
- Async methods: `XxxAsync`
- Private fields: `_camelCase`
- Use `CancellationToken`
- Max 30 lines per method

**TypeScript**
- Functional components + Hooks
- Strict mode enabled
- Custom hooks: `useXxx`

### Git Commit Convention | 提交规范

```
<type>(<scope>): <description>

feat(api): add device batch import
fix(ui): fix chart color update
docs: update API documentation
```

---

## Roadmap | 路线图

### Completed | 已完成
- [x] Data collection (OPC UA + LibPlcTag)
- [x] Real-time monitoring (SignalR)
- [x] Alarm engine (5 types + aggregation)
- [x] Health assessment (4D scoring)
- [x] Motor fault detection (FFT)
- [x] Trend prediction
- [x] JWT + RBAC authentication
- [x] Docker deployment

### Planned | 规划中
- [ ] Modbus TCP/RTU protocol
- [ ] Mobile app (iOS/Android)
- [ ] Advanced anomaly detection
- [ ] Knowledge graph
- [ ] Multi-tenancy

---

## Contributing | 贡献指南

1. Fork the repository
2. Create feature branch: `git checkout -b feature/amazing-feature`
3. Commit changes: `git commit -m 'feat: add amazing feature'`
4. Push to branch: `git push origin feature/amazing-feature`
5. Open a Pull Request

---

## License | 许可证

MIT License - see [LICENSE](LICENSE) for details.

---

## Support | 支持

- Documentation: [docs/](docs/)
- Issues: [GitHub Issues](https://github.com/your-org/intellimaint-pro/issues)

---

**Built for industrial reliability.**
