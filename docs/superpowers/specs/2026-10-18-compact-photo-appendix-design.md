# Compact quality-report photo appendix

Approved in chat: two columns, up to four photos per page, retaining each photo's full note and scheduled shift date. Long notes may use extra pages.

Replace the one-photo Page definition in QualityCyclePdfGenerator.AppendEvidence with batches of four evidence cards in a two-column grid. Batch within each company/branch/shift report; cards may span different occurrence dates, so every card carries its own tenant-local date/time and photo number. Images fit proportionally inside a bounded area without cropping. Each photo retains its own full note; a general shift note is printed once, on the first card for that occurrence. Notes-only historical entries remain visible. Missing/invalid images retain the existing localized warning and note.

Each batch starts on a new appendix page and contains at most four photos. Ordinary notes fit a two-by-two page. Long notes flow onto additional pages without truncation, overlapping, or shrinking text. Existing English/Finnish/Swedish labels, brand header/footer and chronological ordering remain. The common generator covers downloaded, shared and legacy reports; no stored evidence or share tokens change.

Verify generated PDF page text and embedded image counts for four and five photos across dates, odd counts, note-only records, invalid images, portrait/landscape images and long notes in all three languages. Render a synthetic sample and inspect the grid and overflow pages.
