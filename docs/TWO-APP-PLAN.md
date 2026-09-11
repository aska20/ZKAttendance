# Plan: splitting into two apps, and shift groups

Written in plain language on purpose. Nothing is built yet — this is the agreement
about what we are going to build, so we can argue about it before writing code
rather than after.

---

## Part 1 — Why two apps

### The problem

A ZKTeco device sits on your office network with an address like `192.168.1.201`.
That address is **private**. It only means something inside your building. A server
on the internet cannot reach it, the same way a stranger cannot dial your
extension number without going through reception first.

Today the whole system runs on one machine on that same network, so it works. The
moment the app moves to the internet, it loses the ability to see the devices.

### The fix

Split the work in two.

| | Where it runs | What it does |
|---|---|---|
| **App1** (you already have this) | Internet / public server | Employees, departments, attendance, reports, settings. The brain and the database. |
| **App2** (new, small) | A PC inside the office | Talks to the ZKTeco devices. Reads punches. Sends them to App1. That is all. |

App2 is deliberately dumb. It does not decide who is late, it does not know about
departments, it does not calculate anything. It fetches and forwards. Every rule
stays in App1, so there is only ever one place where attendance logic lives.

### The flow

```
you press Sync in App2
        ↓
pick which device
        ↓
App2 connects to the device over the office network (TCP port 4370)
        ↓
reads the punch records
        ↓
saves them into its own small local table  ← the outbox
        ↓
a background worker posts them to App1 over HTTPS
        ↓
App1 replies "got them"
        ↓
App2 marks those rows complete
```

---

## Part 2 — The outbox, explained simply

### What it is

An **outgoing letter tray**.

App2 never sends punches straight from memory. It writes them into a local table
first. A separate small worker takes rows out of that table and posts them to App1.

### Why bother

Because the internet will drop. It always does, usually on the day it matters.

Without the tray: App2 reads 40 punches, the connection dies mid-send, the punches
were only ever in memory, and they are gone. Nobody knows Ram came to work on
Tuesday.

With the tray: those 40 rows are already saved on disk. The link can be down for
three days. When it comes back, they flush.

### The table in App2

```sql
CREATE TABLE OutboxPunches (
    OutboxId        BIGINT IDENTITY(1,1) PRIMARY KEY,
    BiometricUserId VARCHAR(12)   NOT NULL,
    DeviceId        INT           NOT NULL,
    PunchTime       DATETIME2     NOT NULL,
    Payload         NVARCHAR(MAX) NOT NULL,
    Status          TINYINT       NOT NULL DEFAULT 0,   -- 0 waiting, 1 sent, 2 given up
    Attempts        INT           NOT NULL DEFAULT 0,
    NextAttemptAt   DATETIME2     NULL,
    LastError       NVARCHAR(500) NULL,
    CreatedAt       DATETIME2     NOT NULL DEFAULT GETDATE()
);

CREATE INDEX IX_Outbox_Pending ON OutboxPunches (Status, NextAttemptAt)
    WHERE Status = 0;
```

The filtered index matters. The worker asks "what is waiting and due now" every few
seconds, and without it that query would scan months of already-sent rows.

### What happens when sending fails

- **Network error / timeout** — leave the row waiting, try again later, wait a bit
  longer each time (2s, 4s, 8s, up to 5 minutes).
- **App1 says 500** — its problem, not ours. Retry.
- **App1 says 400** — our payload is wrong and will never be accepted. After three
  tries, mark it "given up" so it stops clogging the tray, and show it in the UI so
  a human notices.

### Sending the same punch twice

An outbox delivers **at least once**. If the POST succeeds but the reply is lost,
App2 does not know it worked, so it sends again.

Normally you would need extra machinery to stop duplicates. **You already have it.**
`AttendanceLogs` has a unique index on `(BiometricUserId, AttendanceTime, DeviceId)`.
The second insert is simply refused by SQL Server. The same index that makes
re-running a device sync safe makes duplicate delivery safe.

So App1's receiving endpoint reuses the existing ingest path and duplicates fall out
harmlessly. Nothing new to write.

---

## Part 3 — How the two apps talk

Plain HTTPS, using `IHttpClientFactory`.

Not WebSockets, for now. WebSockets earn their place when the **server** needs to
push something down to the agent immediately, such as "start a fingerprint scan on
that device, someone is standing at it right now". Sending punches upward does not
need that — a one-minute delay is invisible.

### Registering the client in App2

```csharp
builder.Services.AddHttpClient("central", c =>
{
    c.BaseAddress = new Uri(config["Central:BaseUrl"]);
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.Add("X-Agent-Key", config["Central:AgentKey"]);
})
.AddStandardResilienceHandler();
```

