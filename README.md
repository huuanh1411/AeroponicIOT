# Smart Farm IoT Monitoring and Control System

A comprehensive IoT system for monitoring and controlling aeroponic farming environments using ASP.NET Core Web API and a modern web dashboard.

## Features

- **Real-time Sensor Monitoring**: Collect pH, TDS, temperature, and humidity data from IoT devices
- **Automatic Control**: Smart actuator control based on crop-specific thresholds
- **Manual Control**: Web-based manual control of pumps, fans, lights, and heaters
- **Crop Management**: Configurable crop types with growth stages and environmental parameters
- **Alert System**: Automatic alerts for out-of-range environmental conditions
- **Web Dashboard**: Modern, responsive dashboard for monitoring and control
- **Notification System**: Email & dashboard notifications for alerts and warnings
- **Analytics & Charts**: Real-time graphs and historical data analysis with Chart.js
- **Automation Rules**: Schedule-based and threshold-based automatic device control
- **REST API**: Complete API for integration with IoT devices
- **MQTT Broker**: Built-in MQTT broker for real-time IoT device communication
- **User Authentication**: JWT-based login and role-based access control (Farmer, Administrator)

## Architecture

| Layer | Technology |
|-------|-----------|
| **Backend** | ASP.NET Core 10 Web API with Entity Framework Core |
| **IoT Messaging** | MQTT Broker (MQTTnet) on port 1883 |
| **Database** | SQL Server |
| **Frontend** | HTML5, CSS3, JavaScript (Vanilla) |
| **Authentication** | JWT with role-based access control |
| **API** | RESTful design with CORS enabled |

## Quick Start

### Prerequisites
- .NET 10 SDK

### Installation & Run

```bash
cd AeroponicIOT
dotnet restore
dotnet run
```

Access the dashboard at `http://localhost:5062`

On startup, EF Core migrations are applied automatically if the database is available.

## API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/sensor` | POST | Receive sensor data from IoT devices (JWT or `X-Device-Key`) |
| `/api/dashboard/latest` | GET | Get latest dashboard data |
| `/api/dashboard/history/{deviceId}` | GET | Get device sensor history |
| `/api/actuator/control` | POST | Send control commands to devices |
| `/api/actuator/logs/{deviceId}` | GET | Get actuator command logs |
| `/api/mqtt/status` | GET | Check MQTT broker status |
| `/api/mqtt/publish` | POST | Publish message to MQTT topic |

### HTTP Sensor Ingestion Authentication

For direct HTTP sensor ingestion, use one of:
- JWT Bearer token (regular authenticated API flow)
- `X-Device-Key` header that matches `Provisioning:SharedKey`

Example:
```bash
curl -X POST http://localhost:5062/api/sensor \
  -H "Content-Type: application/json" \
  -H "X-Device-Key: YOUR_PROVISIONING_SHARED_KEY" \
  -d '{
    "macAddress": "AA:BB:CC:DD:EE:01",
    "ph": 6.2,
    "tds": 950,
    "waterTemperature": 22.5,
    "airHumidity": 68.0
  }'
```

### Authentication Response Compatibility

Authentication endpoints now return a standard success envelope by default:

```json
{
  "success": true,
  "message": "Login successful",
  "data": {
    "token": "...",
    "username": "farmer-1",
    "role": "Farmer",
    "userId": 1,
    "expiresAt": "2026-03-23T00:00:00Z"
  },
  "timestamp": "2026-03-23T00:00:00Z"
}
```

For backward compatibility with legacy clients, send either:
- Query: `?legacyAuthResponse=true`
- Header: `X-Legacy-Auth-Response: true`

When enabled, `/api/authentication/register`, `/api/authentication/login`, and `/api/authentication/me` return their previous payload shape.

### CORS Configuration

The API uses an allowlist from `Cors:AllowedOrigins`.
- Development: if no origins are configured, permissive CORS is enabled.
- Non-development: if no origins are configured, cross-origin requests are blocked.

Environment-variable example:
`Cors__AllowedOrigins__0=https://your-frontend.example.com`

## Health And Readiness

The API exposes structured JSON health endpoints:

- `GET /health/live`: Liveness probe (process is running)
- `GET /health/ready`: Readiness probe (database + MQTT dependencies)
- `GET /health`: Alias for readiness

Example response:

```json
{
  "status": "Healthy",
  "totalDurationMs": 3.14,
  "timestamp": "2026-03-25T12:00:00Z",
  "checks": [
    {
      "name": "database",
      "status": "Healthy",
      "description": "Database is reachable",
      "durationMs": 1.2,
      "error": null,
      "data": {}
    }
  ]
}
```

