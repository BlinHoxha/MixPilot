# Shared web/mobile API contract

Both React web and React Native/Expo clients use `/api/v1`. No client-specific domain endpoints are needed. The web client may present advanced controls and the mobile client may present a smaller subset of the same requests.

## Conventions

- JSON property names and enum values use camelCase. Numeric enum values and unknown JSON properties are rejected.
- GUID identifiers are strings. Timestamps are UTC ISO-8601 values. Durations are integer seconds, BPM is numeric, energy is 1–10, keys are Camelot `1A`–`12B`.
- A nullable BPM/key/energy/vocal flag means **unknown**, never zero or false. Clients should render unknown values distinctly.
- No entity, EF navigation graph, owner key, access token or connection secret appears in public DTOs.
- Ownership comes exclusively from the authenticated issuer/subject pair, not a request body or user ID header. Every track, collection, profile and set query is scoped to that owner.
- Create responses use HTTP 201 and `Location`; successful deletes use 204 without an envelope.
- Successful JSON responses reuse Framework's envelope: `{ "succeeded": true, "data": ..., "error": null }`.
- Errors use `application/problem+json`, containing `type`, `title`, `status`, optional `detail`, `instance`, `code`, `traceId`, and validation `errors` keyed by field. OpenAPI exposes these as `ApiProblemResponse`.
- Clients branch on status and `code`, not localized messages. Example codes: `validation_failed`, `duplicate_track`, `revision_conflict`, `resource_in_use`, `no_eligible_tracks`, `locked_track_unavailable`, `locked_position_unreachable`.
- New profile/collection writes use `expectedRevision: 0`; updates use the last fetched revision. Stale writes return 409. Clients re-fetch and offer reconciliation; do not silently overwrite.
- GET requests may be retried. The four POST operations for tracks, collections, CSV imports and set generation accept an optional owner-scoped `Idempotency-Key` header for safe retries after an ambiguous network failure. Keys are 1-100 ASCII letters, digits, underscores or hyphens and expire after 24 hours. The same key, route and raw body replays the original status and JSON body with `Idempotent-Replayed: true`; a different route or body returns 422 `idempotency_key_reused`, and a pending request returns 409 `idempotency_in_progress`. Without a key, mutations retain their existing behavior. A crash between the business commit and marking a key completed can leave it pending until expiry. CSV sequential re-imports skip normalized duplicates.

Pagination is one-based: `pageNumber=1&pageSize=20` with a maximum size of 100. Data contains `items`, `pageNumber`, `pageSize`, `totalCount`, and `totalPages`. Sorting always adds an ID tie-breaker. Pages can shift as the library changes; cursor paging for high-volume discovery is a later contract.

## Current endpoints

| Method | Route | Contract |
| --- | --- | --- |
| GET | `/api/v1/capabilities` | Available/planned features and limits |
| POST | `/api/v1/set-intents` | Preview validated set requirements from a natural-language prompt; no set is generated |
| GET/POST | `/api/v1/tracks` | Search/page or authorized metadata registration |
| GET/PUT/DELETE | `/api/v1/tracks/{id}` | Detail, revisioned correction or removal |
| POST | `/api/v1/imports/csv` | Bounded atomic metadata import |
| GET/POST | `/api/v1/collections` | Page or create collections |
| GET/PUT/DELETE | `/api/v1/collections/{id}` | Ordered membership and revisioned edits |
| GET/PUT | `/api/v1/me/profile` | DJ preferences |
| GET | `/api/v1/sets` | Lightweight summaries, not full track lists |
| POST | `/api/v1/sets/generate` | Structured personal-library set generation |
| GET | `/api/v1/sets/{id}` | Latest set plan |
| POST | `/api/v1/sets/{id}/explanation` | Evidence-based explanation through Framework AI services |
| PUT | `/api/v1/sets/{id}/tracks` | Full order, removals/replacements and locks |
| POST | `/api/v1/sets/{id}/regenerate` | New requirements, preserving locked positions |
| GET | `/api/v1/sets/{id}/versions` | Version metadata |
| GET | `/api/v1/sets/{id}/versions/{version}` | Historical snapshot |
| POST | `/api/v1/sets/{id}/versions/{version}/approve` | Approve an existing version with `expectedRevision` |
| GET | `/health/live`, `/health/ready` | Anonymous operational checks |

