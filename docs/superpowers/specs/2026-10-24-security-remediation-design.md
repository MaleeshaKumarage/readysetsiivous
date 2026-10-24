# Remaining security remediation design

Status: proposed, awaiting consolidated approval.
Date: 2026-10-24
Scope and closure evidence: docs/security-remediation-ledger.md. Keep every ledger item in scope until proven resolved or disproved with evidence.

## Delivery approach

Recommended: small related PRs, each with a bounded synthetic regression, code review and merge after verified checks. Alternate approaches are one large PR (harder review/rollback) or infrastructure-only mitigation (leaves application access/lifecycle defects). Use isolated services and dummy identities, never customer records or paid AI calls. Production role migration and agent activation are separate observable rollout gates; do not claim them complete when source merges.

## 1. Customer and cleaner access

Booking credentials follow the separate 2026-10-24-booking-access-hardening-design.md: 32-byte random management key, hashed storage, POST body credentials, no phone fallback; existing bookings remain staff-managed. Preserve current cancellation rules. OTP/accounts and a full management/recovery screen are separate product decisions, not silently bundled.

QC: centralize current employee and form availability checks for public token reads and every mutation, including started visits. Inactive/missing employee, deleted form/shift, inactive shift and stale assignment must not grant anonymous access or mutation. Started work/history/attachments must be retained. Preserve authorized read-only historical reporting and existing conflict/idempotency rules. Make security dependencies required in production rather than allowing optional injection to skip checks. Confirm authenticated owner end/start rules and intentional started-assignment behavior before unifying them. Test deactivation during a request under the existing work reservation, no partial writes, completed replay, and tenant/owner boundaries.

## 2. Game admission and reconnect

Keep durable game state authoritative. Give each in-memory room explicit active lease/waiter and connection accounting. Enforce capacity atomically under one short registry lock; never hold it while awaiting a room gate or performing I/O. Reclaim an empty room only when no active or queued lease can still reference it; use identity checks so eviction cannot create two gates for one group. Retain empty rooms for a short configurable grace period (default 10 minutes), opportunistically sweep expired rooms before capacity checks, and reclaim idle action queues. Preserve the 1000 concurrent-room cap and current player credentials. Test sequential turnover beyond the cap, active saturation, concurrent creation, acquire cancellation/timeout, last disconnect, reconnect and cleanup races. No gameplay/marking changes.

## 3. Request identity and authentication resilience

Use explicit trusted proxy addresses/networks with a bounded forwarded chain; no trust-all setting and no unconditional CF-Connecting-IP trust. Configure middleware before rate limiting/authentication; a direct caller's spoofed forwarded headers must not change its quota identity. Keep per-client limits and bound/evict idle state, with clear 429 and Retry-After. Configure actual proxy topology only after verifying it; do not infer that every private address is trusted.

JWKS: validate configured issuer and strict realm shape before any network request. Make retrieval asynchronous without mutating shared JWT options; bound concurrent fetches and cache cardinality, collapse same-realm misses, negative-cache failures briefly, cap HTTP time and body, and refresh unknown signing keys with a bounded cooldown. Preserve tenant/platform audience separation, cryptographic verification and fail-closed behavior. Prove valid rotation, failed/slow/oversized responses, malicious realm strings, same/different-realm concurrency and cancellation. Select framework interfaces against the pinned SDK/package API during implementation.

## 4. Runtime privilege and persistence

Replace bootstrap Keycloak admin credentials in API runtime with dedicated service accounts scoped to required tenant identity operations; isolate the platform provisioner identity from tenant employee administration. Keep bootstrap credentials only in explicit setup/migration tooling. Rehearse client permissions and partial failures against synthetic Keycloak before switching runtime settings; no silent fallback to master admin.

Separate PostgreSQL bootstrap, migration/schema owner and runtime roles. Runtime gets only required database/schema/data/sequence privileges, no superuser/createdb/createrole. Disable runtime schema creation once a tested migration path exists. Include reviewed setup SQL, migration checks, backup/rollback and a rehearsal on a disposable database. Production credential/role changes require a concrete rollout review; no ad hoc SQL against live records.

Persist shared-dev uploads, application keys and Keycloak identity state using named volumes/external dev DB. A recreate/restart test must preserve synthetic records and files. Keep dev separate from production; existing state requires backup/import before replacing ephemeral services.

## 5. Documents, supply chain and release integrity

PDF signing: inspect page geometry and constrain raster dimensions/pixel count before native rendering; cap page count/aggregate decoded and encoded bytes, wall time and concurrent workers. Use a bounded isolated worker if native cancellation cannot enforce a deadline. Preserve signature evidence and attachments; never delete a signed agreement as cleanup. Test normal multi-page output and hostile synthetic geometry/expansion/failure fixtures.

Deploy the selected commit's reviewed scripts and Compose, with an immutable produced image digest and preserved rollback inputs. Verify workflow provenance and refuse unrelated refs/artifacts. Pin reviewed Actions and base/service images by immutable identifiers with an update inventory. Add a narrow Docker context ignore and generate reviewed NuGet lockfiles enforced in CI. Scan current exact dependency versions using primary advisory evidence; remediate confirmed reachable issues and document false positives. No speculative broad version upgrades.

Prove each actual AI HTTP attempt reserves configured quota, including internal retries and key switching; keep Retry-only user behavior and Gemini-only provider selection. Finish the original audit validators on a compatible Linux isolation host, retaining limitations and all dispositions rather than treating unavailable tools as clean results.

## Approval boundary

Approval accepts these remediation behaviors and the booking legacy-access change as one design, then permits implementation/testing/review/PR work in the listed order. It does not authorize live customer mutation, external AI code sharing, paid test calls, unreviewed production credentials/role changes, or claiming full closure without ledger evidence.
