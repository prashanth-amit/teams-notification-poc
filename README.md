# Teams Alert POC

Ways to send alert messages to Microsoft Teams from code. This POC contains a .NET console app that sends test alerts through a Power Automate (Workflows) webhook.

| Method | Status |
|---|---|
| **Method 1: Workflows (Power Automate) webhook** | Users path: implemented and tested. Channel path: built in the flow but **not tested** (no test team available). See section 12. |

> **Draft for review.** This is a proof of concept. Anything that touches production alert systems needs testing and your normal change-control and IT review before release.

---

## Method 1: Workflows

### 1. Overview

Your code sends an HTTP POST with JSON to a private URL. A Power Automate flow receives it and posts an Adaptive Card from **Flow bot**, either to individual users (private chats) or to one Team channel. A field named `target` selects which.

```
Alert system / console app
        |  HTTPS POST  { "target": ..., "card": { ... }, ... }
        v
Power Automate flow (HTTP trigger)
        v
Condition: target equals "channel"?
   |- True  -> Post card to a Team channel          (one shared post)       [not yet tested]
   '- False -> Apply to each email in userEmails
                  -> Post card in a chat (Flow bot -> user)               (private chats) [tested]
```

**Payload: to individual users** (also the default when `target` is missing)

```json
{
  "target": "users",
  "userEmails": ["a@company.com", "b@company.com"],
  "card": { "type": "AdaptiveCard", "version": "1.4", "body": [ ... ] }
}
```

**Payload: to a channel** (not yet tested)

```json
{
  "target": "channel",
  "teamId": "<team id>",
  "channelId": "<channel id>",
  "card": { "type": "AdaptiveCard", "version": "1.4", "body": [ ... ] }
}
```

- `target`: `users` or `channel`. If missing, the flow treats it as `users`, so older callers that send only `userEmails` and `card` keep working.
- `userEmails`: JSON array of receivers (users path). The flow loops over it.
- `teamId`, `channelId`: where to post (channel path). Chosen by the caller, not hardcoded in the flow.
- `card`: the full Adaptive Card. Because the code builds the card, alert layouts can change without editing the flow.

### 2. Characteristics

