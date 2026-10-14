# Company tenant integrity implementation

1. Replace the existing Company BusinessId global index definition with the same named per-tenant index. Add company-specific constraint translation to 409. Test legacy migration, idempotence, tenant separation and duplicate create/update using PostgreSQL.
2. Resolve public URL tenants before authenticated private-route tenants; reject foreign JWT realms and unknown/inactive URL tenants. Test signed JWT requests across public endpoint families and verify denied calls do not dispatch.
   Extend the shared active-tenant and provisioner guards to manager endpoints; prove unknown/suspended manager reads/writes cannot reach employee lookups or operations.
3. Validate company existence in the current tenant before branch/agreement creation. Test no durable writes for missing/foreign parents and preserve valid-parent behavior.
   Also validate optional checklist-template company/branch scopes before create/update; preserve nullable scope semantics and verify rejected updates do not mutate tracked templates.
4. Display company conflict details without changing unrelated shared request semantics. Verify failed creation retains input and retry succeeds. Include branch failure feedback where the new parent guard affects the same screen.
5. Update feature docs and Playwright case catalogue with fixed and remaining issue scope. Run backend, frontend and relevant end-to-end checks, review combined diff.
6. Back up production and rehearse the index change on a private restored database before merge/release. Verify exact main deployment and health, close fully resolved issues and document the remaining #81 work.

Parallel ownership: public middleware/tests; parent handlers/tests; company mapping/conflict/PostgreSQL tests. Root owns frontend, documentation, integration, release and production verification. Shared Program.cs conflict mapping is owned by company task only. Agents do not commit, push or deploy.
