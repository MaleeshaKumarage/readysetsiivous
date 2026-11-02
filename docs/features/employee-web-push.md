# Employee Web Push

The feature defaults to unavailable. Configure stable VAPID credentials through the protected deployment environment, then enable the reviewed release. Never commit credentials, rotate them on every restart, or print them in CI output.

API configuration keys are PushNotifications:Enabled, VapidSubject, VapidPublicKey and VapidPrivateKey. Compose reads PUSH_NOTIFICATIONS_ENABLED, PUSH_NOTIFICATIONS_VAPID_SUBJECT, PUSH_NOTIFICATIONS_VAPID_PUBLIC_KEY and PUSH_NOTIFICATIONS_VAPID_PRIVATE_KEY. The existing protected API environment file can also provide the corresponding double-underscore configuration keys. A subject is a maintained mailto: contact or HTTPS URL.

Generate a VAPID key pair locally with the maintained web-push tooling (for example `npx web-push generate-vapid-keys` in a private terminal), place both keys directly in the protected environment, and retain them across restarts. Do not run key generation in CI or attach terminal output to issues. Key rotation requires device reenrollment.

Employees and linked administrators opt in individually from My shifts or their account. Administrators can enroll without shift-calendar permission and receive generic new partner-request alerts. Tests report provider acceptance rather than confirmed display. Lock-screen payloads omit names, addresses and work evidence.

Rollout verification requires a physical iPhone Home Screen installation and compatible Android browser: opt in by tap, lock screen receipt, click/login navigation, test acceptance, and disable per device. These checks cannot be inferred from automated tests. Existing partner emails are independent.

Direct branch routing also queues a generic approval alert for the selected opted-in cleaner, linking to Account. It checks the request is still offered to that employee before every attempt. Requests routed directly to a cleaner do not send the administrator new-request alert; fallback routing retains it.
