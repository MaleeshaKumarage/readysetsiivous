# Issue93 implementation and verification

Base: latest main `68dedb7`. User requested no QC emails and a login/shift start/end
workflow; independently reviewed design approved including clock/retry edge rules.
Issue65 remains on its pushed branch while this requested correction ships first.

1. Remove QC dispatcher email dependencies and delivery/retry logic; retain task
   generation and snapshot history. Prove no delivery-attempt writes, including
   legacy failed attempts and periodic/manual repeat generation.
2. Link active bound accounts to My Shifts and replace email instructions. Correct
   checkbox labeling/double toggling, keep optional notes and no/subset/all choices.
3. Permit empty owned tasks when no active template exists, preserve existing
   snapshots after deactivation, and require start before authenticated end.
4. Use uncached owned shift reads, explicit load errors and retry; recover already
   persisted start/end state after ambiguous responses without losing selections.
5. Verify backend snapshots, ownership, no-template monthly/PDF evidence, zero-mail
   generation and worker behavior. Run real owned PostgreSQL/Keycloak cleaner
   login/shift flow and all conditional integration checks.
6. Run desktop/mobile checkbox/start/end/failure/empty-task/login cases, retain
   complete admin/report/sharing/booking/game regression and type/production build.
7. Document exact evidence, push PR, resolve review, merge and confirm production
   website/API before closing93. Resume65 against the newly merged main.
