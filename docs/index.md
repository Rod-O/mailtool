# mailtool

Local Microsoft 365 mail cache CLI. Syncs inbox and sent mail to disk for offline search, reading, and replying — without querying the Graph API on every lookup.

## Quick start

```bash
# First run — authenticates via device code
mailtool sync

# Incremental sync
mailtool sync

# Search
mailtool search project update --from alice --since 2026-01-01 --limit 20

# Read a message
mailtool show <id>

# Read a full thread
mailtool thread <conversation-id>
```

## Compose

```bash
# Reply with attachment
mailtool reply-all <id> --body "See attached." --attach report.pdf

# Forward to new recipients
mailtool forward <id> --to bob@example.com --body "FYI"

# New email (with CC and attachment)
mailtool send --to alice@example.com --cc boss@example.com \
              --subject "Q1 Report" --body "Hi Alice," --attach q1.xlsx

# Save as draft (returns draft id on stdout)
mailtool draft --to alice@example.com --subject "WIP" --body "Draft body"

# Delete
mailtool delete <id>
```

## See also

- [Commands reference](articles/commands.md)
- [Getting started](articles/getting-started.md)
- [API reference](api/MailTool.yml)
