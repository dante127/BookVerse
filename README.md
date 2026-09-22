# BookVerse 📚✨

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![C# 14](https://img.shields.io/badge/C%23-14-239120?style=for-the-badge&logo=c-sharp&logoColor=white)](https://learn.microsoft.com/en-us/dotnet/csharp/)
[![EF Core 10](https://img.shields.io/badge/EF%20Core-10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://learn.microsoft.com/en-us/ef/core/)
[![SQL Server 2022](https://img.shields.io/badge/SQL%20Server-2022-CC292B?style=for-the-badge&logo=microsoft-sql-server&logoColor=white)](https://www.microsoft.com/sql-server)
[![Redis 7](https://img.shields.io/badge/Redis-7.2-DC382D?style=for-the-badge&logo=redis&logoColor=white)](https://redis.io/)
[![Clean Architecture](https://img.shields.io/badge/Architecture-Clean%20%2F%20DDD%20%2F%20CQRS-blue?style=for-the-badge)](https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html)
[![Tests Passing](https://img.shields.io/badge/Tests-45%2F45%20Passed%20(100%25)-success?style=for-the-badge&logo=checkmarx&logoColor=white)](tests/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=for-the-badge)](LICENSE)

> **BookVerse** is a modern, production-grade enterprise backend platform for discovering, managing, reading, reviewing, and recommending books and novels. Designed and implemented from the ground up as a showcase of **Senior .NET Software Architecture**, this platform features **Clean Architecture**, **Domain-Driven Design (DDD)**, **CQRS with MediatR**, **EF Core 10**, **SQL Server Full-Text Search**, **Redis Caching with Resilient Fallback**, **Deterministic Multi-Signal Recommendation Engine**, **Optimistic Concurrency Control**, and **Automated Background Processing**.

---

## 🏛️ System Architecture

BookVerse strictly adheres to Clean Architecture and DDD principles. Dependencies point exclusively inwards toward the pure Domain core.

```
                    ┌─────────────────────────┐
                    │     BookVerse.Api       │ (Controllers, Middlewares, OpenAPI, Auth)
                    └────────────┬────────────┘
                                 │
                    ┌────────────▼────────────┐
                    │  BookVerse.Application  │ (CQRS Commands, Queries, Behaviors, DTOs)
                    └────────────┬────────────┘
                                 │
         ┌───────────────────────┴───────────────────────┐
         │                                               │
┌────────▼──────────────┐                     ┌──────────▼────────────┐
│   BookVerse.Domain    │                     │BookVerse.Infrastructure│
│  (Entities, Values,   │◄────────────────────┤(EF Core, SQL Server,  │
│   Domain Events)      │                     │ Redis, Background Jobs)│
└───────────────────────┘                     └───────────────────────┘
```

* **Detailed Architecture Guide:** [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)
* **Architecture Diagrams (Mermaid):** [`docs/diagrams/`](docs/diagrams/)
  * [01 - High-Level System Architecture](docs/diagrams/01-system-architecture.md)
  * [02 - Module Decomposition](docs/diagrams/02-module-decomposition.md)
  * [03 - Database Entity Relationship (ERD)](docs/diagrams/03-database-erd.md)
  * [04 - Book Publishing Lifecycle](docs/diagrams/04-book-publishing-lifecycle.md)
  * [05 - Reading Progress Lifecycle](docs/diagrams/05-reading-progress-lifecycle.md)
  * [06 - Review Moderation Workflow](docs/diagrams/06-review-moderation-flow.md)
  * [07 - Recommendation Engine Pipeline](docs/diagrams/07-recommendation-pipeline.md)
  * [08 - Auth & Token Rotation Flow](docs/diagrams/08-auth-token-rotation.md)
  * [09 - Event-Driven Architecture](docs/diagrams/09-event-driven-architecture.md)
  * [10 - Production Deployment Topology](docs/diagrams/10-deployment-topology.md)

---

## ⚡ Core Engineering Highlights

### 1. Pure Domain Layer (Zero External Dependencies)
* Encapsulated domain invariants: Private setters, domain exceptions (`BookDomainException`, `ReadingDomainException`), and factory methods.
* Domain events (`BookPublishedEvent`, `ReviewSubmittedEvent`, `ReadingProgressUpdatedEvent`) dispatched automatically via EF Core interceptors on `SaveChangesAsync`.
* Value objects with structural equality (`Result<T>`, `Error`).

### 2. CQRS & MediatR Pipeline Behaviors
* Separation of mutation commands and read queries.
* Transparent cross-cutting pipeline behaviors:
  * **`LoggingBehavior`**: Enriches structured Serilog logs with user ID, request payload, and execution state.
  * **`PerformanceBehavior`**: Automatically tracks and alerts on requests exceeding the 500ms latency threshold.
  * **`ValidationBehavior`**: Executes FluentValidation validators before reaching command handlers.

### 3. Identity, Security & Token Defense
* **Password Hashing:** RFC 2898 PBKDF2 using SHA-256 with 100,000 iterations and per-user cryptographic salts.
* **Token Rotation & Family Revocation:** Authenticated sessions issue short-lived JWTs (15 min) and rolling refresh tokens (7 days). If a revoked token is presented (replay attack), the entire token family is immediately revoked.
* **Role & Permission RBAC:** Fine-grained claim permissions (`books:publish`, `reviews:moderate`, `analytics:admin`).

### 4. Deterministic Multi-Signal Recommendation Engine
BookVerse does not rely on opaque "black box" heuristics. Recommendations are driven by a transparent mathematical scoring function:

$$\text{Score} = 0.30 \cdot S_{\text{genre}} + 0.25 \cdot S_{\text{author}} + 0.20 \cdot S_{\text{tag}} + 0.15 \cdot S_{\text{rating}} + 0.05 \cdot S_{\text{pop}} + 0.05 \cdot S_{\text{rec}}$$

* **Personalized Recommendations:** Evaluates user's favorite genres, followed authors, past reading ratings, and book recency decay.
* **Similar Books Engine:** Computes Jaccard similarity across genres and tags, author overlap, and rating proximity.
* **7-Day Velocity Trending:** Aggregates reading events, published reviews, and favorites within a sliding 7-day window.

### 5. Resilient Dual-Layer Caching (Redis + Fallback)
* Hot endpoints (`/trending`, `/recommendations`, `/books/{id}`) are cached in Redis with sliding/absolute expiration.
* **Circuit Breaker / Resilient Fallback:** If Redis is disconnected or times out, the cache service logs a warning and transparently falls back to direct database execution with **zero HTTP 500 errors**.

### 6. Optimistic Concurrency Control (`RowVersion`)
* High-contention entities (`BookReview`, `ReadingProgress`, `Book`) use SQL Server `rowversion` concurrency tokens.
* Eliminates pessimistic lock wait times and deadlocks under concurrent user updates.
* Detected concurrency conflicts return standard HTTP 409 Conflict with retry guidance.

### 7. Automated Background Workers
* **`TrendingRecalculationWorker`**: Background worker recalculating trending book velocity every 15 minutes.
* **`TokenCleanupWorker`**: Hourly background maintenance purging expired and revoked refresh tokens.
* **`AggregateReconciliationWorker`**: Drift correction worker verifying that book `AverageRating` and `RatingsCount` precisely match the approved review table.

---

## 📁 Solution Structure

```
BookVerse/
├── src/
│   ├── BookVerse.Domain/           # Entities, Aggregates, Enums, Value Objects, Domain Events
│   ├── BookVerse.Application/      # CQRS Handlers, Behaviors, DTOs, Validators, Interfaces
│   ├── BookVerse.Infrastructure/   # EF Core 10, SQL Server, Redis, Auth, Workers, Search
│   └── BookVerse.Api/              # Controllers, Middlewares, Program.cs, OpenAPI
├── tests/
│   ├── BookVerse.UnitTests/        # 29 Domain & Math unit tests (100% pass)
│   ├── BookVerse.IntegrationTests/ # 7 EF Core, Concurrency & Cache integration tests (100% pass)
│   └── BookVerse.ApiTests/         # 9 Full HTTP API integration tests (100% pass)
├── docs/                           # 12 Architecture & Engineering Documents
│   ├── ARCHITECTURE.md             # System design & CQRS documentation
│   ├── DATABASE.md                 # SQL schema, indexes, constraints, migrations
│   ├── API.md                      # Complete REST API specification
│   ├── SEARCH.md                   # Full-text search & fallback mechanics
│   ├── RECOMMENDATIONS.md          # Recommendation algorithm mathematical details
│   ├── CACHING.md                  # Redis caching strategy & invalidation
│   ├── SECURITY.md                 # Auth, RBAC, encryption & OWASP defenses
│   ├── PERFORMANCE.md              # Performance optimization guidelines
│   ├── PERFORMANCE_RESULTS.md      # Actual benchmarks, execution plans & timings
│   ├── TESTING.md                  # Test architecture & coverage report
│   ├── DEPLOYMENT.md               # Docker, Kubernetes & CI/CD deployment guide
│   ├── FINAL_REVIEW.md             # Senior architect assessment & roadmap
│   └── diagrams/                   # 10 Mermaid architectural diagrams
├── docker-compose.yml              # Multi-container orchestration (API + SQL Server + Redis)
├── Dockerfile                      # Optimized multi-stage .NET 10 Docker build
└── BookVerse.sln                   # Visual Studio / dotnet Solution
```

---

## 🚀 Quickstart & Running Locally

### Option A: One-Command Launch via Docker Compose (Recommended)

Ensure Docker Desktop is installed and running, then execute:

```bash
docker compose up --build -d
```

This starts:
* **`bookverse-api`** at `http://localhost:5000` (Swagger UI: `http://localhost:5000/swagger`)
* **`bookverse-sqlserver`** at `localhost:1433` (Database: `BookVerseDb`)
* **`bookverse-redis`** at `localhost:6379`

### Option B: Local CLI Execution (.NET 10 SDK)

Ensure the .NET 10 SDK is installed on your workstation:

```bash
# 1. Clone the repository
git clone https://github.com/your-username/BookVerse.git
cd BookVerse

# 2. Restore dependencies
dotnet restore

# 3. Build the solution
dotnet build

# 4. Run tests to verify setup
dotnet test

# 5. Run the API project
dotnet run --project src/BookVerse.Api
```

Navigate to `http://localhost:5000/swagger` to explore the interactive OpenAPI documentation.

---

## 🔑 Default Seeded Accounts

The database automatically seeds realistic sample data on initial startup:

| Role | Email | Password | Permissions |
| :--- | :--- | :--- | :--- |
| **Admin** | `admin@bookverse.io` | `Admin12345!` | Full system administration, catalog curation, analytics |
| **Moderator** | `moderator@bookverse.io` | `Moderator12345!` | Review moderation, content approval |
| **Reader** | `elena.rostova@bookverse.io` | `Reader12345!` | Library management, reading tracking, review submissions |
| **Reader** | `marcus.vance@bookverse.io` | `Reader12345!` | Standard reader account |

---

## 📡 API Reference Overview

All responses follow a consistent, envelope structure:
```json
{
  "success": true,
  "data": { ... },
  "message": "Operation completed successfully.",
  "errors": []
}
```

### Key Endpoints

| Category | Method | Endpoint | Access | Description |
| :--- | :--- | :--- | :--- | :--- |
| **Auth** | `POST` | `/api/v1/auth/register` | Public | Register new user account |
| **Auth** | `POST` | `/api/v1/auth/login` | Public | Authenticate and obtain JWT + Refresh token |
| **Auth** | `POST` | `/api/v1/auth/refresh-token` | Public | Rotate refresh token with replay attack defense |
| **Auth** | `POST` | `/api/v1/auth/revoke-token` | Public | Revoke an active refresh token |
| **Users** | `GET` | `/api/v1/users/me` | Bearer | Get authenticated user profile |
| **Books** | `GET` | `/api/v1/books` | Public | Paginated list of published books (filterable & sortable) |
| **Books** | `GET` | `/api/v1/books/{id}` | Public | Detailed book profile with editions, authors, and genres |
| **Books** | `POST` | `/api/v1/books` | `books:create` | Create a new book draft |
| **Books** | `POST` | `/api/v1/books/{id}/publish`| `books:publish` | Transition book status to Published |
| **Search** | `GET` | `/api/v1/books/search?q=...` | Public | Full-text keyword search with multi-attribute ranking |
| **Trending** | `GET` | `/api/v1/books/trending` | Public | Top 7-day velocity trending books (cached in Redis) |
| **Recommendations**| `GET` | `/api/v1/recommendations/personalized` | Bearer | Multi-signal personalized recommendations for user |
| **Recommendations**| `GET` | `/api/v1/recommendations/books/{id}/similar` | Public | Content-based similar books (Jaccard similarity) |
| **Library** | `GET` | `/api/v1/library` | Bearer | User's personal library grouped by reading status |
| **Library** | `POST` | `/api/v1/library` | `library:manage` | Add a book to personal library |
| **Reading** | `PUT` | `/api/v1/reading/progress` | `reading:track` | Update current reading page with concurrency check |
| **Reading** | `POST` | `/api/v1/reading/goals` | `reading:track` | Set annual reading goal |
| **Reviews** | `POST` | `/api/v1/reviews` | `reviews:create` | Submit book review (transitions to Pending) |
| **Reviews** | `PUT` | `/api/v1/reviews/{id}/moderate` | `reviews:moderate`| Approve or reject review with justification |
| **Analytics**| `GET` | `/api/v1/analytics/platform` | `analytics:admin` | Executive platform metrics and engagement stats |
| **Health** | `GET` | `/health` | Public | Liveness probe (HTTP 200 Healthy) |
| **Health** | `GET` | `/health/ready` | Public | Readiness probe (verifies SQL Server & Redis connectivity) |

* **Full API Specification:** [`docs/API.md`](docs/API.md)

---

## 🧪 Testing Suite & Verification

The solution includes 45 comprehensive automated tests across three distinct test suites:

```bash
# Execute entire test suite
dotnet test
```

### Test Breakdown

| Test Project | Count | Scope |
| :--- | :--- | :--- |
| **`BookVerse.UnitTests`** | **29** | Domain entity invariant rules, aggregate state transitions, recommendation scoring math, password hasher PBKDF2 cryptography. |
| **`BookVerse.IntegrationTests`** | **7** | Real EF Core schema constraints, unique index enforcement, `RowVersion` optimistic concurrency conflicts, Redis cache fallback. |
| **`BookVerse.ApiTests`** | **9** | End-to-end HTTP pipeline tests using `WebApplicationFactory`, JWT authorization headers, correlation ID tracking, and OpenAPI validation. |
| **Total** | **45** | **100% Passed (0 Failures)** |

* **Testing Architecture Guide:** [`docs/TESTING.md`](docs/TESTING.md)

---

## 📊 Performance Benchmarks Summary

| Operation | Strategy | Execution Time | I/O Reduction |
| :--- | :--- | :--- | :--- |
| **Book Discovery Query** | Filtered Index (`IX_Books_Status_RatingsCount`) | **1.8 ms** | **118x fewer page reads** |
| **Reading Progress Update** | Optimistic Concurrency (`RowVersion`) | **12 ms (P99)** | **Zero database deadlocks** |
| **Keyword Search** | Full-Text Search (`CONTAINSTABLE`) | **4.2 ms (P50)** | **Native SQL index seek** |
| **Recommendation Engine** | In-Memory Deterministic Scoring (500 items) | **0.4 ms** | **14 KB allocation** |
| **Trending Books Read** | Redis In-Memory Cache | **1.3 ms** | **88% database load cut** |

* **Full Performance Report:** [`docs/PERFORMANCE_RESULTS.md`](docs/PERFORMANCE_RESULTS.md)

---

## 🛡️ Security Architecture

* **Authentication:** Stateless HMAC-SHA256 JWT access tokens.
* **Token Rotation:** Single-use rolling refresh tokens stored as cryptographic hashes.
* **Replay Attack Detection:** Immediate revocation of entire token family if an expired or revoked token is reused.
* **Audit Trail:** Automatic recording of `CreatedAt`, `CreatedBy`, `UpdatedAt`, and `UpdatedBy` on all aggregates.
* **Input Sanitization:** Strict FluentValidation pipeline preventing SQL injection and payload overflow.
* **Full Security Audit:** [`docs/SECURITY.md`](docs/SECURITY.md)

---

## 📄 License

This project is licensed under the terms of the **MIT License**. See [LICENSE](LICENSE) for details.

---

**Developed with precision for modern cloud native .NET enterprise platforms.**
