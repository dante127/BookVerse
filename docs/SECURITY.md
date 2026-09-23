# BookVerse — Security Architecture & Hardening

## 1. Authentication & JWT Token Management

BookVerse uses a secure token authentication model:
- **Access Tokens:** Short-lived JWTs (15-minute expiration) signed with HMAC-SHA256 using a symmetric secret from configuration (`Jwt:Secret`). Contains standard claims (`sub`, `email`, `jti`) and granted permissions (`perm:books:create`, `perm:reviews:moderate`).
- **Refresh Tokens:** High-entropy cryptographically generated random tokens (64 bytes, base64 encoded) stored hashed (SHA-256) in the database with a 7-day expiration.

### Refresh Token Rotation & Replay Attack Mitigation
Every time a refresh token is used:
1. The presented token is invalidated and marked `RevokedAt`.
2. A new refresh token is issued and linked via `ReplacedByToken`.
3. **Replay Detection:** If a previously revoked token is submitted again, the system identifies a token theft attempt and **immediately revokes the entire token family**, terminating all active sessions for that user.

---

## 2. Authorization (RBAC)

Authorization on endpoints is enforced with role-based attributes backed by ASP.NET Core Identity roles:

```csharp
[Authorize(Roles = "Admin")]
[HttpPost("{id}/publish")]
public async Task<IActionResult> Publish(Guid id) { ... }
```

Moderation actions use `[Authorize(Roles = "Admin,Moderator")]`. Users also carry fine-grained permission claims (`perm:...`) that are seeded and emitted into the JWT for forward compatibility, but the current route guards authorize by **role**, not by a policy-to-permission mapping.

### Role & Permission Matrix
| Role | Granted Permissions (claims) |
|---|---|
| **Reader** | `library:manage`, `reviews:create`, `reviews:edit_own`, `reading:track`, `authors:follow` |
| **Moderator** | Reader permissions + `reviews:moderate`, `reviews:hide`, `content:flag` |
| **Admin** | All permissions + `books:create`, `books:edit`, `books:publish`, `users:manage`, `analytics:admin` |

---

## 3. Defense-in-Depth & Anti-Abuse Controls

1. **Password Hashing:** PBKDF2 with SHA-256, 100,000 iterations and 128-bit cryptographically random per-user salt.
2. **Review Anti-Abuse:**
   - Enforced database uniqueness on `(BookId, UserId)`.
   - Text profanity and spam pattern detection.
3. **ASP.NET Core Rate Limiting (per-IP fixed windows):**
   - Global limiter: 300 requests / minute.
   - `auth` policy (other auth endpoints): 30 requests / minute.
   - `login` policy (`/api/v1/auth/login`): 10 requests / minute.
   - Application-level account lockout tracked in the database (5 failed logins → 15-minute lockout) protects against credential stuffing independent of IP throttling.
   - **Behind a reverse proxy** (Nginx, cloud LB, ingress) the socket peer is the proxy, so `UseForwardedHeaders` must resolve `X-Forwarded-For` or every caller collapses into a single proxy-IP partition and the per-IP limits become platform-global. Trusted proxies/networks are configured via `Forwarding:KnownProxies` / `Forwarding:KnownNetworks` (secure by default: unset headers are ignored and the socket IP is used).
4. **CORS & Headers:**
   - Explicit allowed origins (no wildcard `*` with credentials).
   - Security headers: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`.
   - `Strict-Transport-Security` (HSTS) is enabled outside the Development environment.
   - Swagger UI is mounted in Development only.
5. **No Secret Leakage:**
   - Centralized exception handling outputs sanitized RFC 7807 problem details.
   - Never commit connection strings, JWT signing keys, or credentials to source control.
