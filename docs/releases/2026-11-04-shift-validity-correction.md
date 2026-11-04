# Shift validity correction on deployment

## What runs

On API start, `ShiftValidityCorrectionService` runs `ShiftValidityCorrectionRunner` for every active tenant.
It corrects stored records that point past a shift's `ValidUntil`:

- **Removed** (no visit, no work evidence): roster assignments on a generated visit beyond the end date, and
  cancellation or move-away records whose original and current times are both beyond the end date.
- **Reported, never removed**: extra visits added beyond the end date, assignments on extra visits, visits with
  work evidence, and visits moved into or out of the window. These are logged for a person to review.

Each tenant gets a `DataCorrectionRecord` marker when applied, so a later deployment skips it.

## Mode

`SHIFT_VALIDITY_CORRECTION_MODE` in the deployment environment (compose default `Report`, so nothing is deleted until Apply is chosen):

- `Report`: logs every planned change and writes nothing. Use this to preview on a deployment first.
- `Apply`: removes the planned records and writes the marker. Shifts with an issued or paid invoice are never removed, only reported.
- `Off`: does nothing. Local runs and test hosts use this when the setting is absent.

Logs use the `Shift validity` message prefix, with one line per finding and a summary per tenant.
