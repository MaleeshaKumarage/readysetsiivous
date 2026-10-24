# Public booking management keys

New public bookings return `managementKey` once alongside `bookingNumber` and the display-only `customerReference`. The key contains 32 cryptographically random bytes encoded as 64 uppercase hexadecimal characters. Persistence holds only its SHA256 digest in `ManagementKeyHash`. The active confirmation view shows the key and an explicit Copy action with accessible Finnish, English and Swedish success/failure feedback. Reloading or leaving the view loses the key; it is never saved to browser storage, URLs or analytics.

Anonymous lookup now uses `POST /api/v1/public/{slug}/bookings/lookup` with JSON `{ "bookingNumber": "…", "managementKey": "…" }`. Cancellation uses `POST /api/v1/public/{slug}/bookings/{bookingNumber}/cancel` with JSON `{ "managementKey": "…" }`. Both validate canonical encoding before comparing digests in fixed time. The previous phone query-string lookup and cancellation contract is removed. Phone numbers and short references never authorize management. Creation, lookup and cancellation responses use `Cache-Control: no-store`.

Missing, incorrect, malformed, other-booking and legacy credentials return the same unavailable response for each operation. Repository reads retain their request tenant partition. The public response remains limited to booking number, status, local date/time, service name and customer name. Staff and self-work response contracts exclude both the raw key and digest. Credential record string representations omit secrets; endpoint failures never interpolate supplied keys.

Cancellation preserves the existing rules: cancelled/completed bookings reject further cancellation; eligible bookings are marked cancelled with a customer history event and therefore stop occupying availability. There is no newly introduced time cutoff.

## Compatibility and release note

Existing bookings have no digest and can no longer be managed anonymously. Authorized staff retain their existing read and management workflow. No credentials are backfilled or emailed. Losing a new booking's key requires staff assistance. Verified recovery/reissue, OTP/accounts, a complete management screen, idempotency and lost-response recovery remain separate work; this change does not resolve every requirement of issue #79.

## Verification

`BookingManagementKeyTests`, `CreatePublicBookingHandlerTests`, `PublicBookingCredentialHttpTests` and `PublicTenantResolutionTests` cover canonical encoding, hash-only creation, incorrect/missing/oversized credentials, legacy staff access, tenant and booking mismatch, terminal cancellation rules, limited DTOs, removed phone endpoints and no-store HTTP behavior. `booking-management-key.spec.ts` exercises the actual frontend API-result path and confirmation Copy success/failure in all three languages, desktop/mobile, with reload reset and URL/storage/overflow checks. Browser fixtures intercept API requests using synthetic data only.
