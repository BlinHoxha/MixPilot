# Framework-based repositories, services and AI

MixPilot and Framework remain separate solutions in the same workspace. Shared Framework and AI-Framework projects are included in the solution for configuration consistency; project references consume their sibling libraries.

## Request flow

```text
TracksController -> ITrackService -> TrackService -> ITrackRepository -> TrackRepository
                                                       |                   |
                                                Framework.BaseService  Framework.BaseRepository
                                                                           |
                                                                   MixPilotDbContext
```

Collections, DJ profiles and sets follow the same pattern. Each abstraction has its own file under `Application/Abstractions`. The previous all-purpose `Abstractions.cs`, `IMixPilotStore`, `EfMixPilotStore` and `LibraryService` have been replaced.

Controllers handle HTTP binding and status codes. They depend on service interfaces and never query EF or repositories directly. Services own authorization declarations, duplicate decisions, revisions, rights, set generation and DTO mapping. Repositories own EF queries, includes, filtering, paging and persistence. All repository instances in one request share the same scoped MixPilotDbContext and its owner filters.

`MixPilotRepository<TEntity>` extends Framework's three-parameter `BaseRepository` with the app context and existing persistence error translation. Specialized repositories reuse inherited `GetById`, `Add`, `AddRange`, `Remove` and `SaveChanges`, and add only their feature queries. Database calls stay in Infrastructure; services do not use the inherited repository IQueryable surface.

`MixPilotService<TEntity,TRepository>` extends Framework's `BaseService`. Its protected persistence helpers reuse Framework's create/bulk-create/delete orchestration, logging and single SaveChanges call after the typed service validates the operation. Self-maps preserve already-validated aggregates. Tracked edits save once through the same repository. CSV imports and aggregate version changes retain one transactional save.

Framework's generic public mutation methods accept arbitrary mapped DTOs and do not carry MixPilot's expectedRevision or authorization declaration. MixPilot closes those inherited generic entry points and exposes typed application operations instead, including when a caller casts the service to `IBaseService`. This prevents a second path from bypassing business rules. Generic base CRUD controllers therefore remain optional; the existing HTTP contracts are preserved.

## AI request flow

```text
SetExplanationsController
  -> ISetExplanationService / SetExplanationService
    -> ISetService -> owner-scoped set repository
    -> AIFramework.KnowledgeAnswerService
      -> IKnowledgeRetriever / request-scoped SetExplanationEvidence
      -> AIFramework.IChatModel / LocalGroundedChatModel or AzureOpenAiClient
```

AI-Framework's AI services use composition and provider interfaces; there is no generic BaseAiService class to inherit. MixPilot reuses the existing concrete KnowledgeAnswerService and provider abstractions instead of duplicating model calls.

MixPilot explicitly selects AddLocalAi, AddAzureAi or AddBedrockChat from AI-Framework when AI is enabled, then supplies its own scoped evidence retriever and document profile. Framework no longer registers AI. It does not register FrameworkDbContext, generic reference-host repositories, or a second application database.

The explanation endpoint accepts only a question. Set ownership is resolved before any provider call. The owner-derived tenant, profile, document ID and scope are supplied by the application, never by the client. Evidence exists only for the current request and is not uploaded into a shared search index. Up to 20 chunks contain a set overview and selected track snapshots; questions mentioning an artist/title prioritize matching tracks. Answers must not claim evidence for omitted tracks.

Saved metadata/rights are historical explanation evidence, not new permission grants. AI cannot change set order, score compatibility, grant rights or save revisions through this endpoint. Provider failure returns 503 and leaves deterministic planning available. `grounded` means evidence was supplied, not independent verification of every model statement.

## Configuration

Base configuration disables AI. Development enables Framework's `Local` provider, which returns source excerpts and citations; it is **not an LLM**. Capabilities include `setExplanations` only when AI is enabled.

```text
AI__Enabled=true
AI__Provider=Azure
AI__Azure__OpenAiEndpoint=...
AI__Azure__ChatDeployment=...
AI__Azure__EmbeddingDeployment=...
AI__Azure__SearchEndpoint=...
AI__Azure__SearchIndex=...
```

Azure uses AI-Framework's compatibility registration and DefaultAzureCredential. That registration validates all five Azure settings even though this explanation workflow replaces search retrieval with set evidence and does not call embeddings/search. Configure credentials and deployments through the AI-Framework provider configuration and application deployment process. The Local provider is rejected outside Development when AI is enabled. No Azure network calls or credentials were used during this refactor's tests.

Example: `POST /api/v1/sets/{id}/explanation` with `{ "question": "Why were these tracks selected?" }`. The response retains Framework's CommonResult envelope and includes set ID/version plus AIFramework.Contracts.GroundedAnswerResponse with provider and citations. AI-disabled and provider-unavailable errors use the same Problem Details contract as other endpoints.

Natural-language conversion into structured SetRequirements, document ingestion/learning content, audio analysis and conversational editing remain separate future operations. Their adapters should reuse applicable Framework building blocks without asking the grounded-answer API to act as an unconstrained JSON parser or audio engine.
