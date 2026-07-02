# AeroponicIOT API Reference

> **Base URL:** `http://localhost:5062` (development)  
> **Response format:** All endpoints return `ApiResponse<T>` — `{ success, message, data, timestamp }`  
> **Error format:** RFC 7231 Problem Details (`application/problem+json`)  
> **Authentication:** JWT Bearer token (see [Authentication](#authentication) section)

---

## Table of Contents

| # | Controller | Prefix |
|---|-----------|--------|
| 1 | [Authentication](#1-authentication) | `POST /api/authentication/*` |
| 2 | [Actuator](#2-actuator) | `api/actuator/*` |
| 3 | [AI Suggestion (via Automation)](#3-ai-suggestion) | `api/automation/analyze/*` |
| 4 | [Automation](#4-automation) | `api/automation/*` |
| 5 | [Crop](#5-crop) | `api/crop/*` |
| 6 | [Dashboard](#6-dashboard) | `api/dashboard/*` |
| 7 | [Debug](#7-debug) | `api/debug/*` |
| 8 | [Device](#8-device) | `api/device/*` |
| 9 | [Garden](#9-garden) | `api/garden/*` |
| 10 | [MQTT](#10-mqtt) | `api/mqtt/*` |
| 11 | [Notification](#11-notification) | `api/notification/*` |
| 12 | [Sensor](#12-sensor) | `api/sensor/*` |
| 13 | [Users](#13-users) | `api/users/*` |

---

## Authentication

All authenticated endpoints require a JWT Bearer token in the `Authorization` header:

```
Authorization: Bearer eyJhbGciOiJIUzI1NiIs...
```

**Roles:**
- `Farmer` — default role, manages own devices/crops/gardens
- `Administrator` — full system access

**Policies:**
| Policy | Roles allowed |
|--------|--------------|
| `[Authorize]` | Any authenticated user |
| `AdminOnly` | Administrator |
| `FarmerOrAdmin` | Farmer, Administrator |

---

## 1. Authentication

**Prefix:** `POST /api/authentication`  
**Default auth:** None (public with rate limiting)

### `POST /api/authentication/register`

Register a new user account.

**Rate limit:** `auth` policy (default 10 req / 60 s)

**Request body:**
```json
{
  "username": "string (required, 3-100 chars)",
  "email": "string (required, valid email)",
  "password": "string (required, 8-100 chars)",
  "role": "string (optional, only Admins can assign roles; default: \"Farmer\")"
}
```

**Response `200`:** `ApiResponse<{ user: { id, username, email, role }, token: string }>`

---

### `POST /api/authentication/login`

Authenticate and receive a JWT token.

**Rate limit:** `auth` policy

**Request body:**
```json
{
  "username": "string (required)",
  "password": "string (required)"
}
```

**Response `200`:** `ApiResponse<{ user: { id, username, email, role }, token: string, expiresAt: string }>`

---

### `GET /api/authentication/me`

Get the currently authenticated user's info.

**Auth:** `[Authorize]`

**Response `200`:** `ApiResponse<{ id, username, email, role, createdAt, lastLogin }>`

---

## 2. Actuator

**Prefix:** `api/actuator`  
**Auth:** `[Authorize]`

### `POST /api/actuator/control`

Send an ON/OFF/PULSE command to a device actuator.

**Request body:**
```json
{
  "macAddress": "string (required, MAC address of device)",
  "actuatorType": "int (0=Pump, 1=Fan, 2=Light, 3=Heater)",
  "action": "string (ON, OFF, or PULSE)"
}
```

**Response `200`:** `ApiResponse<{ success: true }>`  
**Response `404`:** Device not found  
**Response `403`:** Not your device

---

### `GET /api/actuator/logs/{deviceId}`

Get actuator logs for a device.

| Parameter | Type | Location | Description |
|-----------|------|----------|-------------|
| `deviceId` | int | Path | Device ID |
| `days` | int | Query | Look-back window (default: 7, max: 90) |

**Response `200`:** `ApiResponse<{ logs: ActuatorLog[] }>`

---

## 3. AI Suggestion

**Prefix:** `api/automation` (merged into Automation controller)  
**Auth:** `[Authorize(Policy = "AdminOnly")]`

### `POST /api/automation/analyze/{deviceId}`

Manually trigger an AI analysis of the latest sensor data for a device. The resulting suggestion is delivered as a notification to the device owner.

| Parameter | Type | Location | Description |
|-----------|------|----------|-------------|
| `deviceId` | int | Path | Device ID |

**Response `200`:** `ApiResponse<AiSuggestionResult | null>`

---

## 4. Automation

**Prefix:** `api/automation`  
**Auth:** `[Authorize]`

### `GET /api/automation/rules`

Get all automation rules for the current user (or all rules for Admins).

**Response `200`:** `ApiResponse<AutomationRuleDto[]>`

---

### `GET /api/automation/rules/{id}`

Get a single automation rule by ID.

| Parameter | Type | Location | Description |
|-----------|------|----------|-------------|
| `id` | int | Path | Rule ID |

**Response `200`:** `ApiResponse<AutomationRuleDto>`  
**Response `404`:** Rule not found

---

### `POST /api/automation/rules`

Create a new automation rule.

**Request body:**
```json
{
  "deviceId": "int (required)",
  "ruleName": "string (required, max 100 chars)",
  "ruleType": "int (0=Schedule, 1=Threshold, 2=Time-based)",
  "actuatorType": "int (0=Pump, 1=Fan, 2=Light, 3=Heater)",
  "action": "string (ON, OFF, PULSE)",
  "conditionParameter": "string? (pH, TDS, Temperature, Humidity)",
  "conditionValue": "decimal?",
  "conditionOperator": "string? (>, <, ==, >=, <=)",
  "scheduleTime": "string? (HH:mm)",
  "scheduleDays": "string? (comma-separated day names)",
  "durationMinutes": "int?",
  "priority": "int (1-10, default: 1)"
}
```

**Response `200`:** `ApiResponse<AutomationRuleDto>`

---

### `PUT /api/automation/rules/{id}`

Update an existing automation rule.

| Parameter | Type | Location | Description |
|-----------|------|----------|-------------|
| `id` | int | Path | Rule ID |

**Response `200`:** `ApiResponse<AutomationRuleDto>`

---

### `PUT /api/automation/rules/{id}/toggle`

Toggle a rule's active/inactive status.

| Parameter | Type | Location | Description |
|-----------|------|----------|-------------|
| `id` | int | Path | Rule ID |

**Response `200`:** `ApiResponse<AutomationRuleDto>`

---

### `DELETE /api/automation/rules/{id}`

Delete an automation rule.

| Parameter | Type | Location | Description |
|-----------|------|----------|-------------|
| `id` | int | Path | Rule ID |

**Response `200`:** `ApiResponse<null>`

---

### `GET /api/automation/rules/device/{deviceId}`

Get all automation rules for a specific device.

| Parameter | Type | Location | Description |
|-----------|------|----------|-------------|
| `deviceId` | int | Path | Device ID |

**Response `200`:** `ApiResponse<AutomationRuleDto[]>`

---

## 5. Crop

**Prefix:** `api/crop`  
**Auth:** `[Authorize]`

### `GET /api/crop`

Get all crops.

**Response `200`:** `ApiResponse<CropDto[]>`

---

### `GET /api/crop/{id}`

Get a crop by ID (includes stages and device count).

| Parameter | Type | Location | Description |
|-----------|------|----------|-------------|
| `id` | int | Path | Crop ID |

**Response `200`:** `ApiResponse<CropDto>`

---

### `POST /api/crop`

Create a new crop with stages.

**Request body:**
```json
{
  "name": "string (required, max 100 chars)",
  "description": "string?",
  "totalDaysEst": "int?",
  "stages": [
    {
      "stageName": "string",
      "dayStart": "int",
      "dayEnd": "int",
      "phMin": "decimal",
      "phMax": "decimal",
      "ppmMin": "int",
      "ppmMax": "int",
      "waterTempMin": "int",
      "waterTempMax": "int",
      "humidityMin": "int",
      "humidityMax": "int",
      "pumpOnMinutes": "int",
      "pumpOffMinutes": "int"
    }
  ]
}
```

**Response `200`:** `ApiResponse<CropDto>`

---

### `PUT /api/crop/{id}`

Update an existing crop.

| Parameter | Type | Location | Description |
|-----------|------|----------|-------------|
| `id` | int | Path | Crop ID |

**Response `200`:** `ApiResponse<CropDto>`

---

### `DELETE /api/crop/{id}`

Delete a crop. Fails if any devices are currently assigned to this crop.

| Parameter | Type | Location | Description |
|-----------|------|----------|-------------|
| `id` | int | Path | Crop ID |

**Response `200`:** `ApiResponse<null>`  
**Response `409`:** Crop has assigned devices

---

## 6. Dashboard

**Prefix:** `api/dashboard`  
**Auth:** `[Authorize]`

### `GET /api/dashboard/latest`

Get a paginated view of devices with their latest sensor readings and active alerts.

| Parameter | Type | Location | Description |
|-----------|------|----------|-------------|
| `gardenId` | int? | Query | Filter by garden |
| `page` | int | Query | Page number (default: 1) |
| `pageSize` | int | Query | Items per page (default: 50, max: 200) |

**Response `200`:**
```json
{
  "success": true,
  "data": {
    "devices": [
      {
        "id": "int",
        "name": "string",
        "macAddress": "string",
        "gardenId": "int?",
        "gardenName": "string?",
        "isActive": "bool",
        "lastSeen": "datetime?",
        "cropName": "string?",
        "latestSensorData": {
          "ph": "double?",
          "tds": "double?",
          "waterTemperature": "double?",
          "airHumidity": "double?",
          "lightIntensity": "double?"
        }
      }
    ],
    "activeAlerts": [ "Alert[]" ],
    "totalDevices": "int",
    "activeDevices": "int"
  }
}
```

---

### `GET /api/dashboard/kpi`

Get system-wide key performance indicators.

**Auth:** `[Authorize]`

**Response `200`:**
```json
{
  "success": true,
  "data": {
    "systemHealthPercent": "double",
    "averagePh": "double?",
    "averageTds": "double?",
    "averageTemperature": "double?",
    "activeDeviceCount": "int",
    "criticalAlertCount": "int",
    "totalAlerts": "int"
  }
}
```

---

## 7. Debug

**Prefix:** `api/debug`  
**Auth:** Development environment only

### `POST /api/debug/create-test-user`

Create a local development test user (`devuser` / `P@ssw0rd1`, Administrator role).

**Response `200`:** `ApiResponse<{ username, email, role }>`  
**Response `409`:** Test user already exists

---

## 8. Device

**Prefix:** `api/device`  
**Auth:** `[Authorize]` (exceptions noted)

### `GET /api/device`

Get all devices for the current user (or all devices for Admins).

**Response `200`:** `ApiResponse<DeviceDto[]>`

---

### `GET /api/device/pending`

Get all unclaimed/pending devices.

**Auth:** `[Authorize(Policy = "AdminOnly")]`

**Response `200`:** `ApiResponse<DeviceDto[]>`

---

### `POST /api/device/self-register`

Anonymous device self-registration (provisioning). Generates a claim code.

**Auth:** `[AllowAnonymous]`  
**Rate limit:** `device-onboarding` policy

**Headers:** `X-Device-Key: <shared_key>` (required unless authenticated)

**Request body:**
```json
{
  "macAddress": "string (required, MAC format)",
  "chipId": "string? (max 100 chars)",
  "firmwareVersion": "string? (max 50 chars)",
  "deviceName": "string? (max 100 chars)",
  "protocolType": "string? (\"wifi\" or \"zigbee\")"
}
```

**Response `200`:** `ApiResponse<{ claimCode: string, claimCodeExpiresAt: datetime }>`

---

### `POST /api/device/claim`

Claim a pending device using its claim code, and optionally assign it to a crop and garden.

**Auth:** `[Authorize]`  
**Rate limit:** `device-onboarding` policy

**Request body:**
```json
{
  "claimCode": "string (required, 16 chars)",
  "cropId": "int?",
  "gardenId": "int?"
}
```

**Response `200`:** `ApiResponse<DeviceDto>`

---

### `GET /api/device/{id}`

Get a device by ID.

| Parameter | Type | Location | Description |
|-----------|------|----------|-------------|
| `id` | int | Path | Device ID |

**Auth:** `[Authorize]` (Farmers can only access their own)

**Response `200`:** `ApiResponse<DeviceDto>`

---

### `POST /api/device`

Create a new device (admin provisioning).

**Auth:** `[Authorize]`

**Request body:**
```json
{
  "macAddress": "string (required)",
  "deviceName": "string?",
  "chipId": "string?",
  "firmwareVersion": "string?",
  "protocolType": "string? (\"wifi\" or \"zigbee\")",
  "status": "string?",
  "currentCropId": "int?",
  "gardenId": "int?",
  "userId": "int?"
}
```

**Response `200`:** `ApiResponse<DeviceDto>`

---

### `PUT /api/device/{id}`

Update a device.

| Parameter | Type | Location | Description |
|-----------|------|----------|-------------|
| `id` | int | Path | Device ID |

**Auth:** `[Authorize]`

**Response `200`:** `ApiResponse<DeviceDto>`

---

### `DELETE /api/device/{id}`

Delete a device and all associated sensor/actuator logs.

| Parameter | Type | Location | Description |
|-----------|------|----------|-------------|
| `id` | int | Path | Device ID |

**Auth:** `[Authorize]`

**Response `200`:** `ApiResponse<null>`

---

## 9. Garden

**Prefix:** `api/garden`  
**Auth:** `[Authorize]`

### `GET /api/garden`

Get all gardens.

**Response `200`:** `ApiResponse<GardenDto[]>`

---

### `GET /api/garden/{id}`

Get a garden by ID.

| Parameter | Type | Location | Description |
|-----------|------|----------|-------------|
| `id` | int | Path | Garden ID |

**Response `200`:** `ApiResponse<GardenDto>`

---

### `POST /api/garden`

Create a new garden.

**Request body:**
```json
{
  "name": "string (required, max 100 chars)",
  "location": "string? (max 200 chars)",
  "description": "string?"
}
```

**Response `200`:** `ApiResponse<GardenDto>`

---

### `PUT /api/garden/{id}`

Update a garden.

**Auth:** `[Authorize(Policy = "AdminOnly")]`

| Parameter | Type | Location | Description |
|-----------|------|----------|-------------|
| `id` | int | Path | Garden ID |

**Response `200`:** `ApiResponse<GardenDto>`

---

### `DELETE /api/garden/{id}`

Delete a garden. Devices are detached but not deleted.

**Auth:** `[Authorize(Policy = "AdminOnly")]`

| Parameter | Type | Location | Description |
|-----------|------|----------|-------------|
| `id` | int | Path | Garden ID |

**Response `200`:** `ApiResponse<null>`

---

### `GET /api/garden/{id}/devices`

Get all devices in a garden.

| Parameter | Type | Location | Description |
|-----------|------|----------|-------------|
| `id` | int | Path | Garden ID |

**Auth:** `[Authorize]` (Farmers see only their own devices in the garden)

**Response `200`:** `ApiResponse<DeviceDto[]>`

---

## 10. MQTT

**Prefix:** `api/mqtt`  
**Auth:** `[Authorize(Policy = "FarmerOrAdmin")]`

### `GET /api/mqtt/status`

Get MQTT broker connection status and configuration details.

**Response `200`:** `ApiResponse<{ connected, brokerAddress, port, tlsEnabled, zigbeeBridgeConnected, uptime }>`

---

### `POST /api/mqtt/publish`

Publish a raw MQTT message to a topic.

**Auth:** `[Authorize(Policy = "AdminOnly")]`

**Request body:**
```json
{
  "topic": "string (required, max 256 chars)",
  "payload": "string (required, max 4096 chars)",
  "retain": "bool (default: false)"
}
```

**Response `200`:** `ApiResponse<null>`

---

## 11. Notification

**Prefix:** `api/notification`  
**Auth:** `[Authorize]`

### `GET /api/notification/email-health`

Check email service health/connectivity.

**Auth:** `[Authorize(Policy = "AdminOnly")]`

| Parameter | Type | Location | Description |
|-----------|------|----------|-------------|
| `testConnectivity` | bool | Query | Whether to test SMTP connection (default: true) |

**Response `200`:** `ApiResponse<EmailHealthCheckResult>`

---

### `GET /api/notification/unread`

Get all unread notifications for the current user.

**Response `200`:**
```json
{
  "success": true,
  "data": {
    "unreadCount": "int",
    "notifications": [
      {
        "id": "int",
        "userId": "int?",
        "title": "string?",
        "message": "string?",
        "type": "int (0=Alert, 1=Warning, 2=Info, 3=Error)",
        "isRead": "bool",
        "createdAt": "datetime",
        "readAt": "datetime?"
      }
    ]
  }
}
```

---

### `POST /api/notification/{notificationId}/read`

Mark a specific notification as read.

| Parameter | Type | Location | Description |
|-----------|------|----------|-------------|
| `notificationId` | int | Path | Notification ID |

**Response `200`:** `ApiResponse<null>`

---

### `DELETE /api/notification/clear`

Clear (delete) all notifications for the current user.

**Response `200`:** `ApiResponse<null>`

---

### `POST /api/notification/test-email`

Send a test email to the current user's email address.

**Auth:** `[Authorize(Policy = "AdminOnly")]`

**Response `200`:** `ApiResponse<null>`

---

## 12. Sensor

**Prefix:** `api/sensor`  
**Auth:** `[AllowAnonymous]` with `X-Device-Key` header OR `[Authorize]`

### `POST /api/sensor`

Receive sensor data from a device. Devices can authenticate via the `X-Device-Key` header (shared key) or by presenting a valid JWT.

**Headers:** `X-Device-Key: <shared_key>` (optional — used when no JWT)

**Request body:**
```json
{
  "macAddress": "string (required, MAC address)",
  "ph": "double? (0-14)",
  "tds": "double? (0-50000 ppm)",
  "waterTemperature": "double? (-20 to 100 °C)",
  "airHumidity": "double? (0-100 %)",
  "lightIntensity": "double? (0-200000 lux)"
}
```

Out-of-range values are silently discarded and logged as warnings.

**Response `200`:** `ApiResponse<null>`

Processing includes:
1. Device lookup by MAC address
2. `LastSeen` timestamp update
3. Sensor log creation
4. Threshold check against crop stage optimal ranges → alert generation
5. Alert notification dispatch
6. AI suggestion analysis (if enabled)

---

## 13. Users

**Prefix:** `api/users`  
**Auth:** `[Authorize(Policy = "AdminOnly")]`

### `GET /api/users`

Get all registered users.

**Response `200`:** `ApiResponse<UserAdminDto[]>`

---

### `GET /api/users/{id}`

Get a user by ID.

| Parameter | Type | Location | Description |
|-----------|------|----------|-------------|
| `id` | int | Path | User ID |

**Response `200`:** `ApiResponse<UserAdminDto>`

---

### `PUT /api/users/{id}`

Update a user's role or details. Cannot demote yourself from Administrator.

| Parameter | Type | Location | Description |
|-----------|------|----------|-------------|
| `id` | int | Path | User ID |

**Request body:**
```json
{
  "username": "string?",
  "email": "string?",
  "role": "string?"
}
```

**Response `200`:** `ApiResponse<UserAdminDto>`

---

### `DELETE /api/users/{id}`

Delete a user and their associated notifications. Cannot delete your own account.

| Parameter | Type | Location | Description |
|-----------|------|----------|-------------|
| `id` | int | Path | User ID |

**Response `200`:** `ApiResponse<null>`

---

## Appendix: Common Error Codes

| Status Code | Meaning |
|-------------|---------|
| `200` | Success |
| `400` | Bad request (validation error, missing fields) |
| `401` | Unauthorized (missing or invalid JWT) |
| `403` | Forbidden (insufficient role/permissions) |
| `404` | Resource not found |
| `409` | Conflict (duplicate, resource in use) |
| `429` | Too many requests (rate limited) |
| `500` | Internal server error |

## Appendix: Authorization Matrix

| Controller | Anonymous | Authenticated | FarmerOrAdmin | AdminOnly |
|-----------|-----------|---------------|---------------|-----------|
| Authentication | `register`, `login` | `me` | — | — |
| Actuator | — | `control`, `logs/{id}` | — | — |
| AI Suggestion | — | — | — | `analyze/{id}` |
| Automation | — | All CRUD | — | — |
| Crop | — | All CRUD | — | — |
| Dashboard | — | `latest`, `kpi` | — | — |
| Debug | `create-test-user` (dev only) | — | — | — |
| Device | `self-register` | `claim`, CRUD (own) | — | `pending` |
| Garden | — | `getAll`, `getById`, `create`, `getDevices` | — | `update`, `delete` |
| MQTT | — | — | `status` | `publish` |
| Notification | — | `unread`, `read`, `clear` | — | `email-health`, `test-email` |
| Sensor | `POST /` (with device key) | `POST /` (with JWT) | — | — |
| Users | — | — | — | All CRUD |

## Appendix: Quick Links

- **Swagger UI:** `http://localhost:5062/swagger` (development only)
- **OpenAPI JSON:** `http://localhost:5062/openapi/v1.json`
- **Health checks:** `GET /health/live`, `GET /health/ready`

---

*Last updated: June 4, 2026*
