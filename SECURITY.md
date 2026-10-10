# Security

## Reporting a problem
Please report security problems **privately**: on the repository page open the **Security** tab and choose
**Report a vulnerability** (GitHub's private reporting). If that button is not there, open an ordinary issue that says
only "I would like to report a security problem" and no details, and a private channel will be arranged.

Please do not post API keys, server addresses or logs from your own server in public issues.

## What the plugin does about security
- Admin routes need an Emby administrator login; the public routes need the plugin API key with "Enable Public API" ticked.
- The key is compared in constant time, and a client address that fails 5 times in 5 minutes is locked out for 5 minutes.
- The live status push only goes to administrators or to a caller that presents the plugin key.
- A summary of the review and fixes is in `PROGRESS.md` (section 5).

## Known limits
- The API key is stored in plain text in the plugin's configuration file on the server.
- Emby writes request URLs to its own log, so keys should be sent in headers, never in URLs.
- Enforcement relies on the playback reports and the Stop command that Emby clients support.

## Supported versions
Only the latest release receives fixes.
