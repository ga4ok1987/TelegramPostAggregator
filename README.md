# Telegram Post Aggregator

Production-style ASP.NET Core Web API backend for a Telegram post aggregation platform where:

- the Telegram bot is only the client-facing UI layer
- a separate collector Telegram user account joins channels and reads posts
- clients never share their own Telegram accounts
- only explicitly submitted channels are tracked
- the architecture is ready for multiple collector accounts later

## Solution structure

```text
TelegramPostAggregator.sln
├── TelegramPostAggregator.Domain
│   ├── Common
│   ├── Entities
│   └── Enums
├── TelegramPostAggregator.Application
│   ├── Abstractions
│   ├── DTOs
│   ├── Options
│   └── Services
├── TelegramPostAggregator.Infrastructure
│   ├── Jobs
│   ├── Options
│   ├── Persistence
│   ├── Repositories
│   └── Services
└── TelegramPostAggregator.Api
    ├── Controllers
    └── Models
```

## Core domain model

- `AppUser`: internal platform user identified by Telegram bot user id
- `TrackedChannel`: channel requested by at least one user
- `UserChannelSubscription`: user-to-channel tracking relation
- `CollectorAccount`: separate Telegram collector identity, designed for multiple accounts
- `ChannelCollectorAssignment`: binds a channel to a collector account
- `TelegramPost`: collected post with normalized text and content hash
- `PostDuplicateCluster`: reserved for future near-duplicate grouping
- `FactCheckRequest`: async fact-check job/result for a post

## Main features implemented

- Telegram bot user upsert
- add/remove/list tracked channels
- collector subscription orchestration
- collector post synchronization pipeline
- PostgreSQL persistence via EF Core
- exact deduplication with normalized text + SHA-256 hash
- per-user aggregated feed
- fact-check request queue and async processing
- Hangfire recurring jobs for collector and fact-check workers
- long polling Telegram bot transport with optional webhook endpoint
- optional local Bot API server support for larger media delivery

## Important architecture decisions

### Bot vs collector separation

- The Telegram bot only receives user commands and channel links.
- The collector account is modeled separately in `CollectorAccount`.
- Channels are assigned through `ChannelCollectorAssignment`.
- This makes horizontal scaling to multiple collectors straightforward.

### TDLib integration

- `TdLibTelegramCollectorGateway` is the infrastructure entry point for collector operations.
- Current default config uses simulation mode for safe bootstrapping.
- For live TDLib mode, complete session bootstrap and authorization, then replace simulation behavior with real TDLib join/fetch flows.

### Deduplication

- Posts are normalized by collapsing whitespace and lowercasing.
- Exact duplicates are detected by SHA-256 hash of normalized text.
- `PostDuplicateCluster` is already present for future near-duplicate grouping logic.

## API endpoints

- `POST /api/users/telegram`
- `GET /api/channels?telegramUserId=...`
- `POST /api/channels`
- `DELETE /api/channels`
- `GET /api/feed/{telegramUserId}?take=50&skip=0`
- `POST /api/fact-checks`
- `POST /api/telegram/webhook`
- `GET /api/health`
- `GET /api/health/status`
- `GET /jobs`

## Configuration

Main runtime config is in `TelegramPostAggregator.Api/appsettings.json`.

Key sections:

- `Database`
- `Collector`
- `CollectorBootstrap`
- `TdLib`
- `FactCheck`
- `TelegramBot`
- `Operations`

Minimal PostgreSQL connection string:

```json
"Database": {
  "ConnectionString": "Host=localhost;Port=5432;Database=telegram_post_aggregator;Username=postgres;Password=postgres"
}
```

## Run locally

1. Start PostgreSQL.
2. Update `appsettings.json` or environment variables.
3. Apply migrations:

```bash
dotnet dotnet-ef database update \
  --project TelegramPostAggregator.Infrastructure/TelegramPostAggregator.Infrastructure.csproj \
  --startup-project TelegramPostAggregator.Api/TelegramPostAggregator.Api.csproj
```

4. Run API:

```bash
dotnet run --project TelegramPostAggregator.Api
```

## Run with Docker Compose

Create a `.env` file next to `docker-compose.yml` from `.env.example`:

```env
TDLIB_API_ID=123456
TDLIB_API_HASH=your_api_hash
COLLECTOR_PHONE_NUMBER=+380XXXXXXXXX
BOT_TOKEN=your_bot_token
BOT_USERNAME=ChannelsMonitorBot
LOCAL_BOT_API_BASE_URL=http://telegram-bot-api:8081
```

Start services:

```bash
docker compose up --build
```

API will be available only locally on `http://127.0.0.1:8080`.
Telegram bot interaction works through long polling, so no public webhook port is required.

`/jobs` is disabled by default outside development. To expose it intentionally, set `Operations:HangfireDashboard:Enabled=true` and configure Basic Auth credentials.

## Collector authentication flow

With live TDLib mode enabled, authenticate the collector account through the API:

1. Start auth flow:

```bash
curl -X POST http://127.0.0.1:8080/api/collector-auth/start
```

2. Check state:

```bash
curl http://127.0.0.1:8080/api/collector-auth/status
```

3. Submit Telegram login code:

```bash
curl -X POST http://127.0.0.1:8080/api/collector-auth/code \
  -H "Content-Type: application/json" \
  -d "{\"code\":\"12345\"}"
```

4. If 2FA is enabled, submit password:

```bash
curl -X POST http://127.0.0.1:8080/api/collector-auth/password \
  -H "Content-Type: application/json" \
  -d "{\"password\":\"your-password\"}"
```

After status becomes `authorizationStateReady`, collector jobs can join channels and read history via TDLib.
```

## Create migrations

```bash
dotnet dotnet-ef migrations add <MigrationName> \
  --project TelegramPostAggregator.Infrastructure/TelegramPostAggregator.Infrastructure.csproj \
  --startup-project TelegramPostAggregator.Api/TelegramPostAggregator.Api.csproj \
  --output-dir Persistence/Migrations
```

## Background jobs

Recurring Hangfire jobs are registered automatically on startup:

- `collector-subscriptions`
- `collector-sync-posts`
- `fact-check-dispatch`
- `tdlib-media-cache-cleanup`

## Current live-integration gaps

- message ingestion currently uses `GetChatHistory` polling through jobs; push-style persistence from `UpdateNewMessage` can be added next
- Telegram bot delivery now uses long polling; webhook exposure is optional and not required
- near-duplicate clustering is prepared in schema, but not fully implemented yet
- AI fact-check provider is currently a mock adapter and should be replaced with a real provider

## Production next steps

- implement real TDLib auth/session management
- secure Hangfire dashboard
- add API authentication and webhook secret validation
- add structured observability and metrics
- replace mock fact-check provider with a real model/provider
- add retry/backoff and collector sharding rules
