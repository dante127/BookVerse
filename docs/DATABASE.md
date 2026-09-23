# BookVerse — Database Design & Schema Specification

## 1. Overview & Principles

BookVerse utilizes **Microsoft SQL Server 2022** as its primary relational datastore. The database design emphasizes:
- **Strict Referential Integrity:** Foreign keys on all relational boundaries with appropriate cascade/restrict behaviors.
- **Normalization & Intentional Denormalization:** 3NF normalization for transaction fidelity, with controlled aggregation on high-frequency read entities (e.g. `Book.AverageRating`, `Book.RatingsCount`).
- **Optimistic Concurrency Control:** SQL Server `rowversion` columns on high-contention aggregates.
- **High-Performance Indexing:** Covering indexes, composite indexes for search filters, and SQL Server Full-Text Search catalogs.
- **Audit & Traceability:** Automated population of `CreatedAt`, `UpdatedAt`, `CreatedBy`, and immutable `AuditLogs`.

---

## 2. Table Specifications & Data Dictionary

### 2.1 Identity & Security

#### `Users`
| Column | Type | Nullable | Constraints / Index | Description |
|---|---|---|---|---|
| `Id` | `uniqueidentifier` | No | PK, Default `NEWSEQUENTIALID()` | User unique identifier |
| `Email` | `nvarchar(256)` | No | UNIQUE INDEX, Normalized | Login email |
| `PasswordHash` | `nvarchar(512)` | No | | Secure PBKDF2 hash |
| `PasswordSalt` | `nvarchar(128)` | No | | Cryptographic salt |
| `Status` | `int` | No | Index | `Active = 1`, `Inactive = 2`, `Suspended = 3` |
| `FailedLoginCount` | `int` | No | Default `0` | Consecutive failed logins (resets on success) |
| `LockoutUntil` | `datetimeoffset(7)` | Yes | | Account locked until this time (5 failures → 15 min) |
| `CreatedAt` | `datetimeoffset(7)` | No | | Registration timestamp |
| `UpdatedAt` | `datetimeoffset(7)` | Yes | | Last profile update |

#### `UserProfiles`
| Column | Type | Nullable | Constraints / Index | Description |
|---|---|---|---|---|
| `Id` | `uniqueidentifier` | No | PK | Profile unique identifier |
| `UserId` | `uniqueidentifier` | No | FK -> `Users.Id` (1:1, UNIQUE) | Linked user account |
| `DisplayName` | `nvarchar(100)` | No | Index | Public display name |
| `Bio` | `nvarchar(1000)` | Yes | | Author/Reader bio |
| `AvatarUrl` | `nvarchar(500)` | Yes | | Profile picture URL |
| `PreferredLanguage` | `nvarchar(10)` | No | Default `'en'` | UI / reading language |
| `CreatedAt` | `datetimeoffset(7)` | No | | Profile creation timestamp |
| `UpdatedAt` | `datetimeoffset(7)` | Yes | | Last update timestamp |

#### `Roles`, `Permissions`, `RolePermissions`, `UserRoles`
- Standard RBAC table set supporting fine-grained permissions (e.g., `books:create`, `books:publish`, `reviews:moderate`).

#### `RefreshTokens`
| Column | Type | Nullable | Constraints / Index | Description |
|---|---|---|---|---|
| `Id` | `uniqueidentifier` | No | PK | Token ID |
| `UserId` | `uniqueidentifier` | No | FK -> `Users.Id` | User reference |
| `TokenHash` | `nvarchar(128)` | No | UNIQUE INDEX | SHA-256 hash of refresh token |
| `JwtId` | `nvarchar(128)` | No | Index | JTI identifier of paired JWT |
| `ExpiresAt` | `datetimeoffset(7)` | No | Index | Absolute token expiry |
| `CreatedAt` | `datetimeoffset(7)` | No | | Creation timestamp |
| `CreatedByIp` | `nvarchar(45)` | Yes | | IP address of request |
| `RevokedAt` | `datetimeoffset(7)` | Yes | | Revocation timestamp |
| `RevokedByIp` | `nvarchar(45)` | Yes | | IP address of revoker |
| `ReplacedByToken` | `nvarchar(128)` | Yes | | Ptr to newer token in family |

---

### 2.2 Books & Catalog

