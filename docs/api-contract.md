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
- Empty-body status responses, including 401 authentication challenges, 403 authorization failures and unmatched-route 404s, also use problem JSON regardless of `Accept`. Their codes are `authentication_required`, `forbidden` and `not_found` respectively.
- Clients branch on status and `code`, not localized messages. Example codes: `validation_failed`, `duplicate_track`, `revision_conflict`, `resource_in_use`, `no_eligible_tracks`, `locked_track_unavailable`, `locked_position_unreachable`.
- New profile/collection writes use `expectedRevision: 0`; updates use the last fetched revision. Stale writes return 409. Clients re-fetch and offer reconciliation; do not silently overwrite.
- GET requests may be retried. The five POST operations for tracks, collections, CSV imports, synchronous set generation and set-generation jobs accept an optional owner-scoped `Idempotency-Key` header for safe retries after an ambiguous network failure. Keys are 1-100 ASCII letters, digits, underscores or hyphens and expire after 24 hours. The same key, route and raw body replays the original status and JSON body with `Idempotent-Replayed: true`; a different route or body returns 422 `idempotency_key_reused`, and a pending request returns 409 `idempotency_in_progress`. Without a key, mutations retain their existing behavior. A crash between the business commit and marking a key completed can leave it pending until expiry. CSV sequential re-imports skip normalized duplicates.

Pagination is one-based: `pageNumber=1&pageSize=20` with a maximum size of 100. Data contains `items`, `pageNumber`, `pageSize`, `totalCount`, and `totalPages`. Sorting always adds an ID tie-breaker. Pages can shift as the library changes; cursor paging for high-volume discovery is a later contract.

## Current endpoints

| Method | Route | Contract |
| --- | --- | --- |
| GET | `/api/v1/capabilities` | Available/planned features and limits |
| POST | `/api/v1/set-generation-jobs` | Queue durable set generation; returns 202, a job body and `Location` |
| GET | `/api/v1/jobs` | Owner-scoped newest-first job page |
| GET | `/api/v1/jobs/{id}` | Owner-scoped job status and result set ID |
| POST | `/api/v1/jobs/{id}/cancel` | Cancel queued job or request running-job cancellation |
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
| GET | `/api/v1/sets/{id}/positions/{position}/alternatives` | Ranked replacement suggestions for an unlocked position in the latest version |
| POST | `/api/v1/sets/{id}/explanation` | Evidence-based explanation through Framework AI services |
| PUT | `/api/v1/sets/{id}/tracks` | Full order, removals/replacements and locks |
| POST | `/api/v1/sets/{id}/regenerate` | New requirements, preserving locked positions |
| GET | `/api/v1/sets/{id}/versions` | Version metadata |
| GET | `/api/v1/sets/{id}/versions/{version}` | Historical snapshot |
| POST | `/api/v1/sets/{id}/versions/{version}/approve` | Approve an existing version with `expectedRevision` |
| GET | `/health/live`, `/health/ready` | Anonymous operational checks |

The detailed field definitions, validation limits and stable operation IDs are in `openapi.json`.

`POST /api/v1/set-intents` accepts a 1–1000 character nonblank `prompt` and a two-letter uppercase `territory`. It returns `requirements`, `assumptions`, `unresolved`, `provider` and `model` in the normal success envelope. The returned requirements always use planning mode, the request territory and an empty exclusion list. Clients should show the assumptions and unresolved terms for confirmation or editing, then submit the confirmed requirements to `/api/v1/set-generation-jobs` or `/api/v1/sets/generate`. The model sees only the prompt, the closed output schema, the five energy-curve values and up to 200 distinct genre and mood values from that owner's library. Unknown genres or moods are left unresolved; unsupported duration or BPM values are defaulted with an assumption. Invalid model output returns 422 `prompt_not_interpretable`; an unconfigured or disabled structured provider returns 503 `prompt_interpretation_unavailable`; provider transport failures and timeouts return 503 `ai_provider_unavailable` or `ai_provider_timeout`. Azure structured generation is available only when AI is enabled, `AI:Provider` is `Azure`, and `AI:Azure:Structured` supplies an HTTPS `Endpoint` and a `Deployment`. In Development, `ollama pull qwen3:4b` and `$env:AI__Provider = "Ollama"` enable structured interpretation against the configured `AI:Ollama` loopback endpoint root. Set explanations remain Local extractive. Local and Aws providers do not enable prompt interpretation.

Model-supplied interpretation assumptions describe missing or interpreted prompt values, with at most five entries of 160 characters each. Mapping assumptions about defaulted requirements are retained. The web builder uses this preview to fill editable requirements before generation.

## Rights and availability