**Use the factory, never `new HttpClient()`.** Creating one per request runs the
machine out of sockets; keeping one static forever means it never notices a DNS
change. The factory handles both.

### The methods you will actually use

| Method | Used for |
|---|---|
| `PostAsJsonAsync(url, obj, ct)` | Sending a batch of punches |
| `GetFromJsonAsync<T>(url, ct)` | Fetching the device list, checking reports |
| `SendAsync(request, ct)` | When a single call needs its own headers |

### Endpoints App1 will expose for App2

```
POST /api/Agent/login       agent key + secret  → token
GET  /api/Agent/devices     which devices this agent is responsible for
POST /api/Agent/punches     receive a batch     → per-row accepted/duplicate
GET  /api/Agent/reports     read back a summary
```

All of them require the agent key. Without that, anyone who finds the URL can post
fake attendance.

---

## Part 4 — Making App1 public

### It is not public today

App1 listens on `localhost:5107`. `localhost` means *this computer only*. App2 on
another machine cannot reach it, and neither can anything on the internet.

### Three options

| Option | Effort | Use when |
|---|---|---|
| **Cloudflare Tunnel** or ngrok | ~10 minutes, free | Demo, viva, testing |
| **Deploy to a VPS** | Half a day | Real deployment |
| **Same office LAN only** | One config line | Testing App2 today |

For a college project the tunnel is the sensible choice. One command gives you a
real `https://` address that points at your machine, and App2 talks to it exactly as
it would talk to a production server. No firewall changes, no router config.

### Cheapest path for testing right now

Bind to the network instead of localhost, in `launchSettings.json`:

```
"applicationUrl": "http://0.0.0.0:5107"
```

Then App2 on another PC uses `http://192.168.1.50:5107`, using App1's actual LAN
address. This proves the two apps talk before any hosting is involved.

### Three changes App1 needs either way

1. **Listen on all addresses**, not just localhost.
2. **CORS** — allow App2's origin. Already configured via `Cors:AllowedOrigins`;
   just needs the value.
3. **Agent authentication** — the `LocalServers` table already exists, holding an
   agent key and a hashed secret. The endpoints need to check it.

### Then, for real hosting

- HTTPS certificate (free with Cloudflare or Let's Encrypt)
- SQL Server reachable from the host, or move the database with it
- Change the connection string
- Keep `DeviceProtocol` as `Fake` on the public box, `Tcp` in App2

---

## Part 5 — Shift groups

### What exists

Work shifts exist: name, start, end, grace, break. Employees are assigned to a
shift **one at a time**, and an employee's shift end time already decides when their
day closes.

### What you asked for

Different time rules for different **groups**, so IT works 10 to 6 and Support works
2 to 10, without ticking two hundred checkboxes.

### The change

Shift resolution becomes a chain, most specific first:

```
1. Does this employee have their own shift?        → use it
2. Does their department have a shift?             → use it
3. Otherwise                                       → office hours from Settings
```

This means one field on `Departments`:

```sql
ALTER TABLE Departments ADD DefaultShiftId INT NULL
    REFERENCES WorkShifts(ShiftId);
```

Nullable, so nothing that works today changes. A department with no shift behaves
exactly as it does now.

The individual override still wins, which matters because there is always one person
who comes in early.

### On screen

- Departments page gets a shift dropdown
- Work Shifts page shows which departments use each shift
- The employee form shows the inherited shift greyed out, with an option to override

---

## What we build, in order

| Step | What | Why first |
|---|---|---|
| 1 | Department shift + resolution chain | Smallest, self-contained, no new app |
| 2 | Agent endpoints on App1 + agent-key auth | App2 has nothing to talk to without them |
| 3 | Make App1 reachable (tunnel or LAN bind) | Proves the connection before writing App2 |
| 4 | App2 skeleton: login, device list | Smallest thing that proves auth works |
| 5 | App2 sync: read device, write to outbox | Reuses the existing device reader |
| 6 | Outbox worker: drain, retry, mark complete | The bit that survives a dropped link |
| 7 | App2 reports view | Nice to have, last |

Steps 1 to 3 touch App1 only. App2 does not exist until step 4, so nothing is at
risk until then.

---

## Things worth deciding before we start

1. **Is App2 a console app, a Windows Service, or a small web UI?**
   A web UI gives you the "press Sync, choose device" screen you described, and can
   be opened from any machine in the office. A console app is easier to demo. A
   service survives reboots but has no screen.

2. **Does App2 need its own database, or a local file?**
   SQL Server Express for the outbox is straightforward. SQLite is lighter and needs
   no install on the office PC.

3. **Does App2 log in with a user account, or only an agent key?**
   You said "login validated by the central server". That is doable, but the agent
   key is what actually secures the machine-to-machine calls. A user login on top is
   for deciding *who is allowed to press Sync*.