#### `Books`
| Column | Type | Nullable | Constraints / Index | Description |
|---|---|---|---|---|
| `Id` | `uniqueidentifier` | No | PK | Book primary key |
| `Title` | `nvarchar(250)` | No | INDEX, Full-Text Catalog | Canonical work title |
| `Subtitle` | `nvarchar(250)` | Yes | Full-Text Catalog | Optional subtitle |
| `Description` | `nvarchar(max)` | No | Full-Text Catalog | Detailed book summary |
| `ISBN` | `nvarchar(20)` | Yes | UNIQUE INDEX (Filtered) | Canonical primary ISBN |
| `PublicationDate` | `date` | Yes | INDEX | Original release date |
| `PageCount` | `int` | No | CHECK (`PageCount > 0`) | Canonical page count |
| `Language` | `nvarchar(10)` | No | Default `'en'`, INDEX | Primary text language |
| `Publisher` | `nvarchar(150)` | Yes | INDEX | Primary publisher |
| `CoverImageUrl` | `nvarchar(500)` | Yes | | High-res cover image URL |
| `AverageRating` | `decimal(3,2)` | No | Default `0.00`, INDEX | Aggregated rating (1.00 - 5.00) |
| `RatingsCount` | `int` | No | Default `0`, INDEX | Total number of ratings |
| `ReviewsCount` | `int` | No | Default `0` | Total approved text reviews |
| `Status` | `int` | No | INDEX | `Draft = 0`, `Published = 1`, `Archived = 2` |
| `RowVersion` | `rowversion` | No | Concurrency Token | SQL Server optimistic concurrency |
| `CreatedAt` | `datetimeoffset(7)` | No | INDEX | Record creation |
| `UpdatedAt` | `datetimeoffset(7)` | Yes | | Record update |

#### `BookEditions`
| Column | Type | Nullable | Constraints / Index | Description |
|---|---|---|---|---|
| `Id` | `uniqueidentifier` | No | PK | Edition unique identifier |
| `BookId` | `uniqueidentifier` | No | FK -> `Books.Id` (Cascade) | Parent canonical book |
| `ISBN` | `nvarchar(20)` | No | UNIQUE INDEX | Edition specific ISBN-13 |
| `Format` | `int` | No | INDEX | `Hardcover = 1`, `Paperback = 2`, `Ebook = 3`, `Audiobook = 4` |
| `Publisher` | `nvarchar(150)` | Yes | | Specific edition publisher |
| `PublicationDate` | `date` | Yes | | Edition release date |
| `PageCount` | `int` | No | | Edition page count |
| `Language` | `nvarchar(10)` | No | | Edition language |
| `FileSizeInBytes` | `bigint` | Yes | | For digital formats |
| `FileUrl` | `nvarchar(500)` | Yes | | Download / sample link |

#### `Authors` & `BookAuthors`
- `Authors`: `Id`, `Name` (Indexed, Full-Text), `Biography`, `BirthDate`, `Country`, `WebsiteUrl`, `ProfileImageUrl`, `CreatedAt`.
- `BookAuthors`: Composite PK (`BookId`, `AuthorId`, `Role`), `Role` (`Author = 1`, `CoAuthor = 2`, `Translator = 3`, `Editor = 4`), `OrderIndex` (int).

#### `Genres`, `Tags`, `BookTags`
- `Genres`: `Id`, `Name` (UNIQUE), `Slug` (UNIQUE), `Description`, `ParentGenreId` (Self-referential FK -> `Genres.Id` nullable for hierarchical categorization).
- `Tags`: `Id`, `Name` (UNIQUE), `Slug` (UNIQUE).
- `BookTags`: Composite PK (`BookId`, `TagId`).

---

### 2.3 User Library & Reading Activity

#### `UserBooks`
| Column | Type | Nullable | Constraints / Index | Description |
|---|---|---|---|---|
| `Id` | `uniqueidentifier` | No | PK | Entry identifier |
| `UserId` | `uniqueidentifier` | No | FK -> `Users.Id` | Library owner |
| `BookId` | `uniqueidentifier` | No | FK -> `Books.Id` | Target book |
| `Status` | `int` | No | INDEX | `WantToRead = 1`, `Reading = 2`, `Completed = 3`, `Paused = 4`, `Dropped = 5` |
| `AddedAt` | `datetimeoffset(7)` | No | | Added timestamp |
| `StartedAt` | `datetimeoffset(7)` | Yes | | When moved to Reading |
| `CompletedAt` | `datetimeoffset(7)` | Yes | | When marked Completed |
| `LastReadAt` | `datetimeoffset(7)` | Yes | INDEX | Last active session |
- **Unique Constraint:** `UNIQUE (UserId, BookId)`

