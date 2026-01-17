# IntelliMaint Pro v65 API Documentation

## Overview

IntelliMaint Pro provides RESTful APIs for device management, data collection, health assessment, alarm handling, and predictive maintenance.

**Base URL**: `http://localhost:5000`
**Swagger UI**: `http://localhost:5000/swagger` (Development only)

## Authentication

All APIs (except login and health check) require JWT Bearer Token:

```http
Authorization: Bearer <access_token>
```

### Getting a Token

```bash
curl -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username": "admin", "password": "admin123"}'
```

## API Modules

| Module | Endpoint | Description | Doc |
|--------|----------|-------------|-----|
| Authentication | `/api/auth/*` | Login, Refresh Token | [auth](./authentication.md) |
| Devices | `/api/devices/*` | Device CRUD | [devices](./devices.md) |
| Tags | `/api/tags/*` | Tag Management | [tags](./tags.md) |
| Telemetry | `/api/telemetry/*` | Data Query | [telemetry](./telemetry.md) |
| Alarms | `/api/alarms/*` | Alarm Management | [alarms](./alarms.md) |
| Alarm Rules | `/api/alarm-rules/*` | Rule Configuration | [alarm-rules](./alarm-rules.md) |
| **Health Assessment** | `/api/health-assessment/*` | Device Health Scoring | [health](./health-assessment.md) |
| **Motor Diagnostics** | `/api/motors/*` | Motor Fault Detection | [motors](./motors.md) |
| **Predictions** | `/api/predictions/*` | Trend & RUL Prediction | [predictions](./predictions.md) |
| Users | `/api/users/*` | User Management | [users](./users.md) |
| Audit Logs | `/api/audit-logs/*` | Operation Audit | [audit-logs](./audit-logs.md) |
| System Health | `/api/health` | Health Check | - |

## Standard Response Format

**Success:**
```json
{
  "success": true,
  "data": { ... }
}
```

**Error:**
```json
{
  "success": false,
  "error": "Error message",
  "errorCode": "ERROR_CODE",
  "traceId": "0HN4...",
  "timestamp": 1704153600000
}
```

## Role Permissions

| Role | Description | Permissions |
|------|-------------|-------------|
| Admin | Full access | All operations including user management |
| Operator | Operations | Create/update devices, ack alarms, configure rules |
| Viewer | Read-only | View data, no modifications |

## v65 New Features

### Health Assessment API
- `GET /api/health-assessment` - Get all device health scores
- `GET /api/health-assessment/{deviceId}` - Get specific device health
- `POST /api/health-assessment/{deviceId}/learn` - Learn baseline
- 4-dimensional health scoring (Deviation, Trend, Stability, Alarm)

### Motor Diagnostics API
- `GET /api/motors/{id}/diagnosis` - FFT-based fault detection
- 15+ fault types (Bearing, Electrical, Mechanical, Thermal)
- Confidence scores and recommendations

### Prediction API
- `GET /api/predictions/{deviceId}/trend` - Trend prediction
- `GET /api/predictions/{deviceId}/rul` - Remaining useful life

### Enhanced Alarms
- Alarm aggregation (grouping similar alarms)
- `GET /api/alarms/aggregated` - List alarm groups
- `POST /api/alarms/groups/{groupId}/ack` - Acknowledge group

## SignalR Real-time Hub

**Endpoint**: `/hubs/telemetry`

```javascript
// Connect
const connection = new signalR.HubConnectionBuilder()
  .withUrl('/hubs/telemetry', {
    accessTokenFactory: () => token
  })
  .withAutomaticReconnect()
  .build();

// Subscribe
await connection.invoke('SubscribeAll');

// Receive data
connection.on('ReceiveData', (data) => {
  console.log('Telemetry:', data);
});
```

## Rate Limiting

- **Limit**: 100 requests / 60 seconds
- **Scope**: Per IP address
- **Response**: HTTP 429 Too Many Requests

## Error Codes

| Code | HTTP | Description |
|------|------|-------------|
| `INVALID_ARGUMENT` | 400 | Invalid parameter |
| `ARGUMENT_NULL` | 400 | Required parameter missing |
| `NOT_FOUND` | 404 | Resource not found |
| `FORBIDDEN` | 403 | Insufficient permissions |
| `UNAUTHORIZED` | 401 | Authentication required |
| `TIMEOUT` | 504 | Request timeout |
| `INTERNAL_ERROR` | 500 | Server error |

---

*Last Updated: 2026-01-13 | Version: v65*
