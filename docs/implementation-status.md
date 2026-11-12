# Functional requirement coverage

Source: `AI_DJ_Multi_Source_Platform_Functional_Implementation_Plan.pdf` v2.0 and `DJ_AI_Platform_Frontend_Management_Architecture.pdf`.

This increment is a functioning **backend foundation and personal-library planning slice**, not completion of all MVP/UI or future phases.

| Functional requirements | Status in this increment |
| --- | --- |
| FR-001 authentication | JWT integration and owner isolation; external identity provider and signup clients still to configure |
| FR-002 DJ profile | Basic display name, genre, BPM range, vocal preference; richer profile later |
| FR-003 personal library | Metadata CRUD, search/pagination and owner isolation |
| FR-004 upload/registration | Authorized metadata registration only; no media upload |
| FR-005 bulk import | Atomic bounded CSV import; other DJ formats later |
| FR-006 duplicate detection | Normalized artist/title/version detection; explicit keepDuplicate on manual writes; no merge workflow |
| FR-007 track metadata | Artist/title/version, duration/BPM/key/energy/genre/mood/vocals; album, label, release date, subgenre, tags later |
| FR-008 audio analysis | Deferred; manual metadata supported |
| FR-009 collections | Ordered owner-scoped collections and revisioned membership |
| FR-010–019 sources/discovery/identity | Source and rights types established; live adapters, inbox and normalization workflows deferred |
| FR-020–021 natural language | Model-backed set-intent preview with owner-scoped genre/mood vocabulary, validated requirements and confirmation assumptions; Azure structured provider optional, no prompt-to-generation action |
| FR-022 manual controls | Duration/BPM/energy/genre/mood/vocals/territory/exclusions; multi-source controls later |
| FR-023 candidate pool | Personal-library candidates, bounded at 5,000; multi-source identity resolution later |
| FR-024 rights filter | Planning/Mixable eligibility, license windows and territories; permissions fail closed |
| FR-025–028 musical compatibility | Baseline deterministic filtering, BPM/Camelot/energy/genre scoring and energy curves |
| FR-029–031 taste/recency/source scores | Deferred |
| FR-032 optimization | Bounded greedy sequence with duration limits; global/hybrid optimization deferred |
| FR-033 results | Ordered snapshots, source/availability/rights, BPM/key/energy and concise reasons |
| FR-034 missing tracks | Deferred until external references are enabled |
| FR-035 lock | Locked positions preserved during regeneration; unavailable locks fail explicitly |
| FR-036 replace | Manual eligible replacement supported; ranked alternative suggestions deferred |
| FR-037 conversational editing | Deferred |
| FR-038 versions | Immutable recoverable versions after edits/regeneration |
| FR-039 explanation | Deterministic reasons plus Framework grounded-answer service with citations; local extractive provider by default in Development, configurable Azure provider not live-tested |
| FR-040–048 advanced intelligence/discovery/acquisition | Deferred |
| FR-049 DJ software export | Deferred |
| FR-050–051 modes | Planning works; Mixable filtering exists but initial registrations have no audio grants |
| FR-052–060 artist/label/licensing/marketplace/rendering | Deferred future milestones |
| FR-061 privacy/transparency | Owner isolation and visible rights/source status; preference transparency and full deletion/retention workflows later |
| FR-062 provider policy configuration | Documented disabled-provider baseline; runtime policy registry later |

## Requirement sources

By owner decision of 2026-11-12, the MixPilot Documentation v1.2 (Multi-Device + Mobile-Ready) package is the target requirement set; source code is the truth for implemented behavior; the two PDFs in `docs/reference/` are historical inputs. The owner also decided on 2026-11-12 to keep the `expectedRevision` request field for optimistic concurrency; the v1.2 addenda's `If-Match` proposal is not adopted.

## Frontend document coverage

Implemented now in the backend: one API for both clients, DTO boundaries, metadata-only payloads, pagination, stable enums/errors/operation IDs, revision conflicts, capabilities, OpenAPI snapshot/drift checking and configurable browser CORS. The sibling `MixPilot.Frontend` repository has a React web workstation for library, collection, set planning and history workflows, an Expo companion preview for library search/details and saved-set review, a generated OpenAPI client, and shared validation. Its checked-in OpenAPI snapshot comes from this backend.

Pending in the frontend: production mobile sign-in, native secure credential storage, mobile editing/quick-save and full native device testing. Audio upload/playback, durable jobs and SignalR, external discovery and DJ export also await backend capabilities.

Prepared backend design: durable asynchronous jobs with polling and real-time notifications. These are documented interfaces for subsequent work, not working features claimed by the current API.

## Verification and release gaps

The automated suite covers domain rules, relational CRUD and constraints, authenticated owner boundaries, duplicate/CSV validation, stale and concurrent writes, saved versions/locks, OpenAPI drift, architecture boundaries and PostgreSQL migration/model generation.

Tests use SQLite for live relational operations. A real PostgreSQL server, identity provider, browser/mobile client, external service and real music evaluation have not been exercised. Framework's AutoMapper advisory and the joint .NET runtime/dependency upgrade need resolution before production release. The API is a development foundation, not a production launch declaration.
