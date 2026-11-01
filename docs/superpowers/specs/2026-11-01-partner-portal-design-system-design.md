# Partner portal design system and FI/EN

Approved in chat: apply the existing shared navy/gold design system to the partner
portal, with Finnish and English available only there. Retain the portal's current
company-scoped workflows and previously fixed request-form focus/retry behaviour.

Use shared Button, Card, Input, Textarea and Badge components and existing brand
assets/tokens. A compact branded header contains account, sign out and FI/EN
controls. Four accessible sections retain Properties as the default. Property and
request cards use two columns on desktop and one on mobile. Keep red quote tags,
clear primary cleaning actions, labelled fields, visible focus and readable status
badges. Portal-scoped light styling must work even under a saved global dark theme.
Do not alter shared component defaults or the appearance of other portals.

Translate navigation, actions, labels, hints, empty/loading/success states, status
enums, confirmation prompts and validation/errors. Keep entered property/company/
person names, instructions and staff notes unchanged. Localize date/time and price
formatting. Remember selection under a partner-only localStorage key; otherwise
use Finnish for a Finnish browser and English for other browser languages.
Changing language must retain open forms and unsaved data. Set lang on the portal
region, not globally. Unknown server errors get a localized useful fallback;
known validation reasons keep their meaning through a checked translation map.

Verify existing English workflows plus Finnish create/edit/cancel/change flows,
translated validation/errors and language persistence on desktop/mobile. Verify
keyboard controls, 320px containment, saved-dark-theme isolation and actual logo
loading. Render desktop/mobile screenshots; run production build/type checks.