## Redis-Backed Onboarding Protection

Device onboarding attempt tracking uses distributed cache.

- If `Redis:Configuration` is set, Redis is used.
- If not set, the app falls back to in-memory distributed cache.

Run with Redis via Docker Compose profile:

```bash
REDIS_CONFIGURATION=redis:6379 docker compose --profile redis up --build
```

## OpenTelemetry

OpenTelemetry tracing and metrics are enabled with configurable sampling and path exclusions.

Configuration keys:

- `OpenTelemetry:ServiceName`
- `OpenTelemetry:Tracing:SampleRatio` (0.0 to 1.0)
- `OpenTelemetry:ExcludedPaths` (e.g., health endpoints)
- `OpenTelemetry:Otlp:Endpoint` (optional OTLP endpoint)

Environment-variable example:

```bash
OpenTelemetry__ServiceName=AeroponicIOT \
OpenTelemetry__Tracing__SampleRatio=0.25 \
OpenTelemetry__Otlp__Endpoint=http://otel-collector:4317
```

## MQTT Integration

The system includes a built-in MQTT broker for real-time IoT device communication.

### MQTT Broker Details
- **Default Port**: 1883
- **Default Host**: localhost
- **Status Endpoint**: `GET /api/mqtt/status`

### Device Communication Topics

#### Publish Sensor Data (Device → Server)
**Topic**: `devices/{macAddress}/sensor`

**Payload**:
```json
{
  "macAddress": "AA:BB:CC:DD:EE:01",
  "ph": 6.2,
  "tds": 950,
  "waterTemperature": 22.5,
  "airHumidity": 68.0,
  "timestamp": "2026-02-25T10:30:00Z"
}
```

#### Receive Control Commands (Server → Device)
**Topic**: `devices/{macAddress}/control`

**Payload**:
```json
{
  "deviceId": 1,
  "deviceName": "Unit-01",
  "macAddress": "AA:BB:CC:DD:EE:01",
  "actuatorType": 0,
  "action": "ON",
  "timestamp": "2026-02-25T10:30:00Z"
}
```

### Actuator Types
- `0` - Pump
- `1` - Fan
- `2` - Light
- `3` - Heater

### ESP32/ESP8266 Example (Arduino)

```cpp
#include <WiFi.h>
#include <PubSubClient.h>
#include <ArduinoJson.h>

const char* ssid = "YOUR_SSID";
const char* password = "YOUR_PASSWORD";
const char* mqtt_server = "192.168.x.x";  // Server IP
const int mqtt_port = 1883;
const char* device_mac = "AA:BB:CC:DD:EE:01";

WiFiClient espClient;
PubSubClient client(espClient);

void setup() {
  Serial.begin(115200);
  setup_wifi();
  client.setServer(mqtt_server, mqtt_port);
  client.setCallback(callback);
}

void setup_wifi() {
  WiFi.begin(ssid, password);
  while (WiFi.status() != WL_CONNECTED) {
    delay(500);
  }
  Serial.println(WiFi.localIP());
}

void callback(char* topic, byte* payload, unsigned int length) {
  StaticJsonDocument<200> doc;
  deserializeJson(doc, payload, length);
  
  const char* action = doc["action"];
  int actuatorType = doc["actuatorType"];
  
  // Execute control command
  if (strcmp(action, "ON") == 0) {
    // Turn on actuator
  } else {
    // Turn off actuator
  }
}

void loop() {
  if (!client.connected()) {
    reconnect();
  }
  client.loop();
  
  // Read sensors every 5 seconds
  if (millis() % 5000 == 0) {
    publishSensorData();
  }
}

void publishSensorData() {
  StaticJsonDocument<300> doc;
  doc["macAddress"] = device_mac;
  doc["ph"] = readPH();
  doc["tds"] = readTDS();
  doc["waterTemperature"] = readTemp();
  doc["airHumidity"] = readHumidity();
  
  char topic[50];
  sprintf(topic, "devices/%s/sensor", device_mac);
  
  char payload[512];
  serializeJson(doc, payload);
  client.publish(topic, payload);
}

void reconnect() {
  while (!client.connected()) {
    if (client.connect(device_mac)) {
      char topic[50];
      sprintf(topic, "devices/%s/control", device_mac);
      client.subscribe(topic);
    } else {
      delay(5000);
    }
  }
}
```

### MQTT Testing

#### Check Broker Status
```bash
curl http://localhost:5062/api/mqtt/status
```

