# mailtool

A lightweight CLI for Microsoft 365 / Exchange mail — built to work well with AI assistants like Claude.

No MCP server, no plugin, no middleware. Just the Microsoft Graph API called directly from the command line. Mail is synced to a local cache so lookups, searches, and thread reads are instant and token-free. Replies, forwards, and new emails go out through Graph the same way Outlook does — including attachments.

Designed for teams where an AI agent needs to read and send email without burning context on repeated API calls. Each user authenticates independently with their own M365 account via device-code flow. Nothing is shared, nothing is proxied.

## Using with Claude

If you want Claude to help you use mailtool, tell Claude:

> Read https://github.com/Rod-O/mailtool/blob/master/CLAUDE.md and save it to memory so you remember how to use mailtool in future conversations.

Claude will load the full context — commands, rules, and install instructions — and save it to its persistent memory so you never have to explain it again.

## Prerequisites

- A Microsoft 365 account (work or school)

## Install

> **Claude — if you are helping a user install mailtool, do the following automatically:**
> 1. Detect their OS and CPU architecture (`uname -sm` on Mac/Linux, `$env:PROCESSOR_ARCHITECTURE` on Windows).
> 2. Pick the correct binary from the table below.
> 3. Fetch the latest release page at https://github.com/Rod-O/mailtool/releases/latest to get the correct download URL for their platform.
> 4. Download it with `curl` or `Invoke-WebRequest`, make it executable, and move it to PATH.
> 5. Run `mailtool login` to verify.
> Do all of this for the user — do not ask them to run the commands themselves.

### Option A — Download binary (recommended)

