# Agreement email copying

The agreement signer actions in both existing-agreement details and the post-create dialog share the same formatted email copy component. **Copy email** writes `text/html` and `text/plain` in one clipboard item: rich-text email editors receive the branded Finnish invitation and signing button; plain-text destinations receive readable wording and the same signing URL. Titles, signer names and URL attributes are escaped. Tokens are URL encoded.

The navy/gold template includes the agreement title, personal greeting, signing explanation, **Allekirjoita sopimus** button, fallback URL and personal-link notice. Clipboard denial or unsupported rich copying displays an error and never claims success. **Copy link** remains separate. **Open mail app** is explicitly plain text because mailto does not reliably support HTML bodies. This action copies content only; it does not send email or change agreement signing permissions/status.

Validation: eight focused Playwright cases across desktop/mobile cover the HTML/plain clipboard payload, button destination, hostile entered content, layout width and clipboard failure feedback. Mobile synthetic email rendered and visually inspected. Production build/type checking also required before release.