#### Publish Test Message via REST API
```bash
curl -X POST http://localhost:5062/api/mqtt/publish \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -d '{
    "topic": "devices/AA:BB:CC:DD:EE:01/control",
    "payload": "{\"action\":\"ON\",\"actuatorType\":0}",
    "retain": true
  }'
```

#### Using MQTT Client (mosquitto_pub)
```bash
mosquitto_pub -h localhost -p 1883 \
  -t "devices/AA:BB:CC:DD:EE:01/sensor" \
  -m '{"macAddress":"AA:BB:CC:DD:EE:01","ph":6.2,"tds":950,"waterTemperature":22.5,"airHumidity":68.0}'
```

## Notification System

The system provides multi-channel notifications to keep users informed of alerts and events.

### Notification Channels

1. **Dashboard Notifications**
   - Real-time in-app notifications
   - Notification bell with badge count
   - Dropdown list of unread notifications
   - Mark as read or clear all notifications

2. **Email Notifications**
   - Automatic email alerts for system events
   - Detailed HTML-formatted emails
  - Requires SMTP configuration and `Enabled: true`

### Configuration Email Notifications

Update `appsettings.json` with your SMTP server:

```json
{
  "EmailSettings": {
    "SmtpHost": "smtp.gmail.com",
    "SmtpPort": "587",
    "SmtpUsername": "your-email@gmail.com",
    "SmtpPassword": "your-app-password",
    "FromEmail": "noreply@smartfarmiot.com",
    "FromName": "Smart Farm IoT System",
    "Enabled": true
  }
}
```

### Gmail Configuration
For Gmail SMTP:
1. Enable 2-factor authentication
2. Generate app password: https://myaccount.google.com/apppasswords
3. Use app password in `SmtpPassword` field

### Notification API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/notification/unread` | GET | Get unread notifications for current user |
| `/api/notification/{id}/read` | POST | Mark notification as read |
| `/api/notification/clear` | DELETE | Clear all notifications for current user |
| `/api/notification/email-health?testConnectivity=true` | GET | Check email configuration and optional SMTP connectivity/auth (admin only) |
| `/api/notification/test-email` | POST | Send test email notification |

### Notification Types

- **Alert**: Critical system issues (red)
- **Warning**: Important warnings (yellow)
- **Error**: Error messages (red)
- **Info**: Informational messages (blue)

### Trigger Events

Notifications are automatically sent when:
- Sensor values exceed safe thresholds
- Device goes offline
- System errors occur
- Manual control commands are issued

### Dashboard Notification Bell

The notification bell in the top-right of the dashboard shows:
- 🔔 Bell icon
- Red badge with unread count
- Dropdown list with all unread notifications
- Quick-access mark as read buttons

## Analytics & Charts

Access real-time and historical sensor data visualizations at `http://localhost:5062/charts.html`

### Features

- **Time Range Selection**: Choose from 24 hours, 3 days, 7 days, or 30 days of data
- **Multi-Parameter Charts**: Separate graphs for pH, TDS, temperature, and humidity
- **Statistics Panel**: View min, max, and average values for each sensor
- **Combined View**: Compare all sensor parameters in a single multi-axis chart
- **Recent Alerts**: View the 10 most recent system alerts and events
- **Device Selection**: Switch between different devices to view their data

### Chart Types

1. **pH Level Trend** - Tracks water chemistry over time
2. **TDS (Nutrient) Level** - Monitors nutrient concentration in ppm
3. **Water Temperature** - Shows thermal conditions in the aeroponic system
4. **Air Humidity** - Displays environmental humidity levels
5. **Combined Sensors** - Multi-axis view of all parameters for correlation analysis

### Navigation

- Click the **📊 Analytics** button on the main dashboard to access charts
- Use the device dropdown to switch between IoT devices
- Select time range to view different historical periods
- Click **🔄 Refresh** to reload latest data
- Use **← Back to Dashboard** to return to monitoring

## Automation Rules

Create automated control rules to manage devices based on schedules or sensor thresholds.

Access automation at `http://localhost:5062/automation.html`

### Rule Types

1. **Schedule (Time-based)**
   - Execute at specific times of day
   - Repeat on selected days of the week
   - Set duration for how long the device runs
   - Perfect for daily irrigation at sunrise

2. **Threshold (Sensor-based)**
   - Trigger when sensor values exceed limits
   - Monitor: pH, TDS, Temperature, Humidity
   - Set operators: >, <, ==, >=, <=
   - Example: Turn on fan when temperature > 28°C

3. **Timer (Fixed Duration)**
   - Run device for a specific duration
   - Immediate activation with auto-stop
   - Useful for periodic maintenance tasks