The detailed field definitions, validation limits and stable operation IDs are in `openapi.json`.

`POST /api/v1/set-intents` accepts a 1–1000 character nonblank `prompt` and a two-letter uppercase `territory`. It returns `requirements`, `assumptions`, `unresolved`, `provider` and `model` in the normal success envelope. The returned requirements always use planning mode, the request territory and an empty exclusion list. Clients should show the assumptions and unresolved terms for confirmation or editing, then submit the confirmed requirements to `/api/v1/sets/generate` separately. The model sees only the prompt, the closed output schema, the five energy-curve values and up to 200 distinct genre and mood values from that owner's library. Unknown genres or moods are left unresolved; unsupported duration or BPM values are defaulted with an assumption. Invalid model output returns 422 `prompt_not_interpretable`; an unconfigured or disabled structured provider returns 503 `prompt_interpretation_unavailable`; provider transport failures and timeouts return 503 `ai_provider_unavailable` or `ai_provider_timeout`. Azure structured generation is available only when AI is enabled, `AI:Provider` is `Azure`, and `AI:Azure:Structured` supplies an HTTPS `Endpoint` and a `Deployment`. Local and Aws providers do not enable prompt interpretation.

## Rights and availability

Initial registrations are `myLibrary` with availability `registeredMetadata`. This does not claim that an audio file is uploaded, playable, downloadable or verified. The authorization declaration enables reference/planning only. Audio permissions default false and cannot be supplied through track-write requests. Metadata imports never download media.

Planning filters by `CanReference`, dates and territories. Mixable additionally requires `CanMix`; initial metadata-only registrations therefore do not qualify. `Mixable` is an eligibility filter, not a mixing endpoint. Rendering/export/publishing require separate checks and implementations later.

Saved versions contain metadata and rights snapshots for historical explanation. They are **not** current permission grants. Regeneration and manual edits re-read current tracks and validate current rights. Every future media operation must check live rights rather than trusting a saved response.

Approval rechecks every track's current eligibility and rights against the selected version's requirements. A failure returns 422 `track_ineligible` without changing the set. A successful approval increments the set revision and sets `approvedVersion`; repeating approval of that version at the current revision changes nothing. New edit/regeneration versions remain `draft` while the approved version stays `approved`. When another version is approved, the earlier approval becomes `superseded`. `SetResponse` includes `status` and `approvedVersion`; version metadata includes `status` and `approvedAt`.

## Authentication, caching and transport

Use an OIDC provider with client-appropriate authorization code + PKCE flows when the frontend is built. Web and native clients must request the same backend audience. Native tokens belong in secure platform storage. No secrets belong in client bundles. Authentication does not yet include an app-hosted account system.

The browser CORS allowlist is configurable; the Development default permits `http://localhost:5173`. Mobile does not require browser CORS. API authorization applies equally to both. Use HTTPS for deployed clients.

TanStack Query keys should include the active identity and the resource/filter tuple. Clear identity-scoped caches at logout. On a successful mutation, refresh affected detail/list/set-version queries. `registeredMetadata` must not be rendered as a playable audio asset.

## Planned asynchronous contract

Analysis/discovery and large imports will use persistent jobs: POST returns 202 with a job ID and polling URL; GET returns queued/running/succeeded/failed/cancelled state with stable error codes. SignalR will send owner-scoped job-change notifications carrying job ID and revision, prompting re-fetch. Clients must recover by polling after reconnects and missed events. Native push is a separate delivery mechanism.

No job endpoints or SignalR hub are advertised as implemented. Current CSV imports are synchronous and capped at 500 records/500,000 characters; body size is bounded by Kestrel to 1 MiB. This avoids pretending an in-process queue is a durable worker system.
