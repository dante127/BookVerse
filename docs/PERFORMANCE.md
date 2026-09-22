# BookVerse — Performance Optimization & Engineering

## 1. EF Core & Query Optimization

BookVerse strictly avoids typical ORM pitfalls:
1. **No Untracked Bloat:** All read queries utilize `.AsNoTracking()` to eliminate the memory and CPU overhead of EF Core change tracking.
2. **Selective Projections (`Select`):** Queries project directly into DTOs. Entity models containing large columns (like book descriptions, audit JSON, or blob URLs) are never queried unless specifically required.
3. **No Client-Side Evaluation:** All filter expressions (`Where`), sorts (`OrderBy`), and pagination (`Skip/Take`) are converted into server-side SQL execution.
4. **Preventing N+1 Queries:** Related collections (such as authors or tags for book cards) are retrieved via explicit `.Include()` or consolidated projection queries.
5. **Split Queries:** Where multiple collections are loaded for a single detail view, `.AsSplitQuery()` is applied to avoid Cartesian product explosion.

---

## 2. Concurrency & High-Throughput Aggregates

### The Aggregate Drift Problem
In many book platforms, displaying average ratings and review counts requires scanning the entire `BookReviews` table with `COUNT()` and `AVG()` queries. On popular books with 50,000+ reviews, this causes massive table lock contention and CPU spikes.

### The BookVerse Solution
- The `Book` aggregate root stores denormalized `AverageRating`, `RatingsCount`, and `ReviewsCount` columns.
- When a review is approved, updated, or removed, the aggregate recalculates its counters in-memory and saves with optimistic concurrency (`rowversion`).
- A background reconciliation worker (`AggregateRecalculationBackgroundService`) runs off-peak to detect and heal any minor drift.

---

## 3. Asynchronous Processing & Background Jobs

Heavy operations are offloaded from HTTP request threads into .NET `BackgroundService` workers:
- **`TrendingCalculationBackgroundService`:** Recalculates trending scores every 10 minutes and refreshes Redis caches.
- **`RecommendationRefreshBackgroundService`:** Pre-computes candidate recommendations for active users.
- **`TokenCleanupBackgroundService`:** Purges expired and revoked refresh tokens to keep the `RefreshTokens` table compact and index trees balanced.
