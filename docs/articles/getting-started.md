# Getting Started

## Prerequisites

- .NET 10 runtime
- A Microsoft 365 account (`rod@coralvita.co`)

## Installation

The binary is at `/Volumes/Samsung990/coralvita/tools/mailtool/mailtool`.

To rebuild after source changes:

```bash
cd /Volumes/Samsung990/coralvita/tools/mailtool
dotnet publish -c Release -r osx-x64 -p:PublishSingleFile=true --self-contained false -o publish
cp publish/mailtool mailtool
```

## Authentication

mailtool uses the Microsoft Graph PowerShell app (client id `14d82eec-204b-4c2f-b7e8-296a70dab67e`) with device-code flow.
The token is cached at `~/.local/share/mailtool/auth-record.json`.

First run triggers authentication automatically:

```bash
mailtool sync
# → To sign in, use a web browser to open https://microsoft.com/devicelogin
#   and enter the code XXXXXX to authenticate.
```

Subsequent runs reuse the cached token silently.

To revoke: `mailtool signout`.

## Cache structure

```
/Volumes/Samsung990/coralvita/mail-cache/
├── messages/YYYY/MM/<sanitized-id>.json   # one file per message
├── index.json                              # byId + byConversation lookup
└── state.json                              # delta tokens per folder
```

Messages include full body, metadata, and recipient lists. Attachment payloads are not cached — only `hasAttachments: true` is noted.

## Running tests

```bash
cd /Volumes/Samsung990/coralvita/tools/mailtool
dotnet test mailtool.Tests/
```

## Generating docs

Requires [docfx](https://dotnet.github.io/docfx/) (`dotnet tool install -g docfx`):

```bash
cd /Volumes/Samsung990/coralvita/tools/mailtool/docs
docfx docfx.json --serve
```

Site is generated at `docs/_site/`. `--serve` starts a local preview at `http://localhost:8080`.
