# Leads implementation plan

Approved design: ../specs/2026-10-21-leads-management-design.md

1. Add Lead domain/validation and application service, DTOs and repository/file-store contracts. Require expected versions and retain converted provenance.
2. Add tenant-scoped Marten repository with atomic company+branch conversion, duplicate/race handling; private validated disk image storage and admin-only controller. Register dependencies/schema and map actionable errors.
3. Add admin Leads list/detail/edit, email primary selection, private image management and conversion/lifecycle controls. Add navigation and customer deep link. Handle stale responses, failed operations and object URL cleanup.
4. Verify unit validations/lifecycle/conversion, real DB tenancy/transaction/concurrency and API roles, plus desktop/mobile browser workflows. Run frontend build/typecheck and backend checks. Review PR, fix comments and merge/deploy only after required checks.

The writing-plans skill is not installed in the available skill directories. This focused plan follows the approved design directly. No email integration or production customer/lead test data.
