# AeroponicIOT - Deployment Ready Guide

## Overview
This document describes the **production-ready AeroponicIOT application** following completion of all 4 optimization phases. The application is fully containerized, horizontally scalable, with comprehensive health checks and graceful shutdown support.

## System Architecture

### Components
- **API Server** (ASP.NET Core 10 + C# 13)
  - Runs on port 5062 (configurable via docker-compose)
  - Connects to external MQTT broker, Redis cache, SQL Server database
  - Includes background job queue for async processing (Hangfire)

- **External MQTT Broker** (EMQX 5.4)
  - Port 1883 (MQTT protocol)
  - Port 8883 (MQTT + TLS)
  - Port 18083 (Management UI)
  - Managed separately from API for horizontal scaling

- **SQL Server 2022**
  - Database with auto-migrations on startup
  - Health checks via SQL query
  - Connection pooling enabled (256 pool size, 600s idle)

- **Redis 7** (Alpine)
  - Distributed cache for dashboard queries
  - 5-minute TTL for dashboard data
  - Optional - falls back to no-op if not configured

- **Zigbee2MQTT** (Optional)
  - Enabled via `--profile zigbee` flag
  - Connects to EMQX broker
  - Bridge device events to Zigbee protocol

## Health Check Endpoints

All endpoints are accessible at `http://localhost:5062/health/`:

### `/health/live` (Liveness Probe)
- **Purpose**: Indicates if the process is alive
- **Returns**: 200 OK (always, unless process is dead)
- **Usage**: Pod restart trigger in Kubernetes

```json
{"status":"alive"}
```

### `/health/ready` (Readiness Probe)
- **Purpose**: Indicates if the application is ready for traffic
- **Returns**: 
  - 200 OK if all dependencies (DB, MQTT, Redis) are healthy
  - 503 Service Unavailable if any dependency fails
- **Usage**: Load balancer traffic routing decision

```json
{
  "status": "ready",
  "checks": {
    "database": true,
    "redis": true,
    "mqtt": true
  },
  "timestamp": "2025-01-15T10:30:45.123Z"
}
```

### `/health/startup` (Startup Probe)
- **Purpose**: Indicates if the application has completed initialization
- **Returns**: 
  - 200 OK after database migrations complete
  - 503 Service Unavailable if migrations are pending
- **Usage**: Deployment validation before marking pod as ready

```json
{
  "status": "started",
  "timestamp": "2025-01-15T10:30:45.123Z"
}
```

## Deployment Configuration

### Environment Variables (Required)

```env
# Database
DB_CONNECTION_STRING=Server=db,1433;Database=AeroponicIOT;User Id=sa;Password=AeroponicIOT_DB_P@ss123;TrustServerCertificate=true;

# Authentication
JWT_SECRET_KEY=your-secret-key-min-32-characters-long
PROVISIONING_SHARED_KEY=your-provisioning-key

# MQTT Broker
MQTT_HOST=aeroponiciot-mqtt
MQTT_PORT=1883
MQTT_ADMIN_USERNAME=admin
MQTT_ADMIN_PASSWORD=public

# Redis Cache
REDIS_CONFIGURATION=aeroponiciot-redis:6379

# Optional: Email Notifications
EMAIL_ENABLED=false
SMTP_HOST=smtp.gmail.com
SMTP_PORT=587
```

### Docker Compose Quick Start

```bash
# Development environment (with Swagger UI)
docker compose up -d

# Production environment (with Zigbee support)
docker compose --profile zigbee up -d

# Stop all services
docker compose down

# View logs
docker compose logs -f app
docker compose logs -f aeroponiciot-mqtt
docker compose logs -f db
```

### Kubernetes Deployment Example

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: aeroponiciot
spec:
  replicas: 3
  selector:
    matchLabels:
      app: aeroponiciot
  template:
    metadata:
      labels:
        app: aeroponiciot
    spec:
      containers:
      - name: aeroponiciot
        image: n3mnuonghn/aeroponiciot:1.0.0
        ports:
        - containerPort: 80
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: Production
        - name: DB_CONNECTION_STRING
          valueFrom:
            secretKeyRef:
              name: aeroponiciot-secrets
              key: db-connection-string
        - name: JWT_SECRET_KEY
          valueFrom:
            secretKeyRef:
              name: aeroponiciot-secrets
              key: jwt-secret
        livenessProbe:
          httpGet:
            path: /health/live
            port: 80
          initialDelaySeconds: 10
          periodSeconds: 30
          timeoutSeconds: 3
          failureThreshold: 3
        readinessProbe:
          httpGet:
            path: /health/ready
            port: 80
          initialDelaySeconds: 20
          periodSeconds: 10
          timeoutSeconds: 3
          failureThreshold: 3
        startupProbe:
          httpGet:
            path: /health/startup
            port: 80
          failureThreshold: 30
          periodSeconds: 2
```

## Graceful Shutdown

The application supports graceful shutdown:

1. **On `SIGTERM` signal**: Application stops accepting new requests
2. **Waits up to 5 seconds** for in-flight requests to complete
3. **Gracefully disconnects** from MQTT broker
4. **Drains Hangfire background jobs** (configured to persist on disk)
5. **Exits cleanly** with exit code 0

### Docker Swarm / Kubernetes Behavior

- **Grace period**: 30+ seconds (default)
- **Application shutdown time**: ~5 seconds
- **Job persistence**: All queued jobs saved to SQL Server, resume on restart

## Performance Optimizations

### 1. Authorization Consolidation (Phase 1)
- Centralized `IResourceOwnershipService` eliminating 50+ lines of duplication
- Single-line permission checks in all controllers
- Consistent admin/owner verification pattern

### 2. Async Background Job Queue (Phase 2)
- **Hangfire** for reliable job execution
- Sensor data ingestion returns immediately
- Alerts and AI analysis run asynchronously without blocking
- Auto-retry on failure with exponential backoff

### 3. External Services Architecture (Phase 3)
- **MQTT client** connecting to external EMQX broker (horizontally scalable)
- **Redis caching** with 5-minute TTL for dashboard queries
- Fallback to no-op cache if Redis unavailable
- Database connection pooling (256 connections, 600s idle timeout)

### 4. Production Hardening (Phase 4)
- **Multi-stage Docker build** with chiseled runtime (reduced image size)
- **Non-root user** execution (security best practice)
- **Health check probes** for container orchestration
- **Graceful shutdown** support for zero-downtime deployments

## Monitoring & Observability

### Health Checks
- Automated by container orchestration (Kubernetes, Docker Swarm)
- Visual status at `/health/ready` and `/health/startup`
- Structured JSON responses for parsing

### Hangfire Dashboard
- Accessible at `http://localhost:5062/hangfire` (admin-only)
- Job history, retry counts, execution times
- Automatic job cleanup after 30 days

### Logs
- Structured logging with correlation IDs
- All middleware operations logged
- Searchable by device ID, user ID, job ID

## Deployment Checklist

- [ ] All secrets configured in environment variables
- [ ] Database connection string validated
- [ ] MQTT broker accessible on configured host:port
- [ ] Redis cache configured (optional but recommended)
- [ ] JWT secret key is 32+ characters and strong
- [ ] Provisioning shared key set for device registration
- [ ] SMTP configured if email notifications enabled
- [ ] Health checks responding 200 OK
- [ ] Hangfire dashboard accessible to admins only
- [ ] Graceful shutdown tested (SIGTERM signal)
- [ ] Log aggregation configured (if applicable)
- [ ] Database backups configured
- [ ] SSL/TLS certificates configured for MQTT (if TLS enabled)

## Troubleshooting

### Application fails to start
1. Check database connectivity: `telnet db 1433`
2. Check MQTT broker: `telnet aeroponiciot-mqtt 1883`
3. Verify all required environment variables are set
4. Check application logs: `docker compose logs app`

### Health check fails
1. Read `/health/ready` endpoint for specific component failures
2. Check MQTT broker status: `/health/ready` shows `"mqtt": false`
3. Check database connectivity: `/health/ready` shows `"database": false`
4. Check Redis connectivity: `/health/ready` shows `"redis": false`

### Sensors not ingesting
1. Verify MQTT connectivity in logs: `MQTT client connected`
2. Check device MAC address is registered
3. Verify sensor topic format: `devices/{macAddress}/telemetry`
4. Check device protocol type matches (WiFi, Zigbee, etc.)

### Performance degradation
1. Check Hangfire dashboard for stuck jobs
2. Monitor database query times (enable query logging)
3. Verify Redis cache is working (check hit rates)
4. Review connection pool exhaustion warnings in logs

## References

- [ASP.NET Core Health Checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks)
- [Kubernetes Probes](https://kubernetes.io/docs/tasks/configure-pod-container/configure-liveness-readiness-startup-probes/)
- [Hangfire Documentation](https://docs.hangfire.io/)
- [EMQX Documentation](https://www.emqx.io/docs/)
- [Redis Caching](https://redis.io/)
