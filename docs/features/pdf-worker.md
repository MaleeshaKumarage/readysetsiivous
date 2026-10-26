# Isolated agreement PDF processing

Agreement upload inspection, signing and generated-document inspection use `PdfWorker:Url` (Compose: `http://pdf-worker:8080/process`). There is no in-process native fallback. Deploy the companion service before enabling the API revision. Development outside Compose needs the same isolated worker; do not point this setting at an internet endpoint.

The companion runs the same release image in `--pdf-worker-host` mode, before normal configuration, authentication and database initialization. A separate `--pdf-worker-task` child inspects/renders each request. Only input PDFs, bounded signature snapshots and processing operation cross the boundary; the worker has no database, identity, email credentials or upload mount. Never expose its port publicly.

Compose enforces a separate 1 GiB memory/swap ceiling, one CPU, 64 PIDs, read-only root, non-root UID, dropped capabilities, no new privileges, 384 MiB temporary filesystem and internal-only network. These controls contain native parser/decompression allocations that page dimensions alone cannot bound. The child environment is cleared. The worker admits one job before reading a body, rejects concurrent requests, kills/reaps a child after 40 seconds and removes the private job directory before releasing admission. `PDF_WORKER_DEADLINE_SECONDS` can lower the deadline to 1–40 seconds for rehearsals.

Input remains 20 MiB, generated output 100 MiB, signatures 2 MiB each and at most 20 signers. The bounded JSON envelope is 190 MiB. Inspection retains the 500-page metadata ceiling. Before rendering any page, every page must fit 144 DPI, 4096 pixels per dimension, 8 million pixels per page and 64 million aggregate pixels. PNG intermediates are spooled with a 64 MiB aggregate cap. Generated documents are reinspected inside the worker. Exceeding a budget fails the attempt rather than silently dropping pages.

The API serializes document worker calls with immediate rejection when occupied (no unbounded waiting queue). Requests have a 50-second network deadline, no redirect/proxy fallback and bounded output. Signed output is written to a new attempt path; existing original/signed files are never worker inputs with write access. The existing signing concurrency/retry logic decides whether to publish the new artifact. Failed attempts clean only their own temporary outputs.

Activation requires the Linux published-container integration job and synthetic signing rehearsal. A green unit suite does not prove container resource containment. Production records must not be used as test fixtures.

Operators may lower raster limits using PDF_RASTER_DPI, PDF_RASTER_MAX_DIMENSION, PDF_RASTER_PAGE_PIXELS, PDF_RASTER_TOTAL_PIXELS and PDF_RASTER_ENCODED_BYTES. Values are clamped to positive values at or below the reviewed ceilings and explicitly allowlisted into the child environment. Raising ceilings requires a reviewed code change.


The release controller starts and verifies the companion before replacing API and
restores the prior companion (or its absence) during rollback. Both services use
the manifest-bound immutable image. `/healthz` includes `status` and `releaseSha`;
`--pdf-worker-check <SHA>` runs only an internal synthetic signing/reinspection
probe before normal application configuration. Activation still requires reviewed
release compatibility and a separate approved policy commit; the default policy
remains disabled.
