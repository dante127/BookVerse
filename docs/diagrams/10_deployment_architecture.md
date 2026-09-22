# 10. Deployment Topology & Container Architecture

```mermaid
graph TB
    subgraph Internet["Public Internet"]
        User["Reader / Administrator"]
    end

    subgraph Host["Docker Host (Bridge Network)"]
        subgraph Ingress["Ingress & Edge"]
            Proxy["Reverse Proxy / Nginx / Direct"]
        end

        subgraph Containers["Services (docker-compose)"]
            API["BookVerse.Api (.NET 10 Container)<br/>Port: 8080<br/>Health: /health/ready"]
            SQL["SQL Server 2022 Container<br/>Image: mcr.microsoft.com/mssql/server:2022-latest<br/>Port: 1433"]
            Redis["Redis 7 Container<br/>Image: redis:7-alpine<br/>Port: 6379"]
        end

        subgraph Storage["Persistent Docker Volumes"]
            SqlData[("mssql_data Volume")]
            RedisData[("redis_data Volume")]
        end
    end

    User -->|HTTP/HTTPS :8080| Proxy
    Proxy --> API
    API -->|TCP 1433 (Pooled)| SQL
    API -->|TCP 6379 (Multiplexed)| Redis
    SQL --> SqlData
    Redis --> RedisData
```
