# Manual invoices

On **Admin → Invoices**, select **Manual invoice** in Invoice type. Choose the company, enter its billing address, choose the PDF language, then enter descriptions, quantities and agreed unit prices before VAT. Use Add row/Remove row as needed (1–100 rows). Preview shows net, VAT and total; Issue invoice saves the invoice in the existing history, where PDF, Mark paid and Void work as before.

The supplier address, banking details, default VAT and payment terms come from the existing saved Company and payment details. Unsaved VAT edits do not change the configured rate used for manual rows. Each row's net and VAT amounts are rounded to cents before summing. Quantities allow two decimals; net unit prices allow two decimals and cannot be negative. The final total must be positive.

Manual invoices use the **same yearly invoice number sequence and payment-reference calculation** as shift/company invoices. There is no manual prefix or counter. They have no shift references or billing period and do not block scheduled work billing. Issued snapshots retain their original company, issuer, address, row and tax values after later edits to settings.

Issuing requires a current preview. Changed inputs or saved issuer/company data require another preview. If a network failure leaves issuance uncertain, input stays locked and **Retry issuing invoice** resends the same submission ID. The API returns the original invoice when already saved, including if it was later paid or voided. Reusing an ID with different request data returns a conflict. A confirmed new invoice uses a new ID. No invoice email is sent automatically.

API: admin-only POST `/api/v1/admin/invoices/manual-preview` and `/api/v1/admin/invoices/manual`. Both accept companyId, submissionId, billingAddress, language and lines (description, quantity, unitPriceNet); issue additionally requires previewToken. Server calculations and snapshots are authoritative. Submission lookup and invoice numbering use tenant scope and the existing cross-replica reservation and shared persistence transaction.

Tests: ManualInvoiceServiceTests, ManualInvoiceAuthorizationTests, ManualInvoiceDatabaseTests, ShiftInvoicePdfTests and lifecycle-manual-invoices.spec.ts. Database tests require an isolated PostgreSQL connection in QC_TEST_CONNECTION_STRING; CI supplies one. Production invoices are not used for verification.
