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

---

## 4. Reverse Proxy & Client IP (Nginx)

The rate limiters and stored client IPs partition on `Connection.RemoteIpAddress`. When a reverse proxy (Nginx, a cloud LB, or an ingress) sits in front, that socket address is the **proxy**, not the caller — so every user would share one rate-limit bucket and audit logs would record the wrong IP.

The API honors `X-Forwarded-For` / `X-Forwarded-Proto`, but **only from explicitly trusted proxies** (secure by default; untrusted forwarded headers are ignored). Configure the proxy's address or subnet:

| Setting | Example | Source |
|---|---|---|
| `Forwarding:KnownProxies[]` | `10.0.0.5` | exact proxy IP |
| `Forwarding:KnownNetworks[]` | `172.16.0.0/12` | proxy subnet (Docker/nginx) |

With Docker Compose, set `TRUSTED_PROXY_NETWORK` in `.env` to the subnet your Nginx container reaches the API over (e.g. the compose network range). Make sure Nginx **sets** (overwrites, not appends) `X-Forwarded-For`, and set `ASPNETCORE_HTTPS_PORT` when TLS terminates at the proxy.

A typical Nginx location block:

```nginx
location / {
    proxy_pass http://bookverse-api:8080;
    proxy_set_header Host              $host;
    proxy_set_header X-Real-IP         $remote_addr;
    proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;  # single trusted hop
    proxy_set_header X-Forwarded-Proto $scheme;
}
```

---

## 5. Metrics & Observability (OTLP)

The `BookVerse` OpenTelemetry `Meter` publishes cache hit/miss ratios and background-worker health (iteration counts, error counts, iteration-duration histograms), alongside the built-in runtime, ASP.NET Core and HTTP-client meters. A metrics reader is always attached; export happens only when a collector endpoint is configured, so a machine with no collector pays no background push cost:

```bash
# .env / environment — any OTLP/gRPC collector
OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
# optional auth/resource attributes are honored by the exporter as usual
OTEL_EXPORTER_OTLP_HEADERS="api-key=..."
```

When unset, the instruments are still collected in-process (and observed by the unit tests via `MeterListener`) but nothing is shipped off the host.
