# mailtool

A lightweight CLI for Microsoft 365 / Exchange mail — built to work well with AI assistants like Claude.

No MCP server, no plugin, no middleware. Just the Microsoft Graph API called directly from the command line. Mail is synced to a local cache so lookups, searches, and thread reads are instant and token-free. Replies, forwards, and new emails go out through Graph the same way Outlook does — including attachments.

Designed for teams where an AI agent needs to read and send email without burning context on repeated API calls. Each user authenticates independently with their own M365 account via device-code flow. Nothing is shared, nothing is proxied.

## Prerequisites

- .NET 10 runtime (see below)
- A Microsoft 365 account (work or school)

### Installing .NET 10

> **Claude — if you are helping a user install .NET 10, detect their OS and run the appropriate commands below. Do not ask them to do it manually.**

**macOS (Homebrew):**
```bash
brew install --cask dotnet-sdk
```

**macOS (without Homebrew):**
```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0
echo 'export PATH="$HOME/.dotnet:$PATH"' >> ~/.zshrc && source ~/.zshrc
```

**Linux (Ubuntu/Debian):**
```bash
sudo apt-get update && sudo apt-get install -y dotnet-runtime-10.0
```

**Linux (other distros):**
```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0 --runtime dotnet
echo 'export PATH="$HOME/.dotnet:$PATH"' >> ~/.bashrc && source ~/.bashrc
```

**Windows:**
```powershell
winget install Microsoft.DotNet.Runtime.10
```

Verify the install:
```bash
dotnet --version   # should print 10.x.x
```

## Install

> **Claude — if you are helping a user install mailtool, do the following automatically:**
> 1. Detect their OS and CPU architecture (`uname -sm` on Mac/Linux, `$env:PROCESSOR_ARCHITECTURE` on Windows).
> 2. Pick the correct binary from the table below.
> 3. Download it with `curl` or `Invoke-WebRequest`.
> 4. Make it executable and move it to PATH.
> 5. Run `mailtool login` to verify.
> Do all of this for the user — do not ask them to run the commands themselves.

### Option A — Download binary (recommended)

| File | Platform |
|------|----------|
| `mailtool-osx-arm64` | Mac — Apple Silicon (M1/M2/M3) |
| `mailtool-osx-x64` | Mac — Intel |
| `mailtool-linux-x64` | Linux |
| `mailtool-win-x64.exe` | Windows |

**macOS (Apple Silicon):**
```bash
curl -L https://github.com/coralvita/mailtool/releases/latest/download/mailtool-osx-arm64 -o mailtool
chmod +x mailtool && sudo mv mailtool /usr/local/bin/mailtool
```

**macOS (Intel):**
```bash
curl -L https://github.com/coralvita/mailtool/releases/latest/download/mailtool-osx-x64 -o mailtool
chmod +x mailtool && sudo mv mailtool /usr/local/bin/mailtool
```

**Linux:**
```bash
curl -L https://github.com/coralvita/mailtool/releases/latest/download/mailtool-linux-x64 -o mailtool
chmod +x mailtool && sudo mv mailtool /usr/local/bin/mailtool
```

**Windows (PowerShell):**
```powershell
Invoke-WebRequest -Uri https://github.com/coralvita/mailtool/releases/latest/download/mailtool-win-x64.exe -OutFile mailtool.exe
Move-Item mailtool.exe "$env:USERPROFILE\AppData\Local\Microsoft\WindowsApps\mailtool.exe"
```

### Option B — Build from source

Requires .NET 10 SDK (see [Installing .NET 10](#installing-net-10) above).

```bash
git clone https://github.com/coralvita/mailtool.git
cd mailtool
dotnet publish -c Release -r osx-arm64 -p:PublishSingleFile=true --self-contained false -o publish
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
mailtool search [query] [--from addr] [--to addr] [--subject text]
                [--since DATE] [--until DATE] [--limit N] [--body]

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