Initial registrations are `myLibrary` with availability `registeredMetadata`. This does not claim that an audio file is uploaded, playable, downloadable or verified. The authorization declaration enables reference/planning only. Audio permissions default false and cannot be supplied through track-write requests. Metadata imports never download media.

Planning filters by `CanReference`, dates and territories. Mixable additionally requires `CanMix`; initial metadata-only registrations therefore do not qualify. `Mixable` is an eligibility filter, not a mixing endpoint. Rendering/export/publishing require separate checks and implementations later.

Saved versions contain metadata and rights snapshots for historical explanation. They are **not** current permission grants. Regeneration and manual edits re-read current tracks and validate current rights. Every future media operation must check live rights rather than trusting a saved response.

Approval rechecks every track's current eligibility and rights against the selected version's requirements. A failure returns 422 `track_ineligible` without changing the set. A successful approval increments the set revision and sets `approvedVersion`; repeating approval of that version at the current revision changes nothing. New edit/regeneration versions remain `draft` while the approved version stays `approved`. When another version is approved, the earlier approval becomes `superseded`. `SetResponse` includes `status` and `approvedVersion`; version metadata includes `status` and `approvedAt`.

Set version numbers are contiguous within a set and are independent of its `revision`. `approvedAt` records when a version was first approved; later supersession does not change it.

`GET /api/v1/sets/{id}/positions/{position}/alternatives?limit=10` returns the latest version number, position, current track ID and up to `limit` candidates from the owner's current library (maximum 5,000 registered tracks). `limit` is 1–25, default 10; invalid values return 400 `validation_failed`. Missing or unowned sets and missing positions return 404 `not_found`; locked positions return 422 `position_locked`. When no candidate qualifies, `items` is empty (200).

Suggestions recheck current rights and musical constraints, exclude tracks already in the set, and reject longer swaps that exceed the 110% duration budget. Equal or shorter swaps are allowed even if the set is already over budget. Ranking combines transition compatibility with the existing neighbours (55%), the requested energy curve (30%) and duration similarity (15%), then track ID for ties. Scores and components are reported to three decimals, with BPM, key, energy and duration reasons. The endpoint is read-only and changes no revision or version. Apply a suggestion by replacing the track ID in the full ordered list sent to `PUT /api/v1/sets/{id}/tracks`, with the current `expectedRevision` and desired locks; that existing write revalidates eligibility. Capabilities advertise `setAlternatives`.

## Authentication, caching and transport

Use an OIDC provider with client-appropriate authorization code + PKCE flows when the frontend is built. Web and native clients must request the same backend audience. Native tokens belong in secure platform storage. No secrets belong in client bundles. Authentication does not yet include an app-hosted account system.

The browser CORS allowlist is configurable; the Development default permits `http://localhost:5173` and `http://127.0.0.1:5173`. Mobile does not require browser CORS. API authorization applies equally to both. Use HTTPS for deployed clients.

TanStack Query keys should include the active identity and the resource/filter tuple. Clear identity-scoped caches at logout. On a successful mutation, refresh affected detail/list/set-version queries. `registeredMetadata` must not be rendered as a playable audio asset.

## Set-generation jobs and planned asynchronous contract

Set creation and a successful job finish commit in one database transaction. If the worker loses its lease before finish, that attempt's set is rolled back; a reclaimed attempt can generate one result. The synchronous generation endpoint is unchanged.

Set generation now has persistent jobs. `POST /api/v1/set-generation-jobs` accepts a name plus exactly one of structured `requirements` or `prompt` with `territory`. It returns 202 and a `Location` pointing to `/api/v1/jobs/{id}`. The job contains `kind: setGeneration`, status (`queued`, `running`, `succeeded`, `failed`, `cancelled`), progress, optional `errorCode` and `resultSetId`, timestamps and a correlation ID. `GET /api/v1/jobs` is offset-paged, newest first. The worker polls PostgreSQL, claims with a two-minute lease and retries an expired lease up to three attempts. Queued cancellation is immediate; running cancellation is honored between interpretation and generation steps. Terminal cancellation returns 409 `job_not_cancellable`. Jobs are enabled by default and can be disabled with `Jobs:Enabled=false`; `Jobs:PollSeconds` defaults to 2. The synchronous set-generation endpoint remains available.

Analysis/discovery and large imports remain planned for persistent jobs. SignalR will send owner-scoped job-change notifications carrying job ID and revision, prompting re-fetch. Clients must recover by polling after reconnects and missed events. Native push is a separate delivery mechanism.

No SignalR hub is implemented. Current CSV imports are synchronous and capped at 500 records/500,000 characters; body size is bounded by Kestrel to 1 MiB.
