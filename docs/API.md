# BookVerse — REST API Specification

All endpoints are versioned under `/api/v1/` and follow REST conventions with JSON payloads.

## 1. Response Envelopes

### Standard Success Response
```json
{
  "success": true,
  "data": { ... },
  "message": null,
  "errors": []
}
```

### Standard Paginated Response
```json
{
  "success": true,
  "data": {
    "items": [ ... ],
    "page": 1,
    "pageSize": 20,
    "totalCount": 150,
    "totalPages": 8,
    "hasNextPage": true,
    "hasPreviousPage": false
  },
  "message": null,
  "errors": []
}
```

### Standard Validation Error (HTTP 400 / RFC 7807)
```json
{
  "type": "https://bookverse.io/errors/validation",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "detail": "See errors for specific details.",
  "instance": "/api/v1/books/3fa85f64-5717-4562-b3fc-2c963f66afa6/reviews",
  "errors": [
    {
      "field": "rating",
      "message": "Rating must be between 1 and 5."
    }
  ]
}
```

### Concurrency Conflict (HTTP 409)
```json
{
  "type": "https://bookverse.io/errors/concurrency-conflict",
  "title": "The resource was modified by another operation.",
  "status": 409,
  "detail": "The entity has been modified since it was loaded. Please reload and retry."
}
```

---

## 2. API Endpoint Catalog

### 2.1 Authentication & Authorization (`/api/v1/auth`)
- `POST /api/v1/auth/register` — Register a new reader account.
- `POST /api/v1/auth/login` — Authenticate and receive JWT access token + refresh token cookie/header.
- `POST /api/v1/auth/refresh` — Rotate refresh token; revokes token family if replay attack detected.
- `POST /api/v1/auth/logout` — Revoke active refresh token and invalidate user session.
- `POST /api/v1/auth/change-password` — Change password requiring current credential verification.

### 2.2 Users & Profiles (`/api/v1/users`)
- `GET /api/v1/users/me` — Retrieve current authenticated user profile and reading stats.
- `PUT /api/v1/users/me/profile` — Update display name, bio, avatar, and language.
- `GET /api/v1/users/me/preferences` — Get favorite genres and preferred authors.
- `PUT /api/v1/users/me/preferences` — Update reading preferences.

### 2.3 Books Catalog (`/api/v1/books`)
- `GET /api/v1/books` — Paginated list of published books with optional genre/tag filters.
- `GET /api/v1/books/{id}` — Detailed canonical book profile including editions, authors, and rating summary.
- `POST /api/v1/books` — [Admin/Editor] Create a new draft book.
- `PUT /api/v1/books/{id}` — [Admin/Editor] Update book metadata.
- `POST /api/v1/books/{id}/publish` — [Admin/Editor] Transition book status from Draft to Published.
- `POST /api/v1/books/{id}/editions` — [Admin/Editor] Add a physical/digital edition to a book.

### 2.4 Search & Discovery (`/api/v1/books/...`)
- `GET /api/v1/books/search?q={query}&genre={slug}&minRating={val}&page=1&pageSize=20` — Multi-signal full-text search.
- `GET /api/v1/books/{id}/similar` — Discover books similar to the target book by genre, tags, and themes.
- `GET /api/v1/books/trending` — Retrieve currently trending books (Redis cached with 5-minute TTL).

### 2.5 Authors & Genres (`/api/v1/authors`, `/api/v1/genres`)
- `GET /api/v1/authors` — Paginated list of authors.
- `GET /api/v1/authors/{id}` — Author biography, bibliography, and statistics.
- `POST /api/v1/authors/{id}/follow` — Follow an author to receive alerts on new releases.
- `DELETE /api/v1/authors/{id}/follow` — Unfollow an author.
- `GET /api/v1/genres` — Retrieve full hierarchical genre taxonomy.

### 2.6 Personal Library (`/api/v1/library`)
- `GET /api/v1/library` — Get authenticated user's bookshelf (filtered by `WantToRead`, `Reading`, `Completed`, etc.).
- `POST /api/v1/library/books` — Add a book to personal library with initial status.
- `PUT /api/v1/library/books/{bookId}` — Change reading status of a book.
- `DELETE /api/v1/library/books/{bookId}` — Remove book from personal library.
- `POST /api/v1/books/{id}/favorite` — Add book to favorites.
- `DELETE /api/v1/books/{id}/favorite` — Remove book from favorites.

### 2.7 Reading Tracking & Goals (`/api/v1/reading`)
- `POST /api/v1/books/{id}/progress` — Update reading progress (CurrentPage). Auto-computes percentage and completion.
- `GET /api/v1/reading/history` — Paginated audit log of user reading activity milestones.
- `GET /api/v1/reading/goals` — Retrieve annual reading goals and completion status.
- `POST /api/v1/reading/goals` — Create or update reading goal target for a specific year.

### 2.8 Reviews & Moderation (`/api/v1/books/{id}/reviews`, `/api/v1/reviews`)
- `GET /api/v1/books/{id}/reviews` — Paginated published reviews for a book.
- `POST /api/v1/books/{id}/reviews` — Submit a 1–5 star rating and review.
- `PUT /api/v1/books/{id}/reviews` — Update user's existing review.
- `PUT /api/v1/reviews/{id}/moderate` — [Admin/Moderator] Approve, reject, or hide a review.

### 2.9 Recommendations & Feed (`/api/v1/recommendations`)
- `GET /api/v1/recommendations` — Personalized recommendations based on reading history, favorite genres, and author affinity.

### 2.10 Analytics & Dashboards (`/api/v1/analytics`)
- `GET /api/v1/analytics/reading` — Authenticated user's reading velocity, total pages read, and genre breakdown.
- `GET /api/v1/analytics/admin` — [Admin] System-wide metrics: active users, total books, reviews, and reading volume.

### 2.11 Notifications (`/api/v1/notifications`)
- `GET /api/v1/notifications` — List user notifications (unread and recent).
- `PATCH /api/v1/notifications/{id}/read` — Mark notification as read.
- `POST /api/v1/notifications/read-all` — Mark all notifications as read.