### Creating Rules

1. Enter a descriptive **Rule Name**
2. Select **Device** and **Actuator** (Pump, Fan, Light, Heater)
3. Choose **Action**: ON, OFF, or PULSE (alternating 1 min intervals)
4. Configure rule type-specific parameters
5. Set **Priority** (1-10) for execution order when multiple rules trigger
6. Click **➕ Create Rule**

### Rule Management

- **Active Rules** - Currently enabled rules with status
- **Inactive Rules** - Disabled rules (can be re-enabled)
- **Recent Executions** - History of when rules ran
- **Toggle Rules** - Enable/disable without deleting
- **Delete Rules** - Remove rules no longer needed

### Examples

**Example 1: Daily Irrigation**
- Type: Schedule
- Time: 06:00 AM
- Days: Monday, Wednesday, Friday
- Device: Pump
- Duration: 15 minutes
- Action: ON

**Example 2: Temperature Control**
- Type: Threshold
- Parameter: Temperature
- Condition: > 28°C
- Device: Fan
- Duration: 30 minutes (auto-off)
- Action: ON

### API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/automation/rules` | GET | List all rules |
| `/api/automation/rules` | POST | Create new rule |
| `/api/automation/rules/{id}` | PUT | Update rule |
| `/api/automation/rules/{id}` | DELETE | Delete rule |
| `/api/automation/rules/{id}/toggle` | PUT | Enable/disable rule |
| `/api/automation/rules/device/{deviceId}` | GET | Get rules for device |

## Testing the API

### Using VS Code REST Client
Use the included `AeroponicIOT.http` file with the REST Client extension.

### Sample Request - Send Sensor Data
```json
POST http://localhost:5062/api/sensor

{
  "macAddress": "AA:BB:CC:DD:EE:01",
  "ph": 6.2,
  "tds": 950,
  "waterTemperature": 22.5,
  "airHumidity": 68.0
}
```

### Using cURL
```bash
curl -X POST "http://localhost:5062/api/sensor" \
  -H "Content-Type: application/json" \
  -d '{"macAddress": "AA:BB:CC:DD:EE:01", "ph": 6.2, "tds": 950, "waterTemperature": 22.5, "airHumidity": 68.0}'
```

### Test Email Notification
```bash
curl -X POST http://localhost:5062/api/notification/test-email \
  -H "Authorization: Bearer YOUR_JWT_TOKEN"
```

## Database

### Configuration
- Default (development): SQL Server LocalDB via `appsettings.json`
- Docker deployment: SQL Server container via `docker-compose.yml`
- Custom database: Update `ConnectionStrings:DefaultConnection`

### Main Tables
- **Devices**: IoT device information
- **Crops**: Crop types and configurations
- **CropStages**: Growth stages with environmental parameters
- **SensorLogs**: Historical sensor readings
- **ActuatorLogs**: Control command history
- **Alerts**: System alerts and notifications

### Pre-configured Crop Stages (Lettuce)
- **Germination**: pH 5.5-6.5, TDS 500-800 ppm, Temp 18-24°C
- **Vegetative**: pH 5.8-6.2, TDS 800-1200 ppm, Temp 20-25°C
- **Harvest**: pH 6.0-6.5, TDS 600-900 ppm, Temp 18-22°C

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Port already in use | Change port in `Properties/launchSettings.json` |
| Database issues | Verify SQL Server connectivity and connection string, then restart app |
| Build errors | Run `dotnet clean && dotnet build` |
| .NET version | Verify with `dotnet --version` (requires 8.x.x) |

## Docker Deployment

### Using docker-compose
```bash
docker-compose up --build
```
API available at `http://localhost:5062`, SQL Server at `1433`

### Manual Docker Build
```bash
docker build -t aeroponiciot:latest .
docker run -e ConnectionStrings__DefaultConnection="Server=<host>;Database=AeroponicIOT;User Id=sa;Password=Your_password123;" -p 5062:80 aeroponiciot:latest
```

## Security Notes

- Development: CORS allows all origins
- Production: Restrict CORS, implement authentication, use HTTPS
- Validate all input data
- Apply API rate limiting

## Future Enhancements

- Irrigation scheduling and automation engine
- Real-time notifications (WebSocket/SignalR)
- Advanced analytics and reporting
- Mobile application
- Cloud IoT platform integration (AWS IoT, Azure IoT Hub)
- Machine learning for predictive control
- Integration with weather forecast services
- Automatic nutrient optimization

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Submit a pull request

## License

MIT License
