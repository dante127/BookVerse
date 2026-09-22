# BookVerse — Deployment & Docker Architecture

## 1. Containerized Topology

BookVerse is fully containerized and deployable via **Docker** and **Docker Compose**:

```
 ┌─────────────────────────────────────────────────────────┐
 │                      Docker Network                     │
 │                                                         │
 │  ┌─────────────────┐   Port 1433   ┌─────────────────┐  │
 │  │  bookverse-db   │ ◄──────────── │  bookverse-api  │  │
 │  │ (SQL Server 2022│               │   (.NET 10 App) │  │
 │  └─────────────────┘               └────────┬────────┘  │
 │                                             │           │
 │  ┌─────────────────┐   Port 6379            │ Port 8080 │
 │  │ bookverse-cache │ ◄──────────────────────┘     ▲     │
 │  │    (Redis 7)    │                              │     │
 │  └─────────────────┘                              │     │
 └───────────────────────────────────────────────────┼─────┘
                                                     │
                                            Host Port 8080
```

---

## 2. Quickstart with Docker Compose

Run the entire platform with one command:
```bash
docker compose -f docker/docker-compose.yml up -d
```

### Health Check Verification
```bash
# Verify API liveness
curl -I http://localhost:8080/health

# Verify API readiness (SQL Server + Redis connectivity)
curl -I http://localhost:8080/health/ready
```

### Swagger UI
Navigate to `http://localhost:8080/swagger` in your browser.
