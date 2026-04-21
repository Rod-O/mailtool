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
mailtool search [query] [--from X] [--to X] [--subject X] [--since DATE] [--until DATE] [--limit N] [--body]
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
```

## Cache location

Default: `~/.local/share/mailtool/cache`. Override with `MAILTOOL_CACHE` env var.

## Install (if not already installed)

Claude: detect the user's OS and run the correct commands — do not ask them to do it manually.

**macOS Apple Silicon:**
```bash
curl -L https://github.com/Rod-O/mailtool/releases/latest/download/mailtool-osx-arm64 -o mailtool
chmod +x mailtool && sudo mv mailtool /usr/local/bin/mailtool
```

**macOS Intel:**
```bash
curl -L https://github.com/Rod-O/mailtool/releases/latest/download/mailtool-osx-x64 -o mailtool
chmod +x mailtool && sudo mv mailtool /usr/local/bin/mailtool
```

**Linux:**
```bash
curl -L https://github.com/Rod-O/mailtool/releases/latest/download/mailtool-linux-x64 -o mailtool
chmod +x mailtool && sudo mv mailtool /usr/local/bin/mailtool
```

**Windows:**
```powershell
Invoke-WebRequest -Uri https://github.com/Rod-O/mailtool/releases/latest/download/mailtool-win-x64.exe -OutFile mailtool.exe
Move-Item mailtool.exe "$env:USERPROFILE\AppData\Local\Microsoft\WindowsApps\mailtool.exe"
```

## Build from source

```bash
dotnet publish mailtool.csproj -c Release -r osx-arm64 -p:PublishSingleFile=true --self-contained false -o publish
```

## Tests

```bash
dotnet test mailtool.Tests/
```
