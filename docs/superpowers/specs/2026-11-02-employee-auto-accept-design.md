# A01 employee-owned automatic acceptance

Approved scope: the employee chooses Manual (default false, including historical documents) or automatic acceptance of future partner offers. Only the authenticated active, linked, synchronized employee/manager/admin can read or change their preference; partner and unbound administration have no self preference. General employee administration cannot change consent.

GET/PUT `/api/v1/me/work-preferences` returns `{autoAcceptWork:boolean}`. PUT requires exactly that boolean and takes no employee ID. Admin and manager employee response allowlists expose this operational metadata without broadening HR access.

Preference writes and Offer acquire the existing tenant `IShiftWorkReservation` and reload employee state after acquiring it. Enabling fails with a clear 409 while any pending Offered request belongs to the employee; disabling changes only future offers. This serializes enabling against pending-offer creation across replicas.

Offer validates current role/synchronization, company/property, request version and unstarted/unbilled guards. Automatic acceptance requires an explicit admin-planned start or a fixed exact appointment already confirmed by staff through Offer. A flexible date window supplies no implicit start. Manual offers retain optional planned time.

A private lock-held materialization routine is shared by manual Accept and automatic Offer. It preserves appointment/window/DST checks, overlap safety, deterministic shift/occurrence identity and assignment/billing snapshots, and stores all documents and notification intents in one commit. Offer never calls public Accept or commits a pending offer first. Manual lost-response retries retain their existing contract; stale Offer versions fail 409.

Automatic offers omit OfferCreated/AwaitingAcceptance and emit existing company/staff RequestAccepted plus employee WorkAutoAccepted information. Employee intent is delivered only for the current Accepted employee and revision; it carries no approval prompt. Partner shift mutations retain their existing guard and lock.

Verification covers DTO/JSON boundaries, consent defaults and ownership, pending/preference races, atomic overlap failure, deterministic materialization, exact timing, stale versions, notifications and manual compatibility. Real PostgreSQL tests use only the isolated `QC_TEST_CONNECTION_STRING` harness (synthetic CI database); skipped local database tests are reported explicitly. Frontend implementation and E2E are owned separately.
