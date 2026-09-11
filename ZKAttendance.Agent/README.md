# App2 — the local agent

Runs on a PC **inside the office**, on the same network as the ZKTeco terminals.
Reads punches and forwards them to App1. Nothing else.

It knows nothing about employees, lateness or approvals. All of that stays in
App1, so there is one copy of the rules and the two apps cannot disagree.

---

## Setup

### 1. Register the agent in App1

As Admin:

```
POST /api/LocalServers
{ "serverName": "Head office agent", "branchId": 1 }
```

Copy `agentKey` and `secret` from the reply. **The secret is shown once.** If you
lose it, `POST /api/LocalServers/{id}/rotate-secret` issues a new one.

### 2. Point App2 at App1

`appsettings.json`:

```json
"Central": {
  "BaseUrl": "http://192.168.1.50:5107/",
  "AgentKey": "agt_...",
  "Secret": "..."
}
```

`BaseUrl` is App1's address as seen from this PC. Not `localhost` unless both run
on the same machine.

### 3. Run it

```
cd ZKAttendance.Agent
dotnet run
```

Opens on <http://localhost:5200>. The outbox database is created automatically.

No hardware? Set `"DeviceProtocol": "Fake"` and it generates believable punches.

---

## Using it

The page shows connection state, the outbox, and the devices App1 says this agent
is responsible for.

Press **Sync** on a device:

```
connect over the LAN → read punches → save to the outbox → post to App1 → mark sent
```

The outbox drains every 20 seconds on its own. **Send now** forces it.

---

## Why the outbox

The agent never posts straight from memory. It commits punches to a local table
first, and a separate worker drains it.

If the internet drops mid-send, the punches are already on disk. The link can be
down for days; they flush when it returns. Without this, a dropped connection means
nobody knows Ram came to work on Tuesday.

### Retries

| Situation | What happens |
|---|---|
| Network down, timeout | Retry: 2s, 4s, 8s… capped at 5 minutes. Forever. |
| App1 returns 500 | Their problem. Retry. |
| App1 returns 400 | Our payload is wrong. Three tries, then marked failed. |

Failed rows show in the UI with the reason. Fix the cause in App1, then press
**Retry failed**.

### Duplicates are safe

An outbox delivers *at least once*. If the POST succeeds but the reply is lost, the
agent sends again.

That is fine here. App1's unique index on `(BiometricUserId, AttendanceTime,
DeviceId)` refuses the second copy and reports it as a duplicate, which the agent
treats as success. The same index that makes re-running a device sync safe makes
duplicate delivery safe.

---

## Ports and layout

| | App1 | App2 |
|---|---|---|
| Port | 5107 | 5200 |
| Database | `ZKAttendance` | `ZKAgent` (outbox only) |
| Reaches devices | No | Yes |
| Reachable from internet | Yes | No, and must not be |

App2 has **no login**. It sits inside the office and is not exposed. The credential
that matters is the agent secret, which lives in config and never reaches the
browser. If you ever put App2 on a public address, put a login in front of it first.
