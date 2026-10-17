# Branded quality reports

The API embeds a byte-identical copy of `site/public/brand/readyset-primary-logo.png` under its own build context; a regression checks the approved asset matches. Container rendering requires no network or website checkout.

Monthly shift and branch PDFs use the approved primary logo, navy/gold section bands and bordered property, activity and visit tables. Branch addresses come from existing branch data, with Not recorded when absent. Each shift remains a separate section. The dated per-photo note appendix and standalone shift notes are preserved; unavailable historical images show a clear placeholder. Headers repeat, table headings repeat on continuation pages, and all pages include generated UTC time and Page X of Y.

Scheduled hours describe visit duration. Recorded staff hours sum valid actual submitted durations per distinct form ID: two cleaners recording two hours each contribute four staff hours. Missing or reversed start/end pairs are excluded and counted as incomplete. No valid pairs displays Not available. Actual work rows retain scheduled and actual timestamps separately in the tenant timezone. These are activity measurements, without new ratings or invoice fields.

New report snapshots include public-safe work timing rows and totals alongside existing approved evidence. Work rows contain no employee identity, form ID or QC token. Sharing still removes private Forms and retains the 30-day expiry/revocation behavior. Earlier snapshots render their captured content with new styling; absent address/timing fields remain unavailable and are never rebuilt from current data.

Validation includes deduplication, two-cleaner totals, partial and invalid timings, public/internal aggregate parity, Finnish text, long tables, legacy/empty snapshots, safe image decoding and portrait/landscape photo appendices. PDF creation, downloads and sharing retain the existing tenant-admin authorization.
