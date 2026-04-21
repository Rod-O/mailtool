# Commands Reference

## Read commands

### sync

Pulls new messages via Graph delta API. Only fetches changes since last sync.

```bash
mailtool sync [--folder inbox|sent|all]
```

Default: inbox + sentitems.

---

### backfill

Pages backward from the oldest cached message to fill gaps.

```bash
mailtool backfill [--folder inbox|sent|all] [--pages N]
```

Default: 2 pages × 50 messages.

---

### search

Searches the local cache. All filters are ANDed together.

```bash
mailtool search [query] [--from substr] [--to substr] [--subject substr]
                [--since DATE] [--until DATE] [--limit N] [--body]
```

| Flag | Description |
|------|-------------|
| `query` | Free-text match against subject, body preview, and from address. |
| `--from` | Substring match on sender address or display name. |
| `--to` | Substring match on any recipient address or display name. |
| `--subject` | Substring match on subject. |
| `--since` | ISO date — messages received on or after this date. |
| `--until` | ISO date — messages received on or before this date. |
| `--limit` | Maximum results (default 50). |
| `--body` | Include full body text in the free-text match (slower). |

---

### show

Prints a single message. HTML bodies are converted to plain text by default.

```bash
mailtool show <id> [--raw]
```

`--raw` skips HTML→text conversion and prints the raw body.

**Tip:** `id` can be a prefix — as long as it uniquely identifies one message.

---

### thread

Reconstructs a full conversation in chronological order.

```bash
mailtool thread <conversation-id | message-id> [--raw]
```

Accepts either a conversation id or any message id from the thread.

---

### status

Prints cache statistics and last-sync time per folder.

```bash
mailtool status
```

---

## Compose commands

All compose commands use the Microsoft Graph API directly — they do not write to the local cache.

### send

Sends a new outbound message.

```bash
mailtool send --to addr [--to addr]... [--cc addr]... \
              --subject "text" [--body "text"] [--attach path]...
```

If `--attach` is provided, a draft is created first (Graph `sendMail` does not accept inline attachments).

---

### draft

Creates a draft in the Drafts folder without sending. Prints the full draft id to stdout.

```bash
mailtool draft [--to addr]... [--cc addr]... [--subject "text"] \
               [--body "text"] [--attach path]...
```

---

### reply / reply-all

Replies to an existing message. `reply` goes to sender only; `reply-all` includes all original recipients.

```bash
mailtool reply     <id> [--body "text"] [--attach path]...
mailtool reply-all <id> [--body "text"] [--attach path]...
```

Uses a three-step Graph flow: `createReply[All]` → `POST /attachments` → `send`.

---

### forward

Forwards an existing message to new recipients.

```bash
mailtool forward <id> --to addr [--to addr]... [--body "text"] [--attach path]...
```

---

### delete

Moves one or more messages to Deleted Items.

```bash
mailtool delete <id> [<id>...]
```

Partial id prefixes are accepted. Deletion is not permanent — messages move to Deleted Items and can be recovered from Outlook.

---

## Other commands

### signout

Removes the cached authentication record. The next command will prompt for device-code login.

```bash
mailtool signout
```

### help

```bash
mailtool help
```

---

## Environment variables

| Variable | Effect |
|----------|--------|
| `MAILTOOL_DEBUG=1` | Print full stack traces on error. |