#### `FavoriteBooks`
- Composite PK / Unique: `(UserId, BookId)`, `CreatedAt`.

#### `ReadingProgresses`
| Column | Type | Nullable | Constraints / Index | Description |
|---|---|---|---|---|
| `Id` | `uniqueidentifier` | No | PK | Progress ID |
| `UserId` | `uniqueidentifier` | No | FK -> `Users.Id` | User |
| `BookId` | `uniqueidentifier` | No | FK -> `Books.Id` | Book |
| `CurrentPage` | `int` | No | CHECK (`CurrentPage >= 0`) | User's current page |
| `TotalPages` | `int` | No | CHECK (`TotalPages > 0`) | Total pages of book |
| `Percentage` | `decimal(5,2)` | No | Server-computed | `(CurrentPage / TotalPages) * 100` |
| `StartedAt` | `datetimeoffset(7)` | No | | Start timestamp |
| `LastReadAt` | `datetimeoffset(7)` | No | INDEX | Last progress update |
| `CompletedAt` | `datetimeoffset(7)` | Yes | | Time of final page |
| `RowVersion` | `rowversion` | No | Concurrency Token | Optimistic lock token |
- **Unique Constraint:** `UNIQUE (UserId, BookId)`
- **Check Constraint:** `CHECK (CurrentPage <= TotalPages)`

#### `ReadingHistories`
- `Id`, `UserId`, `BookId`, `Action` (`StartedBook`, `ProgressUpdated`, `CompletedBook`, `PausedBook`, `DroppedBook`), `DeltaPages`, `PreviousPage`, `NewPage`, `Timestamp` (Indexed).

#### `ReadingGoals`
- `Id`, `UserId`, `Year` (int), `TargetBooks` (CHECK > 0), `CompletedBooks` (int), `CreatedAt`, `UpdatedAt`.
- **Unique Constraint:** `UNIQUE (UserId, Year)`.

---

### 2.4 Reviews & Moderation

#### `BookReviews`
| Column | Type | Nullable | Constraints / Index | Description |
|---|---|---|---|---|
| `Id` | `uniqueidentifier` | No | PK | Review identifier |
| `BookId` | `uniqueidentifier` | No | FK -> `Books.Id` | Target book |
| `UserId` | `uniqueidentifier` | No | FK -> `Users.Id` | Reviewer |
| `Rating` | `int` | No | CHECK (`Rating BETWEEN 1 AND 5`) | 1 to 5 star rating |
| `Title` | `nvarchar(150)` | Yes | | Review headline |
| `Content` | `nvarchar(4000)` | Yes | | Full review text |
| `Status` | `int` | No | INDEX | `Pending = 1`, `Published = 2`, `Rejected = 3`, `Hidden = 4` |
| `ModerationNote`| `nvarchar(500)` | Yes | | Reason for moderation decision |
| `ModeratedBy` | `uniqueidentifier` | Yes | FK -> `Users.Id` | Moderator ID |
| `ModeratedAt` | `datetimeoffset(7)` | Yes | | Moderation timestamp |
| `RowVersion` | `rowversion` | No | Concurrency Token | Concurrency token |
| `CreatedAt` | `datetimeoffset(7)` | No | INDEX | Creation date |
| `UpdatedAt` | `datetimeoffset(7)` | Yes | | Edit date |
- **Unique Constraint:** `UNIQUE (BookId, UserId)`

---

### 2.5 Social, Notifications & Audit

- **`AuthorFollowers`**: Composite PK `(UserId, AuthorId)`, `CreatedAt`.
- **`Notifications`**: `Id`, `UserId`, `Type` (`NewRecommendation`, `ReviewApproved`, `ReviewRejected`, `ReadingGoalAchieved`, `BookCompleted`, `AuthorNewBook`, `ReadingStreakMilestone`), `Title`, `Message`, `IsRead`, `CreatedAt`, `ReadAt`.
- **`AuditLogs`**: `Id`, `UserId` (nullable), `EntityName`, `EntityId`, `Action` (Insert, Update, Delete, Moderate), `OldValues` (JSON), `NewValues` (JSON), `IpAddress`, `Timestamp`.