| | |
|---|---|
| Needs Azure subscription | No |
| Cost | Included with most Microsoft 365 licences (confirm with IT) |
| Network | Outbound HTTPS only, no inbound port needed |
| Sender | Fixed: Flow bot (messages are posted through the flow owner's Teams connection) |
| Receivers | Configurable per call (users list, or a team and channel) |
| Content | Fully dynamic (any Adaptive Card) |
| Delivery: users | One private 1:1 chat per receiver (not a group chat) |
| Delivery: channel | One post that every channel member can see |

**Advantages:** no Azure or app registration, quick to set up, dynamic receivers and content, works from a plant network that cannot accept inbound connections, one URL for both users and channels.

**Disadvantages and limits:**

- The sender is always Flow bot, and it cannot be chosen per alert.
- The flow depends on the connection of the account that owns it. If that account leaves, changes password or loses its licence, the flow can stop working.
- Each user receiver costs one flow action. Power Automate request limits and Teams throttling apply. The users path suits tens of receivers, not thousands. Check Microsoft's current limits and ask IT. For large audiences, post once to a channel instead, or use an Azure bot with a queue.
- Messages to a channel are not private. Everyone in the channel sees them.
- The webhook URL is a secret. Anyone who has it can post to the flow, and with the dynamic channel option, to any channel the flow owner can reach (see section 10).
- There is no built-in delivery receipt for the end user. Success means the flow accepted the request.
- **Group chats are not supported.** A group chat (shown under Chat in Teams, with tabs such as Chat, Shared, Notes) is not a team and has no channel. The flow has no branch for it. See section 12.

### 3. Prerequisites

- A Microsoft 365 work account that can create flows in Power Automate (https://make.powerautomate.com).
- Permission to set the trigger **Who can trigger the flow** to **Anyone**. If this is blocked, ask IT.
- .NET SDK 10 (the console app targets `net10.0`).
- Users path: receivers must be in the same tenant and have Teams.
- Channel path: a Team and channel that the flow owner's account is a member of. Use a small test team for testing. Do not use a company-wide team (for example one with thousands of members) for tests.

### 4. Create the flow

1. Open https://make.powerautomate.com and sign in with your work account.
2. Select **Create**, then **Instant cloud flow**.
3. Name it `Teams Alert Webhook`. Choose the trigger **When a Teams webhook request is received**, then **Create**.
4. In the trigger, set **Who can trigger the flow** to **Anyone**.
5. Select **+**, **Add an action**, and search **Condition** (Control). Set the single row to:
   - **Left:** Expression (fx) tab, enter `coalesce(triggerBody()?['target'], 'users')`, select **Add**
   - **Operator:** is equal to
   - **Right:** type `channel`
6. **False branch (users):**
   1. Select **+** in **False**, **Add an action**, and search **Apply to each** (Control).
   2. In **Select an output from previous steps**, use the Expression tab, enter `triggerBody()?['userEmails']`, select **Add**.
   3. Inside the loop, select **+**, **Add an action**, and search **Post card in a chat or channel** (Microsoft Teams). Set:
      - **Post as:** Flow bot
      - **Post in:** Chat with Flow bot
      - **Recipient:** Expression tab, enter `items('Apply_to_each')` (use the loop's actual name, see notes), select **Add**
      - **Adaptive Card:** Expression tab, enter `string(triggerBody()?['card'])`, select **Add**
      - If asked to sign in to Teams, sign in with your work account.
7. **True branch (channel):**
   1. Select **+** in **True**, **Add an action**, and search **Post card in a chat or channel**. Set:
      - **Post as:** Flow bot
      - **Post in:** Channel
      - **Team:** Expression tab, enter `triggerBody()?['teamId']`, select **Add**
      - **Channel:** Expression tab, enter `triggerBody()?['channelId']`, select **Add**
      - **Adaptive Card:** Expression tab, enter `string(triggerBody()?['card'])`, select **Add**
8. Select **Save** and wait for the saved confirmation.
9. Open the trigger again. The **HTTP URL** field is now filled. Copy it. This is the flow URL (secret).

**Notes (designer behaviour seen while building this flow)**

- **Save often and before reloading.** Unsaved edits, including a newly added action, are lost when the page is reloaded.
- **Moving or pasting an action renames it.** Moving the loop into the False branch renamed it `Apply to each 1`. The loop's Recipient expression then pointed at a name that no longer existed and showed "Invalid parameters". After any move, re-enter the Recipient as `items('<loop name with spaces as underscores>')`, for example `items('Apply_to_each_1')`. Easiest is to build the loop directly inside the False branch instead of moving it.
- **Spare blank row in the Condition.** The designer keeps adding an empty row after you fill one. Leave the blank row alone. If you filled the second row, make it identical to the first (two identical tests with And behave like one). Check the Condition's Code view and run both payloads to confirm it behaves correctly.
- The Recipient field accepts one user per post. A joined list such as `a@x.com;b@x.com` fails with "No user details with email or UPN ... were found in Graph". That is why the loop is required.
- The trigger in this designer has no sample-payload box, so no schema is needed. Fields are read with expressions.
- Simpler Condition: you can use `triggerBody()?['target']` on the left instead of the `coalesce(...)`. A missing target is empty, which is not equal to `channel`, so it still goes to the users branch.

#### 4.1 How to enter an expression

Use this method for every expression field below. Typing an expression as plain text will not work.

1. Click the field (for example **Recipient**). A small popup appears, and a lightning and **fx** button shows at the right edge of the field.
2. Click **fx** to open the **Expression** tab.
3. Paste the expression, with no leading `@`.
4. Click **Add**. The field now shows a purple token such as `items(...)`, `string(...)` or `triggerBody(...)`.

To check or edit one later, click the token. To remove it, click the **x** on the token. Tokens for different fields can look alike (for example two `triggerBody()` tokens), so click each to confirm which expression it holds.

#### 4.2 Expressions reference

| Where | Field | Expression | What it does |
|---|---|---|---|
| **Condition** | Left value | `coalesce(triggerBody()?['target'], 'users')` | Returns `target` from the request, or `users` if it is missing. The condition is true only when it equals `channel`. |
| False: **Apply to each** | Select an output from previous steps | `triggerBody()?['userEmails']` | Reads the `userEmails` array from the request body. The loop runs once per email. |
| False: **Post card** (inside the loop) | Recipient | `items('Apply_to_each')` | Current email in the loop. One post per receiver. Use the loop's real name. |
| False: **Post card** (inside the loop) | Adaptive Card | `string(triggerBody()?['card'])` | Takes the `card` object from the request and converts it to the text Teams expects. |
| True: **Post card** (channel) | Team | `triggerBody()?['teamId']` | Team ID sent by the caller. |
| True: **Post card** (channel) | Channel | `triggerBody()?['channelId']` | Channel ID sent by the caller. |
| True: **Post card** (channel) | Adaptive Card | `string(triggerBody()?['card'])` | Same as above. |

Fixed values (not expressions): users branch **Post as** = Flow bot, **Post in** = Chat with Flow bot. Channel branch **Post as** = Flow bot, **Post in** = Channel.

Name check: the loop name in `items('...')` must match the loop's actual name, with spaces shown as underscores. A loop named `Apply to each 1` needs `items('Apply_to_each_1')`.

Expressions that do **not** work for this setup:

| Expression | Why not |
|---|---|
| `join(triggerBody()?['userEmails'], ';')` as Recipient | The field takes one user. A joined list fails with "No user details with email or UPN ... were found in Graph". |
| `triggerBody()?['userEmail']` as Recipient | Only works for the old single-receiver payload. The current app sends `userEmails`. |
| `@{triggerBody()?['title']}` inside a hand-typed card | Not used now. The code sends the finished card, so the flow does not read `title` or `message`. |

Optional expressions (not required, untested here, use only if you need them):

| Purpose | Expression |
|---|---|
| Read an extra field you add to the payload, such as `alertType` | `triggerBody()?['alertType']` |
| Count receivers (for a guard condition) | `length(triggerBody()?['userEmails'])` |
| Fall back to a default when a field is missing | `coalesce(triggerBody()?['alertType'], 'general')` |

#### 4.3 Final flow structure

When finished, the flow should look like this:

```
When a Teams webhook request is received          (Who can trigger: Anyone)
 └─ Condition: coalesce(triggerBody()?['target'], 'users') is equal to channel
      ├─ True
      │    └─ Post card in a chat or channel
      │         Post as:        Flow bot
      │         Post in:        Channel
      │         Team:           triggerBody()?['teamId']
      │         Channel:        triggerBody()?['channelId']
      │         Adaptive Card:  string(triggerBody()?['card'])
      └─ False
           └─ Apply to each                          <- triggerBody()?['userEmails']
                └─ Post card in a chat or channel
                     Post as:        Flow bot
                     Post in:        Chat with Flow bot
                     Recipient:      items('Apply_to_each_1')
                     Adaptive Card:  string(triggerBody()?['card'])
```

To verify, open the flow, select **Code view** on each step, and compare with the values above.

#### 4.4 Test the flow without the console app

After saving, send a minimal request from PowerShell. Replace the placeholders, and do not paste the real URL into chat or code files.

**Users path**

```powershell
$url = "<paste the flow URL here>"
$body = @{
  target     = "users"
  userEmails = @("your.name@company.com")
  card = @{
    type    = "AdaptiveCard"
    version = "1.4"
    body    = @(@{ type = "TextBlock"; text = "Test alert from PowerShell"; wrap = $true })
  }
} | ConvertTo-Json -Depth 10
Invoke-RestMethod -Uri $url -Method Post -Body $body -ContentType "application/json"
```

A successful call returns without error, and the flow's **Run history** shows a green run through the **False** branch. Teams then shows the card from Flow bot.

**Channel path (not yet tested). Use a small test team only.**

1. In Teams, right-click the channel (or select its **...**) and choose **Get link to channel**. The link looks like `https://teams.microsoft.com/l/channel/19%3A...%40thread.tacv2/Name?groupId=<teamId>&tenantId=...`.
2. Run:

```powershell
Add-Type -AssemblyName System.Web
$flowUrl = "<paste the flow URL here>"
$link    = "<paste the channel link here>"

$uri       = [uri]$link
$channelId = [uri]::UnescapeDataString(($uri.AbsolutePath -split '/')[3])
$teamId    = [System.Web.HttpUtility]::ParseQueryString($uri.Query)["groupId"]
"teamId    = $teamId"
"channelId = $channelId"

$body = @{
  target    = "channel"
  teamId    = $teamId
  channelId = $channelId
  card      = @{
    type    = "AdaptiveCard"
    version = "1.4"
    body    = @(@{ type = "TextBlock"; text = "Channel test alert"; weight = "Bolder"; wrap = $true })
  }
} | ConvertTo-Json -Depth 10

Invoke-RestMethod -Uri $flowUrl -Method Post -Body $body -ContentType "application/json"
```

`teamId` should be a GUID, and `channelId` should look like `19:...@thread.tacv2`. In **Run history** the Condition should go through **True**.

If a run fails, open it and read the error on the failing step:

| Failing step | Check |
|---|---|
| **Condition** | The `target` value, and that the Condition has the expression described in 4.2 |
| **Apply to each** | The input expression is exactly `triggerBody()?['userEmails']`, and the request contains `userEmails` as an array |
| **Post card** (users) | The recipient email exists in your tenant, the Recipient references the loop's real name, and `card` is a valid Adaptive Card object |
| **Post card** (channel) | `teamId` and `channelId` are correct, the flow owner is a member of that team and channel, and `card` is valid |

### 5. Configure the console app

The app is in this folder. Settings load in this order, and later sources win:

1. `appsettings.json` (defaults, keep the URL blank here)
2. `appsettings.local.json` (optional, git-ignored, for the secret URL)
3. Environment variables

**Option A: local file (recommended for development)**

Create `appsettings.local.json` next to `appsettings.json`:

```json
{
  "TeamsAlert": {
    "FlowUrl": "<paste the flow URL here>"
  }
}
```

**Option B: environment variable (recommended for servers and services)**

```powershell
$env:TeamsAlert__FlowUrl = "<paste the flow URL here>"
```

Use a double underscore. To keep it permanently for your user:

```powershell
[Environment]::SetEnvironmentVariable("TeamsAlert__FlowUrl", "<paste the flow URL here>", "User")
```

Other settings (in `appsettings.json`):

| Key | Default | Meaning |
|---|---|---|
| `TeamsAlert:FlowUrl` | empty | Flow HTTP URL (secret) |
| `TeamsAlert:TimeoutSeconds` | 15 | HTTP timeout per attempt |
| `TeamsAlert:MaxAttempts` | 3 | Attempts per alert (retries on network errors, timeouts, throttling) |
| `TeamsAlert:Channels:<name>:TeamId` | none | Team ID (a GUID) for a named channel |
| `TeamsAlert:Channels:<name>:ChannelId` | none | Channel ID (like `19:...@thread.tacv2`) for a named channel |

**Named channels.** Define each channel once under a name, then send with `dotnet run -- channel <name>`. Put them in `appsettings.local.json`. `appsettings.local.example.json` shows the layout:

```json
{
  "TeamsAlert": {
    "Channels": {
      "test": {
        "TeamId": "<groupId value from the channel link>",
        "ChannelId": "<channel id decoded from the channel link>"
      }
    }
  }
}
```

Get both values from **Get link to channel** in Teams (see 4.4). The app refuses to send if `TeamId` is not a GUID or `ChannelId` does not look like `19:...@thread...`, so placeholder values are caught before anything is posted.

> Do not commit the flow URL. `appsettings.local.json`, `bin/` and `obj/` are in `.gitignore`. Keep `FlowUrl` empty in `appsettings.json`.

### 6. Run the test app

```powershell
cd D:\Service_Project\POC\TeamsAlert

# To individual users (separate emails with a comma, semicolon or space)
dotnet run -- a@company.com,b@company.com

# To a named channel from TeamsAlert:Channels (not yet tested against a real channel)
dotnet run -- channel test
```

Or run `dotnet run` and answer the prompts (users or channel, then emails or channel name). For users, duplicates and invalid entries are removed, and invalid ones are reported.

The app sends the payloads from section 1: `target` `users` with `userEmails`, or `target` `channel` with `teamId` and `channelId`. It sends three hardcoded alerts to the chosen destination, one second apart:

| # | Alert | Structure |
|---|---|---|
| 1 | Critical | Red header, time, short message |
| 2 | Warning | Yellow header, message, facts table (machine, parameter, value, limit, shift) |
| 3 | Info | Blue header, message, image, "Open dashboard" button |

Alert 3 uses an image URL and `https://example.com/dashboard` as placeholders. Replace both with your own in `BuildImageAlert()` in `Program.cs`.

In channel mode all three alerts are visible to every member of the channel, so use a small test team for channel runs.

**Output and exit codes**

- Prints the HTTP status per alert. `202 Accepted` means the flow accepted the request.
- Exit code `0`: all alerts accepted. `1`: configuration or input problem. `2`: one or more sends failed.

### 7. Use it in your alert system

1. Build the Adaptive Card JSON in your code (see `BuildSimpleAlert`, `BuildFactsAlert`, `BuildImageAlert` in `Program.cs` as templates). Design cards at https://adaptivecards.io/designer, with Teams as the host app.
2. POST one of the payloads from section 1 to the flow URL as `application/json`.
3. Treat `2xx` as accepted. Retry on network errors, timeouts and `429`. Do not retry other `4xx` responses.
4. Log each send with its result, and keep a plain-text fallback alert in case the card fails.
5. For channel alerts, keep a name-to-IDs map in configuration so alert code uses a name such as `production` and the IDs live in one place:
   ```json
   "TeamsAlert": {
     "Channels": {
       "production": { "TeamId": "<id>", "ChannelId": "<id>" }
     }
   }
   ```
   This map is a suggested design. It is not implemented in the console app yet.

Card template (adapt per alert type):

```json
{
  "type": "AdaptiveCard",
  "version": "1.4",
  "body": [
    {
      "type": "Container",
      "style": "attention",
      "items": [
        { "type": "TextBlock", "text": "<title>", "weight": "Bolder", "size": "Medium", "wrap": true },
        { "type": "TextBlock", "text": "<severity> | <timestamp>", "isSubtle": true, "spacing": "None" }
      ]
    },
    { "type": "TextBlock", "text": "<message>", "wrap": true },
    { "type": "FactSet", "facts": [ { "title": "Machine", "value": "<machine>" } ] }
  ],
  "actions": [ { "type": "Action.OpenUrl", "title": "Open dashboard", "url": "<url>" } ]
}
```

Container `style` by severity: `attention` (red, critical), `warning` (yellow), `good` (green, resolved), `accent` (blue, info).

Rules of thumb:

- Keep the whole card under about 25 KB. Teams limits card size.
- `card` must be a JSON object, not a pre-serialised string.
- Test cards in Teams desktop and mobile.

### 8. Testing and verification

1. Run the app with your own email first.
2. In Power Automate open the flow, then **Run history**. Open the latest run, confirm the Condition went to **False**, and check **Apply to each**. Each receiver should have its own green iteration.
3. Confirm the message arrived in Teams from **Flow bot**. Only the receiver sees it, not the flow owner (unless the owner is also a receiver).
4. Test with two or more receivers, one invalid email, and a long message.
5. Channel path: once a small test team exists, run the channel test in 4.4 and confirm the Condition went to **True** and the card appears in the channel.

### 9. Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| App says `TeamsAlert:FlowUrl is not set` | URL missing from config | Set it in `appsettings.local.json` or `TeamsAlert__FlowUrl` |
| HTTP 4xx from the app | Wrong or regenerated URL | Re-copy the HTTP URL from the trigger |
| `202` but no Teams message | Flow run failed after accepting | Open **Run history** and read the error on the Teams step |
| `No user details with email or UPN ... were found in Graph` | Recipient got a joined list, a typo, or a non-tenant email | Use the **Apply to each** loop with the loop's real name in `items('...')`; check the address |
| Loop shows "Invalid parameters", or its inner post card does | Loop input not set, or Recipient references an old loop name after a move or paste | Set the loop input to `triggerBody()?['userEmails']`; re-enter Recipient as `items('<real loop name>')` |
| A newly added action disappears after reload | It was never saved | Save before reloading; save after each major change |
| Condition keeps an empty second row | The designer adds a spare blank row | Leave it blank, or copy row 1 into it; verify in Code view and with test runs |
| Users request goes to the channel branch (or the reverse) | `target` missing or misspelled, or Condition changed | Check the `target` value and the Condition expression in 4.2 |
| Channel post fails with permission or not-found error | Wrong IDs, or the flow owner is not a member of that team or channel | Re-extract IDs from **Get link to channel**; add the owner to the team |
| Card shows literal `@{...}` or fails to render | Card field not an expression, or invalid card JSON | Set **Adaptive Card** to `string(triggerBody()?['card'])`; validate the card in the designer |
| Image shows as broken with alt text | Image URL unreachable by Teams (404, redirect, or intranet-only) | Use a public `https` image URL that returns the image directly, or a small base64 image within the card size limit |
| Throttling or slow delivery | Too many receivers or alerts at once | Add delays, reduce fan-out, or use a channel post or bot |

### 10. Security and operations

- Treat the flow URL like a password. Store it in a secret store or environment variable on the server. If it leaks, regenerate it by re-saving the trigger and update your config.
- **Dynamic channel risk:** because the caller supplies `teamId` and `channelId`, anyone with the URL can post to any team and channel that the flow owner's account can reach. Mitigations: add a check in the flow that rejects `channelId` values not on an allowed list, keep the URL tightly controlled, or use one flow per channel with fixed IDs instead.
- Do not put confidential production, customer or personal data in messages unless needed. Messages sit in Teams history, and channel posts are visible to every member.
- Never test on a large or company-wide team. A test card posted there reaches every member.
- Validate and trim dynamic text before building cards. Escape quotes in dynamic values (use a JSON serializer, not string concatenation).
- Ownership: the flow runs under the creator's Teams connection. Ask IT whether to move ownership to a shared or service account, or add co-owners, so it survives staff changes.
- Monitor **Run history** for failures, and set up a flow failure notification if IT allows.
- Production use needs testing, IT review and your normal change-control process. Confirm that the "Anyone" trigger setting meets company security policy.

### 11. File reference

| File | Purpose |
|---|---|
| `Program.cs` | Console app: picks users or a named channel, builds the three sample cards, posts to the flow with retries |
| `TeamsAlert.csproj` | .NET 10 project, configuration packages |
| `appsettings.json` | Default settings (keep `FlowUrl` empty) |
| `appsettings.local.json` | Optional, git-ignored, holds the secret URL and the named channels |
| `appsettings.local.example.json` | Template for `appsettings.local.json` with placeholders only (not loaded by the app) |
| `.gitignore` | Excludes `bin/`, `obj/`, `appsettings.local.json` |

### 12. Open items

- **Channel path untested.** The True branch is built, but no test run has been done because creating a small test team was not possible (the account only belongs to a large company-wide team, and team creation from scratch was not available). Next step: ask IT for a small test team that the flow owner belongs to, then run the channel test in 4.4.
- **Console app channel mode is untested against a real channel.** The app now sends `target`, `teamId` and `channelId` from the `TeamsAlert:Channels` map. It builds and its input checks were exercised (unknown channel and invalid email both exit with code 1 before sending), but no request has been sent to a channel.
- **Group chat posting is not supported.** Teams offers a **Post in: Group chat** option in the action, which needs a chat ID and a chat the flow owner belongs to. It would be a separate branch and has not been designed or tested.
- **Allow-list for channel IDs** is recommended before production (see section 10) but not built.
- **Ownership and volume limits** need IT input before production use.
