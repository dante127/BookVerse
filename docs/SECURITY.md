# BookVerse — Security Architecture & Hardening

## 1. Authentication & JWT Token Management

BookVerse uses a secure token authentication model:
- **Access Tokens:** Short-lived JWTs (15-minute expiration) signed with HMAC-SHA256 (or asymmetric RSA). Contains standard claims (`sub`, `email`, `jti`) and granted permissions (`perm:books:create`, `perm:reviews:moderate`).
- **Refresh Tokens:** High-entropy cryptographically generated random tokens (64 bytes, base64 encoded) stored hashed (SHA-256) in the database with a 7-day expiration.

### Refresh Token Rotation & Replay Attack Mitigation
Every time a refresh token is used:
1. The presented token is invalidated and marked `RevokedAt`.
2. A new refresh token is issued and linked via `ReplacedByToken`.
3. **Replay Detection:** If a previously revoked token is submitted again, the system identifies a token theft attempt and **immediately revokes the entire token family**, terminating all active sessions for that user.

---

## 2. Authorization (RBAC + Permission-Based)

Authorization is enforced using ASP.NET Core policy-based authorization linked to fine-grained domain permissions:

```csharp
[Authorize(Policy = Permissions.Books.Publish)]
[HttpPost("{id}/publish")]
public async Task<IActionResult> Publish(Guid id) { ... }
```

### Permission Matrix
| Role | Assigned Permissions |
|---|---|
| **Reader** | `library:manage`, `reviews:create`, `reviews:edit_own`, `reading:track`, `authors:follow` |
| **Moderator** | Reader permissions + `reviews:moderate`, `reviews:hide`, `content:flag` |
| **Admin** | All permissions + `books:create`, `books:edit`, `books:publish`, `users:manage`, `analytics:admin` |

---

## 3. Defense-in-Depth & Anti-Abuse Controls

1. **Password Hashing:** PBKDF2 with SHA-256, 100,000 iterations and 128-bit cryptographically random per-user salt.
2. **Review Anti-Abuse:**
   - Enforced database uniqueness on `(BookId, UserId)`.
   - Rate limiting: max 5 review submissions per hour per user.
   - Text profanity and spam pattern detection.
3. **ASP.NET Core Rate Limiting:**
   - Fixed window on `/api/v1/auth/login` (5 requests / minute per IP).
   - Sliding window on public search endpoints (60 requests / minute).
4. **CORS & Headers:**
   - Explicit allowed origins (no wildcard `*` with credentials).
   - Security headers: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Strict-Transport-Security`.
5. **No Secret Leakage:**
   - Centralized exception handling outputs sanitized RFC 7807 problem details.
   - Never commit connection strings, JWT signing keys, or credentials to source control.
