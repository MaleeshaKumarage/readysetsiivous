# Security upgrade implementation

1. Prepare checksum-verified task-local .NET10 SDK and retain the current v8 source/image.
2. Align all backend projects, Docker, CI and ASP.NET packages; pin patched Marten
   plus explicit Newtonsoft serialization/v8 defaults/identity sessions.
3. Fail startup on mapping/schema initialization failure and test that boundary.
4. Audit repository query/tenant inputs; add real PostgreSQL injection/isolation,
   round-trip and concurrency coverage using synthetic isolated databases.
5. Rehearse v8 schema/data to v9 upgrade, repeated apply, previous-image compatibility
   and backup restoration; inspect SQL delta and GUID metadata preservation.
6. Verify a private production backup via isolated restore and old-image startup.
7. Merge passing full checks, deploy the exact API image, verify patched package,
   runtime, health and access behavior; close95 only after production proof.

Keep real customer data private, never use it for adversarial/destructive tests,
and clean only explicitly named rehearsal resources. No product permission changes.
