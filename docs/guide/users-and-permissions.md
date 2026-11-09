# Users and permissions

## Users

The admin, made at setup, may do everything. Add other people under **Settings → Users**, giving each only what they
need. Nobody can give a permission they don't have, and only the admin can change the admin's account. Removing
someone signs them out and revokes their API keys.

Anyone can change their own username and password from the account menu at the top right. A new password signs them
out everywhere else.

## What each permission allows

| Permission | Allows |
| --- | --- |
| **Browse** | Seeing the systems, what's owned and missing, what's arriving and what's running |
| **Intake** | Accepting, rejecting and retrying dumps as they arrive, setting what each system prefers, and ignoring titles |
| **Play** | Playing games from the library in the browser |
| **Review** | Looking over unknown files and bad dumps, and keeping or deleting them |
| **Tidy files** | Renaming, moving and converting files in the library |
| **Scan** | Scanning the library, and pausing, cancelling and reordering tasks |
| **Settings and plugins** | Changing settings and plugins, and reading the log |
| **API keys and webhooks** | Making API keys for apps, and managing webhooks |
| **Manage users** | Adding, changing and removing other users |

Someone who just plays needs Browse and Play. Buttons for what someone may not do don't show.

## API keys

Apps use a key from **Settings → API**, sent in the `X-Api-Key` header. A key belongs to whoever made it and can never
do more than they may: if their permissions shrink, or they're removed, so does the key. **Everything you may do**
gives a key all its owner's permissions, now and later. The API reference is at `/api/docs`.

## A lost admin password

Start Romdex once with `Romdex__ResetLogin=true`, open the web app and set a new password, then remove the variable
and restart. Other users, and everyone's keys, are kept.

Next: [Plugins](plugins.md).
