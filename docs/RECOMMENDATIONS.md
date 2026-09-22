# BookVerse — Recommendation & Discovery Architecture

## 1. Vision & Architecture

Rather than relying on black-box external AI services or opaque machine learning models, BookVerse uses a **transparent, deterministic multi-signal scoring engine** implemented in `IRecommendationService`.

The engine powers three primary discovery features:
1. **Personalized Recommendations** (`GET /api/v1/recommendations`)
2. **Similar Books** (`GET /api/v1/books/{id}/similar`)
3. **Trending Books** (`GET /api/v1/books/trending`)

---

## 2. Multi-Signal Personalized Recommendation Formula

For an authenticated user $U$ and candidate book $B$:

$$\text{RecommendationScore}(B, U) = \sum_{i=1}^{6} w_i \cdot S_i(B, U)$$

Subject to candidate filtering: $B \notin \text{UserLibrary}(U)$ (books already marked `Reading`, `Completed`, or `Dropped` are filtered out).

### Signal Breakdown & Weights

| Signal ($S_i$) | Weight ($w_i$) | Description & Computation |
|---|---|---|
| **$S_{\text{genre}}$ (Genre Affinity)** | **0.30** | Jaccard overlap between book's genre hierarchy and genres of books $U$ has rated $\ge 4$ stars or marked `Completed`. |
| **$S_{\text{author}}$ (Author Affinity)** | **0.25** | $1.0$ if $B$ is authored by someone $U$ follows; $0.7$ if $U$ previously rated that author $\ge 4$ stars; $0.0$ otherwise. |
| **$S_{\text{tag}}$ (Tag Similarity)** | **0.20** | Overlap coefficient between tags of $B$ and tags present in $U$'s reading history. |
| **$S_{\text{rating}}$ (Quality Signal)** | **0.15** | Normalized aggregate book rating: $\text{AverageRating} / 5.0$. |
| **$S_{\text{pop}}$ (Popularity)** | **0.05** | Log-scaled popularity: $\min(1.0, \log_{10}(\text{RatingsCount} + 1) / 4.0)$. |
| **$S_{\text{rec}}$ (Recency)** | **0.05** | Decay based on years since publication: $\exp(-0.05 \cdot \Delta \text{years})$. |

---

## 3. Similar Books Discovery Algorithm

When viewing a specific book $B_{\text{target}}$, similar books are determined by vector similarity:

$$\text{Similarity}(B_1, B_2) = 0.45 \cdot \text{GenreOverlap} + 0.35 \cdot \text{TagOverlap} + 0.10 \cdot \text{AuthorMatch} + 0.10 \cdot \text{RatingProximity}$$

- **Genre Overlap:** Considers exact genre and parent genre branch.
- **Tag Overlap:** Jaccard index $\frac{|T_1 \cap T_2|}{|T_1 \cup T_2|}$.
- **Rating Proximity:** $1.0 - \frac{|\text{Rating}_1 - \text{Rating}_2|}{5.0}$.

---

## 4. Trending Books Algorithm & Time Decay

To calculate trending books without simply returning all-time high review counts, BookVerse computes a **7-day engagement velocity with time decay**:

$$\text{TrendingScore}(B) = (3.0 \cdot R_{7d}) + (2.5 \cdot V_{7d}) + (4.0 \cdot C_{7d}) + (2.0 \cdot F_{7d}) + (1.5 \cdot \text{AvgRating})$$

Where:
- $R_{7d}$ = Books added to `Reading` in the last 7 days.
- $V_{7d}$ = Reviews created in the last 7 days.
- $C_{7d}$ = Books marked `Completed` in the last 7 days.
- $F_{7d}$ = Books favorited in the last 7 days.

### Background Job & Caching
- A background worker (`TrendingCalculationBackgroundService`) recalculates trending scores periodically and caches the top 50 books in Redis (`books:trending`) with a 5-minute TTL.
