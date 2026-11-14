# Teams Alert POC

Proof of concept for sending alert messages to Microsoft Teams from code. `main` holds only the overview and shared guidance. Each method lives on its own branch, with its own code and full setup guide.

> **Draft for review.** Anything that touches production alert systems needs testing and your normal change-control and IT review before release.

## Branches

| Branch | Method | Contents |
|---|---|---|
| `main` | none | This overview |
| `workflow` | Workflows (Power Automate) webhook | .NET test console app and the full setup guide |

Convention: one branch per method, named after the method. Add a row here when a new branch is created.

## Methods

| Method | How it works | Status | Branch |
|---|---|---|---|
| **Workflows (Power Automate) webhook** | Code POSTs JSON to a flow URL. The flow posts an Adaptive Card from Flow bot to users (private chats) or to a Team channel. | Users path **tested and working**. Channel path built in the flow and the app but **not tested** (no small test team available). | `workflow` |
| **Bot via Teams Developer Portal** | Bot registered without an Azure subscription, sends proactive messages. | **Tried, blocked** by insufficient privileges (details not recorded). | none |
| **Azure Bot (Bot Framework)** | Bot registered in Azure, sends proactive messages through stored conversation references. | **Not attempted.** Needs an Azure subscription and IT approval. | none |
| **Microsoft Graph, delegated** | Posts to a chat as a signed-in user. | **Not built.** Needs an app registration and admin consent, and depends on a person's account. | none |

Methods that are not usable for new work: the retired Office 365 connector incoming webhooks, and Graph with a stored username and password.

## Choosing a method

- **Simple alerts to chosen users or a channel, no Azure:** Workflows webhook.
- **Configurable sender, buttons, large volume:** a bot (needs Azure and IT).
- **Many receivers (hundreds or more):** post once to a channel, or use a bot with a queue. Per-user fan-out through a flow hits volume limits.

## Shared design

All methods follow the same shape, so alert code stays the same when the method changes:

1. The alert system builds the **content** (an Adaptive Card or text).
2. It names the **receivers** (a list of users, or a channel).
3. A **sender** component delivers it, with retries and logging.

Keep the sender behind one interface in your alert service, next to the email, Telegram and WhatsApp channels.

## Shared rules

- Secrets (flow URLs, bot secrets, tokens) stay out of source control. Use a git-ignored local file, environment variables or a secret store. If a URL or secret leaks, regenerate it.
- Do not put confidential production, customer or personal data in messages unless needed.
- Never test against a large or company-wide team. Use a small test team.
- Validate and trim dynamic text, and build JSON with a serializer, not string concatenation.
- Retry on network errors, timeouts and throttling (`429`). Do not retry other `4xx` responses. Log every send and its result, and keep a plain-text fallback.
- Production use needs testing, IT review and change control. Confirm the setup meets company security policy.
