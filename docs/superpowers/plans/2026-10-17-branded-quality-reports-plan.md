# Branded report implementation

Implement the approved 2026-10-17 branded-quality-reports design with the existing QuestPDF renderer.

1. Add a public-safe work timing projection; deduplicate form IDs, sum submitted valid start/end pairs and count incomplete records. Keep scheduled hours separate.
2. Add optional branch address, work records and timing aggregates to monthly report DTOs. Preserve these in new shared snapshots while clearing private Forms. Old absent fields remain unavailable without live lookups.
3. Embed the existing primary logo and use the established navy/gold palette. Apply common header, table cells, section bands and generated/Page X of Y footer to monthly, appendix and legacy summary paths.
4. Render populated, long-table, empty, legacy and photo/note examples. Verify pagination, Finnish text, aspect ratios and safe image handling.
5. Test two-cleaner totals, duplicate IDs, partial/invalid timings, internal/share parity, address fallback and old snapshot compatibility. Run full backend and existing browser report coverage.
6. Publish the PDF PR after occurrence-assignment changes, address review, merge in dependency order and verify deployed image/health.

The referenced writing-plans skill is unavailable in this workspace. This plan follows the previously approved and reviewed written spec directly.
