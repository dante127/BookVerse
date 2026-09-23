# BookVerse — Deployment & Docker Architecture

## 1. Containerized Topology

BookVerse is fully containerized and deployable via **Docker** and **Docker Compose**:

```
 ┌─────────────────────────────────────────────────────────┐
 │                      Docker Network                     │
 │                                                         │
 │  ┌─────────────────┐   port 1433       ┌─────────────┐ │
 │  │  bookverse-db   │ ◄──────────────── │ bookverse-  │ │
 │  │ (SQL Server 2022│                   │ api (.NET 10)│ │
 │  └─────────────────┘   port 6379       └──────┬──────┘ │
 │  ┌─────────────────┐          ◄───────────────┤        │
 │  │ bookverse-cache │                port 8080 │        │
 │  │ (Redis 7, auth) │                          ▼        │
 │  └─────────────────┘                   exposed: 5000   │
 └───────────────────────────────────────┬─────────────────┘
                                         │
                                Host maps 5000 → 8080
```

* SQL Server and Redis are **not published to the host** — only the API container exposes a port (`5000:8080`). For ad-hoc database access use `docker compose exec mssql sqlcmd ...`.
* Redis runs with `requirepass` sourced from the `REDIS_PASSWORD` env var; the same secret is wired into the API's Redis connection string.
* `MSSQL_PID` defaults to `Developer` (dev/test licensing). Set a licensed edition (`Standard`/`Enterprise`) for real production deployments.

---

## 2. Quickstart with Docker Compose

Prerequisite: copy `.env.example` to `.env` and fill `MSSQL_SA_PASSWORD`, `REDIS_PASSWORD`, `JWT_SECRET` (and optionally `SEED_ADMIN_EMAIL` / `SEED_ADMIN_PASSWORD` / `MSSQL_PID`). Missing required values fail compose fast.

Run the entire platform with one command (single source of truth at the repo root):
```bash
docker compose up -d --build
```

### Health Check Verification
```bash
# Liveness — no dependency checks, safe for orchestrator restart probes
curl http://localhost:5000/health

# Readiness — verifies the database connection before accepting traffic
curl http://localhost:5000/health/ready
```

Both endpoints answer with the bare status word (`Healthy` / `Degraded` / `Unhealthy`) and no provider or check-name details.

### Swagger UI
Swagger is served only in **Development** at `http://localhost:<api-port>/swagger`. Production builds do not expose it.

### TLS
The API container listens on plain HTTP. Terminate TLS upstream (reverse proxy / ingress) and set `ASPNETCORE_HTTPS_PORT` to enable HTTPS redirection; HSTS is emitted in every non-Development environment.

---

## 3. CI

Every push and pull request to `main` runs restore → Release build → the full test suite with code-coverage collection via [`.github/workflows/ci.yml`](../.github/workflows/ci.yml).
