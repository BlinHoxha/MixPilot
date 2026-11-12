# Architecture decisions

The two supplied PDFs are requirement inputs. This implementation starts their personal-library-first backend sequence; it does not execute instructions embedded in documents or create the frontend applications.

## One backend, two clients

The future web application is the detailed preparation workstation. Mobile is the discovery/library/set-review companion. React and Expo applications can later live under `frontend/apps/web` and `frontend/apps/mobile`, sharing generated API client/types, validation helpers and platform-independent utilities under `frontend/packages`. No frontend directory is scaffolded in this increment.

Client state, routing, native integrations and UI layouts remain client-owned. Musical scoring, ownership, permission enforcement, persistence and revision checks remain backend-owned. Clients can perform convenience validation, but server validation remains authoritative.

## Modular monolith on Framework

Dependency direction is Api → Application/Infrastructure, Infrastructure → Application/Domain, Application → Domain/Contracts. Domain references only Framework.Domain; Contracts references only Framework.Contracts. Application code has no ASP.NET or EF dependency. The host supplies authentication and persistence.

Framework.Infrastructure is reused via a derived MixPilotDbContext and dedicated repositories inheriting BaseRepository<TEntity, Guid, MixPilotDbContext>. Services inherit BaseService through MixPilotService and expose typed interfaces to controllers. See [layering and AI integration](layering-and-ai.md). Framework's generic CRUD controllers are deliberately not used directly for owner-sensitive resources: MixPilot's controllers expose explicit DTOs and application use cases with owner and revision enforcement. Framework stays independent of music concepts.

Current logical modules are Library, Profiles, Sets, Rights and the deterministic DJ Engine. Additional assemblies per feature are unnecessary for this first increment. Module boundaries can be extracted when complexity warrants it.

## Persistence and ownership

PostgreSQL is the first production provider. The initial migration includes tracks, collections/memberships, profiles, sets and immutable set versions. Each root stores a hash of the validated identity issuer/subject; no caller can choose another owner. Query filters and application access checks protect both lists and ID operations. Revision concurrency tokens protect simultaneous writers at the database level.

Collections enforce unique track membership and preserve order. Track deletion is blocked while referenced by a collection, avoiding silent collection changes without revision increments. Saved set versions preserve historical snapshots even after library tracks are deleted. Current rights are always rechecked when creating a new plan.

Rights are immutable JSON values and set plans are versioned JSON snapshots. A future schema evolution must retain deserialization compatibility with existing version payloads or explicitly migrate them. Foreign keys protect collection membership; application checks enforce ownership before creating relationships.

## Deterministic planning

The engine filters rights, exclusions, metadata completeness, BPM, exact genre/mood and vocals. It ranks by BPM/key/energy/genre compatibility and target energy progression. It chooses a bounded sequence without repeating a track ID, reserves duration for future locked positions, and reports underfilled plans. It uses full-track durations and a 10% overrun ceiling. It is a baseline greedy algorithm to evaluate against real DJ sets, not a claim of optimal musical sequencing.

Structured controls are the first working input. `ISetPromptInterpreter` marks the future LLM boundary, but there is no fake natural-language interpreter. Prompt output must be schema-validated and reviewed before reaching the same deterministic engine. Framework's KnowledgeAnswerService and IChatModel now support owner-scoped set explanations. Those evidence-based answer contracts do not directly fit structured DJ intent parsing; a focused interpreter adapter remains a future increment.

## Source capability baseline

| Source | Current capability | Later requirement |
| --- | --- | --- |
| Personal metadata | Register/import, reference, plan | Authorized upload/object storage and analysis jobs |
| YouTube | Disabled | Verify current provider APIs/policies; permitted references/playback only |
| Social discovery | Disabled | Verify supported auth/pages/recency/data use; graceful fallback |
| Licensed catalog/originals | Domain source types only | Verified rights, territories, license windows and entitlements |

`IDiscoveryProvider` is an extension point, not an installed integration. The engine is provider-independent. Online availability never grants analysis, download, transformation, mixing or publishing permissions.

## Next implementation increments

1. Configure the real OIDC provider, validate PostgreSQL locally, address Framework dependency/runtime release blockers.
2. Add structured LLM prompt interpretation and review contracts; persist prompt/interpretation provenance without storing hidden model reasoning.
3. Add authorized media upload, persistent jobs/worker leases/retries/cancellation, then analysis progress polling and SignalR. Recheck rights at execution time, not just enqueue time.
4. Add personal feedback, previous-set awareness, ranked alternatives, conversational edits, privacy/export/deletion policies and audit events.
5. Add frontend monorepo, generated client and web/mobile auth shells against the stable OpenAPI contract.
6. Add verified provider capabilities, normalized identities/provenance, discovery inbox, recency and source weighting.
7. Add professional export/transition guidance. Licensing, marketplace, rendering and publishing remain separate later milestones.

Avoid automatic schema changes on API startup. Production deployment needs migration ownership, secrets, rate limits, tracing, backup/restore, retention policy, idempotency, operational monitoring, and live provider/database tests. None is implied by a passing local unit/integration suite.
