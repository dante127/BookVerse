# 9. Domain Event Architecture Flow

```mermaid
flowchart TD
    subgraph AggregateRoots["Domain Aggregates"]
        BookRoot["Book Aggregate"]
        ReviewRoot["Review Aggregate"]
        ReadingRoot["ReadingProgress Aggregate"]
        AuthorRoot["Author Aggregate"]
    end

    subgraph DomainEvents["Domain Events (In-Memory)"]
        E1["BookPublishedEvent"]
        E2["ReviewApprovedEvent"]
        E3["BookCompletedEvent"]
        E4["AuthorFollowedEvent"]
    end

    subgraph Handlers["MediatR Event Handlers"]
        H1["NotifyFollowersHandler"]
        H2["WarmSearchCacheHandler"]
        H3["RecalculateRatingAggregateHandler"]
        H4["InvalidateBookCacheHandler"]
        H5["UpdateReadingGoalHandler"]
        H6["LogReadingHistoryHandler"]
    end

    subgraph Targets["Target Side-Effects"]
        DB[("SQL Server Notifications & Audit")]
        Redis[("Redis Cache Eviction / Warming")]
        Workers["Background Processing Queue"]
    end

    BookRoot -->|Publish()| E1
    ReviewRoot -->|Approve()| E2
    ReadingRoot -->|Complete()| E3
    AuthorRoot -->|Follow()| E4

    E1 --> H1
    E1 --> H2
    E2 --> H3
    E2 --> H4
    E3 --> H5
    E3 --> H6

    H1 --> DB
    H2 --> Redis
    H3 --> DB
    H4 --> Redis
    H5 --> DB
    H6 --> DB
```
