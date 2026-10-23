# Admin appearance

The admin dashboard uses an explicit light Mantine theme in browsers and the installed PWA, regardless of the device color scheme or the public website's saved theme. This includes login, access checks and portaled dialogs. The public website keeps its existing theme preference.

The root body has public dark-mode utilities. The admin-only body override takes precedence while the admin layout is mounted and is removed when leaving admin. Do not reintroduce an unconditional root `dark` class in the admin layout.

Regression coverage: `site/e2e/lifecycle-admin-theme.spec.ts` checks desktop/mobile, light/dark devices, standalone display mode, dialogs, login and denied access, with computed text contrast of at least 4.5:1.