Go to **[https://github.com/Rod-O/mailtool/releases](https://github.com/Rod-O/mailtool/releases)**, download the file for your platform, and install it:

| File | Platform |
|------|----------|
| `mailtool-osx-arm64` | Mac — Apple Silicon (M1/M2/M3) |
| `mailtool-osx-x64` | Mac — Intel |
| `mailtool-linux-x64` | Linux |
| `mailtool-win-x64.exe` | Windows |

**macOS / Linux** — after downloading:
```bash
chmod +x mailtool-osx-arm64
sudo mv mailtool-osx-arm64 /usr/local/bin/mailtool
```

**Windows** — move the `.exe` to a folder in your PATH.

### Option B — Build from source

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
git clone https://github.com/Rod-O/mailtool.git
cd mailtool
dotnet publish -c Release -r osx-arm64 -p:PublishSingleFile=true --self-contained true -o publish
sudo cp publish/mailtool /usr/local/bin/mailtool
```

Replace `osx-arm64` with your platform: `osx-x64`, `linux-x64`, or `win-x64`.

## First run

```bash
mailtool login
```

Opens a device-code authentication prompt:

```
To sign in, use a web browser to open https://microsoft.com/devicelogin
and enter the code ABCD1234 to authenticate.
```

Open the URL, enter the code, and sign in with your M365 account. On success, mailtool prints your name and email to confirm. The token is cached locally and reused on subsequent runs — no repeated logins needed.

To sign out: `mailtool signout`

## Commands

### Auth

```bash
# Authenticate (device-code — opens browser prompt)
mailtool login

# Remove cached credentials
mailtool signout
```

### Read

```bash
# Sync new messages (incremental)
mailtool sync [--folder inbox|sent|all]

# Page backward from oldest cached message
mailtool backfill [--folder inbox|sent|all] [--pages N]

# Search
mailtool search [query] [--from addr] [--to addr] [--subject text] [--subject-match <regex>]
                [--in-folder <alias|path|id>] [--since DATE] [--until DATE]
                [--limit N] [--body] [--json]

# Aggregates (top senders/domains, by-month)
mailtool stats [--in-folder <alias|path|id>] [--from addr] [--subject-match <regex>]
               [--since DATE] [--until DATE] [--json]

# Read a message
mailtool show <id> [--raw]

# Read a full thread
mailtool thread <conversation-id | message-id> [--raw]

# Cache stats
mailtool status
```

### Compose

```bash
# New email
mailtool send --to addr [--to addr]... [--cc addr]... \
              --subject "text" [--body "text"] [--attach path]...

# Save as draft (prints draft id)
mailtool draft [--to addr]... [--cc addr]... \
               [--subject "text"] [--body "text"] [--attach path]...

# Reply / reply-all (supports attachments)
mailtool reply     <id> [--body "text"] [--attach path]...
mailtool reply-all <id> [--body "text"] [--attach path]...

# Forward
mailtool forward <id> --to addr [--to addr]... \
                 [--body "text"] [--attach path]...

# Delete (moves to Deleted Items)
mailtool delete <id> [<id>...]
```

**Tip:** `<id>` accepts a prefix — paste the first 20 characters of a message id and mailtool resolves it if unique.

### Organize

```bash
# Move messages by explicit ids
mailtool move <id> [<id>...] --to <folder> [--create] [--dry-run]

# Move messages by selector (no ids required)
mailtool move --to <folder> [--create] [--dry-run] \
              [--from <sender>] [--subject-match <regex>] \
              [--in-folder <src>] [--since DATE] [--until DATE]

# List all mail folders (tree view with ids and counts)
mailtool folders list [--json]

# Create a folder (optionally nested, e.g. "Projects/Maldives")
mailtool folders create <path>

# Delete a folder by alias, display name, path, or raw id
mailtool folders delete <name-or-id>
```

`<folder>` accepts:

- Aliases: `inbox`, `sent`, `drafts`, `trash`, `archive`, `junk`, `outbox`
- Display name: `Archive`, `Important`
- Nested path: `Inbox/xds`, `Projects/Maldives`
- Raw Graph folder id

With `--create`, missing path segments are created on the fly; otherwise `move` errors if the destination doesn't exist.

### Rules

Server-side Exchange/Outlook inbox rules. Unlike `move`, these run on the server for every incoming message even when mailtool is not running. Conditions and actions map to the standard Outlook rules feature.

```bash
# List rules on the inbox (default folder)
mailtool rules list [--json] [--in-folder <folder>]

# Show one rule's full conditions and actions
mailtool rules show <id-or-name> [--in-folder <folder>]

# Create a rule
mailtool rules create --name "Vendor → purchases" \
              [--from <addr>]... [--sent-to <addr>]... \
              [--subject-contains <text>]... [--body-contains <text>]... \
              [--has-attachment] \
              [--to-folder <folder>] [--mark-read] [--delete] \
              [--forward-to <addr>]... [--stop] \
              [--sequence <n>] [--disabled] \
              [--in-folder <folder>]

# Toggle / remove
mailtool rules enable  <id-or-name>
mailtool rules disable <id-or-name>
mailtool rules delete  <id-or-name>
```

At least one condition (`--from`, `--sent-to`, `--subject-contains`, `--body-contains`, or `--has-attachment`) and one action (`--to-folder`, `--mark-read`, `--delete`, `--forward-to`, or `--stop`) are required when creating.

`--stop` sets *Stop processing rules* on the action so later rules don't fire. `--sequence` controls evaluation order (lower runs first). `--disabled` creates the rule disabled; toggle with `enable`/`disable` later.

> Rules require the `MailboxSettings.ReadWrite` Graph scope. If you upgrade an existing install, run `mailtool signout && mailtool login` once so the new scope is consented.

## Environment variables

| Variable | Default | Description |
|----------|---------|-------------|
| `MAILTOOL_CACHE` | `~/.local/share/mailtool/cache` | Cache directory |
| `MAILTOOL_DEBUG` | — | Set to `1` to print stack traces on error |

## Cache layout

```
$MAILTOOL_CACHE/
├── messages/YYYY/MM/<id>.json   # one file per message (full body + metadata)
├── index.json                    # byId + byConversation lookup
└── state.json                    # delta tokens per folder
```

Attachment payloads are not cached — only `hasAttachments: true` is noted.

## Authentication

Uses the [Microsoft Graph PowerShell app](https://learn.microsoft.com/en-us/powershell/microsoftgraph/authentication-commands) (client id `14d82eec-204b-4c2f-b7e8-296a70dab67e`) with delegated device-code flow. No app registration required.

Auth record is stored at `~/.local/share/mailtool/auth-record.json`.

To sign out: `mailtool signout`

## Tests

```bash
dotnet test mailtool.Tests/
```

## Docs

Requires [docfx](https://dotnet.github.io/docfx/):

```bash
dotnet tool install -g docfx
cd docs && docfx docfx.json --serve
```
