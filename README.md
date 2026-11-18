# MixPilot

Personal music-library management and deterministic DJ set planning, built on the sibling **Framework** repository. This is the first backend increment of the AI DJ Multi-Source platform. The React web app and Expo mobile preview live in the sibling `MixPilot.Frontend` repository.

## Included

- ASP.NET Core API with versioned `/api/v1` routes and generated OpenAPI.
- Shared request/response DTOs, Framework `CommonResult<T>` and `PageResponse<T>`, structured Problem Details, bounded pagination, and optimistic concurrency.
- Owner-isolated track metadata CRUD, authorization declarations, duplicate detection, collections and DJ profiles.
- Atomic CSV imports of up to 500 records; a synthetic 60-track sample catalog.
- BPM, Camelot key, genre and energy scoring; whole-track duration planning; explicit rights filtering.
- Saved sets, manual reorder/remove/replace/lock, regeneration around locked positions, and immutable version snapshots.
- Optional set explanations through Framework KnowledgeAnswerService and IChatModel, with owner-scoped evidence and citations.
- Dedicated owner-scoped repositories and typed services; Framework supplies the lean persistence context.
- PostgreSQL persistence and initial EF migration. No data is stored in an in-memory production repository.
- JWT bearer authentication integration; one fixed demo identity only when explicitly enabled in Development.
- Unit, relational integration, concurrency, architecture and OpenAPI drift tests.

The set builder accepts structured requirements and natural-language prompt previews. Durable set-generation jobs are available to the web client. Audio processing, SignalR, external sources, personalization, export, marketplace and automatic mixing remain future increments. `/api/v1/capabilities` exposes this distinction to clients.

## Repository layout

```text
repos/
  Framework/                 shared foundation, separate repository
  AI-Framework/              independent AI libraries, separate repository
  MixPilot/
    MixPilot.sln
    src/
      MixPilot.Api/          HTTP, authentication, composition, OpenAPI
      MixPilot.Contracts/    public client contracts; no EF or domain entities
      MixPilot.Domain/       entities, rights and deterministic DJ engine
      MixPilot.Application/  use cases and persistence/provider interfaces
      MixPilot.Infrastructure/ PostgreSQL and FrameworkDbContext implementation
    tests/MixPilot.Tests/
    docs/                    architecture, requirements and OpenAPI snapshot
    samples/                 synthetic metadata only
    scripts/
```

`FrameworkRoot` defaults to `../Framework`. Override it at build time if your checkout layout differs:

```powershell
dotnet build MixPilot.sln -p:FrameworkRoot=C:\path\to\Framework
```

MixPilot references Framework's domain base, common contracts and lean persistence context. DJ business rules and authorization remain application-owned. AI-Framework's document Q&A pipeline is reused for explanations, while musical scoring remains in the deterministic DJ engine. See [layering and AI integration](docs/layering-and-ai.md).

## Build and test

Build and run MixPilot with the .NET 10 SDK and runtime. The .NET 9 runtime is no longer needed for MixPilot; the sibling Framework repository supplies its net10.0 target.

```powershell
dotnet restore MixPilot.sln
dotnet build MixPilot.sln --no-restore
dotnet test MixPilot.sln --no-build
```

Integration tests run against a disposable SQLite relational database with test-only UTC/decimal conversions. PostgreSQL migration SQL and model drift are tested without connecting to a server. A live PostgreSQL migration/application smoke test is still required in your deployment environment.

MixPilot uses `Framework.Persistence` directly. Its application and infrastructure projects do not reference Framework's legacy mapping facades.

## Start locally

1. Open `MixPilot.sln` in Visual Studio and set **MixPilot.Api** as the startup project.
2. Start PostgreSQL. If Docker is installed, from the repository root run:

   ```powershell
   docker compose up -d postgres
   ```

3. Restore the repository-local EF tool and apply the migration:

   ```powershell
   dotnet tool restore
   dotnet ef database update --project src/MixPilot.Infrastructure --startup-project src/MixPilot.Api
   ```

4. Start the `http` launch profile:

   ```powershell
   dotnet run --project src/MixPilot.Api --launch-profile http
   ```

5. Open `http://localhost:5290/swagger`.

The Development configuration targets `localhost:5432`, database/user `mixpilot`, password `mixpilot_dev`. These are local demo credentials. Bindings in compose and the launch profile are local-only. Supply `ConnectionStrings__MixPilot` to use another database. Migrations are explicit; the API does not automatically mutate the database on startup.

The Development profile signs every request in as one demo user. Never expose it as a multi-user service. To use real identity, disable `Authentication__UseDevelopmentIdentity` and configure `Authentication__Authority` and `Authentication__Audience`. The authority must use HTTPS and issue `iss` and `sub` claims. Registration, password management and OIDC web/mobile flows belong to the selected identity provider and future clients.

## Try the first product loop

With the API running:

```powershell
./scripts/Import-SampleLibrary.ps1
```

Then submit this body to `POST /api/v1/sets/generate` in Swagger:

```json
{
  "name": "One-hour practice",
  "requirements": {
    "durationSeconds": 3600,
    "minBpm": 120,
    "maxBpm": 130,
    "genre": "House",
    "energyCurve": "gradualBuild",
    "mode": "planning",
    "territory": "HU",
    "excludedTrackIds": []
  }
}
```

Keep the returned set ID and revision. `PUT /api/v1/sets/{id}/tracks` accepts the complete desired track order and locked track IDs. It supports reorder, removal and replacement with eligible personal-library tracks. `POST /api/v1/sets/{id}/regenerate` preserves locked positions. Every edit creates a new recoverable version. A stale `expectedRevision` returns HTTP 409.

The engine uses a deterministic greedy heuristic, not a global optimizer. It considers tracks with known BPM and energy, uses exact case-insensitive genre/mood constraints, and fills up to 110% of the target duration. Underfilled results carry warnings. Durations are sums of whole tracks, not rendered-mix durations. Assess musical quality with real DJ listening tests.

## Web and mobile handoff

The sibling `MixPilot.Frontend` workspace has a React web app and Expo mobile preview. Both use this API through its checked-in `docs/openapi.json` snapshot and generated client. See the [API contract](docs/api-contract.md) and [OpenAPI document](docs/openapi.json). No frontend toolchain is required to build this backend.

After an intentional contract change, run `./scripts/Export-OpenApi.ps1`, review the schema diff, then run the tests. The normal test suite fails when the generated contract drifts from the snapshot. Update the frontend's checked-in snapshot and regenerate its TypeScript client separately.

See [implementation status](docs/implementation-status.md) for requirement coverage and [architecture decisions](docs/architecture.md) for the next increments.

AI libraries now come from the sibling AI-Framework repository. AIFrameworkRoot defaults to ../AI-Framework and can be overridden alongside FrameworkRoot. Shared source projects are included in the solution so Release builds use consistent configurations. AI-Framework owns provider configuration and deployment modules; Framework owns general persistence/web conventions.

For local structured prompt interpretation, run `ollama pull qwen3:4b` and set `$env:AI__Provider = "Ollama"` before starting the API in Development. The `AI:Ollama` defaults in `appsettings.Development.json` target `http://localhost:11434/`; only loopback endpoint roots are accepted. Ollama is Development-only and is not the default. Set explanations continue to use the Local extractive model. Restore `AI__Provider=Local` when Ollama is unavailable.
