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

> **Confirmation gate.** `send`, `reply`, `reply-all`, `forward`, and `calendar create` (with attendees) print a preview and prompt before dispatch. See [Confirmation gate](#confirmation-gate) below for details and the `--yes` bypass.

### send

Sends a new outbound message.

```bash
mailtool send --to addr [--to addr]... [--cc addr]... \
              --subject "text" [--body "text"] [--attach path]... [--yes]
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
mailtool reply     <id> [--body "text"] [--attach path]... [--yes]
mailtool reply-all <id> [--body "text"] [--attach path]... [--yes]
```

Uses a three-step Graph flow: `createReply[All]` → `POST /attachments` → `send`.

---

### forward

Forwards an existing message to new recipients.

```bash
mailtool forward <id> --to addr [--to addr]... [--body "text"] [--attach path]... [--yes]
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

---

## Confirmation gate

`send`, `reply`, `reply-all`, `forward`, and `calendar create` (when attendees are present) **stop the flow before dispatch**. The gate exists to prevent accidental sends, especially when mailtool is invoked from automation, scripts, or AI agents — surfaces where a single typo can ship a message to the wrong recipient with no chance to recover.

### Behaviour

When you run a gated command, mailtool always prints a preview first:

```
─── about to send ───
To:      alice@example.com
Subject: Q1 Report

Hi Alice,

Attaching the Q1 report. Let me know if anything looks off.

Best,
Rod
... (3 more line(s) — choose [R]ead more to view full body)
```

Then one of two things happens, depending on whether stdin is a terminal:

| Stdin                         | What happens                                                                                                                                                                            |
|-------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **TTY** (you're at a terminal)  | Prompts `[Y]es / [N]o / [R]ead more`. `Read more` prints the full body and re-prompts `[Y]es / [N]o`. Sends only on `Y` / `yes` / `sí` / `si`. Anything else cancels.                  |
| **Redirected** (script, agent, CI) | Refuses with exit code 1 and a "stdin is not a terminal" message. The preview is still printed so the operator can see what would have gone out.                                       |

Only `Yes` actually dispatches. Any other input — `N`, empty, an unrecognized character followed by canceling — is treated as a cancel.

### Bypassing the gate (`--yes` / `-y`)

If you've already reviewed the message and want to skip the prompt — typically in a script or one-shot pipeline — pass `--yes` (or `-y`):

```bash
mailtool send --to alice@example.com --subject "Build complete" --body "$(cat report.txt)" --yes
```

`--yes` is per-invocation. There is no global "always yes" setting and no environment variable. Bypassing the gate is treated as its own deliberate decision every time.

### What's not gated

- `draft` creates a draft in your Drafts folder and never dispatches — no confirmation needed.
- `delete` moves messages to Deleted Items, which is recoverable from Outlook.
- All read commands (`sync`, `search`, `show`, `thread`, `stats`, `status`, `folders list`).

### Calendar create

`calendar create` triggers Graph to send invites whenever attendees are listed. The same gate applies, with attendee addresses shown in the preview. Events created without attendees (a personal calendar entry) are not gated — nothing leaves your account.

```bash
# Gated — dispatches invites
mailtool calendar create --subject "Sync" --start "..." --end "..." --attendees alice@example.com

# Not gated — local entry only
mailtool calendar create --subject "Focus block" --start "..." --end "..."
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
