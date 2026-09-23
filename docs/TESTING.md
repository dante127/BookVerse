# BookVerse — Testing Strategy & Quality Assurance

## 1. Test Pyramid & Automation Levels

```
               ┌───────────────────────┐
               │       API Tests       │  (WebApplicationFactory E2E)
               │      (Smoke / E2E)    │
               ├───────────────────────┤
               │   Integration Tests   │  (EF Core, Concurrency, Redis)
               ├───────────────────────┤
               │      Unit Tests       │  (Domain Invariants, Scoring, Handlers)
               └───────────────────────┘
```

### Test Suite Structure
1. **`BookVerse.UnitTests`:**
   - Domain entity tests (e.g. `Book.Publish()`, `ReadingProgress.UpdateProgress()`).
   - Value object equality & validation rules.
   - Command & Query handlers isolated with mocks.
   - Recommendation scoring algorithm tests with deterministic mathematical assertions.
   - FluentValidation validator tests.
2. **`BookVerse.IntegrationTests`:**
   - EF Core database schema verification.
   - Unique constraints & foreign key cascades.
   - Optimistic concurrency conflict simulation (`DbUpdateConcurrencyException`).
   - Redis caching and fallback resilience testing.
3. **`BookVerse.ApiTests`:**
   - Full API lifecycle using `Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory`.
   - Registration -> Login -> Browse -> Add to Library -> Progress update -> Review flow.

---

## 2. Test Execution

Execute all test projects:
```powershell
dotnet test BookVerse.sln --logger "console;verbosity=normal"
```

Filter by test category:
```powershell
dotnet test --filter Category=Unit
dotnet test --filter Category=Integration
```

---

## 3. Database Provider Fidelity & CI (TST-02)

Tests run against **SQLite in-memory** while production runs **SQL Server**. Accepted trade-off:

| SQL Server-only surface | How it is covered instead |
| :--- | :--- |
| Full-text `CONTAINS` search | Fallback LIKE path is tested; the full-text path requires the compose stack smoke run. |
| `RowVersion` concurrency tokens | `IntegrationTests` assert optimistic-concurrency conflict behavior on the SQLite row-version generator path. |
| Provider-specific SQL | The analytics queries group by FK ids (never by navigation properties) so both providers translate them identically. |

Deferred: Testcontainers-based SQL Server API tests once container infrastructure exists in CI.

Every push and pull request to `main` runs restore → Release build → full suite + coverage via `.github/workflows/ci.yml`.
