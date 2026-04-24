# mailtool — Claude Context

**Claude: if you are working in this directory or helping a user with mailtool, read this file first.**

## What is mailtool

A CLI tool that syncs Microsoft 365 / Exchange mail to a local cache and lets you search, read, reply, forward, send, and delete — without hitting the Graph API on every lookup. Binary at `./mailtool` (or wherever the user installed it).

## Rules for Claude

1. **Use mailtool for all mail operations** — search, read threads, reply, forward, send, delete. It is faster and cheaper than any MCP or API call.
2. **For reads, always search locally first.** If the message isn't in cache, run `mailtool sync` then retry.
3. **For replies to existing threads, use `mailtool reply-all`** — never `send`, which creates a new thread and loses history.
4. **For new outbound emails, use `mailtool send`.**
5. **Partial ids are fine** — first 20 chars of a message id is enough if unique.
6. **Run mailtool interactively** — device-code auth has a 120s timeout and will fail as a background task.

## Commands

```bash
# Auth
mailtool login                          # first-time setup — opens browser prompt
mailtool signout                        # clear credentials

# Read
mailtool sync [--folder inbox|sent|all]
mailtool backfill [--folder ...] [--pages N]
mailtool search [query] [--from X] [--to X] [--subject X] [--subject-match <regex>]
                [--in-folder <alias|path|id>] [--since DATE] [--until DATE]
                [--limit N] [--body] [--json]
mailtool stats [--in-folder <alias|path|id>] [--since DATE] [--until DATE]
               [--from X] [--subject-match <regex>] [--json]
mailtool show <id> [--raw]
mailtool thread <conv-id|msg-id> [--raw]
mailtool status

# Compose
mailtool send --to addr [--to addr]... [--cc addr]... --subject "text" [--body "text"] [--attach path]...
mailtool draft [--to addr]... [--cc addr]... [--subject "text"] [--body "text"] [--attach path]...
mailtool reply     <id> [--body "text"] [--attach path]...
mailtool reply-all <id> [--body "text"] [--attach path]...
mailtool forward   <id> --to addr [--to addr]... [--body "text"] [--attach path]...
mailtool delete <id> [<id>...]

# Organize
# by explicit ids:
mailtool move <id> [<id>...] --to <folder> [--create] [--dry-run]
# by selector (no ids needed):
mailtool move --to <folder> [--create] [--dry-run] \
              [--from <sender>] [--subject-match <regex>] \
              [--in-folder <src>] [--since DATE] [--until DATE]
mailtool folders list [--json]
mailtool folders create <path>          # supports nesting: "Projects/Maldives"
mailtool folders delete <name-or-id>
```

### Always dry-run first on selector-based moves

`move` with a selector can sweep hundreds of messages. Run with `--dry-run` first. The output prints the count plus the first 50 subjects so you can spot-check before committing.

### Folder specs

`<folder>` accepts:
- Aliases: `inbox`, `sent`, `drafts`, `trash` (aka `deleted`/`deleteditems`), `archive`, `junk`, `outbox`
- A top-level display name: `Archive`, `Important`
- A nested path: `Inbox/xds`, `Projects/Maldives`
- A raw Graph folder id (as returned by `folders list`)

`--create` makes `move` or a path resolve missing segments on the fly instead of erroring. Safer default is without `--create`, which forces the caller to know the folder exists.

### Triage workflow (AI-driven)

When asked to triage or classify the inbox:

1. `mailtool folders list` — see existing structure, reuse before proposing new ones.
2. `mailtool stats --in-folder inbox` — understand the traffic: top senders, domains, monthly distribution. Drives the classification plan.
3. Propose a classification plan to the user BEFORE executing. Rules like "all messages from X → folder Y" or "subjects matching /maldives|tanfon/ → folder Z".
4. For each rule, run `mailtool move --from … --to … --dry-run` (or `--subject-match …`). Inspect the sample list. Commit only after user sign-off.
5. Execute live with `--create` to auto-create missing destination folders.
6. Vendor/order noise (DigiKey, Amazon, Ramp, PayPal, NoIR, etc.) → `Inbox/purchases`; pure SaaS auto-notifications → `Inbox/notifications`; meeting bots → `Inbox/meetings`.

### Aliases for `--in-folder` and `--to`

Aliases are case-insensitive, resolve to the real Graph id: `inbox`, `sent` (= sentitems), `drafts`, `trash` (= deleteditems), `archive`, `junk` (= junkemail), `outbox`. Display names and nested paths also work: `Inbox/purchases`, `Archive`, `Important`.

## Cache location

Default: `~/.local/share/mailtool/cache`. Override with `MAILTOOL_CACHE` env var.

## Install (if not already installed)

Claude: detect the user's OS/arch, fetch https://github.com/Rod-O/mailtool/releases/latest to find the correct download URL, then download, make executable, and move to PATH — do not ask the user to do it manually.

Releases page: **https://github.com/Rod-O/mailtool/releases**

| File | Platform |
|------|----------|
| `mailtool-osx-arm64` | Mac — Apple Silicon |
| `mailtool-osx-x64` | Mac — Intel |
| `mailtool-linux-x64` | Linux |
| `mailtool-win-x64.exe` | Windows |

**macOS / Linux** (after determining the correct filename from the releases page):
```bash
curl -L <url-from-releases-page> -o mailtool
chmod +x mailtool && sudo mv mailtool /usr/local/bin/mailtool
```

**Windows:**
```powershell
Invoke-WebRequest -Uri <url-from-releases-page> -OutFile mailtool.exe
Move-Item mailtool.exe "$env:USERPROFILE\AppData\Local\Microsoft\WindowsApps\mailtool.exe"
```

## Build from source

```bash
dotnet publish MailTool.csproj -c Release -r osx-arm64 -p:PublishSingleFile=true --self-contained true -o publish
```

## Tests

```bash
dotnet test mailtool.Tests/
```
