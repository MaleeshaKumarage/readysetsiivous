# Public booking management credentials

Status: proposed; implementation awaits design approval.
Date: 2026-10-24
Baseline: main a57c6bdc2c1e0361d8bf7dcb2430e50f303382c5
Related: issue #79; security audit run-1 lead 2 (source-grounded, runtime demonstration pending).

## Problem and scope

PublicBookingsController currently accepts a sequential booking number and phone for lookup and cancellation. A phone number is contact information rather than strong proof of booking ownership. The existing CustomerReference is only six characters and is not checked by these endpoints. There is no customer Manage booking page in the current frontend.

This increment hardens the existing API and preserves a customer's management credential on the existing booking confirmation screen. It does not claim to complete all of #79: durable request idempotency, lost-response recovery and a complete customer management screen remain separate work.

## Options

1. Recommended: a random per-booking management key, returned at creation and supplied with the booking number. Smallest change; requires customers to keep the key.
2. Email or SMS OTP: recoverable proof, but needs delivery, expiry, throttling and recovery handling; email is currently optional and SMS infrastructure is not present in this flow.
3. Customer accounts: stronger recovery and history, but substantially expands the anonymous booking journey.

## Recommended behavior

Generate a distinct management key from 32 cryptographically random bytes. Keep the short customer reference for display only. Persist only the management key's SHA256 hash; return the raw key once in the successful booking result. Avoid putting the key in URLs, query strings, logs, analytics or browser persistent storage.

The existing confirmation view clearly labels the management key, explains that it permits viewing/cancelling this booking, and offers an explicit Copy action with accessible success/failure feedback. Provide Finnish, English and Swedish system labels. The key is shown only for the active booking confirmation, without writing it to localStorage or shared caches. This increment adds no email delivery or public management page.

Use POST /api/v1/public/{slug}/bookings/lookup with booking number and key in the JSON body. Cancellation keeps POST /api/v1/public/{slug}/bookings/{bookingNumber}/cancel but requires a key in its JSON body. Remove the phone-query lookup/cancellation contract completely; do not preserve an insecure fallback. Mark credential responses and lookup responses no-store. Do not accept the existing short reference as a credential.

Centralize key validation for lookup and cancellation, with strict length/encoding limits and fixed-time digest comparison. Preserve tenant-scoped repository reads, the limited public DTO, existing rate limiting and existing cancellation-state/time rules. Missing, wrong, other-booking, other-tenant and legacy credentials must not disclose whether a booking exists.

Do not rewrite existing bookings or send credentials to customers automatically. Legacy bookings without a management-key hash have no anonymous management access; staff retain existing authorized management. Recovery/reissue is deferred until a verified delivery or authenticated recovery design is agreed. This compatibility change must be explicit in release notes.

## Verification

Use synthetic records only. First demonstrate current number+phone behavior in an isolated fixture, then prove it no longer authorizes access.

- Correct key can read its tenant's limited public DTO and cancel only eligible work.
- Phone alone and short customer reference never authorize either operation.
- Wrong/missing/malformed/oversized keys and wrong booking/tenant return safe failures without mutation.
- Legacy records remain readable/managable through authorized staff APIs and reject anonymous management.
- New persistence holds a hash only; raw key appears only in the creation result and active confirmation UI.
- Terminal-state and existing cancellation-window rules remain enforced; cancellation frees the slot only under current domain rules.
- Copy success/failure, translated labels and confirmation reload/reset behavior have browser coverage.
- Logs/error responses do not echo the key; URLs contain no key; no-store headers are asserted.

No production booking cancellation, customer-data changes, live email delivery or paid provider calls are needed for this work.

## Approval boundary

Approval accepts the management-key contract and the legacy-booking compatibility change above. It does not authorize changing cancellation windows, adding customer accounts/OTP, or claiming all of #79 is resolved.
